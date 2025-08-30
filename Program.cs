using AiTranslatorDotnet.Configuration;
using AiTranslatorDotnet.Endpoints;
using AiTranslatorDotnet.Middleware; // for UseExceptionHandling and UseRequestLogging

var builder = WebApplication.CreateBuilder(args);

// Add Swagger (useful for local testing and API docs)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Gemini translator services (options + HttpClient + GeminiClient)
builder.Services.AddGeminiTranslator(builder.Configuration);

// Register CORS configuration
builder.Services.AddCorsConfiguration(builder.Configuration);

var app = builder.Build();

// Global exception handling should be as early as possible
app.UseExceptionHandling();

// Request logging after exception handling
app.UseRequestLogging();

// Enable CORS
app.UseCors();

// Enable Swagger in all environments for testing
app.UseSwagger();
app.UseSwaggerUI();

// NOTE:
// - Do not force HTTPS redirection since TLS will terminate at your reverse proxy.
// app.UseHttpsRedirection();  // intentionally omitted

// Map translation endpoint(s)
app.MapTranslateEndpoints();

// Simple health check
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

// Start the app
app.Run();