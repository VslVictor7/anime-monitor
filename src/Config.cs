using Microsoft.Extensions.Logging;

namespace AnimeMonitor;

public sealed class Config
{
    public string AnimePattern { get; }
    public string SearchUrl { get; }
    public string EpisodeFile { get; }

    public string QbUrl { get; }
    public string QbUsername { get; }
    public string QbPassword { get; }
    public string SavePath { get; }

    public int CheckIntervalSeconds { get; }
    public int HttpTimeoutSeconds { get; }

    private Config(
        string animePattern,
        string searchUrl,
        string episodeFile,
        string qbUrl,
        string qbUsername,
        string qbPassword,
        string savePath,
        int checkIntervalSeconds,
        int httpTimeoutSeconds)
    {
        AnimePattern = animePattern;
        SearchUrl = searchUrl;
        EpisodeFile = episodeFile;
        QbUrl = qbUrl.TrimEnd('/');
        QbUsername = qbUsername;
        QbPassword = qbPassword;
        SavePath = savePath;
        CheckIntervalSeconds = checkIntervalSeconds;
        HttpTimeoutSeconds = httpTimeoutSeconds;
    }

    public static Config LoadFromEnv(ILogger logger)
    {
        var missing = new List<string>();

        string? Need(string name)
        {
            var val = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(val))
            {
                missing.Add(name);
            }
            return val;
        }

        int ReadInt(string name, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        var animePattern = Need("ANIME_PADRAO") ?? string.Empty;
        var searchUrl = Need("SEARCH_URL") ?? string.Empty;
        var episodeFile = Need("EPISODIO_FILE") ?? string.Empty;

        var qbUrl = Need("QB_URL") ?? string.Empty;
        var qbUsername = Need("QB_USERNAME") ?? string.Empty;
        var qbPassword = Need("QB_PASSWORD") ?? string.Empty;
        var savePath = Need("SAVE_PATH") ?? string.Empty;

        int checkInterval = ReadInt("CHECK_INTERVAL", 300);
        int httpTimeout = ReadInt("HTTP_TIMEOUT", 15);

        if (missing.Count > 0)
        {
            var msg = $"[ERROR] Variáveis de ambiente ausentes: {string.Join(", ", missing)}";
            logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("Configuração carregada com sucesso. CHECK_INTERVAL={CheckInterval}s, HTTP_TIMEOUT={HttpTimeout}s",
            checkInterval, httpTimeout);

        return new Config(
            animePattern,
            searchUrl,
            episodeFile,
            qbUrl,
            qbUsername,
            qbPassword,
            savePath,
            checkInterval,
            httpTimeout);
    }

    public static LogLevel GetLogLevelFromEnv()
    {
        var levelRaw = (Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information").Trim().ToUpperInvariant();

        return levelRaw switch
        {
            "TRACE" => LogLevel.Trace,
            "DEBUG" => LogLevel.Debug,
            "INFORMATION" or "INFO" => LogLevel.Information,
            "WARNING" or "WARN" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "CRITICAL" or "FATAL" => LogLevel.Critical,
            _ => LogLevel.Information
        };
    }
}
