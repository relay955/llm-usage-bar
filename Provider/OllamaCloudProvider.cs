using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

/// <summary>
/// Ollama Cloud 할당량 프로바이더.
/// Ollama Cloud settings 페이지를 세션 쿠키로 조회해 session/weekly 잔여 할당량을 파싱합니다.
/// 타인의 코드를 참조함: https://github.com/slkiser/opencode-quota/blob/main/src/lib/ollama-cloud.ts
/// </summary>
public sealed class OllamaCloudProvider(HttpClient? httpClient = null) : ILlmProvider {
    static readonly Uri SettingsEndpoint = new("https://ollama.com/settings");
    const string UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Gecko/20100101 Firefox/148.0";
    const string CookieNamePrefix = "__Secure-session=";
    const string DataUsageTrackPattern = @"<[^>]*\bdata-usage-track\b[^>]*>";
    const string UsagePercentPattern = @"(\d+(?:\.\d+)?)%\s*used";
    const string WidthPercentPattern = @"(?:^|;)\s*width\s*:\s*([0-9.]+)%";

    readonly HttpClient _httpClient = httpClient ?? new HttpClient(new HttpClientHandler {
        AllowAutoRedirect = false
    });

    public string Name => "Ollama Cloud";
    public string QuotaUrl => "https://ollama.com/settings";
    public bool HasShortQuota => true;
    public bool HasLongQuota => true;
    public string ShortQuotaLabel => "session";
    public string LongQuotaLabel => "weekly";
    public bool HasBalance => false;

    /// <summary>
    /// Ollama Cloud settings HTML에서 session/weekly 사용률을 읽고 잔여 비율로 변환합니다.
    /// </summary>
    public async Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        if (string.IsNullOrWhiteSpace(App.Settings.OllamaCloudSessionCookie)) {
            throw new InvalidOperationException("No session cookie");
        }

        if (App.Settings.OllamaCloudSessionCookie.Contains('\r') || App.Settings.OllamaCloudSessionCookie.Contains('\n')) {
            throw new InvalidOperationException("Session cookie contains invalid CRLF characters");
        }

        try {
            using HttpRequestMessage request = new(HttpMethod.Get, SettingsEndpoint);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            request.Headers.TryAddWithoutValidation("Cookie", $"{CookieNamePrefix}{NormalizeCookie(App.Settings.OllamaCloudSessionCookie)}");

            using HttpResponseMessage response = await this._httpClient.SendAsync(request);
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) {
                string location = response.Headers.Location?.ToString() ?? "";
                throw new InvalidOperationException($"Authentication error: redirected to {TrimError(location)} - cookie may be expired");
            }

            if (!response.IsSuccessStatusCode) {
                string errorText = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Ollama Cloud settings error {(int)response.StatusCode}: {TrimError(errorText)}");
            }

            string html = await response.Content.ReadAsStringAsync();
            var tracks = Regex.Matches(html, DataUsageTrackPattern, RegexOptions.Singleline)
                .Select(match => match.Value)
                .ToArray();

            if (tracks.Length == 0) {
                throw new InvalidOperationException("Could not parse usage tracks from Ollama Cloud settings page");
            }

            double? sessionPercent = tracks.Length > 0 ? ExtractUsagePercentFromTrack(tracks[0]) : null;
            double? weeklyPercent = tracks.Length > 1 ? ExtractUsagePercentFromTrack(tracks[1]) : null;

            if (sessionPercent is null && weeklyPercent is null) {
                throw new InvalidOperationException("Could not extract any usage percentages from Ollama Cloud settings page");
            }

            return new OllamaCloudQuota {
                Short = sessionPercent is null ? 0 : 100 - sessionPercent.Value,
                Long = weeklyPercent is null ? 0 : 100 - weeklyPercent.Value
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
    /// Ollama Cloud는 현재 이 프로바이더에서 잔액 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        throw new NotSupportedException("Ollama Cloud provider does not support balance lookup.");
    }

    /// <summary>
    /// 쿠키 이름이 포함된 값이면 실제 세션 값만 남깁니다.
    /// </summary>
    static string NormalizeCookie(string rawCookie) {
        string cookie = rawCookie.Trim();
        if (cookie.StartsWith(CookieNamePrefix, StringComparison.Ordinal)) {
            cookie = cookie[CookieNamePrefix.Length..];
        }

        return cookie;
    }

    /// <summary>
    /// usage track HTML 조각에서 사용률을 추출합니다.
    /// </summary>
    static double? ExtractUsagePercentFromTrack(string trackHtml) {
        Match ariaMatch = Regex.Match(trackHtml, UsagePercentPattern);
        if (ariaMatch.Success && TryReadPercent(ariaMatch.Groups[1].Value, out double ariaPercent)) {
            return ariaPercent;
        }

        Match styleMatch = Regex.Match(trackHtml, "style=\"([^\"]*)\"");
        if (styleMatch.Success) {
            Match widthMatch = Regex.Match(styleMatch.Groups[1].Value, WidthPercentPattern);
            if (widthMatch.Success && TryReadPercent(widthMatch.Groups[1].Value, out double widthPercent)) {
                return widthPercent;
            }
        }

        return null;
    }

    /// <summary>
    /// 문자열 숫자를 0~100 범위의 percentage 값으로 읽습니다.
    /// </summary>
    static bool TryReadPercent(string valueText, out double value) {
        return double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 100;
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

    public sealed class OllamaCloudQuota : ILlmProvider.Quota;
}
