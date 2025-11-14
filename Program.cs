using System.Net;
using Microsoft.Extensions.Logging;
using DotNetEnv;

namespace AnimeMonitor;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 1) Load .env into environment variables
        // This is the equivalent of Python's load_dotenv()
        Env.Load();

        // 2) Configure logging based on LOG_LEVEL
        var logLevel = Config.GetLogLevelFromEnv();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(logLevel)
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                });
        });

        var logger = loggerFactory.CreateLogger("anime-monitor");

        try
        {
            // 3) Load config from environment
            var config = Config.LoadFromEnv(logger);

            // 4) Create HttpClient configured like Python's requests Session
            using var handler = new HttpClientHandler
            {
                CookieContainer = new System.Net.CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("anime-monitor/1.0 (+github.com/your-org) HttpClient");

            // 5) Instantiate services
            var scraperLogger = loggerFactory.CreateLogger<AnimeScraper>();
            var qbLogger = loggerFactory.CreateLogger<QBittorrentClient>();
            var trackerLogger = loggerFactory.CreateLogger<EpisodeTracker>();

            var scraper = new AnimeScraper(httpClient, config, scraperLogger);
            var qbClient = new QBittorrentClient(httpClient, config, qbLogger);
            var tracker = new EpisodeTracker(config.EpisodeFile, trackerLogger);

            // 6) Load next episode from JSON
            int episode = tracker.LoadNextEpisode();

            // 7) Monitoring loop
            await MonitorAsync(scraper, qbClient, tracker, config, logger, episode);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "[FATAL] Erro não tratado. Encerrando aplicação.");
        }
    }

    private static async Task MonitorAsync(
        AnimeScraper scraper,
        QBittorrentClient qbClient,
        EpisodeTracker tracker,
        Config config,
        ILogger logger,
        int episode)
    {
        var cancellationToken = CancellationToken.None;

        while (true)
        {
            try
            {
                var pageUrl = await scraper.FindEpisodePageAsync(episode, cancellationToken);

                if (pageUrl != null)
                {
                    var magnet = await scraper.ExtractMagnetAsync(pageUrl, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(magnet))
                    {
                        logger.LogInformation("[INFO] Enviando para o qBittorrent: {Magnet}", magnet);

                        await qbClient.AuthenticateAsync(cancellationToken);
                        await qbClient.AddMagnetAsync(magnet, cancellationToken);

                        tracker.SaveNextEpisode(episode + 1);

                        logger.LogInformation("[OK] Processo finalizado para episódio {Episode}.", episode);
                        break;
                    }
                    else
                    {
                        logger.LogWarning("[ERROR] Magnet não encontrado na página do episódio.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ERROR] Exceção no ciclo principal.");
            }

            var delaySeconds = config.CheckIntervalSeconds;
            logger.LogInformation("[WAIT] Tentando novamente em {Minutes:F1} minutos...", delaySeconds / 60.0);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }
    }
}