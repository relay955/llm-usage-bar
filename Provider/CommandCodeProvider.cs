using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

/// <summary>
/// Command Code 요금제 할당량 프로바이더.
/// ~/.commandcode/auth.json의 API 키로 billing credits API를 호출해
/// 5시간/주간 및 구독 월간 잔여 할당량을 조회합니다.
/// </summary>
public sealed class CommandCodeProvider(HttpClient? httpClient = null) : ILlmProvider {
    const string ApiKeyEnvironmentVariable = "COMMAND_CODE_API_KEY";
    static readonly Uri CreditsEndpoint = new("https://api.commandcode.ai/alpha/billing/credits");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public string Name => "Command Code";
    public string QuotaUrl => "https://commandcode.ai/usage";
    public bool HasShortQuota => true;
    public bool HasLongQuota => true;
    public bool HasThirdQuota => true;
    public string ShortQuotaLabel => "5h";
    public string LongQuotaLabel => "weekly";
    public string ThirdQuotaLabel => "monthly";
    public string ThirdQuotaFormat => "${0:0.00}";
    public bool HasBalance => false;

    /// <summary>
    /// 크레딧 API에서 잔여 사용량을 조회하고 설정의 월간 최대 크레딧을 적용합니다.
    /// </summary>
    public async Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        string apiKey = ResolveApiKey(App.Settings.CommandCodeAuthJsonPath);

        try {
            var credits = await GetResponseAsync<CommandCodeCreditsResponse>(CreditsEndpoint, apiKey);
            CommandCodeLimitWindow? fiveHour = credits?.WindowLimits?.FiveHour;
            CommandCodeLimitWindow? weekly = credits?.WindowLimits?.Weekly;
            if (fiveHour is null || weekly is null) {
                throw new InvalidOperationException("No quota data");
            }

            return new CommandCodeQuota {
                Short = RemainingPercent(fiveHour),
                Long = RemainingPercent(weekly),
                ShortResetAt = GetResetAt(fiveHour),
                LongResetAt = GetResetAt(weekly),
                Third = credits?.Credits?.MonthlyCredits,
                ThirdMax = App.Settings.CommandCodeMonthlyMaxCredits
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException) {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            throw new InvalidOperationException("Request failed", exception);
        }
    }

    /// <summary>
    /// 인증 헤더를 포함해 API를 호출하고 공통 오류를 처리한 뒤 응답을 역직렬화합니다.
    /// </summary>
    async Task<T?> GetResponseAsync<T>(Uri endpoint, string apiKey) {
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.UserAgent.ParseAdd("LLMUsageBar/1.0");
        request.Headers.Accept.ParseAdd("application/json");

        using HttpResponseMessage response = await this._httpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized) {
            throw new InvalidOperationException("Command Code authentication failed - run login again");
        }
        if (!response.IsSuccessStatusCode) {
            string errorText = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Command Code API error {(int)response.StatusCode}: {TrimError(errorText)}");
        }

        await using Stream responseStream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(responseStream, JsonOptions);
    }

    /// <summary>
    /// Command Code는 현재 이 프로바이더에서 잔액 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        throw new NotSupportedException("Command Code provider does not support balance lookup.");
    }

    /// <summary>
    /// 환경 변수를 우선 사용하고, 없으면 auth.json의 apiKey를 읽습니다.
    /// </summary>
    static string ResolveApiKey(string authJsonPath) {
        string environmentKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) ?? "";
        if (!string.IsNullOrWhiteSpace(environmentKey)) {
            return environmentKey.Trim();
        }

        string path = NormalizeAuthJsonPath(authJsonPath);
        if (string.IsNullOrWhiteSpace(path)) {
            throw new InvalidOperationException("No auth.json path");
        }

        if (!File.Exists(path)) {
            throw new FileNotFoundException("auth.json not found", path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("apiKey", out JsonElement apiKeyElement)
            || apiKeyElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(apiKeyElement.GetString())) {
            throw new InvalidOperationException("No API key");
        }

        return apiKeyElement.GetString()!.Trim();
    }

    /// <summary>
    /// 환경 변수와 홈 디렉터리 표기를 실제 파일 경로로 변환합니다.
    /// </summary>
    static string NormalizeAuthJsonPath(string authJsonPath) {
        if (string.IsNullOrWhiteSpace(authJsonPath)) {
            return "";
        }

        string path = Environment.ExpandEnvironmentVariables(authJsonPath.Trim());
        if (path.StartsWith("~")) {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(userProfile, path[1..].TrimStart('\\', '/'));
        }

        return path;
    }

    /// <summary>
    /// 사용량 창의 잔여 비율을 0~100 범위로 변환합니다.
    /// </summary>
    static double RemainingPercent(CommandCodeLimitWindow window) {
        if (window.Cap <= 0) {
            return 0;
        }

        return Math.Clamp((window.Cap - window.Used) / window.Cap * 100, 0, 100);
    }

    /// <summary>
    /// 밀리초 단위의 Unix 초기화 시각을 변환하며 미시작 창이나 유효하지 않은 값은 표시하지 않습니다.
    /// </summary>
    static DateTimeOffset? GetResetAt(CommandCodeLimitWindow window) {
        return window.ResetAt is > 0 and <= 253402300799999
            ? DateTimeOffset.FromUnixTimeMilliseconds(window.ResetAt.Value)
            : null;
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

    public sealed class CommandCodeQuota : ILlmProvider.Quota;

    sealed class CommandCodeCreditsResponse {
        [JsonPropertyName("credits")]
        public CommandCodeCreditBalance? Credits { get; init; }

        [JsonPropertyName("windowLimits")]
        public CommandCodeWindowLimits? WindowLimits { get; init; }
    }

    sealed class CommandCodeCreditBalance {
        [JsonPropertyName("monthlyCredits")]
        public double? MonthlyCredits { get; init; }
    }

    sealed class CommandCodeWindowLimits {
        [JsonPropertyName("fiveHour")]
        public CommandCodeLimitWindow? FiveHour { get; init; }

        [JsonPropertyName("weekly")]
        public CommandCodeLimitWindow? Weekly { get; init; }
    }

    sealed class CommandCodeLimitWindow {
        [JsonPropertyName("used")]
        public double Used { get; init; }

        [JsonPropertyName("cap")]
        public double Cap { get; init; }

        [JsonPropertyName("resetAt")]
        public long? ResetAt { get; init; }
    }
}
