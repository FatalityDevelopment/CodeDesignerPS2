using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Thrown when Discord rejects the configured credentials or target
/// (401/403/404) — retrying won't help until the configuration changes.
/// </summary>
public sealed class DiscordConfigurationException : Exception
{
    public DiscordConfigurationException(string message) : base(message) { }
}

/// <summary>
/// Posts embeds to a Discord channel over the REST API — either as a bot
/// (<c>POST /channels/{id}/messages</c> with a bot token) or through a channel
/// webhook. Honours Discord's rate limits: waits out 429s and pauses when a
/// bucket reports no remaining requests.
/// </summary>
public sealed class DiscordRestClient : IDisposable
{
    private const string ApiBase = "https://discord.com/api/v10";
    private const string WebhookUsername = "CodeDesigner AI";
    private const int MaxRateLimitRetries = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public DiscordRestClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/TylerDev888/CodeDesigner, 1.0)");
    }

    public async Task SendEmbedsAsync(DiscordBotOptions options, IReadOnlyList<DiscordEmbed> embeds, CancellationToken cancellationToken = default)
    {
        if (embeds.Count == 0) { return; }

        for (int attempt = 0; ; attempt++)
        {
            using var request = BuildRequest(options, embeds);
            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
            {
                await Task.Delay(await GetRetryAfterAsync(response, cancellationToken), cancellationToken);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new DiscordConfigurationException(
                    $"Discord rejected the {(options.UsesBotToken ? "bot token / channel ID" : "webhook URL")} " +
                    $"({(int)response.StatusCode} {response.StatusCode}): {body}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Discord returned {(int)response.StatusCode} {response.StatusCode}: {body}");
            }

            // Bucket exhausted — wait for it to reset so the next send doesn't 429.
            if (TryGetHeader(response, "X-RateLimit-Remaining") == "0"
                && double.TryParse(TryGetHeader(response, "X-RateLimit-Reset-After"), NumberStyles.Float, CultureInfo.InvariantCulture, out var resetAfter))
            {
                await Task.Delay(TimeSpan.FromSeconds(resetAfter), cancellationToken);
            }

            return;
        }
    }

    private static HttpRequestMessage BuildRequest(DiscordBotOptions options, IReadOnlyList<DiscordEmbed> embeds)
    {
        var files = embeds.SelectMany(e => e.Attachments).ToList();

        // allowed_mentions with an empty parse list: tool output can never ping anyone.
        var payload = new Dictionary<string, object>
        {
            ["embeds"] = embeds,
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
        };
        if (!options.UsesBotToken) { payload["username"] = WebhookUsername; }
        if (files.Count > 0)
        {
            payload["attachments"] = files.Select((f, i) => new { id = i, filename = f.FileName }).ToList();
        }

        string url;
        if (options.UsesBotToken)
        {
            url = $"{ApiBase}/channels/{options.ChannelId}/messages";
        }
        else
        {
            url = options.WebhookUrl!;
            url += url.Contains('?') ? "&wait=true" : "?wait=true";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (options.UsesBotToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", options.Token);
        }

        if (files.Count == 0)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            return request;
        }

        // Attachments go as multipart: the JSON body in payload_json, each file in files[n].
        var multipart = new MultipartFormDataContent();
        var json = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), System.Text.Encoding.UTF8);
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipart.Add(json, "payload_json");
        for (int i = 0; i < files.Count; i++)
        {
            var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(files[i].Content));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            multipart.Add(file, $"files[{i}]", files[i].FileName);
        }

        request.Content = multipart;
        return request;
    }

    private static async Task<TimeSpan> GetRetryAfterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty("retry_after", out var retry) && retry.TryGetDouble(out var seconds))
            {
                return TimeSpan.FromSeconds(Math.Max(seconds, 0.1));
            }
        }
        catch (JsonException) { /* fall back to the header */ }

        return response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    public void Dispose() => _http.Dispose();
}
