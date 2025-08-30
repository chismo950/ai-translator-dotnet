using System.Security.Cryptography;

namespace AiTranslatorDotnet.KeyManagement
{
    /// <summary>
    /// Provides a shuffled API Key sequence for "single translation requests".
    /// Usage:
    ///   var rotator = ApiKeyRotator.FromEnv();
    ///   while (rotator.TryGetNext(out var key)) { ... try using key to call ... }
    /// If one key fails, continue with TryGetNext to get the next one until exhausted.
    /// </summary>
    public sealed class ApiKeyRotator
    {
        private readonly string[] _keys;
        private int _cursor = 0;

        private ApiKeyRotator(IEnumerable<string> keys)
        {
            _keys = keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            ShuffleInPlace(_keys);
        }

        /// <summary>
        /// Reads GEMINI_API_KEYS from .env.local (or environment variables) and builds a shuffled sequence.
        /// </summary>
        /// <param name="searchStartDirectory">Starting search directory (defaults to current directory), searches upward for .env.local</param>
        /// <param name="envFileName">Defaults to ".env.local"</param>
        /// <param name="envVarName">Defaults to "GEMINI_API_KEYS"</param>
        public static ApiKeyRotator FromEnv(
            string? searchStartDirectory = null,
            string envFileName = ".env.local",
            string envVarName = "GEMINI_API_KEYS")
        {
            var keys = LoadKeys(searchStartDirectory ?? Directory.GetCurrentDirectory(), envFileName, envVarName);
            if (keys.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No API keys found. Please set {envVarName} in {envFileName} or as an environment variable.");
            }
            return new ApiKeyRotator(keys);
        }

        /// <summary>
        /// Gets the next key. Returns false if exhausted.
        /// </summary>
        public bool TryGetNext(out string key)
        {
            if (_cursor < _keys.Length)
            {
                key = _keys[_cursor++];
                return true;
            }
            key = string.Empty;
            return false;
        }

        /// <summary>
        /// The number of keys in this sequence.
        /// </summary>
        public int Count => _keys.Length;

        private static void ShuffleInPlace<T>(T[] array)
        {
            using var rng = RandomNumberGenerator.Create();
            for (int n = array.Length; n > 1; n--)
            {
                // Generate uniform random index in [0, n)
                Span<byte> bytes = stackalloc byte[4];
                rng.GetBytes(bytes);
                int k = BitConverter.ToInt32(bytes) & int.MaxValue;
                int j = k % n;

                // Swap j with n-1
                (array[j], array[n - 1]) = (array[n - 1], array[j]);
            }
        }

        private static List<string> LoadKeys(string startDir, string envFileName, string envVarName)
        {
            var keys = new List<string>();

            // 1) .env.local
            var envPath = FindUpwards(startDir, envFileName);
            if (envPath is not null && File.Exists(envPath))
            {
                var kv = ParseEnvFile(envPath);
                if (kv.TryGetValue(envVarName, out var rawFromFile))
                    keys.AddRange(SplitKeys(rawFromFile));
            }

            // 2) Fallback: system environment variables
            if (keys.Count == 0)
            {
                var rawFromEnv = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrWhiteSpace(rawFromEnv))
                    keys.AddRange(SplitKeys(rawFromEnv));
            }

            return keys;
        }

        private static string? FindUpwards(string startDir, string fileName)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
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
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                // Remove possible quotes
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
            // Support comma, semicolon, and newline separation
            return raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0);
        }
    }
}