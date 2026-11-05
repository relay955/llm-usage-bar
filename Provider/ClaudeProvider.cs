using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

/// <summary>
/// Claude Desktop의 OAuth 인증 정보로 5시간 및 주간 잔여 할당량을 조회합니다.
/// </summary>
public sealed class ClaudeProvider(HttpClient? httpClient = null) : ILlmProvider {
    static readonly Uri UsageEndpoint = new("https://api.anthropic.com/api/oauth/usage");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public string Name => "Claude";
    public string QuotaUrl => "https://claude.ai/settings/usage";
    public bool HasShortQuota => true;
    public bool HasLongQuota => true;
    public string ShortQuotaLabel => "hourly";
    public string LongQuotaLabel => "weekly";
    public bool HasBalance => false;

    /// <summary>
    /// Desktop의 암호화된 캐시에서 토큰을 읽어 잔여 비율과 초기화 시각을 반환합니다.
    /// </summary>
    public async Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        try {
            string accessToken = ClaudeDesktopAuth.ReadAccessToken(App.Settings.ClaudeDesktopDataDirectory);
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("LLMUsageBar/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage response = await this._httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized) {
                throw new InvalidOperationException("Claude Desktop token expired or invalid. Open Claude Desktop and sign in again.");
            }
            if (response.StatusCode == HttpStatusCode.Forbidden) {
                throw new InvalidOperationException("Claude usage access denied. Check your subscription and sign in again with Claude Desktop.");
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests) {
                throw new InvalidOperationException("Claude usage rate limited. Try again later.");
            }
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"Claude API error {(int)response.StatusCode}");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            var usage = await JsonSerializer.DeserializeAsync<ClaudeUsageResponse>(stream, JsonOptions);
            if (usage?.FiveHour?.Utilization is null || usage.SevenDay?.Utilization is null) {
                throw new InvalidOperationException("No Claude quota data");
            }

            return new ClaudeQuota {
                Short = RemainingPercent(usage.FiveHour),
                Long = RemainingPercent(usage.SevenDay),
                ShortResetAt = usage.FiveHour.ResetsAt,
                LongResetAt = usage.SevenDay.ResetsAt
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException) {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            throw new InvalidOperationException("Claude request failed", exception);
        }
    }

    /// <summary>
    /// Claude 프로바이더는 잔액 조회를 지원하지 않으므로 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        throw new NotSupportedException("Claude provider does not support balance lookup.");
    }

    /// <summary>
    /// 유효한 사용률을 0~100 범위의 잔여 사용률로 변환합니다.
    /// </summary>
    static double RemainingPercent(UsageWindow window) {
        if (window.Utilization is not double utilization || !double.IsFinite(utilization)) {
            throw new InvalidOperationException("Invalid Claude quota data");
        }
        return Math.Clamp(100 - utilization, 0, 100);
    }

    public sealed class ClaudeQuota : ILlmProvider.Quota;

    sealed class ClaudeUsageResponse {
        [JsonPropertyName("five_hour")]
        public UsageWindow? FiveHour { get; init; }

        [JsonPropertyName("seven_day")]
        public UsageWindow? SevenDay { get; init; }
    }

    sealed class UsageWindow {
        [JsonPropertyName("utilization")]
        public double? Utilization { get; init; }

        [JsonPropertyName("resets_at")]
        public DateTimeOffset? ResetsAt { get; init; }
    }
}
