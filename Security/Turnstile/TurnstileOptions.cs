namespace AiTranslatorDotnet.Security.Turnstile;

/// <summary>
/// Options for Cloudflare Turnstile.
/// Bind from configuration section "Turnstile":
/// {
///   "Turnstile": {
///     "SiteKey": "1x00000000000000000000AA",
///     "SecretKey": "2x0000000000000000000000000000000AA"
///   }
/// }
/// </summary>
public sealed class TurnstileOptions
{
    /// <summary>
    /// Public site key used by the browser widget.
    /// </summary>
    public string SiteKey { get; set; } = string.Empty;

    /// <summary>
    /// Secret key used by the server for verification.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional: whether to require Turnstile on /v1/translate.
    /// You can toggle this per environment.
    /// </summary>
    public bool RequireOnTranslate { get; set; } = true;

    /// <summary>
    /// Name of the header where the client (Swagger/React) will send the token.
    /// Default: "CF-Turnstile-Token".
    /// </summary>
    public string HeaderName { get; set; } = "CF-Turnstile-Token";
}