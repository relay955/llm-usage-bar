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
/// 5시간/주간 잔여 할당량을 조회합니다.
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
    public string ShortQuotaLabel => "5h";
    public string LongQuotaLabel => "weekly";
    public bool HasBalance => false;

    /// <summary>
    /// API 키로 credits API를 호출해 fiveHour/weekly 창의 잔여 사용률을 조회합니다.
    /// </summary>
    public async Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        string apiKey = ResolveApiKey(App.Settings.CommandCodeAuthJsonPath);

        try {
            using HttpRequestMessage request = new(HttpMethod.Get, CreditsEndpoint);
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
            var credits = await JsonSerializer.DeserializeAsync<CommandCodeCreditsResponse>(responseStream, JsonOptions);
            CommandCodeLimitWindow? fiveHour = credits?.WindowLimits?.FiveHour;
            CommandCodeLimitWindow? weekly = credits?.WindowLimits?.Weekly;
            if (fiveHour is null || weekly is null) {
                throw new InvalidOperationException("No quota data");
            }

            return new CommandCodeQuota {
                Short = RemainingPercent(fiveHour),
                Long = RemainingPercent(weekly)
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
        [JsonPropertyName("windowLimits")]
        public CommandCodeWindowLimits? WindowLimits { get; init; }
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
    }
}
