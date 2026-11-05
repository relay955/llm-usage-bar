using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using LLMUsageBar.Module;

namespace LLMUsageBar.Provider;

public enum AntigravityModelGroup {
    ClaudeGpt,
    Gemini,
    GeminiFrontier
}

/// <summary>
/// 실행 중인 Antigravity의 로컬 language server에서 모델 그룹별 잔여 할당량을 조회합니다.
/// Google OAuth 토큰을 외부 프로세스로 복사하거나 원격 API에 직접 전송하지 않습니다.
/// </summary>
public sealed partial class AntigravityProvider(AntigravityModelGroup modelGroup) : ILlmProvider {
    const string ServicePath = "exa.language_server_pb.LanguageServerService";
    const string RequestJson = "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\",\"ideVersion\":\"unknown\"}}";

    static readonly string[] QuotaMethods = [
        "RetrieveUserQuotaSummary",
        "GetUserStatus",
        "GetCommandModelConfigs"
    ];

    public string Name => modelGroup switch {
        AntigravityModelGroup.ClaudeGpt => "Antigravity Claude/GPT",
        AntigravityModelGroup.Gemini => "Antigravity Gemini",
        AntigravityModelGroup.GeminiFrontier => "Antigravity Gemini Frontier",
        _ => throw new ArgumentOutOfRangeException(nameof(modelGroup))
    };
    public string QuotaUrl => "https://antigravity.google/settings";
    public bool HasShortQuota => modelGroup != AntigravityModelGroup.GeminiFrontier;
    public bool HasLongQuota => true;
    public string ShortQuotaLabel => "hourly";
    public string LongQuotaLabel => "weekly";
    public bool HasBalance => false;

