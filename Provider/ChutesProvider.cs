using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

/// <summary>
/// Chutes.ai LLM 프로바이더.
/// fingerprint를 사용하여 로그인하고 쿠키 기반 인증으로 잔액을 조회합니다.
/// </summary>
public sealed class ChutesProvider(HttpClient? httpClient = null) : ILlmProvider {
    static readonly Uri LoginEndpoint = new("https://chutes.ai/api/auth/login");
    static readonly Uri BalanceEndpoint = new("https://chutes.ai/api/dashboard/balance");
    static readonly JsonSerializerOptions JsonSerializeOptions = new(JsonSerializerDefaults.Web);

    readonly CookieContainer _cookieContainer = new();
    bool _isAuthenticated;

    public string Name => "Chutes";
    public bool HasShortQuota => false;
    public bool HasLongQuota => false;
    public bool HasBalance => true;

    /// <summary>
    /// Chutes는 현재 이 프로바이더에서 할당량 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        throw new NotSupportedException("Chutes provider does not support quota lookup.");
    }

    /// <summary>
    /// Chutes 사용자 잔액 API를 호출해 남은 잔액을 조회합니다.
    /// 이전에 로그인되어 쿠키가 유효하면 재로그인 없이 바로 잔액을 조회합니다.
    /// 쿠키가 없거나 balance API 호출이 실패하면 fingerprint로 재로그인합니다.
    /// </summary>
    public async Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.ChutesFingerprint)) {
            throw new InvalidOperationException("No fingerprint");
        }

        try {
            // 이미 인증된 상태면 바로 balance API 호출 시도
            if (_isAuthenticated) {
                using var response = await SendBalanceRequestWithCookieAsync();
                if (response.IsSuccessStatusCode) {
                    return await ReadBalanceAsync(response);
                }
                // balance API 실패 → 쿠키 만료 등으로 간주하고 재로그인
                _isAuthenticated = false;
            }

            // 로그인 및 balance API 호출
            if (!_isAuthenticated) {
                await LoginAsync(settings.ChutesFingerprint);
                _isAuthenticated = true;
            }

            using var response2 = await SendBalanceRequestWithCookieAsync();
            response2.EnsureSuccessStatusCode();
            return await ReadBalanceAsync(response2);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            await Console.Out.WriteLineAsync(exception.Message);
            _isAuthenticated = false;
            throw new InvalidOperationException("Request failed", exception);
        }
    }

    /// <summary>
    /// fingerprint를 사용하여 Chutes에 로그인합니다.
    /// 응답의 Set-Cookie 헤더를 내부 CookieContainer에 저장합니다.
    /// </summary>
    async Task LoginAsync(string fingerprint) {
        var loginRequest = new { fingerprint, returnTo = "/" };
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(loginRequest, JsonSerializeOptions),
            System.Text.Encoding.UTF8,
            "application/json");

        var handler = new HttpClientHandler {
            CookieContainer = _cookieContainer,
            UseCookies = true
        };

        using var loginClient = new HttpClient(handler);
        using var response = await loginClient.PostAsync(LoginEndpoint, jsonContent);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 저장된 쿠키를 사용하여 balance API를 호출합니다.
    /// </summary>
    async Task<HttpResponseMessage> SendBalanceRequestWithCookieAsync() {
        var handler = new HttpClientHandler {
            CookieContainer = _cookieContainer,
            UseCookies = true
        };

        using var cookieClient = new HttpClient(handler);
        return await cookieClient.GetAsync(BalanceEndpoint);
    }

    /// <summary>
    /// balance API 응답에서 잔액 값을 파싱합니다.
    /// </summary>
    static async Task<ILlmProvider.Balance> ReadBalanceAsync(HttpResponseMessage response) {
        await using Stream responseStream = await response.Content.ReadAsStreamAsync();
        using JsonDocument document = await JsonDocument.ParseAsync(responseStream);

        double remain = ReadBalanceFromDocument(document.RootElement);
        return new ChutesBalance {
            Remain = remain
        };
    }

    /// <summary>
    /// balance API 응답 JSON에서 잔액 값을 읽습니다.
    /// 응답 구조: { "balance": { "usd": 4.85, "tao": 0 }, ... }
    /// </summary>
    static double ReadBalanceFromDocument(JsonElement element) {
        // 직접 숫자인 경우
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double balance)) {
            return balance;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            return 0;
        }

        // { "balance": { "usd": ... } } 구조에서 balance 객체 탐색
        if (element.TryGetProperty("balance", out JsonElement balanceElement) && balanceElement.ValueKind == JsonValueKind.Object) {
            // usd 필드 우선 조회
            if (balanceElement.TryGetProperty("usd", out JsonElement usdElement) && usdElement.ValueKind == JsonValueKind.Number) {
                usdElement.TryGetDouble(out balance);
                return balance;
            }
            // tao 필드
            if (balanceElement.TryGetProperty("tao", out JsonElement taoElement) && taoElement.ValueKind == JsonValueKind.Number) {
                taoElement.TryGetDouble(out balance);
                return balance;
            }
        }

        // 일반적인 balance 필드 탐색 (하위 재귀)
        foreach (string propertyName in new[] { "balance", "remain", "remaining", "remaining_balance", "credits", "credit", "amount" }) {
            if (element.TryGetProperty(propertyName, out JsonElement property) && TryReadBalance(property, out balance)) {
                return balance;
            }
        }

        if (element.TryGetProperty("data", out JsonElement data) && TryReadBalance(data, out balance)) {
            return balance;
        }

        return 0;
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
