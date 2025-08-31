# Next.js Client Guide (Cloudflare Turnstile + /v1/translate)

This guide shows how to call your .NET translation API from a Next.js app with Cloudflare Turnstile verification on every request.

## Server assumptions

• Your API exposes POST /v1/translate.
• Turnstile is enforced via the endpoint filter and expects a header (default: CF-Turnstile-Token).
• Your API also exposes GET /\_turnstile/sitekey returning:

```json
{ "siteKey": "<public-site-key>", "headerName": "CF-Turnstile-Token" }
```

---

## 1. Create a Next.js app

```bash
# Node 18+ recommended
pnpm create next-app@latest my-translator --typescript --tailwind --eslint --app --src-dir --import-alias "@/*"
cd my-translator
```

(Optional) Add a .env.local to hold your API base URL:

```bash
# my-translator/.env.local
NEXT_PUBLIC_API_BASE=http://localhost:8080
```

---

## 2. Plan the flow

1. Fetch { siteKey, headerName } from /\_turnstile/sitekey.
2. Render a Turnstile widget (Managed mode is fine).
3. When the widget returns a token, send it with every API call in the header the server expects.
4. On 400/403 (missing/invalid token), force the widget to refresh and retry only after user solves again.

Turnstile tokens are short-lived and typically single-use—get a fresh token for each request.

---

## 3. A tiny Turnstile hook

Create src/hooks/useTurnstile.tsx to dynamically load the Turnstile script, render the widget, and give you a fresh token on demand.

```tsx
// src/hooks/useTurnstile.tsx
import { useCallback, useEffect, useRef, useState } from "react";

declare global {
  interface Window {
    turnstile?: {
      render: (el: HTMLElement, opts: any) => string | number;
      reset: (id?: string | number) => void;
    };
  }
}

export function useTurnstile() {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [widgetId, setWidgetId] = useState<string | number | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  const loadScript = useCallback(() => {
    if (window.turnstile) return Promise.resolve();
    return new Promise<void>((resolve, reject) => {
      const s = document.createElement("script");
      s.src =
        "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
      s.async = true;
      s.defer = true;
      s.onload = () => resolve();
      s.onerror = () => reject(new Error("Failed to load Turnstile API"));
      document.head.appendChild(s);
    });
  }, []);

  const render = useCallback(
    async (siteKey: string) => {
      await loadScript();
      if (!containerRef.current || !window.turnstile) return;

      // Clear previous
      containerRef.current.innerHTML = "";
      setToken(null);

      const id = window.turnstile.render(containerRef.current, {
        sitekey: siteKey,
        action: "react-app",
        callback: (t: string) => setToken(t || null),
        "expired-callback": () => setToken(null),
        "error-callback": () => setToken(null),
        theme: "auto",
      });

      setWidgetId(id);
      setReady(true);
    },
    [loadScript]
  );

  const refresh = useCallback(() => {
    setToken(null);
    if (window.turnstile && widgetId !== null) {
      window.turnstile.reset(widgetId);
    }
  }, [widgetId]);

  return { containerRef, render, refresh, token, ready };
}
```

---

## 4. A minimal Translate page

Create src/components/TranslatePage.tsx. It fetches the site key + header name, renders the widget, and posts to /v1/translate with the token.