    /// <summary>
    /// Antigravity language server의 loopback 포트와 CSRF 토큰을 자동 탐색하고 선택한 모델 그룹의 잔여 할당량을 조회합니다.
    /// </summary>
    public async Task<ILlmProvider.Quota> GetCurrentQuotaAsync() {
        IReadOnlyList<AntigravityProcess> processes = FindAntigravityProcesses();
        if (processes.Count == 0) {
            throw new InvalidOperationException("Antigravity is not running");
        }

        Exception? lastException = null;
        foreach (AntigravityProcess process in processes) {
            foreach (int port in GetListeningPorts(process.ProcessId)) {
                try {
                    AntigravityQuota? quota = await TryReadQuotaAsync(port, process.CsrfToken);
                    if (quota is not null) {
                        return quota;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) {
                    lastException = exception;
                }
            }
        }

        throw new InvalidOperationException("Antigravity quota endpoint not found", lastException);
    }

    /// <summary>
    /// Antigravity는 잔액 조회를 지원하지 않으므로 호출 시 예외를 발생시킵니다.
    /// </summary>
    public Task<ILlmProvider.Balance> GetCurrentBalanceAsync(AppSettings settings) {
        throw new NotSupportedException("Antigravity provider does not support balance lookup.");
    }

    /// <summary>
    /// 실행 중인 Antigravity language server 프로세스의 ID와 CSRF 토큰을 찾습니다.
    /// </summary>
    static IReadOnlyList<AntigravityProcess> FindAntigravityProcesses() {
        var result = new List<AntigravityProcess>();
        foreach (Process process in Process.GetProcesses()) {
            using (process) {
                string processName;
                try {
                    processName = process.ProcessName;
                }
                catch {
                    continue;
                }

                if (!processName.StartsWith("language_server", StringComparison.OrdinalIgnoreCase)
                    && !processName.StartsWith("language-server", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                string commandLine = ReadProcessCommandLine(process.Id);
                if (!commandLine.Contains("antigravity", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                Match tokenMatch = CsrfTokenRegex().Match(commandLine);
                string csrfToken = tokenMatch.Success
                    ? tokenMatch.Groups["quoted"].Success
                        ? tokenMatch.Groups["quoted"].Value
                        : tokenMatch.Groups["plain"].Value
                    : "";

                result.Add(new AntigravityProcess(process.Id, csrfToken));
            }
        }

        return result;
    }

    /// <summary>
    /// Windows 프로세스 조회 API로 지정된 프로세스의 명령행을 읽습니다.
    /// </summary>
    static string ReadProcessCommandLine(int processId) {
        IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero) {
            return "";
        }

        try {
            int bufferLength = 0;
            NtQueryInformationProcess(processHandle, ProcessCommandLineInformation, IntPtr.Zero, 0, ref bufferLength);
            if (bufferLength <= 0) {
                return "";
            }

            IntPtr buffer = Marshal.AllocHGlobal(bufferLength);
            try {
                int status = NtQueryInformationProcess(
                    processHandle,
                    ProcessCommandLineInformation,
                    buffer,
                    bufferLength,
                    ref bufferLength);
                if (status != 0) {
                    return "";
                }

                UnicodeString commandLine = Marshal.PtrToStructure<UnicodeString>(buffer);
                return commandLine.Buffer == IntPtr.Zero
                    ? ""
                    : Marshal.PtrToStringUni(commandLine.Buffer, commandLine.Length / sizeof(char)) ?? "";
            }
            finally {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally {
            CloseHandle(processHandle);
        }
    }

    /// <summary>
    /// 지정된 프로세스가 수신 대기 중인 TCP loopback 포트를 반환합니다.
    /// </summary>
    static IReadOnlyList<int> GetListeningPorts(int processId) {
        int bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AddressFamily.InterNetwork, TcpTableOwnerPidListener, 0);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try {
            uint result = GetExtendedTcpTable(buffer, ref bufferSize, true, AddressFamily.InterNetwork, TcpTableOwnerPidListener, 0);
            if (result != 0) {
                return [];
            }

            int rowCount = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            IntPtr rowPointer = IntPtr.Add(buffer, sizeof(int));
            var ports = new List<int>();

            for (int index = 0; index < rowCount; index++) {
                TcpRowOwnerPid row = Marshal.PtrToStructure<TcpRowOwnerPid>(IntPtr.Add(rowPointer, index * rowSize));
                if (row.OwningPid != processId) {
                    continue;
                }

                int port = (ushort)IPAddress.NetworkToHostOrder((short)(row.LocalPort & 0xFFFF));
                if (port > 0) {
                    ports.Add(port);
                }
            }

            return ports.Distinct().ToArray();
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 로컬 서버의 quota 엔드포인트를 순서대로 호출하고 해석 가능한 첫 응답을 반환합니다.
    /// </summary>
    async Task<AntigravityQuota?> TryReadQuotaAsync(int port, string csrfToken) {
        using var handler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri?.IsLoopback == true
        };
        using var client = new HttpClient(handler) {
            Timeout = TimeSpan.FromSeconds(2)
        };

        foreach (string method in QuotaMethods) {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://127.0.0.1:{port}/{ServicePath}/{method}");
            request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
            if (!string.IsNullOrWhiteSpace(csrfToken)) {
                request.Headers.TryAddWithoutValidation("X-Codeium-Csrf-Token", csrfToken);
            }
            request.Content = new StringContent(RequestJson, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) {
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using JsonDocument document = await JsonDocument.ParseAsync(stream);
            AntigravityQuota? quota = ParseQuota(document.RootElement);
            if (quota is not null) {
                return quota;
            }
        }

        return null;
    }

    /// <summary>
    /// 선택한 모델 그룹의 가장 제한적인 잔여 비율과 초기화 시각을 창별로 선택합니다.
    /// Frontier는 주간 창만 표시하며 일반 Gemini 및 Claude/GPT는 5시간과 주간 창을 표시합니다.
    /// </summary>
    AntigravityQuota? ParseQuota(JsonElement root) {
        var hourlyWindows = new List<QuotaWindow>();
        var weeklyWindows = new List<QuotaWindow>();
        CollectQuotaWindows(root, "", false, hourlyWindows, weeklyWindows);

        if ((HasShortQuota && hourlyWindows.Count == 0) || weeklyWindows.Count == 0) {
            return null;
        }

        QuotaWindow? hourly = hourlyWindows.OrderBy(window => window.Fraction).ThenByDescending(window => window.ResetAt).FirstOrDefault();
        QuotaWindow weekly = weeklyWindows.OrderBy(window => window.Fraction).ThenByDescending(window => window.ResetAt).First();
        return new AntigravityQuota {
            Short = hourly is null ? 0 : Math.Clamp(hourly.Fraction * 100, 0, 100),
            Long = Math.Clamp(weekly.Fraction * 100, 0, 100),
            ShortResetAt = HasShortQuota ? hourly?.ResetAt : null,
            LongResetAt = weekly.ResetAt
        };
    }

    /// <summary>
    /// JSON 트리를 순회하며 선택한 모델 그룹의 잔여 비율과 초기화 시각을 수집하고 상위 객체의 창 구분을 유지합니다.
    /// </summary>
    void CollectQuotaWindows(
        JsonElement element,
        string inheritedName,
        bool inheritedWeekly,
        ICollection<QuotaWindow> hourlyWindows,
        ICollection<QuotaWindow> weeklyWindows) {
        if (element.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement item in element.EnumerateArray()) {
                CollectQuotaWindows(item, inheritedName, inheritedWeekly, hourlyWindows, weeklyWindows);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            return;
        }

        string localName = ReadFirstString(
            element,
            "displayName",
            "label",
            "modelId",
            "modelName",
            "model_name",
            "model",
            "name",
            "bucketId",
            "id");
        string currentName = string.IsNullOrWhiteSpace(localName)
            ? inheritedName
            : $"{inheritedName} {localName}".Trim();

        bool isWeekly = inheritedWeekly || IsWeeklyWindow(element, localName);
        string bucketId = ReadFirstString(element, "bucketId");
        if (TryReadRemainingFraction(element, out double fraction)) {
            if (MatchesModelGroup(string.IsNullOrWhiteSpace(bucketId) ? currentName : bucketId)) {
                ICollection<QuotaWindow> bucket = isWeekly ? weeklyWindows : hourlyWindows;
                bucket.Add(new QuotaWindow(fraction, ReadResetAt(element)));
            }
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject()) {
            CollectQuotaWindows(property.Value, currentName, isWeekly, hourlyWindows, weeklyWindows);
        }
    }

    /// <summary>
    /// 객체의 window/bucketId 필드 또는 그룹 이름으로부터 주어진 창이 weekly인지 판정합니다.
    /// </summary>
    static bool IsWeeklyWindow(JsonElement element, string localName) {
        if (element.TryGetProperty("window", out JsonElement window)
            && window.ValueKind == JsonValueKind.String
            && IsWeeklyToken(window.GetString())) {
            return true;
        }

        if (element.TryGetProperty("bucketId", out JsonElement bucketId)
            && bucketId.ValueKind == JsonValueKind.String
            && IsWeeklyToken(bucketId.GetString())) {
            return true;
        }

        return IsWeeklyToken(localName);
    }

    /// <summary>
    /// 창 토큰이 weekly(7d/168h 등)를 포함하면 true를 반환합니다.
    /// </summary>
    static bool IsWeeklyToken(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "weekly" or "week" or "wk" or "7d" or "168h";
    }

    /// <summary>
    /// 버킷 식별자 또는 모델 이름으로 Frontier를 먼저 구분하여 일반 Gemini와 섞이지 않도록 합니다.
    /// </summary>
    bool MatchesModelGroup(string identifier) {
        if (identifier.Contains("argon", StringComparison.OrdinalIgnoreCase)
            || identifier.Contains("frontier", StringComparison.OrdinalIgnoreCase)) {
            return modelGroup == AntigravityModelGroup.GeminiFrontier;
        }

        if (identifier.StartsWith("3p-", StringComparison.OrdinalIgnoreCase)
            || identifier.Contains("claude", StringComparison.OrdinalIgnoreCase)
            || identifier.Contains("gpt", StringComparison.OrdinalIgnoreCase)) {
            return modelGroup == AntigravityModelGroup.ClaudeGpt;
        }

        return modelGroup == AntigravityModelGroup.Gemini
            && identifier.Contains("gemini", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 객체 자체 또는 remaining/quotaInfo 하위 객체에서 remainingFraction을 읽습니다.
    /// </summary>
    static bool TryReadRemainingFraction(JsonElement element, out double fraction) {
        if (element.TryGetProperty("remainingFraction", out JsonElement direct)
            && direct.ValueKind == JsonValueKind.Number
            && direct.TryGetDouble(out fraction)) {
            return true;
        }

        if (element.TryGetProperty("remainingFraction", out direct)
            && direct.ValueKind == JsonValueKind.String
            && double.TryParse(direct.GetString(), out fraction)) {
            return true;
        }

        foreach (string containerName in new[] { "remaining", "quotaInfo" }) {
            if (element.TryGetProperty(containerName, out JsonElement container)
                && container.ValueKind == JsonValueKind.Object
                && container.TryGetProperty("remainingFraction", out JsonElement nested)
                && nested.ValueKind == JsonValueKind.Number
                && nested.TryGetDouble(out fraction)) {
                return true;
            }

            if (element.TryGetProperty(containerName, out container)
                && container.ValueKind == JsonValueKind.Object
                && container.TryGetProperty("remainingFraction", out nested)
                && nested.ValueKind == JsonValueKind.String
                && double.TryParse(nested.GetString(), out fraction)) {
                return true;
            }
        }

        fraction = 0;
        return false;
    }

    /// <summary>
    /// 잔여 비율이 있는 객체 또는 remaining/quotaInfo 하위 객체의 초기화 시각을 UTC로 읽습니다.
    /// </summary>
    static DateTimeOffset? ReadResetAt(JsonElement element) {
        if (element.TryGetProperty("remainingFraction", out _)) {
            string resetTime = ReadFirstString(element, "resetTime");
            return DateTimeOffset.TryParse(resetTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset resetAt)
                ? resetAt
                : null;
        }

        foreach (string containerName in new[] { "remaining", "quotaInfo" }) {
            if (element.TryGetProperty(containerName, out JsonElement container)
                && container.ValueKind == JsonValueKind.Object
                && container.TryGetProperty("remainingFraction", out _)) {
                return ReadResetAt(container);
            }
        }

        return null;
    }

    /// <summary>
    /// 지정된 후보 속성 중 첫 문자열 값을 반환합니다.
    /// </summary>
    static string ReadFirstString(JsonElement element, params string[] propertyNames) {
        foreach (string propertyName in propertyNames) {
            if (element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.String) {
                return property.GetString() ?? "";
            }
        }

        return "";
    }

    [GeneratedRegex("--(?:extension_server_)?csrf_token(?:=|\\s+)(?:\\\"(?<quoted>[^\\\"]+)\\\"|(?<plain>[^\\s]+))", RegexOptions.IgnoreCase)]
    private static partial Regex CsrfTokenRegex();

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        bool order,
        AddressFamily addressFamily,
        int tableClass,
        uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        ref int returnLength);

    public sealed class AntigravityQuota : ILlmProvider.Quota;

    sealed record QuotaWindow(double Fraction, DateTimeOffset? ResetAt);

    sealed record AntigravityProcess(int ProcessId, string CsrfToken);

    [StructLayout(LayoutKind.Sequential)]
    struct TcpRowOwnerPid {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public int OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    const int TcpTableOwnerPidListener = 3;
    const int ProcessCommandLineInformation = 60;
    const uint ProcessQueryLimitedInformation = 0x1000;
}
