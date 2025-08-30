# ---- Base image: ASP.NET Core runtime (no SDK) ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
# Bind Kestrel to 0.0.0.0:8080 (TLS is expected to terminate at your reverse proxy)
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# ---- Build stage: use .NET SDK to restore/build/publish ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything (the solution is a single project in this layout)
COPY . .

# Restore and publish
RUN dotnet restore "./ai-translator-dotnet.csproj"
RUN dotnet publish "./ai-translator-dotnet.csproj" -c Release -o /out /p:UseAppHost=false

# ---- Final image: copy published app from build stage ----
FROM base AS final
WORKDIR /app
COPY --from=build /out ./

# NOTE:
# - Do NOT bake secrets into the image. Provide GEMINI_API_KEYS at runtime:
#   1) As environment variable:
#        docker run -e GEMINI_API_KEYS="key1,key2,key3" -p 8080:8080 image
#   2) Or mount .env.local into /app:
#        docker run -v $(pwd)/.env.local:/app/.env.local:ro -p 8080:8080 image
#
# Entry point
ENTRYPOINT ["dotnet", "AiTranslatorDotnet.dll"]