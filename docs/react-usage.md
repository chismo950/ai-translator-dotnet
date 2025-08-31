# React / Next.js Client Guide (Cloudflare Turnstile **Pass** + `/v1/translate`)

This guide shows how to call your .NET translation API from a React or Next.js app with **Cloudflare Turnstile** and a **short‑lived server pass** (`X-Turnstile-Pass`).  
Flow: user solves Turnstile → server verifies → server returns a short pass header → subsequent requests present the pass and **skip Turnstile** until the pass expires or runs out of uses.

---

## Server assumptions

- Your API exposes `POST /v1/translate`.
- Turnstile is enforced via an endpoint filter and expects a token header (default: `CF-Turnstile-Token`).
- Your API also exposes `GET /_turnstile/sitekey` returning:
  ```json
  { "siteKey": "<public-site-key>", "headerName": "CF-Turnstile-Token" }
  ```
- The server issues a short‑lived pass after a successful verification and returns it in the response header:
  - `X-Turnstile-Pass: <opaque-token>`
- **CORS** exposes the pass header (so browsers can read it):
  - `Access-Control-Expose-Headers: X-Turnstile-Pass`

> The pass is typically bound to IP and User‑Agent, has a small TTL (e.g., 5 minutes) and limited uses (e.g., 3).

---

## 1) Create the app

### React (Vite)

```bash
npm create vite@latest my-translator -- --template react
cd my-translator
npm install
# (optional)
echo 'VITE_API_BASE=http://localhost:8080' > .env
```

### Next.js

```bash
npx create-next-app@latest my-translator --ts
cd my-translator
# (optional)
echo 'NEXT_PUBLIC_API_BASE=http://localhost:8080' > .env.local
```

---

## 2) Plan the client flow

1. `GET /_turnstile/sitekey` → get `{ siteKey, headerName }`.
2. Render Turnstile widget; user clicks **Verify**. Widget returns a **token**.
3. Send the token in header `CF-Turnstile-Token` on the **first** API call.
4. Server verifies and replies **200** with header **`X-Turnstile-Pass`**.
5. Store the **pass in memory** and send it on subsequent API calls via header `X-Turnstile-Pass` until it expires or hits the use limit.
6. On **400/403**, clear the pass, refresh Turnstile, ask user to verify again.

> Keep the pass in memory (not localStorage) to reduce theft risk.

---

## 3) Minimal Turnstile hook

Create the hook to load/refresh the widget and get a token when user verifies.

**React:** `src/useTurnstile.tsx`  
**Next.js App Router:** `src/hooks/useTurnstile.tsx`

```tsx
import { useCallback, useRef, useState } from "react";

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
      containerRef.current.innerHTML = "";
      setToken(null);

      const id = window.turnstile.render(containerRef.current, {
        sitekey: siteKey,
        action: "web-client",
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

## 4) A pass‑aware API client

This helper prefers the server **pass**. If absent, it uses a fresh **Turnstile token**. It also **captures** a new pass from responses and **clears** it on 400/403.

Create **`src/apiClient.ts`** (React) or **`src/lib/apiClient.ts`** (Next.js).

```ts
// src/apiClient.ts  (or: src/lib/apiClient.ts)
let passToken: string | null = null;

export function setPass(token: string | null) {
  passToken = token || null;
}
export function clearPass() {
  passToken = null;
}
export function getPass() {
  return passToken;
}

function getApiBase(): string {
  // Vite
  const vite = (import.meta as any)?.env?.VITE_API_BASE;
  if (vite) return vite as string;
  // Next.js
  const next = (process as any)?.env?.NEXT_PUBLIC_API_BASE;
  if (next) return next as string;
  return "http://localhost:8080";
}

/**
 * POST /v1/translate (pass-aware)
 */
export async function postTranslate(
  body: { text: string; sourceLang: string | null; targetLang: string },
  opts: {
    turnstileHeaderName: string; // e.g., "CF-Turnstile-Token"
    getTurnstileToken: () => string | null; // provide current Turnstile token (if any)
  }
) {
  const base = getApiBase();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };

  // Prefer server-issued pass
  if (passToken) {
    headers["X-Turnstile-Pass"] = passToken;
  } else {
    // Fall back to Turnstile token
    const t = opts.getTurnstileToken();
    if (t) headers[opts.turnstileHeaderName || "CF-Turnstile-Token"] = t;
  }

  const res = await fetch(`${base}/v1/translate`, {
    method: "POST",
    headers,
    body: JSON.stringify(body),
    credentials: "include", // or "omit" if you do not use cookies
  });

  // Capture new pass if server issued one
  const issued = res.headers.get("X-Turnstile-Pass");
  if (issued) setPass(issued);

  if (res.status === 400 || res.status === 403) {
    clearPass(); // invalid/expired pass or token
    const err = await res.json().catch(() => ({}));
    const msg = (err && (err.title || err.detail)) || `HTTP ${res.status}`;
    throw new Error(msg);
  }

  if (!res.ok) {
    throw new Error(`HTTP ${res.status}: ${await res.text()}`);
  }

  return res.json(); // { sourceLang, targetLang, translatedText }
}
```

---

## 5) Minimal page component

Below is a single component you can drop into **React** or **Next.js (app router)**.  
It fetches the site metadata, renders Turnstile, and calls the pass‑aware client.  
If there is **no pass**, it requires a user click to get a **fresh Turnstile token**.

**React:** `src/TranslatePage.tsx`  
**Next.js:** `src/components/TranslatePage.tsx` + `app/page.tsx` to render it.

```tsx
// TranslatePage.tsx
"use client";

