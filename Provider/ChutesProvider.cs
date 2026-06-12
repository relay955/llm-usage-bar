using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace LLMUsageBar.Provider;

public sealed class ChutesProvider : ILlmProvider {
    static readonly Uri UsersEndpoint = new("https://api.chutes.ai/users/");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly HttpClient _httpClient;
    readonly string _apiKey;
    readonly string _userIdOrUsername;

    public ChutesProvider(string apiKey, string userIdOrUsername, HttpClient? httpClient = null) {
        if (string.IsNullOrWhiteSpace(apiKey)) {
            throw new ArgumentException("Chutes API key is required.", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(userIdOrUsername)) {
            throw new ArgumentException("Chutes user id or username is required.", nameof(userIdOrUsername));
        }

        this._apiKey = apiKey;
        this._userIdOrUsername = userIdOrUsername;
        this._httpClient = httpClient ?? new HttpClient();
    }

    public bool HasQuota => false;
    public bool HasBalance => true;

    /// <summary>
    /// Chutes는 현재 이 프로바이더에서 할당량 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        throw new NotSupportedException("Chutes provider does not support quota lookup.");
    }

    /// <summary>
    /// Chutes 사용자 잔액 API를 호출해 남은 잔액을 조회합니다.
    /// </summary>
    public async Task<ILlmProvider.Balance> GetCurrentBalanceAsync() {
        using HttpResponseMessage response = await SendBalanceRequestAsync(useBearerScheme: false);

        if (response.StatusCode == HttpStatusCode.Unauthorized) {
            using HttpResponseMessage bearerResponse = await SendBalanceRequestAsync(useBearerScheme: true);
            bearerResponse.EnsureSuccessStatusCode();
            return await ReadBalanceAsync(bearerResponse);
        }

        response.EnsureSuccessStatusCode();
        return await ReadBalanceAsync(response);
    }

    async Task<HttpResponseMessage> SendBalanceRequestAsync(bool useBearerScheme) {
        Uri balanceEndpoint = new(UsersEndpoint, $"{Uri.EscapeDataString(this._userIdOrUsername)}/balance");
        using HttpRequestMessage request = new(HttpMethod.Get, balanceEndpoint);
        string authorizationValue = useBearerScheme ? $"Bearer {this._apiKey}" : this._apiKey;
        request.Headers.TryAddWithoutValidation("Authorization", authorizationValue);

        return await this._httpClient.SendAsync(request);
    }

    static async Task<ILlmProvider.Balance> ReadBalanceAsync(HttpResponseMessage response) {
        await using Stream responseStream = await response.Content.ReadAsStreamAsync();
        using JsonDocument document = await JsonDocument.ParseAsync(responseStream);

        if (!TryReadBalance(document.RootElement, out double remain)) {
            throw new InvalidOperationException("Chutes balance response did not contain balance.");
        }

        return new ChutesBalance {
            Remain = remain
        };
    }

    static bool TryReadBalance(JsonElement element, out double balance) {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out balance)) {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            balance = 0;
            return false;
        }

        foreach (string propertyName in new[] { "balance", "remain", "remaining", "remaining_balance", "credits", "credit", "amount" }) {
            if (element.TryGetProperty(propertyName, out JsonElement property) && TryReadBalance(property, out balance)) {
                return true;
            }
        }

        if (element.TryGetProperty("data", out JsonElement data) && TryReadBalance(data, out balance)) {
            return true;
        }

        balance = 0;
        return false;
    }

    public sealed class ChutesBalance : ILlmProvider.Balance;
}
