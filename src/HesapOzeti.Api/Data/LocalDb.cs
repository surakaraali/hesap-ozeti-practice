namespace HesapOzeti.Api.Data;

/// <summary>Builds the connection string for the SQL Server in docker-compose.yml. The SA password lives in .env.</summary>
public static class LocalDb
{
    public const string DatabaseName = "HesapOzetiPractice";

    /// <summary>Reads KEY=VALUE lines from the nearest .env above the working directory. Real environment variables win.</summary>
    public static void LoadDotEnv()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadAllLines(path))
            {
                var eq = line.IndexOf('=');
                if (line.StartsWith('#') || eq < 1) continue;
                var key = line[..eq].Trim();
                if (Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, line[(eq + 1)..].Trim());
            }
            return;
        }
    }

    public static string ConnectionString(string database = DatabaseName)
    {
        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")
            ?? throw new InvalidOperationException("MSSQL_SA_PASSWORD is not set. Copy .env.example to .env.");
        return $"Server=localhost,14334;Database={database};User Id=sa;Password={password};TrustServerCertificate=True";
    }
}
