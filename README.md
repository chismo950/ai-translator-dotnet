# AI Translator (.NET 8 + Cloudflare Turnstile + Gemini)

A minimal, production-ready translation API built with .NET 8, using Google Gemini 2.0 Flash for translation and Cloudflare Turnstile for anti-abuse.

## Features

• 🔑 Multi-key rotation for GEMINI_API_KEYS (failover across multiple API keys)
• 🔒 Turnstile verification required on protected endpoints
• 🎟️ Optional short-lived pass (X-Turnstile-Pass) so users don't have to solve a challenge on every request
• 🧩 Custom Swagger UI with Turnstile widget and pass handling
• 🌍 CORS policy with origin suffix allow-list

---

## Live Demo (Swagger)

**URL:** https://aitranslator-fae0f8bxegh8dkb4.newzealandnorth-01.azurewebsites.net/swagger

**Important:** Before calling any API from Swagger, manually click "Verify" (Cloudflare Turnstile) in the top bar to generate a token. Otherwise requests will be rejected (400/403). Some sessions may receive a short-lived X-Turnstile-Pass header on the first successful call.

---

## Requirements

• .NET SDK 8.0+
• A Cloudflare account with a Turnstile Site Key and Secret Key
• 1+ Gemini API keys (for the Flash model), e.g. Gemini 2.0 Flash

---

## Quick Start

### 1. Clone & enter

```bash
git clone git@github.com:chismo950/ai-translator-dotnet.git ai-translator-dotnet
cd ai-translator-dotnet
```

### 2. Create .env.local (at the project root)

The project loads this file at startup and maps `FOO__BAR` → `Foo:Bar` in ASP.NET configuration.

```env
# Public site key for the Turnstile widget (safe to expose)
TURNSTILE__SITEKEY=1x00000000000000000000AA

# Secret key for server-side verification (keep secret!)
TURNSTILE__SECRETKEY=2x0000000000000000000000000000000AA

# Comma-separated Gemini API keys (rotated per request on failure)
# Example: key_aaa,key_bbb,key_ccc
GEMINI_API_KEYS=your_gemini_key_1,your_gemini_key_2
```

Keep `.env.local` out of source control (git-ignored).

### 3. Edit appsettings.json (non-secret config)

```json
{
  "Turnstile": {
    "RequireOnTranslate": true,
    "HeaderName": "CF-Turnstile-Token"
  },
  "TurnstilePass": {
    "Enabled": true,
    "ExpirySeconds": 300,
    "MaxUses": 3,
    "HeaderName": "X-Turnstile-Pass",
    "BindToIp": true,
    "BindToUserAgent": true
  },
  "Cors": {
    "AllowedOriginSuffixes": [
      "localhost",
      "127.0.0.1"
      // add your domains, e.g. "example.com"
    ]
  }
}
```

### 4. Restore & run

```bash
dotnet restore
dotnet run
```

• By default the project config binds Kestrel to `http://127.0.0.1:8080` (via UseUrls or your environment).
• Navigate to: `http://127.0.0.1:8080/swagger`

If port 8080 is occupied, run with a different port:

```bash
ASPNETCORE_URLS=http://127.0.0.1:9090 dotnet run
```

---

## Endpoints

### GET /\_turnstile/sitekey

Returns the public site key and the header name your client must use for Turnstile tokens.

```json
{ "siteKey": "1x00000000000000000000AA", "headerName": "CF-Turnstile-Token" }
```

### POST /v1/translate

Translate text using Gemini. Protected by Turnstile filter (or short-lived pass).

**Request:**

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

**Headers behavior:**
• If you don't already have a pass:
• Include `CF-Turnstile-Token: <token-from-widget>`
• On success, the server may return a short-lived pass header:
• `X-Turnstile-Pass: <opaque-token>`
• If you do have a pass (not expired, uses left):
• Only send `X-Turnstile-Pass: <token>` (no Turnstile token needed).

The pass is short-lived (e.g., 5 minutes) and has limited uses (e.g., 3). It's typically bound to IP & User-Agent to mitigate theft.

---

## Using the API (cURL examples)

You cannot generate a Turnstile token with cURL alone — tokens are produced by the browser widget. Use Swagger (or your web client) to get the first token/pass, then you can use the pass with cURL until it expires.

### With a short-lived pass

```bash
curl -X POST "http://127.0.0.1:8080/v1/translate" \
  -H "Content-Type: application/json" \
  -H "X-Turnstile-Pass: <paste-your-pass>" \
  -d '{
    "text":"こんにちは！",
    "sourceLang": null,
    "targetLang": "en"
  }'
```

### With a Turnstile token (no pass yet)