import { useEffect, useMemo, useState } from "react";
import { useTurnstile } from "./useTurnstile"; // adjust path if Next.js: "@/hooks/useTurnstile"
import { postTranslate, getPass } from "./apiClient"; // adjust path if Next.js: "@/lib/apiClient"

type SiteMeta = { siteKey: string; headerName: string };

export default function TranslatePage() {
  const [meta, setMeta] = useState<SiteMeta | null>(null);
  const [text, setText] = useState("");
  const [sourceLang, setSourceLang] = useState("");
  const [targetLang, setTargetLang] = useState("en");
  const [result, setResult] = useState<string>("");
  const [mustClick, setMustClick] = useState(false); // require manual verify when no pass

  const { containerRef, render, refresh, token } = useTurnstile();

  // Fetch siteKey + headerName
  useEffect(() => {
    const base =
      (import.meta as any)?.env?.VITE_API_BASE ||
      (process as any)?.env?.NEXT_PUBLIC_API_BASE ||
      "http://localhost:8080";
    fetch(`${base}/_turnstile/sitekey`, { cache: "no-store" })
      .then((r) => r.json())
      .then((j: SiteMeta) => setMeta(j))
      .catch(() => setMeta({ siteKey: "", headerName: "CF-Turnstile-Token" }));
  }, []);

  // Render Turnstile when we have siteKey
  useEffect(() => {
    if (meta?.siteKey) render(meta.siteKey);
  }, [meta?.siteKey, render]);

  const headerName = useMemo(
    () => meta?.headerName || "CF-Turnstile-Token",
    [meta?.headerName]
  );

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setResult("");

    // If there is no server pass yet, require a manual verify to get a token
    if (!getPass() && !token) {
      setMustClick(true);
      alert("Please click Verify and complete Turnstile.");
      refresh();
      return;
    }

    try {
      const data = await postTranslate(
        {
          text,
          sourceLang: sourceLang || null,
          targetLang,
        },
        {
          turnstileHeaderName: headerName,
          getTurnstileToken: () => token,
        }
      );
      setResult(data.translatedText || "");
    } catch (err: any) {
      alert(err?.message || "Request failed.");
    } finally {
      // Turnstile tokens are single-use; refresh after each request
      if (!getPass()) {
        refresh();
      }
      setMustClick(false);
    }
  }

  return (
    <div
      style={{
        maxWidth: 720,
        margin: "32px auto",
        fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Arial",
      }}
    >
      <h1>Translator (React/Next.js + Turnstile Pass)</h1>

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
          {mustClick && <small>Please click Verify to generate a token…</small>}
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

For **Next.js App Router**, render this in `src/app/page.tsx`:

```tsx
import TranslatePage from "@/components/TranslatePage"; // adjust path
export default function Home() {
  return (
    <main>
      <TranslatePage />
    </main>
  );
}
```

---

## 6) CORS checklist

On the server, ensure your CORS policy allows your web app origin and the required headers:

- Allowed origins: your React/Next.js URL(s) (e.g., `http://localhost:5173` or `http://localhost:3000`)
- Allowed methods: `POST, GET, OPTIONS`
- Allowed request headers include: `Content-Type`, **`CF-Turnstile-Token`**
- **Exposed response headers** include: **`X-Turnstile-Pass`**
- Allow credentials: `false` (unless you use cookies/auth)

Example (server):

```csharp
policy
  .WithOrigins("http://localhost:5173", "http://localhost:3000")
  .AllowAnyMethod()
  .AllowAnyHeader()
  .WithExposedHeaders("X-Turnstile-Pass");
```

---

## 7) Common pitfalls

- **Pass not applied on 2nd request** → You are not reading `X-Turnstile-Pass` from the first 200 OK, or CORS is not exposing it.
- **“Missing or invalid token” (400/403)** → Pass expired/uses exhausted, or Turnstile token missing/stale. Clear pass, refresh widget, ask user to verify again.
- **Empty `siteKey`** → `.env.local` not loaded on server or mis‑spelled `TURNSTILE__SITEKEY`.
- **CORS preflight fails** → Add the Turnstile header name to `Access-Control-Allow-Headers`. If you don’t whitelist explicitly, keep `.AllowAnyHeader()`.
- **Security** → Keep the pass **in memory only**. Do not persist to localStorage/sessionStorage.

---

## 8) Request/Response structures

**Request** `POST /v1/translate`:

```json
{
  "text": "Bonjour tout le monde!",
  "sourceLang": null,
  "targetLang": "en"
}
```

**Response**:

```json
{
  "sourceLang": "auto",
  "targetLang": "en",
  "translatedText": "Hello everyone!"
}
```

(with response header `X-Turnstile-Pass: <token>` on the first successful call)

---

## 9) Production notes

- Use a reverse proxy for TLS termination.
- Keep Turnstile **SecretKey** in server env; **SiteKey** is public.
- Consider rate limits, quotas, caching identical requests, and logging for abuse detection.
- Tune pass TTL/uses and IP/UA binding on the server to balance UX vs. abuse risk.

---

**Done.** Start your app, click **Verify** once, call **Translate**, and then enjoy **pass‑based** requests for a few minutes or a few calls until the pass expires.
