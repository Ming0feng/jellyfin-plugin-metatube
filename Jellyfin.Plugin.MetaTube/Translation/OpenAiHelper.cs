using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaTube.Translation;

/// <summary>
/// Resolves the model name used by the OpenAI-compatible translation engine.
/// When the configured model is empty, the first chat-capable model reported by
/// the <c>/v1/models</c> endpoint is used instead.
/// </summary>
public static class OpenAiHelper
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly object Sync = new();

    private static string _cachedBaseUrl;
    private static string _cachedApiKey;
    private static string _resolvedModel;

    /// <summary>
    /// Returns the model configured by the user, or auto-discovers one from the API.
    /// </summary>
    public static async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.Configuration;

        // Explicitly configured model always wins.
        if (!string.IsNullOrWhiteSpace(config.OpenAiModel))
            return config.OpenAiModel.Trim();

        var baseUrl = NormalizeBaseUrl(config.OpenAiApiUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedModel)
                && _cachedBaseUrl == baseUrl
                && _cachedApiKey == config.OpenAiApiKey)
            {
                return _resolvedModel;
            }
        }

        var model = await FetchModelAsync(baseUrl, config.OpenAiApiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(model))
            return string.Empty;

        lock (Sync)
        {
            _cachedBaseUrl = baseUrl;
            _cachedApiKey = config.OpenAiApiKey;
            _resolvedModel = model;
        }

        return model;
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        return url.Trim().TrimEnd('/');
    }

    private static async Task<string> FetchModelAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        foreach (var url in GetModelUrls(baseUrl))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                using var response = await HttpClient
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                var payload = await response.Content
                    .ReadFromJsonAsync<ModelListResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var model = payload?.Data?
                    .Select(item => item?.Id)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && IsChatModel(id));

                if (!string.IsNullOrWhiteSpace(model))
                    return model;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Ignore and try the next candidate endpoint.
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetModelUrls(string baseUrl)
    {
        yield return $"{baseUrl}/models";

        // Most OpenAI-compatible servers expect the version segment in the base url.
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            yield return $"{baseUrl}/v1/models";
    }

    private static bool IsChatModel(string id)
    {
        return !id.Contains("embedding", StringComparison.OrdinalIgnoreCase)
               && !id.Contains("whisper", StringComparison.OrdinalIgnoreCase)
               && !id.Contains("tts", StringComparison.OrdinalIgnoreCase)
               && !id.Contains("dall-e", StringComparison.OrdinalIgnoreCase)
               && !id.Contains("moderation", StringComparison.OrdinalIgnoreCase)
               && !id.Contains("rerank", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ModelListResponse
    {
        [JsonPropertyName("data")]
        public List<ModelInfo> Data { get; set; }
    }

    private sealed class ModelInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }
}
