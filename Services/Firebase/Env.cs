using System.Reflection;

namespace SAFETY_STEPS.FireBase;

/// <summary>
/// Reads secrets from .env.local, which is embedded into the app at build time
/// and is NOT committed to git. Copy .env.example to .env.local and fill it in.
/// </summary>
public static class Env
{
    private static readonly Lazy<Dictionary<string, string>> _values = new(Load);

    public static string ServiceAccountEmail => Get("FIREBASE_SERVICE_ACCOUNT_EMAIL");
    public static string ServiceAccountPrivateKey => Get("FIREBASE_SERVICE_ACCOUNT_PRIVATE_KEY");

    public static string Get(string key) =>
        _values.Value.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Missing '{key}' in .env.local. Copy .env.example to .env.local and fill it in, then rebuild.");

    private static Dictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("env.local");
        if (stream is null)
            return values;

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];

            // Private keys are stored on one line with literal \n sequences
            values[key] = value.Replace("\\n", "\n");
        }

        return values;
    }
}
