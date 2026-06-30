using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using LLMUsageBar;
using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

/// <summary>
/// Codex/OpenAI ChatGPT 사용량 할당량 프로바이더.
/// ChatGPT wham usage API를 호출해 5시간/주간 잔여 사용률을 조회합니다.
/// </summary>
public sealed class CodexProvider(HttpClient? httpClient = null) : ILlmProvider {
    static readonly Uri UsageEndpoint = new("https://chatgpt.com/backend-api/wham/usage");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public string Name => "Codex";
    public bool HasShortQuota => true;
    public bool HasLongQuota => true;
    public bool HasBalance => false;

    /// <summary>
    /// Codex auth.json에서 OAuth access token을 읽고 ChatGPT 사용량 API를 호출해 잔여 할당량을 조회합니다.
    /// Daily에는 primary window(일반적으로 5시간), Weekly에는 secondary window 값을 저장합니다.
    /// </summary>
    public async Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        try {
            CodexAuth auth = ReadCodexAuth(App.Settings.CodexAuthJsonPath);

            using HttpRequestMessage request = new(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            request.Headers.UserAgent.ParseAdd("LLMUsageBar/1.0");

            string accountId = ResolveAccountId(auth.AccessToken, auth.AccountId);
            if (!string.IsNullOrWhiteSpace(accountId)) {
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
            }

            using HttpResponseMessage response = await this._httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) {
                string errorText = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"OpenAI API error {(int)response.StatusCode}: {TrimError(errorText)}");
            }

            await using Stream responseStream = await response.Content.ReadAsStreamAsync();
            var usage = await JsonSerializer.DeserializeAsync<CodexUsageResponse>(responseStream, JsonOptions);
            RateLimitWindow? primaryWindow = usage?.RateLimit?.PrimaryWindow;
            if (primaryWindow is null) {
                throw new InvalidOperationException("No quota data");
            }

