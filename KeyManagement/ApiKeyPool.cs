using System.Security.Cryptography;

namespace AiTranslatorDotnet.KeyManagement
{
    /// <summary>
    /// Loads and holds a stable set of API keys in memory.
    /// Intended for app-lifetime reuse and diagnostics (e.g., Count, LoadedAt).
    /// You can request a shuffled copy per request via GetShuffledKeys().
    ///
    /// NOTE: GeminiClient currently uses ApiKeyRotator.FromEnv() directly.
    /// This pool is provided for future use (DI + health tracking) or if you prefer
    /// to avoid re-reading .env.local on each request.
    /// </summary>
    public sealed class ApiKeyPool
    {
        private readonly string[] _keys;

        private ApiKeyPool(IEnumerable<string> keys, DateTimeOffset loadedAt, string source)
        {
            _keys = keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            LoadedAt = loadedAt;
            Source = source;
        }

        /// <summary>
        /// When the keys were last loaded.
        /// </summary>
        public DateTimeOffset LoadedAt { get; }

        /// <summary>
        /// Human-readable source of the keys (e.g., absolute path of .env.local or "ENV").
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Number of distinct keys available.
        /// </summary>
        public int Count => _keys.Length;

        /// <summary>
        /// Exposes the keys as a read-only snapshot (in their canonical, de-duplicated order).
        /// </summary>
        public IReadOnlyList<string> Keys => _keys;

        /// <summary>
        /// Returns a new shuffled copy of the keys for per-request rotation.
        /// </summary>
        public string[] GetShuffledKeys()
        {
            var copy = _keys.ToArray();
            ShuffleInPlace(copy);
            return copy;
        }

        /// <summary>
        /// Loads keys from .env.local (searched upward from current directory),
        /// falling back to the environment variable GEMINI_API_KEYS if the file is not present.
        /// </summary>
        public static ApiKeyPool FromEnv(
            string? searchStartDirectory = null,
            string envFileName = ".env.local",
            string envVarName = "GEMINI_API_KEYS")
        {
            var startDir = searchStartDirectory ?? Directory.GetCurrentDirectory();
            var envPath = FindUpwards(startDir, envFileName);

            if (envPath is not null && File.Exists(envPath))
            {
                var kv = ParseEnvFile(envPath);
                if (kv.TryGetValue(envVarName, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    var keys = SplitKeys(raw);
                    EnsureAny(keys, envVarName, envPath);
                    return new ApiKeyPool(keys, DateTimeOffset.UtcNow, envPath);
                }
            }

            // Fallback to process environment
            var fromEnv = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                var keys = SplitKeys(fromEnv);
                EnsureAny(keys, envVarName, source: "ENV");
                return new ApiKeyPool(keys, DateTimeOffset.UtcNow, "ENV");
            }

            throw new InvalidOperationException(
                $"No API keys found. Set {envVarName} in {envFileName} or as an environment variable.");
        }

        private static void EnsureAny(IEnumerable<string> keys, string varName, string source)
        {
            if (!keys.Any())
                throw new InvalidOperationException(
                    $"Variable {varName} is present in {source} but contains no usable keys.");
        }

        private static string? FindUpwards(string startDir, string fileName)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static Dictionary<string, string> ParseEnvFile(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();

                // skip comments and blank lines
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                // strip quotes if present
                if (value.Length >= 2 &&
                    ((value.StartsWith('"') && value.EndsWith('"')) ||
                     (value.StartsWith('\'') && value.EndsWith('\''))))
                {
                    value = value[1..^1];
                }

                dict[key] = value;
            }
            return dict;
        }

        private static IEnumerable<string> SplitKeys(string raw)
        {
            // Supports comma, semicolon, and newlines as separators.
            return raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0);
        }

        private static void ShuffleInPlace<T>(T[] array)
        {
            using var rng = RandomNumberGenerator.Create();
            Span<byte> bytes = stackalloc byte[4];
            for (int n = array.Length; n > 1; n--)
            {
                rng.GetBytes(bytes);
                int k = BitConverter.ToInt32(bytes) & int.MaxValue;
                int j = k % n;

                (array[j], array[n - 1]) = (array[n - 1], array[j]);
            }
        }
    }
}