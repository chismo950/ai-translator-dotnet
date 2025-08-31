using System.Reflection;
using AiTranslatorDotnet.Configuration;
using AiTranslatorDotnet.Endpoints;
using AiTranslatorDotnet.Middleware; // for UseExceptionHandling and UseRequestLogging
using AiTranslatorDotnet.Security.Turnstile; // for AddTurnstile + MapTurnstileEndpoints

using DotNetEnv;
Env.TraversePath().Load(".env.local");

var builder = WebApplication.CreateBuilder(args);

// Add Swagger (useful for local testing and API docs)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Gemini translator services (options + HttpClient + GeminiClient)
builder.Services.AddGeminiTranslator(builder.Configuration);

// Register CORS configuration (your extension)
builder.Services.AddCorsConfiguration(builder.Configuration);

// Register Cloudflare Turnstile services
builder.Services.AddTurnstile(builder.Configuration);

// Register Turnstile Pass services
builder.Services.AddTurnstilePass(builder.Configuration);

var app = builder.Build();

// Global exception handling should be as early as possible
app.UseExceptionHandling();

// Request logging after exception handling
app.UseRequestLogging();

// Enable CORS
app.UseCors();

// Enable Swagger and use embedded index.html
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    // The logical name must match the <EmbeddedResource LogicalName="..."> in your .csproj
    c.IndexStream = () => Assembly.GetExecutingAssembly()
        .GetManifestResourceStream("AiTranslatorDotnet.Swagger.index.html");
});

// NOTE:
// - Do not force HTTPS redirection since TLS will terminate at your reverse proxy.
// app.UseHttpsRedirection();  // intentionally omitted

// Map endpoints
app.MapTranslateEndpoints();
app.MapTurnstileEndpoints(); // exposes GET /_turnstile/sitekey for the Swagger page

// Simple health check
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

app.MapGet("/", () => "v2");

// Start the app
app.Run();