            return new CodexQuota {
                Short = RemainingPercent(primaryWindow),
                Long = usage?.RateLimit?.SecondaryWindow is null ? 0 : RemainingPercent(usage.RateLimit.SecondaryWindow)
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException) {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            throw new InvalidOperationException("Request failed", exception);
        }
    }

    /// <summary>
    /// Codex는 현재 이 프로바이더에서 잔액 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        throw new NotSupportedException("Codex provider does not support balance lookup.");
    }

    /// <summary>
    /// 지정된 Codex auth.json 파일에서 OAuth token 정보를 읽습니다.
    /// </summary>
    static CodexAuth ReadCodexAuth(string authJsonPath) {
        string path = NormalizeAuthJsonPath(authJsonPath);
        if (string.IsNullOrWhiteSpace(path)) {
            throw new InvalidOperationException("No auth.json path");
        }

        if (!File.Exists(path)) {
            throw new FileNotFoundException("auth.json not found", path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        JsonElement tokenRoot = root.TryGetProperty("tokens", out JsonElement tokens)
            ? tokens
            : root;

        string accessToken = ReadStringProperty(tokenRoot, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken)) {
            accessToken = ReadStringProperty(tokenRoot, "access");
        }

        if (string.IsNullOrWhiteSpace(accessToken)) {
            throw new InvalidOperationException("No access token");
        }

        long expiresAt = ReadLongProperty(tokenRoot, "expires_at");
        if (expiresAt <= 0) {
            expiresAt = ReadLongProperty(tokenRoot, "expires");
        }

        if (expiresAt > 0 && NormalizeUnixTimeToMilliseconds(expiresAt) < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) {
            throw new InvalidOperationException("Token expired");
        }

        string accountId = ReadStringProperty(tokenRoot, "account_id");
        if (string.IsNullOrWhiteSpace(accountId)) {
            accountId = ReadStringProperty(tokenRoot, "accountId");
        }

        return new CodexAuth(accessToken, accountId);
    }

    /// <summary>
    /// 환경 변수와 홈 디렉터리 표기를 실제 파일 경로로 변환합니다.
    /// </summary>
    static string NormalizeAuthJsonPath(string authJsonPath) {
        string path = Environment.ExpandEnvironmentVariables(authJsonPath.Trim());
        if (path.StartsWith("~")) {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(userProfile, path[1..].TrimStart('\\', '/'));
        }

        return path;
    }

    /// <summary>
    /// 사용률 값을 0~100 범위의 잔여 사용률로 변환합니다.
    /// </summary>
    static double RemainingPercent(RateLimitWindow window) {
        return Math.Clamp(100 - window.UsedPercent, 0, 100);
    }

    /// <summary>
    /// JSON 객체에서 문자열 속성을 읽고 없으면 빈 문자열을 반환합니다.
    /// </summary>
    static string ReadStringProperty(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out JsonElement property)) {
            return "";
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    }

    /// <summary>
    /// JSON 객체에서 정수 속성을 읽고 없으면 0을 반환합니다.
    /// </summary>
    static long ReadLongProperty(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out JsonElement property)) {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long number)) {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out number)) {
            return number;
        }

        return 0;
    }

    /// <summary>
    /// 초 단위 epoch 값이면 밀리초 단위로 변환합니다.
    /// </summary>
    static long NormalizeUnixTimeToMilliseconds(long unixTime) {
        return unixTime < 10_000_000_000 ? unixTime * 1000 : unixTime;
    }

    /// <summary>
    /// 설정값을 우선 사용하고, 비어 있으면 JWT payload에서 ChatGPT 계정 ID를 추출합니다.
    /// </summary>
    static string ResolveAccountId(string accessToken, string configuredAccountId) {
        if (!string.IsNullOrWhiteSpace(configuredAccountId)) {
            return configuredAccountId.Trim();
        }

        try {
            string[] parts = accessToken.Split('.');
            if (parts.Length != 3) {
                return "";
            }

            byte[] payloadBytes = Base64UrlDecode(parts[1]);
            using JsonDocument document = JsonDocument.Parse(payloadBytes);
            if (!document.RootElement.TryGetProperty("https://api.openai.com/auth", out JsonElement authElement)) {
                return "";
            }

            return authElement.TryGetProperty("chatgpt_account_id", out JsonElement accountIdElement)
                ? accountIdElement.GetString() ?? ""
                : "";
        }
        catch {
            return "";
        }
    }

    /// <summary>
    /// base64url 문자열을 일반 base64로 보정한 뒤 바이트 배열로 디코딩합니다.
    /// </summary>
    static byte[] Base64UrlDecode(string input) {
        string base64 = input.Replace('-', '+').Replace('_', '/');
        int padLength = (4 - base64.Length % 4) % 4;
        base64 = base64.PadRight(base64.Length + padLength, '=');
        return Convert.FromBase64String(base64);
    }

    /// <summary>
    /// 오류 응답 본문을 한 줄 짧은 문자열로 줄입니다.
    /// </summary>
    static string TrimError(string errorText) {
        string normalized = errorText.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= 120) {
            return normalized;
        }

        return normalized[..120];
    }

    public sealed class CodexQuota : ILlmProvider.Quota;

    sealed record CodexAuth(string AccessToken, string AccountId);

    sealed class CodexUsageResponse {
        [JsonPropertyName("plan_type")]
        public string PlanType { get; init; } = "";

        [JsonPropertyName("rate_limit")]
        public RateLimit? RateLimit { get; init; }
    }

    sealed class RateLimit {
        [JsonPropertyName("primary_window")]
        public RateLimitWindow? PrimaryWindow { get; init; }

        [JsonPropertyName("secondary_window")]
        public RateLimitWindow? SecondaryWindow { get; init; }
    }

    sealed class RateLimitWindow {
        [JsonPropertyName("used_percent")]
        public double UsedPercent { get; init; }
    }
}
