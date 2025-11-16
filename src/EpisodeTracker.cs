using System.Text.Json;

namespace AnimeMonitor;

public sealed class EpisodeState
{
    public int NextEpisode { get; set; } = 1;
}

public sealed class EpisodeTracker
{
    private readonly string _episodeFile;
    private readonly SimpleLogger _logger;

    public EpisodeTracker(string episodeFile, SimpleLogger logger)
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
                _logger.Info("Arquivo de episódio não encontrado. Iniciando do episódio 1.");
                return 1;
            }

            var json = File.ReadAllText(_episodeFile);

            var opts = new JsonSerializerOptions
            {
                TypeInfoResolver = AnimeMonitorJsonContext.Default
            };

            var state = JsonSerializer.Deserialize(json, AnimeMonitorJsonContext.Default.EpisodeState);

            int n = state?.NextEpisode ?? 1;
            if (n < 1) n = 1;

            _logger.Info($"Próximo episódio carregado: {n}");
            return n;
        }
        catch (JsonException)
        {
            _logger.Warn("Arquivo de episódio inválido ou corrompido. Iniciando do episódio 1.");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao carregar arquivo de episódio: " + ex.ToString());
            return 1;
        }
    }

    public void SaveNextEpisode(int episode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_episodeFile) ?? ".");

        var tmpFile = Path.Combine(
            Path.GetDirectoryName(_episodeFile) ?? ".",
            $".episodio_{Guid.NewGuid():N}.json"
        );

        var state = new EpisodeState { NextEpisode = episode };

        try
        {
            var opts = new JsonSerializerOptions
            {
                TypeInfoResolver = AnimeMonitorJsonContext.Default,
                WriteIndented = false
            };

            var json = JsonSerializer.Serialize(state, AnimeMonitorJsonContext.Default.EpisodeState);

            using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }

            File.Copy(tmpFile, _episodeFile, overwrite: true);
            File.Delete(tmpFile);

            _logger.Info($"Próximo episódio salvo: {episode}");
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao salvar episódio no arquivo: " + ex.ToString());

            try
            {
                if (File.Exists(tmpFile))
                    File.Delete(tmpFile);
            }
            catch { }
        }
    }
}