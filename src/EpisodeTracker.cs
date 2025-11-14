using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AnimeMonitor;

public sealed class EpisodeTracker
{
    private readonly string _episodeFile;
    private readonly ILogger<EpisodeTracker> _logger;

    private sealed class EpisodeState
    {
        [JsonPropertyName("proximo_episodio")]
        public int NextEpisode { get; set; } = 1;
    }

    public EpisodeTracker(string episodeFile, ILogger<EpisodeTracker> logger)
    {
        _episodeFile = episodeFile;
        _logger = logger;
    }

    public int LoadNextEpisode()
    {
        try
        {
            if (!File.Exists(_episodeFile))
            {
                _logger.LogInformation("[INFO] Arquivo de episódio não encontrado. Iniciando do episódio 1.");
                return 1;
            }

            var json = File.ReadAllText(_episodeFile);
            var state = JsonSerializer.Deserialize<EpisodeState>(json);

            int n = state?.NextEpisode ?? 1;
            if (n < 1) n = 1;

            _logger.LogInformation("[INFO] Próximo episódio carregado: {Episode}", n);
            return n;
        }
        catch (JsonException)
        {
            _logger.LogWarning("[ERROR] Arquivo de episódio inválido/corrompido. Iniciando do episódio 1.");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERROR] Falha ao carregar arquivo de episódio. Iniciando do episódio 1.");
            return 1;
        }
    }

    public void SaveNextEpisode(int episode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_episodeFile) ?? ".");

        var tmpFile = Path.Combine(
            Path.GetDirectoryName(_episodeFile) ?? ".",
            $".episodio_{Guid.NewGuid():N}.json");

        var state = new EpisodeState
        {
            NextEpisode = episode
        };

        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(true); // fsync-like
            }

            File.Copy(tmpFile, _episodeFile, overwrite: true);
            File.Delete(tmpFile);

            _logger.LogInformation("[OK] Próximo episódio salvo: {Episode}", episode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERROR] Falha ao salvar episódio no arquivo.");
            try
            {
                if (File.Exists(tmpFile))
                {
                    File.Delete(tmpFile);
                }
            }
            catch
            {
                // ignore best-effort cleanup
            }
        }
    }
}