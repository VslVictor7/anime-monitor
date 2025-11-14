using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace AnimeMonitor;

public sealed class AnimeScraper
{
    private readonly HttpClient _httpClient;
    private readonly string _searchUrl;
    private readonly string _animePattern;
    private readonly ILogger<AnimeScraper> _logger;

    public AnimeScraper(HttpClient httpClient, Config config, ILogger<AnimeScraper> logger)
    {
        _httpClient = httpClient;
        _searchUrl = config.SearchUrl;
        _animePattern = config.AnimePattern;
        _logger = logger;
    }

    private async Task<HtmlDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        // Basic retry similar to Python’s requests + Retry adapter
        const int maxRetries = 3;
        var delay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _logger.LogDebug("GET {Url} (tentativa {Attempt})", url, attempt);
                using var resp = await _httpClient.GetAsync(url, cancellationToken);

                if ((int)resp.StatusCode == 429 ||
                    (resp.StatusCode >= HttpStatusCode.InternalServerError &&
                     resp.StatusCode <= HttpStatusCode.HttpVersionNotSupported))
                {
                    // Transient error – retry
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
                _logger.LogWarning(ex, "[WARN] Falha na requisição, tentando novamente em {Delay}ms", delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
                delay += delay; // backoff
            }
        }
    }

    private async Task<IReadOnlyList<HtmlNode>> GetTorrentRowsAsync(CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(_searchUrl, cancellationToken);

        // Equivalent to: soup.select("table.torrent-list tbody tr")
        var nodes = doc.DocumentNode.SelectNodes("//table[contains(@class,'torrent-list')]/tbody/tr");
        return nodes?.ToList() ?? new List<HtmlNode>();
    }

    public async Task<string?> FindEpisodePageAsync(int episode, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[INFO] Procurando episódio {Episode:00}", episode);

        var patternRaw = BuildPatternRaw(episode);
        Regex? pattern = null;

        try
        {
            pattern = new Regex(patternRaw, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("[ERROR] Regex ANIME_PADRAO inválida. Usando busca simples.");
        }

        var rows = await GetTorrentRowsAsync(cancellationToken);

        foreach (var row in rows)
        {
            // Equivalent to CSS selector: td:nth-of-type(2) a[href^='/view/']:not(.comments)
            var titleAnchor = row.SelectSingleNode(".//td[2]//a[starts-with(@href,'/view/') and not(contains(@class,'comments'))]");
            if (titleAnchor is null)
            {
                continue;
            }

            var title = titleAnchor.InnerText.Trim();
            var href = titleAnchor.GetAttributeValue("href", "");

            if (string.IsNullOrEmpty(href))
            {
                continue;
            }

            bool isMatch =
                (pattern != null && pattern.IsMatch(title)) ||
                (pattern == null && title.Contains(patternRaw, StringComparison.OrdinalIgnoreCase));

            if (isMatch)
            {
                _logger.LogInformation("[OK] Encontrado: {Title}", title);
                var fullUrl = $"https://nyaa.si{href}";
                return fullUrl;
            }
        }

        _logger.LogInformation("[INFO] Episódio {Episode:00} não encontrado na lista de torrents.", episode);
        return null;
    }

    public async Task<string?> ExtractMagnetAsync(string pageUrl, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[INFO] Acessando: {Url}", pageUrl);

        var doc = await GetDocumentAsync(pageUrl, cancellationToken);

        // Find <a href="magnet:?xt=urn:btih:...">
        var magnetAnchor = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href,'magnet:?xt=urn:btih:')]");
        var link = magnetAnchor?.GetAttributeValue("href", null);

        if (link is null)
        {
            _logger.LogWarning("[ERROR] Magnet não encontrado na página.");
        }

        return link;
    }

    private string BuildPatternRaw(int episode)
    {
        // Try to be compatible with Python-style placeholders:
        // - {ep:02d}
        // - {ep}
        // If none present, we just return the original pattern unchanged.
        var ep2 = episode.ToString("D2");
        var ep = episode.ToString();

        var raw = _animePattern;
        raw = raw.Replace("{ep:02d}", ep2, StringComparison.OrdinalIgnoreCase);
        raw = raw.Replace("{ep}", ep, StringComparison.OrdinalIgnoreCase);

        // Also allow standard C# string.Format with {0}
        if (raw.Contains("{0}", StringComparison.Ordinal))
        {
            raw = string.Format(raw, episode);
        }

        return raw;
    }
}