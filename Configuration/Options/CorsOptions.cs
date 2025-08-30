namespace AiTranslatorDotnet.Configuration.Options;

public class CorsOptions
{
    public const string SectionName = "Cors";
    
    public string[] AllowedOriginSuffixes { get; set; } = Array.Empty<string>();
}