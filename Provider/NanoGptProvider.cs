using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

public sealed class NanoGptProvider(HttpClient? httpClient = null) : ILlmProvider {
    static readonly Uri BalanceEndpoint = new("https://api.nano-gpt.com/api/check-balance");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public string Name => "NanoGPT";
    public string QuotaUrl => "https://nano-gpt.com";
    public bool HasShortQuota => false;
    public bool HasLongQuota => false;
    public bool HasBalance => true;

    public Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        throw new NotSupportedException("NanoGPT provider does not support quota lookup.");
    }

    public async Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.NanoGptApiKey))
            throw new InvalidOperationException("No API key");

        try {
            using HttpRequestMessage request = new(HttpMethod.Post, BalanceEndpoint);
            request.Headers.TryAddWithoutValidation("x-api-key", settings.NanoGptApiKey);

            using HttpResponseMessage response = await this._httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await using Stream responseStream = await response.Content.ReadAsStreamAsync();
            var balance = await JsonSerializer.DeserializeAsync<NanoGptBalanceResponse>(responseStream, JsonOptions);

            if (balance?.UsdBalance is null ||
                !double.TryParse(balance.UsdBalance, NumberStyles.Float, CultureInfo.InvariantCulture, out double remain) ||
                !double.IsFinite(remain)) {
                throw new InvalidOperationException("Invalid balance response");
            }

            return new NanoGptBalance {
                Remain = remain,
                Max = settings.NanoGptMaxBalance
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InvalidOperationException) {
            throw new InvalidOperationException("Request failed", exception);
        }
    }

    public sealed class NanoGptBalance : ILlmProvider.Balance;

    private sealed class NanoGptBalanceResponse {
        [JsonPropertyName("usd_balance")]
        public string? UsdBalance { get; init; }
    }
}