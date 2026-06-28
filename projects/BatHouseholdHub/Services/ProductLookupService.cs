using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatHouseholdHub.Services;

public class ProductLookupResult
{
    public bool Success { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public string Error { get; set; } = "";
}

/// <summary>Best-effort product info lookup via the Claude API's web search tool.
/// Returns Success=false (never throws) if the API key is missing or the call fails,
/// so the shopping tracker always falls back gracefully to manual entry.</summary>
public class ProductLookupService(HttpClient http, IConfiguration config, ILogger<ProductLookupService> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["ANTHROPIC_API_KEY"]);

    public async Task<ProductLookupResult> LookupAsync(string productUrl, CancellationToken ct = default)
    {
        var apiKey = config["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ProductLookupResult { Success = false, Error = "No API key configured." };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            var body = new
            {
                model = "claude-3-5-haiku-20241022",
                max_tokens = 512,
                tools = new object[] { new { type = "web_search_20250305", name = "web_search" } },
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = $"Look up the product at this URL: {productUrl}\n" +
                                  "Find its name, current price (USD, number only), and a direct image URL if available. " +
                                  "Reply with ONLY a JSON object on the last line, no other text, in this exact shape: " +
                                  "{\"name\":\"...\",\"price\":0.00,\"imageUrl\":\"...\"}. " +
                                  "If you can't find a field, use an empty string or 0."
                    }
                }
            };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Anthropic API call failed: {Status} {Body}", response.StatusCode, responseBody);
                return new ProductLookupResult { Success = false, Error = $"API returned {(int)response.StatusCode}." };
            }

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement.GetProperty("content").EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString() ?? "")
                .LastOrDefault() ?? "";

            var jsonStart = text.LastIndexOf('{');
            var jsonEnd = text.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return new ProductLookupResult { Success = false, Error = "Couldn't parse a product from the response." };

            var extracted = JsonSerializer.Deserialize<ExtractedProduct>(text[jsonStart..(jsonEnd + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (extracted is null) return new ProductLookupResult { Success = false, Error = "Empty product data." };

            return new ProductLookupResult
            {
                Success = true,
                Name = extracted.Name ?? "",
                Price = extracted.Price,
                ImageUrl = extracted.ImageUrl ?? ""
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Product lookup failed for {Url}", productUrl);
            return new ProductLookupResult { Success = false, Error = "Lookup failed." };
        }
    }

    private class ExtractedProduct
    {
        public string? Name { get; set; }
        public decimal Price { get; set; }
        [JsonPropertyName("imageUrl")] public string? ImageUrl { get; set; }
    }
}