```tsx
// src/components/TranslatePage.tsx
"use client";

import { useEffect, useMemo, useState } from "react";
import { useTurnstile } from "@/hooks/useTurnstile";

type SiteMeta = { siteKey: string; headerName: string };

export default function TranslatePage() {
  const API_BASE = process.env.NEXT_PUBLIC_API_BASE || "http://localhost:8080";
  const [meta, setMeta] = useState<SiteMeta | null>(null);
  const [text, setText] = useState("");
  const [sourceLang, setSourceLang] = useState("");
  const [targetLang, setTargetLang] = useState("en");
  const [result, setResult] = useState<string>("");

  const { containerRef, render, refresh, token, ready } = useTurnstile();

  // Fetch siteKey + headerName from your API
  useEffect(() => {
    fetch(`${API_BASE}/_turnstile/sitekey`, { cache: "no-store" })
      .then((r) => r.json())
      .then((j: SiteMeta) => setMeta(j))
      .catch(() => setMeta({ siteKey: "", headerName: "CF-Turnstile-Token" }));
  }, [API_BASE]);

  // Render Turnstile once we have the siteKey
  useEffect(() => {
    if (meta?.siteKey) {
      render(meta.siteKey);
    }
  }, [meta?.siteKey, render]);

  const headerName = useMemo(
    () => meta?.headerName || "CF-Turnstile-Token",
    [meta?.headerName]
  );

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setResult("");

    if (!token) {
      alert("Please complete the Turnstile challenge first.");
      refresh();
      return;
    }

    const body = {
      text,
      sourceLang: sourceLang || null,
      targetLang,
    };

    const res = await fetch(`${API_BASE}/v1/translate`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        [headerName]: token,
      },
      body: JSON.stringify(body),
    });

    if (res.status === 400 || res.status === 403) {
      // Likely missing/invalid/expired token; force a refresh
      refresh();
      const err = await res.json().catch(() => ({}));
      alert(err?.title || "Verification failed. Please try again.");
      return;
    }

    if (!res.ok) {
      const err = await res.text();
      alert(`Error ${res.status}: ${err}`);
      return;
    }

    const data = await res.json();
    setResult(data.translatedText || "");
    // Best practice: refresh token per request (Turnstile tokens are short-lived)
    refresh();
  }

  return (
    <div
      style={{
        maxWidth: 720,
        margin: "32px auto",
        fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Arial",
      }}
    >
      <h1>Translator (React + Turnstile)</h1>

      <form onSubmit={onSubmit}>
        <label>
          Source language (optional):
          <input
            value={sourceLang}
            onChange={(e) => setSourceLang(e.target.value)}
            placeholder="e.g. en or zh-CN"
          />
        </label>
        <br />

        <label>
          Target language:
          <input
            value={targetLang}
            onChange={(e) => setTargetLang(e.target.value)}
            required
            placeholder="e.g. en, ja, de"
          />
        </label>
        <br />

        <label>
          Text:
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={6}
            style={{ width: "100%" }}
            required
          />
        </label>

        <div style={{ margin: "12px 0" }}>
          {/* Turnstile widget renders here */}
          <div ref={containerRef} />
          {!ready && <small>Loading Turnstile…</small>}
        </div>

        <button type="submit">Translate</button>
      </form>

      {result && (
        <>
          <h3>Result</h3>
          <pre
            style={{
              whiteSpace: "pre-wrap",
              background: "#f7f7f8",
              padding: 12,
              borderRadius: 6,
            }}
          >
            {result}
          </pre>
        </>
      )}
    </div>
  );
}
```

Wire it in src/app/page.tsx to render the page.

```tsx
// src/app/page.tsx
import TranslatePage from "@/components/TranslatePage";

export default function Home() {
  return (
    <main className="container mx-auto py-8">
      <TranslatePage />
    </main>
  );
}
```

Run it:

```bash
pnpm dev
```

---

## 5. CORS checklist

On the server, ensure your CORS policy allows your Next.js origin (e.g., http://localhost:3000) and the Turnstile header:
• Allowed origins: your Next.js app URL(s)
• Allowed methods: POST, GET, OPTIONS
• Allowed headers include: Content-Type, CF-Turnstile-Token (or whatever header name you configured)
• Allow credentials: false (unless you’re using cookies/auth)

---

## 6. Common pitfalls

• **Empty siteKey in Swagger or Next.js** → You didn’t load .env.local or mis-spelled TURNSTILE\_\_SITEKEY.
• **Requests succeed without a token** → Ensure TurnstileOptions.RequireOnTranslate = true and SecretKey is set; our filter fails if SecretKey is missing.
• **Frequent 403** → Token expired or re-used. Always refresh the widget after a request and prompt the user to solve again.
• **CORS preflight fails** → Add the Turnstile header name to Access-Control-Allow-Headers.

---

## 7. Optional: “User must click” UX

If you need a hard “click to verify” step (even when Turnstile auto-passes), gate the request on a user action. For example, only accept tokens generated after the user clicked a “Verify” button, and consider them fresh for ≤60s; otherwise block the request and re-render the widget.

---

## 8. Request/Response structures

**Request POST /v1/translate:**

```json
{
  "text": "Bonjour tout le monde!",
  "sourceLang": null,
  "targetLang": "en"
}
```

**Response:**

```json
{
  "sourceLang": "auto",
  "targetLang": "en",
  "translatedText": "Hello everyone!"
}
```

---

## 9. Production notes

• **Put your API behind a reverse proxy** (TLS at the proxy).
• **Keep Turnstile SecretKey in server env only**; SiteKey can be public.
• **Consider rate limiting, quotas, caching identical requests, and logging** for abuse detection.

---

## Done!

Open http://localhost:3000 (or your dev URL), solve Turnstile, and hit Translate—the header will carry the token to your API, which verifies and then calls Gemini.
