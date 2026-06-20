namespace BatHouseholdHub.Services;

/// <summary>Talks to a local Ollama-compatible server (e.g. Qwen) instead of a paid API,
/// so day-to-day "smart" features here don't burn Claude/OpenAI tokens. This is the shell:
/// settings, configuration state, and a real reachability check are wired up and working.
/// The actual chat-completion call is stubbed below pending the server being reachable --
/// fill it in once Ollama is back up (currently on the dual-booted Bat Tower machine).</summary>
public class HomeButlerService(HttpClient http, HouseholdStore store, ILogger<HomeButlerService> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(store.Data.HomeButler.BaseUrl);
    public string BaseUrl => store.Data.HomeButler.BaseUrl;
    public string Model => store.Data.HomeButler.Model;

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
            return (false, "Couldn't reach the server (it may be offline or unreachable from this network).");
        }
    }

    /// <summary>Best-effort product info extraction for a page Open Graph scraping
    /// couldn't fully parse. Returns NotConfigured until wired up -- see the TODO below.</summary>
    public Task<ProductLookupResult> LookupAsync(string url, string pageExcerpt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return Task.FromResult(new ProductLookupResult { Success = false, Error = "Home Butler isn't configured yet." });

        // TODO: wire up the real Ollama chat call once the server is reachable. Shape:
        //
        // var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl.TrimEnd('/')}/api/chat");
        // request.Content = JsonContent.Create(new
        // {
        //     model = Model,
        //     stream = false,
        //     messages = new object[]
        //     {
        //         new { role = "system", content = "Extract product name, price (USD number), and image URL from page text. Reply with ONLY JSON: {\"name\":\"\",\"price\":0,\"imageUrl\":\"\"}" },
        //         new { role = "user", content = pageExcerpt }
        //     }
        // });
        // using var response = await http.SendAsync(request, ct);
        // var body = await response.Content.ReadAsStringAsync(ct);
        // // Ollama's /api/chat returns { "message": { "content": "...model text, hopefully JSON..." } }
        // // Parse response.message.content the same way ProductLookupService parses Claude's reply.

        return Task.FromResult(new ProductLookupResult { Success = false, Error = "Home Butler is configured but the lookup call isn't wired up yet (see TODO in HomeButlerService.cs)." });
    }
}
