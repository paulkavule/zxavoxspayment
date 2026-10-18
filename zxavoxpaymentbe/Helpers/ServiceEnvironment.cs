namespace zxavoxpaymentbe.Helpers;

/// <summary>Loads .env into the process and composes the database connection from DB_* variables.</summary>
public static class ServiceEnvironment
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
            return;
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('=', 2);
            if (line.StartsWith('#') || parts.Length != 2)
                continue;
            var value = parts[1].Trim();
            if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
                value = value[1..^1];
            Environment.SetEnvironmentVariable(parts[0].Trim(), value);
        }
    }

    public static string DatabaseConnection() =>
        $"Host={Value("DB_HOST", "localhost")};Port={Value("DB_PORT", "5432")};Database={Value("DB_NAME", "zxavoxpayments")};" +
        $"Username={Value("DB_UNAME", "postgres")};Password={Value("DB_PWORD", "postgres")}";

    public static string Value(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
