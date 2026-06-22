using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatHouseholdHub.Services;

/// <summary>Talks to a local Ollama-compatible server (e.g. Qwen) instead of a paid API,
/// so day-to-day "smart" features here don't burn Claude/OpenAI tokens.</summary>
public class HomeButlerService(HttpClient http, HouseholdStore store, ILogger<HomeButlerService> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(store.Data.HomeButler.BaseUrl);
    public string BaseUrl => store.Data.HomeButler.BaseUrl;
    public string Model => string.IsNullOrWhiteSpace(store.Data.HomeButler.Model)
        ? "qwen3.6:latest"
        : store.Data.HomeButler.Model;

    /// <summary>Real, working reachability check -- hits Ollama's lightweight /api/tags
    /// endpoint to confirm the server is up and lists the models it has pulled.</summary>
    public async Task<(bool Reachable, string Message)> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return (false, "No base URL set yet.");

        try
        {
            using var response = await http.GetAsync($"{BaseUrl.TrimEnd('/')}/api/tags", ct);
            if (!response.IsSuccessStatusCode) return (false, $"Server responded {(int)response.StatusCode}.");

            var body = await response.Content.ReadAsStringAsync(ct);
            return (true, body.Length > 200 ? body[..200] + "…" : body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Home Butler connection test failed against {BaseUrl}", BaseUrl);
            return (false, "Couldn't reach the server. It may be offline or unreachable from this network.");
        }
    }

    /// <summary>Best-effort product info extraction for a page Open Graph scraping
    /// couldn't fully parse. Uses local Ollama/Qwen only and fails safely.</summary>
    public async Task<ProductLookupResult> LookupAsync(string url, string pageExcerpt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new ProductLookupResult { Success = false, Error = "Home Butler isn't configured yet." };

        try
        {
            var prompt = $"""
            Product URL:
            {url}

            Page excerpt:
            {pageExcerpt}

            Extract the product name, price in USD as a number, and image URL if available.

            Reply with ONLY valid JSON in this exact shape:
            {{"name":"","price":0,"imageUrl":""}}

            Rules:
            - Do not include markdown.
            - Do not explain.
            - If unknown, use an empty string or 0.
            """;

            var requestBody = new
            {
                model = Model,
                stream = false,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You are Home Butler, a private household assistant. Extract product data only. Return only valid JSON."
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            using var response = await http.PostAsJsonAsync($"{BaseUrl.TrimEnd('/')}/api/chat", requestBody, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Home Butler Ollama call failed: {Status} {Body}", response.StatusCode, responseBody);
                return new ProductLookupResult { Success = false, Error = $"Home Butler returned {(int)response.StatusCode}." };
            }

            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement))
            {
                return new ProductLookupResult { Success = false, Error = "Home Butler response did not include message content." };
            }

            var content = contentElement.GetString() ?? "";
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < jsonStart)
                return new ProductLookupResult { Success = false, Error = "Home Butler did not return parseable JSON." };

            var extracted = JsonSerializer.Deserialize<ExtractedProduct>(
                content[jsonStart..(jsonEnd + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (extracted is null)
                return new ProductLookupResult { Success = false, Error = "Home Butler returned empty product data." };

            if (string.IsNullOrWhiteSpace(extracted.Name) && extracted.Price <= 0 && string.IsNullOrWhiteSpace(extracted.ImageUrl))
                return new ProductLookupResult { Success = false, Error = "Home Butler could not extract product details." };

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
            logger.LogWarning(ex, "Home Butler lookup failed for {Url}", url);
            return new ProductLookupResult { Success = false, Error = "Home Butler lookup failed." };
        }
    }

    private class ExtractedProduct
    {
        public string? Name { get; set; }
        public decimal Price { get; set; }

        [JsonPropertyName("imageUrl")]
        public string? ImageUrl { get; set; }
    }
}
