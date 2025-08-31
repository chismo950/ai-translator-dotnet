# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Building and Running
- `dotnet build` - Build the project
- `dotnet run` - Run the application (will start on http://localhost:8080)
- `dotnet run --launch-profile ai-translator-dotnet` - Run with specific profile

### Docker
- `docker build -t ai-translator .` - Build Docker image
- `docker run -e GEMINI_API_KEYS="key1,key2" -p 8080:8080 ai-translator` - Run containerized

### Testing Endpoints
Use the `ai-translator-dotnet.http` file with REST client extensions for testing:
- Health check: `GET /health`
- Translation: `POST /v1/translate` with JSON payload
- CORS testing: Includes requests with allowed and disallowed Origin headers

## Architecture Overview

This is a .NET 8 minimal API application that provides translation services via Google's Gemini API.

### Core Components

**Program.cs**: Application entry point using minimal API pattern with:
- Swagger/OpenAPI for development
- Global exception handling and request logging middleware
- Translation endpoint mapping

**Configuration System**: Centralized in `Configuration/` folder:
- `DependencyInjection.cs`: Service registration for Gemini client, CORS, and options
- `Options/GeminiOptions.cs`: Configuration model for Gemini API settings
- `Options/CorsOptions.cs`: Configuration model for CORS domain suffix allowlist
- `Options/TimeoutOptions.cs`: Centralized timeout configuration for all HTTP clients
- Configuration bound from appsettings.json "Gemini", "Cors", and "Timeout" sections

**API Key Management**: Sophisticated rotation system in `KeyManagement/`:
- `ApiKeyPool.cs`: Manages multiple API keys loaded from .env.local or environment variables
- `ApiKeyRotator.cs`: Handles automatic rotation and retry logic for failed requests
- Keys are expected in `GEMINI_API_KEYS` as comma/semicolon/newline separated values

**Gemini Integration**: Located in `Gemini/` folder:
- `GeminiClient.cs`: Main HTTP client for Gemini API communication
- `GeminiRequestFactory.cs`: Creates properly formatted requests for Gemini
- `GeminiResponseMapper.cs`: Parses and extracts translations from Gemini responses

**API Endpoints**: Minimal API endpoints in `Endpoints/`:
- `TranslateEndpoints.cs`: Maps `/v1/translate` POST endpoint
- Accepts `TranslateRequest` (text, sourceLang, targetLang) 
- Returns `TranslateResponse` with translated text

**Data Contracts**: DTOs in `Contracts/` folder:
- `TranslateRequest.cs`: Input model with auto-detection support for source language
- `TranslateResponse.cs`: Output model for translation results

**Middleware**: Custom middleware in `Middleware/`:
- `ExceptionHandlingMiddleware.cs`: Global exception handling
- `RequestLoggingMiddleware.cs`: Request/response logging

**Security Integration**: Cloudflare Turnstile protection in `Security/Turnstile/`:
- `TurnstileVerifier.cs`: Validates Turnstile tokens with Cloudflare API
- `TurnstileEndpointFilter.cs`: Endpoint filter for automatic token verification
- `TurnstileEndpoints.cs`: Exposes `GET /_turnstile/sitekey` for client applications
- `TurnstileOptions.cs`: Configuration for Turnstile integration
- Applied to `/v1/translate` endpoint via `.RequireTurnstile()` extension

### Key Configuration

The application expects API keys via:
1. `.env.local` file with `GEMINI_API_KEYS=key1,key2,key3`
2. Environment variable `GEMINI_API_KEYS`

Timeout settings in appsettings.json:
- HttpTimeoutSeconds: Global HTTP timeout for all outbound requests (default: 60s)

Gemini settings in appsettings.json:
- BaseUrl: Gemini API endpoint
- Model: AI model to use (default: gemini-2.0-flash)
- Temperature: Response randomness
- MaxInputChars: Input validation limit
- HttpTimeoutSeconds: (DEPRECATED) Override global timeout for Gemini requests only

CORS settings in appsettings.json:
- AllowedOriginSuffixes: Array of domain suffixes that are allowed for cross-origin requests
- Supports exact matches and subdomains (e.g., "example.com" allows both "example.com" and "api.example.com")

Turnstile settings in appsettings.json:
- RequireOnTranslate: Enforces Turnstile verification on translation endpoint
- HeaderName: HTTP header name for Turnstile tokens (default: "CF-Turnstile-Token")
- SecretKey: Server-side Turnstile secret (set via environment variable `TURNSTILE__SECRETKEY`)
- SiteKey: Client-side Turnstile key (set via environment variable `TURNSTILE__SITEKEY`)

### Request/Response Flow

1. Request hits `/v1/translate` endpoint
2. Turnstile verification (if enabled) validates token from request header
3. Basic validation of required fields
4. `GeminiClient.TranslateAsync()` called with automatic key rotation
5. Request formatted via `GeminiRequestFactory`
6. Response parsed via `GeminiResponseMapper`  
7. Structured response returned as `TranslateResponse`

The key rotation system automatically tries different API keys if requests fail, providing resilience against rate limiting or key issues.

### Client Integration

The `docs/react-usage.md` file provides a complete Next.js integration guide showing how to:
- Fetch Turnstile configuration from `/_turnstile/sitekey`
- Render Turnstile widget and handle token lifecycle
- Send requests with proper Turnstile token headers
- Handle token refresh and error scenarios