```bash
curl -X POST "http://127.0.0.1:8080/v1/translate" \
  -H "Content-Type: application/json" \
  -H "CF-Turnstile-Token: <token-from-widget>" \
  -d '{
    "text":"Guten Morgen",
    "sourceLang": null,
    "targetLang": "fr"
  }'

# On success, note the response header `X-Turnstile-Pass` for subsequent calls.
```

---

## Project Structure (high-level)

```
.
├─ Program.cs
├─ ai-translator-dotnet.csproj
├─ appsettings.json
├─ .env.local                        # not in source control
├─ Configuration/
│  ├─ DependencyInjection.cs         # Gemini & CORS registration
│  └─ Options/
│     ├─ GeminiOptions.cs
│     └─ (CorsOptions, TimeoutOptions, TurnstileOptions, etc.)
├─ Security/
│  └─ Turnstile/
│     ├─ DependencyInjection.cs      # AddTurnstile, AddTurnstilePass
│     ├─ TurnstileOptions.cs
│     ├─ TurnstileVerifier.cs
│     ├─ TurnstileEndpointFilter.cs  # enforces Turnstile / pass
│     ├─ TurnstileEndpoints.cs       # GET /\_turnstile/sitekey
│     └─ TurnstilePassService.cs     # issues & validates short passes
├─ Endpoints/
│  └─ TranslateEndpoints.cs          # POST /v1/translate
├─ Gemini/
│  ├─ GeminiClient.cs
│  ├─ GeminiRequestFactory.cs
│  └─ GeminiResponseMapper.cs
├─ Contracts/
│  ├─ TranslateRequest.cs
│  └─ TranslateResponse.cs
└─ Swagger/
   └─ index.html                     # custom Swagger UI (embedded)
```

---

## Environment & Configuration

### .env.local (secrets)

```env
TURNSTILE__SITEKEY=1x00000000000000000000AA
TURNSTILE__SECRETKEY=2x0000000000000000000000000000000AA
GEMINI_API_KEYS=your_gemini_key_1,your_gemini_key_2
```

### appsettings.json (non-secrets)

• `Turnstile.RequireOnTranslate`: set true in production
• `Turnstile.HeaderName`: the request header the client uses for the token (default `CF-Turnstile-Token`)
• `TurnstilePass`: pass options (TTL, max uses, binding, header)
• `Cors.AllowedOriginSuffixes`: array of allowed origins' host suffixes

### CORS

Make sure response exposes the pass header so browsers can read it:

```csharp
policy
  .AllowAnyMethod()
  .AllowAnyHeader()
  .AllowCredentials()
  .WithExposedHeaders("X-Turnstile-Pass");
```

---

## Build, Run & Deploy

### Local

```bash
dotnet restore
dotnet run

# Open http://127.0.0.1:8080/swagger
```

### Change port (optional)

```bash
ASPNETCORE_URLS=http://127.0.0.1:9090 dotnet run
```

### Docker (example snippet)

```dockerfile
# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /out

FROM base AS final
WORKDIR /app
COPY --from=build /out ./

# Set env via container secrets / orchestrator
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "AiTranslatorDotnet.dll"]
```

In production, put `TURNSTILE__SECRETKEY` and `GEMINI_API_KEYS` in environment variables / secrets (e.g., Azure App Settings, Kubernetes secrets). Never commit them.

---

## Troubleshooting

• **Swagger says "Turnstile: siteKey missing on server"**  
 Ensure `.env.local` is loaded and `TURNSTILE__SITEKEY` is correct (double underscore maps to `Turnstile:SiteKey`).

• **Requests succeed without verification**  
 Ensure `Turnstile.RequireOnTranslate = true` and `TURNSTILE__SECRETKEY` is set.

• **Second request fails / pass not applied**  
 Confirm your CORS exposes `X-Turnstile-Pass` and your client reads it from the first 200 OK response.

• **5000/8080 port already in use**  
 Change port via `ASPNETCORE_URLS` or free the port:  
 `lsof -nP -i4TCP:8080 -sTCP:LISTEN` then `kill -9 <PID>` (macOS).

---

## Security Notes

• Keep secrets in environment variables; `.env.local` should not be committed.
• The short pass is time-limited and usage-limited; keep it in memory only on the client.
• Consider rate limiting, quotas, deduplication/caching of identical translation requests, and logging/alerting for abuse detection.

---

## React / Next.js Client Example

See `docs/react-usage.md` for a complete client walkthrough (Turnstile widget, pass-aware API client, and component examples).

---

Happy translating! If you need a Next.js sample project or a Docker Compose, let me know and I'll add it.
