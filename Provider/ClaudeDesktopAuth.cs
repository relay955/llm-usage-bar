using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LLMUsageBar.Provider;

/// <summary>
/// 현재 Windows 사용자의 Claude Desktop OAuth 캐시를 읽기 전용으로 해석합니다.
/// </summary>
static class ClaudeDesktopAuth {
    const string ClaudeCodeClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>
    /// Desktop 데이터 폴더를 찾고 현재 계정의 유효한 토큰을 메모리에서 복호화합니다.
    /// </summary>
    public static string ReadAccessToken(string configuredDirectory) {
        string directory = ResolveDataDirectory(configuredDirectory);
        using JsonDocument config = ReadJson(Path.Combine(directory, "config.json"));
        using JsonDocument state = ReadJson(Path.Combine(directory, "Local State"));
        string cache = ReadString(config.RootElement, "oauth:tokenCacheV2");
        bool isVersionTwo = !string.IsNullOrWhiteSpace(cache);
        if (!isVersionTwo) {
            cache = ReadString(config.RootElement, "oauth:tokenCache");
        }
        if (string.IsNullOrWhiteSpace(cache)) {
            throw new InvalidOperationException("No Claude Desktop OAuth cache. Open Claude Desktop and sign in.");
        }

        if (!state.RootElement.TryGetProperty("os_crypt", out JsonElement osCrypt)) {
            throw new InvalidOperationException("Claude Desktop encryption key not found.");
        }
        byte[] protectedKey = Convert.FromBase64String(ReadString(osCrypt, "encrypted_key"));
        if (!protectedKey.AsSpan().StartsWith("DPAPI"u8)) {
            throw new InvalidOperationException("Unsupported Claude Desktop key protection.");
        }

        byte[] key = UnprotectKey(protectedKey[5..]);
        try {
            byte[] encrypted = Convert.FromBase64String(cache);
            if (encrypted.Length < 31 || !encrypted.AsSpan().StartsWith("v10"u8)) {
                throw new InvalidOperationException("Unsupported Claude Desktop token encryption.");
            }
            byte[] plaintext = new byte[encrypted.Length - 31];
            try {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(encrypted.AsSpan(3, 12), encrypted.AsSpan(15, plaintext.Length),
                    encrypted.AsSpan(encrypted.Length - 16), plaintext);
                using JsonDocument tokens = JsonDocument.Parse(plaintext);
                return SelectToken(tokens.RootElement,
                    ReadString(config.RootElement, "lastKnownAccountUuid"), isVersionTwo);
            }
            finally {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException) {
            throw new InvalidOperationException("Cannot decrypt Claude Desktop login. Run LLMUsageBar as the same Windows user as Claude Desktop.");
        }
        finally {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// 명시한 폴더를 우선 사용하고 새 Desktop, 직접 설치, Store 설치 경로 순서로 탐색합니다.
    /// </summary>
    static string ResolveDataDirectory(string configuredDirectory) {
        if (!string.IsNullOrWhiteSpace(configuredDirectory)) {
            string path = Environment.ExpandEnvironmentVariables(configuredDirectory.Trim());
            if (path.StartsWith("~")) {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    path[1..].TrimStart('\\', '/'));
            }
            if (!IsDataDirectory(path)) {
                throw new InvalidOperationException("Claude Desktop folder must contain config.json and Local State.");
            }
            return path;
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string migratedRoot = Path.Combine(local, "Claude-Data");
        string? migrated = SelectDirectory(Directory.Exists(migratedRoot)
            ? Directory.EnumerateDirectories(migratedRoot, "claude*")
            : []);
        if (migrated is not null) {
            return migrated;
        }

        string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
        if (IsDataDirectory(classic)) {
            return classic;
        }

        string packages = Path.Combine(local, "Packages");
        string? store = SelectDirectory(Directory.Exists(packages)
            ? Directory.EnumerateDirectories(packages, "Claude_*")
                .Select(path => Path.Combine(path, "LocalCache", "Roaming", "Claude"))
            : []);
        return store ?? throw new InvalidOperationException("Claude Desktop data folder not found. Open Claude Desktop and sign in.");
    }

    /// <summary>
    /// 유효한 후보가 여러 개면 다른 계정의 인증을 임의로 선택하지 않고 명시적 경로를 요구합니다.
    /// </summary>
    static string? SelectDirectory(IEnumerable<string> candidates) {
        string[] matches = candidates.Where(IsDataDirectory).Take(2).ToArray();
        if (matches.Length > 1) {
            throw new InvalidOperationException("Multiple Claude Desktop folders found. Set the Desktop data folder in settings.");
        }
        return matches.FirstOrDefault();
    }

    /// <summary>
    /// Desktop 인증을 읽는 데 필요한 두 파일이 모두 있는지 확인합니다.
    /// </summary>
    static bool IsDataDirectory(string path) {
        return File.Exists(Path.Combine(path, "config.json")) && File.Exists(Path.Combine(path, "Local State"));
    }

    /// <summary>
    /// Desktop의 파일 교체와 쓰기를 허용하는 공유 모드로 JSON을 읽습니다.
    /// </summary>
    static JsonDocument ReadJson(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonDocument.Parse(stream);
    }

    /// <summary>
    /// 현재 계정 및 Claude Code 클라이언트에 속한 추론 토큰만 선택하고 만료를 확인합니다.
    /// </summary>
    static string SelectToken(JsonElement root, string accountUuid, bool isVersionTwo) {
        if (root.ValueKind != JsonValueKind.Object || (isVersionTwo && string.IsNullOrWhiteSpace(accountUuid))) {
            throw new InvalidOperationException("Claude Desktop account not found. Sign in again with Claude Desktop.");
        }
        string prefix = isVersionTwo ? $"acct:{accountUuid}|{ClaudeCodeClientId}:" : $"{ClaudeCodeClientId}:";
        JsonProperty[] entries = root.EnumerateObject()
            .Where(entry => entry.Name.StartsWith(prefix, StringComparison.Ordinal)
                && entry.Name.Contains(":https://api.anthropic.com:", StringComparison.Ordinal)
                && HasScope(entry.Name, "user:inference")
                && entry.Value.ValueKind == JsonValueKind.Object)
            .ToArray();
        JsonProperty[] codeEntries = entries.Where(entry => HasScope(entry.Name, "user:sessions:claude_code")).ToArray();
        if (codeEntries.Length > 0) {
            entries = codeEntries;
        }
        if (entries.Length != 1) {
            throw new InvalidOperationException("No unique Claude Desktop Code login found. Open the Code tab in Claude Desktop and sign in.");
        }
        JsonElement token = entries[0].Value;
        string accessToken = ReadString(token, "token");
        if (string.IsNullOrWhiteSpace(accessToken)) {
            throw new InvalidOperationException("Claude Desktop access token not found.");
        }
        if (!token.TryGetProperty("expiresAt", out JsonElement expiry)
            || expiry.ValueKind != JsonValueKind.Number
            || !expiry.TryGetInt64(out long expiresAt)
            || expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) {
            throw new InvalidOperationException("Claude Desktop token expired. Open Claude Desktop to renew your login.");
        }
        return accessToken;
    }

    /// <summary>
    /// 캐시 키의 API 대상 뒤에 있는 공백 구분 범위 목록에서 정확한 권한 이름을 확인합니다.
    /// </summary>
    static bool HasScope(string cacheKey, string scope) {
        const string audience = ":https://api.anthropic.com:";
        int index = cacheKey.IndexOf(audience, StringComparison.Ordinal);
        return index >= 0 && cacheKey[(index + audience.Length)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);
    }

    /// <summary>
    /// JSON 객체에서 문자열 속성만 읽습니다.
    /// </summary>
    static string ReadString(JsonElement element, string name) {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    }

    /// <summary>
    /// Windows DPAPI로 현재 사용자에게 보호된 AES 키를 복호화하고 네이티브 메모리를 해제합니다.
    /// </summary>
    static byte[] UnprotectKey(byte[] encrypted) {
        var input = new DataBlob { Length = encrypted.Length, Data = Marshal.AllocHGlobal(encrypted.Length) };
        var output = new DataBlob();
        try {
            Marshal.Copy(encrypted, 0, input.Data, encrypted.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)) {
                throw new InvalidOperationException("Cannot read Claude Desktop key. Run LLMUsageBar as the same Windows user as Claude Desktop.");
            }
            byte[] key = new byte[output.Length];
            Marshal.Copy(output.Data, key, 0, key.Length);
            return key;
        }
        finally {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DataBlob {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);
}
