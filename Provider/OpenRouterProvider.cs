using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

public sealed class OpenRouterProvider(HttpClient? httpClient = null) : ILlmProvider {
    static readonly Uri CreditsEndpoint = new("https://openrouter.ai/api/v1/credits");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public string Name => "OpenRouter";
    public bool HasQuota => false;
    public bool HasBalance => true;

    /// <summary>
    /// OpenRouter는 현재 할당량 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        throw new NotSupportedException("OpenRouter provider does not support quota lookup.");
    }

    /// <summary>
    /// OpenRouter 크레딧 API를 호출해 남은 잔액을 조회합니다.
    /// </summary>
    public async Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
            throw new InvalidOperationException("No API key");

        try {
            using HttpRequestMessage request = new(HttpMethod.Get, CreditsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenRouterApiKey);

            using HttpResponseMessage response = await this._httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await using Stream responseStream = await response.Content.ReadAsStreamAsync();
            var credits = await JsonSerializer.DeserializeAsync<OpenRouterCreditsResponse>(
                responseStream,
                JsonOptions
            );

            if (credits?.Data is null) {
                throw new InvalidOperationException("Response empty");
            }

            return new OpenRouterBalance {
                Remain = credits.Data.TotalCredits - credits.Data.TotalUsage
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            throw new InvalidOperationException("Request failed", exception);
        }
    }

    public sealed class OpenRouterBalance : ILlmProvider.Balance;

    private sealed class OpenRouterCreditsResponse {
        [JsonPropertyName("data")]
        public OpenRouterCreditsData? Data { get; init; }
    }

    private sealed class OpenRouterCreditsData {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; init; }

        [JsonPropertyName("total_usage")]
        public double TotalUsage { get; init; }
    }
}
