using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AnimeMonitor;

public sealed class QBittorrentClient
{
    private readonly HttpClient _httpClient;
    private readonly Config _config;
    private readonly ILogger<QBittorrentClient> _logger;

    public QBittorrentClient(HttpClient httpClient, Config config, ILogger<QBittorrentClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        var url = $"{_config.QbUrl}/api/v2/auth/login";

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string?, string?>("username", _config.QbUsername),
            new KeyValuePair<string?, string?>("password", _config.QbPassword),
        });

        _logger.LogInformation("[INFO] Autenticando no qBittorrent…");

        using var resp = await _httpClient.PostAsync(url, content, cancellationToken);

        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (resp.StatusCode != HttpStatusCode.OK || !body.Contains("Ok.", StringComparison.OrdinalIgnoreCase))
        {
            var msg = $"[ERROR] Falha ao autenticar no qBittorrent: {(int)resp.StatusCode} - {body}";
            _logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        _logger.LogInformation("[AUTH_OK] Login bem-sucedido no qBittorrent");
    }

    public async Task AddMagnetAsync(string magnetLink, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_config.SavePath);

        var url = $"{_config.QbUrl}/api/v2/torrents/add";

        var payload = new[]
        {
            new KeyValuePair<string?, string?>("urls", magnetLink),
            new KeyValuePair<string?, string?>("paused", "false"),
            new KeyValuePair<string?, string?>("savepath", _config.SavePath),
        };

        var content = new FormUrlEncodedContent(payload);

        _logger.LogInformation("[INFO] Enviando magnet ao qBittorrent (diretório: {SavePath})", _config.SavePath);

        using var resp = await _httpClient.PostAsync(url, content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var msg = $"[ERROR] Erro ao enviar torrent: {(int)resp.StatusCode} - {body}";
            _logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        _logger.LogInformation("[OK] Magnet enviado ao qBittorrent");
    }
}