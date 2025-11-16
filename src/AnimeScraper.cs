using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AnimeMonitor;

public sealed class AnimeScraper
{
    private readonly HttpClient _httpClient;
    private readonly string _searchUrl;
    private readonly string _animePattern;
    private readonly SimpleLogger _logger;

    public AnimeScraper(HttpClient httpClient, Config config, SimpleLogger logger)
    {
        _httpClient = httpClient;
        _searchUrl = config.SearchUrl;
        _animePattern = config.AnimePattern;
        _logger = logger;
    }

    private async Task<HtmlDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var delay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _logger.Debug($"GET {url} (tentativa {attempt})");

                using var resp = await _httpClient.GetAsync(url, cancellationToken);

                if ((int)resp.StatusCode == 429 ||
                    (resp.StatusCode >= HttpStatusCode.InternalServerError &&
                     resp.StatusCode <= HttpStatusCode.HttpVersionNotSupported))
                {
                    if (attempt >= maxRetries)
                    {
                        resp.EnsureSuccessStatusCode();
                    }
                }

                resp.EnsureSuccessStatusCode();
                var html = await resp.Content.ReadAsStringAsync(cancellationToken);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return doc;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warn(ex, $"Falha na requisição, tentando novamente em {delay.TotalMilliseconds}ms");
                await Task.Delay(delay, cancellationToken);
                delay += delay;
            }
        }
    }

    private async Task<IReadOnlyList<HtmlNode>> GetTorrentRowsAsync(CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(_searchUrl, cancellationToken);
        var nodes = doc.DocumentNode.SelectNodes("//table[contains(@class,'torrent-list')]/tbody/tr");
        return nodes?.ToList() ?? new List<HtmlNode>();
    }

    public async Task<string?> FindEpisodePageAsync(int episode, CancellationToken cancellationToken)
    {
        _logger.Info($"Procurando episódio {episode:00}");

        var patternRaw = BuildPatternRaw(episode);
        Regex? pattern = null;

        try
        {
            pattern = new Regex(patternRaw, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            _logger.Warn("Regex ANIME_PADRAO inválida. Usando busca simples.");
        }

        var rows = await GetTorrentRowsAsync(cancellationToken);

        foreach (var row in rows)
        {
            var titleAnchor = row.SelectSingleNode(".//td[2]//a[starts-with(@href,'/view/') and not(contains(@class,'comments'))]");
            if (titleAnchor is null)
                continue;

            var title = titleAnchor.InnerText.Trim();
            var href = titleAnchor.GetAttributeValue("href", "");

            if (string.IsNullOrEmpty(href))
                continue;

            bool isMatch =
                (pattern != null && pattern.IsMatch(title)) ||
                (pattern == null && title.Contains(patternRaw, StringComparison.OrdinalIgnoreCase));

            if (isMatch)
            {
                _logger.Info($"Encontrado: {title}");
                return $"https://nyaa.si{href}";
            }
        }

        _logger.Info($"Episódio {episode:00} não encontrado.");
        return null;
    }

    public async Task<string?> ExtractMagnetAsync(string pageUrl, CancellationToken cancellationToken)
    {
        _logger.Info($"Acessando página do magnet: {pageUrl}");

        var doc = await GetDocumentAsync(pageUrl, cancellationToken);

        var magnetAnchor = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href,'magnet:?xt=urn:btih:')]");
        var link = magnetAnchor?.GetAttributeValue("href", "");

        if (string.IsNullOrEmpty(link))
        {
            _logger.Warn("Magnet não encontrado na página.");
            return null;
        }

        return link;
    }

    private string BuildPatternRaw(int episode)
    {
        var ep2 = episode.ToString("D2");
        var ep = episode.ToString();

        var raw = _animePattern;
        raw = raw.Replace("{ep:02d}", ep2, StringComparison.OrdinalIgnoreCase);
        raw = raw.Replace("{ep}", ep, StringComparison.OrdinalIgnoreCase);

        if (raw.Contains("{0}", StringComparison.Ordinal))
        {
            raw = string.Format(raw, episode);
        }

        return raw;
    }
}