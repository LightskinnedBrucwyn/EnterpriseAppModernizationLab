using System.Text.Json;
using System.Text.RegularExpressions;

namespace BatHouseholdHub.Services;

/// <summary>Pulls product name/price/image straight out of a page's Open Graph meta tags
/// or embedded JSON-LD Product schema. Most storefronts (Shopify, Amazon, big-box retailers)
/// include one or both, so this covers a lot of links with zero LLM tokens spent.</summary>
public class OpenGraphScraperService(HttpClient http, ILogger<OpenGraphScraperService> logger)
{
    static readonly Regex MetaTag = new("""<meta\s+(?:property|name)=["'](?<key>[^"']+)["']\s+content=["'](?<value>[^"']*)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MetaTagReversed = new("""<meta\s+content=["'](?<value>[^"']*)["']\s+(?:property|name)=["'](?<key>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex JsonLdBlock = new("""<script[^>]+type=["']application/ld\+json["'][^>]*>(?<json>.*?)</script>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task<ProductLookupResult> ScrapeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; BatHouseholdHub/1.0)");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new ProductLookupResult { Success = false, Error = $"Page returned {(int)response.StatusCode}." };

            var html = await response.Content.ReadAsStringAsync(ct);
            var meta = ReadMetaTags(html);
            var (name, price, image) = ReadFromMeta(meta);

            if (string.IsNullOrWhiteSpace(name) || price <= 0)
            {
                var (ldName, ldPrice, ldImage) = ReadFromJsonLd(html);
                name = string.IsNullOrWhiteSpace(name) ? ldName : name;
                price = price <= 0 ? ldPrice : price;
                image = string.IsNullOrWhiteSpace(image) ? ldImage : image;
            }

            if (string.IsNullOrWhiteSpace(name) && price <= 0 && string.IsNullOrWhiteSpace(image))
                return new ProductLookupResult { Success = false, Error = "No structured product data on this page." };

            return new ProductLookupResult { Success = true, Name = name ?? "", Price = price, ImageUrl = image ?? "" };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open Graph scrape failed for {Url}", url);
            return new ProductLookupResult { Success = false, Error = "Couldn't fetch that page." };
        }
    }

    static Dictionary<string, string> ReadMetaTags(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in MetaTag.Matches(html)) result[m.Groups["key"].Value] = System.Net.WebUtility.HtmlDecode(m.Groups["value"].Value);
        foreach (Match m in MetaTagReversed.Matches(html)) result.TryAdd(m.Groups["key"].Value, System.Net.WebUtility.HtmlDecode(m.Groups["value"].Value));
        return result;
    }

    static (string? Name, decimal Price, string? Image) ReadFromMeta(Dictionary<string, string> meta)
    {
        var name = meta.GetValueOrDefault("og:title") ?? meta.GetValueOrDefault("twitter:title");
        var image = meta.GetValueOrDefault("og:image") ?? meta.GetValueOrDefault("twitter:image");
        var priceText = meta.GetValueOrDefault("product:price:amount") ?? meta.GetValueOrDefault("og:price:amount");
        decimal.TryParse(priceText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price);
        return (name, price, image);
    }

    static (string? Name, decimal Price, string? Image) ReadFromJsonLd(string html)
    {
        foreach (Match m in JsonLdBlock.Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups["json"].Value);
                var root = doc.RootElement;
                var candidates = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root }.AsEnumerable();
                foreach (var node in candidates)
                {
                    var type = node.TryGetProperty("@type", out var t) ? t.ToString() : "";
                    if (!type.Contains("Product", StringComparison.OrdinalIgnoreCase)) continue;

                    var name = node.TryGetProperty("name", out var n) ? n.GetString() : null;
                    string? image = node.TryGetProperty("image", out var img)
                        ? img.ValueKind == JsonValueKind.Array ? img.EnumerateArray().FirstOrDefault().GetString() : img.GetString()
                        : null;
                    decimal price = 0;
                    if (node.TryGetProperty("offers", out var offers))
                    {
                        var offer = offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().FirstOrDefault() : offers;
                        if (offer.TryGetProperty("price", out var p)) decimal.TryParse(p.ToString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out price);
                    }
                    if (name is not null || price > 0 || image is not null) return (name, price, image);
                }
            }
            catch (JsonException) { /* malformed JSON-LD block, skip it */ }
        }
        return (null, 0, null);
    }
}
