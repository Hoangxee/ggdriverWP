using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace GDriveWP.Services
{
    public sealed class DeviceCodeInfo
    {
        public string DeviceCode;
        public string UserCode;
        public string VerificationUrl;
        public int ExpiresIn;
        public int Interval;
    }

    public enum DevicePollResult { Pending, SlowDown, Success, Failed, Expired }

    /// <summary>
    /// Google OAuth 2.0 for the phone, same approach as YTMusicWP:
    ///  * device-code flow (QR / code typed on google.com/device) -> only the "drive.file" scope is allowed there
    ///  * "paste the code" flow: the auth URL opens in the system browser (redirect http://localhost), the user
    ///    copies the "code" parameter of the final address into the app -> full "drive" scope.
    /// Tokens live in LocalSettings; the access token is refreshed automatically.
    /// </summary>
    public static class GoogleAuth
    {
        public const string ScopeLimited = "https://www.googleapis.com/auth/drive.file openid email profile";
        public const string ScopeFull = "https://www.googleapis.com/auth/drive openid email profile";
        private const string DeviceCodeUrl = "https://oauth2.googleapis.com/device/code";
        private const string TokenUrl = "https://oauth2.googleapis.com/token";
        private const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
        public const string LocalRedirect = "http://localhost";

        private static readonly HttpClient _http = new HttpClient();
        private static Task<string> _refreshing;

        private static Windows.Foundation.Collections.IPropertySet S { get { return ApplicationData.Current.LocalSettings.Values; } }

        private static string Get(string key)
        {
            try { object v; return S.TryGetValue(key, out v) && v != null ? v.ToString() : null; } catch { return null; }
        }

        public static string ClientId { get { return Get("GoogleClientId") ?? ""; } set { S["GoogleClientId"] = value ?? ""; } }
        public static string ClientSecret { get { return Get("GoogleClientSecret") ?? ""; } set { S["GoogleClientSecret"] = value ?? ""; } }
        public static bool HasClient { get { return ClientId.Length > 0 && ClientSecret.Length > 0; } }

        public static bool IsSignedIn { get { return !string.IsNullOrEmpty(Get("GoogleRefreshToken")) || !string.IsNullOrEmpty(Get("GoogleAccessToken")); } }

        /// <summary>true when the session was obtained with the full "drive" scope.</summary>
        public static bool FullAccess { get { return Get("GoogleScope") == ScopeFull; } }

        // -------- device-code flow --------

        public static async Task<DeviceCodeInfo> StartDeviceFlowAsync()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("scope", ScopeLimited)
            });
            var resp = await _http.PostAsync(DeviceCodeUrl, content);
            string body = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write("device/code " + (int)resp.StatusCode + " " + body);
                throw new Exception((string)json["error_description"] ?? (string)json["error"] ?? ("HTTP " + (int)resp.StatusCode));
            }
            return new DeviceCodeInfo
            {
                DeviceCode = (string)json["device_code"],
                UserCode = (string)json["user_code"],
                VerificationUrl = (string)json["verification_url"] ?? "https://www.google.com/device",
                ExpiresIn = (int?)json["expires_in"] ?? 1800,
                Interval = (int?)json["interval"] ?? 5
            };
        }

        public static async Task<DevicePollResult> PollDeviceOnceAsync(string deviceCode)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("client_secret", ClientSecret),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            });
            var resp = await _http.PostAsync(TokenUrl, content);
            string body = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);
            if (resp.IsSuccessStatusCode)
            {
                StoreTokens(json, ScopeLimited);
                return DevicePollResult.Success;
            }
            string err = (string)json["error"] ?? "";
            if (err == "authorization_pending") return DevicePollResult.Pending;
            if (err == "slow_down") return DevicePollResult.SlowDown;
            if (err == "expired_token") return DevicePollResult.Expired;
            DiagLog.Write("device poll " + body);
            LastError = (string)json["error_description"] ?? err;
            return DevicePollResult.Failed;
        }

        public static string LastError;

        // -------- paste-the-code flow (full scope) --------

        public static string BuildAuthUrl()
        {
            return AuthUrl + "?client_id=" + Uri.EscapeDataString(ClientId)
                + "&redirect_uri=" + Uri.EscapeDataString(LocalRedirect)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(ScopeFull)
                + "&access_type=offline&prompt=consent";
        }

        /// <summary>Accepts the bare code or the whole "http://localhost/?code=...&amp;scope=..." address.</summary>
        public static string ExtractCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string s = input.Trim();
            int i = s.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                s = s.Substring(i + 5);
                int amp = s.IndexOf('&');
                if (amp >= 0) s = s.Substring(0, amp);
                s = Uri.UnescapeDataString(s);
            }
            return s.Trim();
        }

        public static async Task ExchangeCodeAsync(string code)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("client_secret", ClientSecret),
                new KeyValuePair<string, string>("redirect_uri", LocalRedirect),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            });
            var resp = await _http.PostAsync(TokenUrl, content);
            string body = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write("token(code) " + (int)resp.StatusCode + " " + body);
                throw new Exception((string)json["error_description"] ?? (string)json["error"] ?? ("HTTP " + (int)resp.StatusCode));
            }
            string scope = (string)json["scope"] ?? ScopeFull;
            StoreTokens(json, scope.Contains("auth/drive ") || scope.EndsWith("auth/drive") ? ScopeFull : ScopeLimited);
        }

        // -------- tokens --------

        private static void StoreTokens(JObject json, string scope)
        {
            S["GoogleAccessToken"] = (string)json["access_token"] ?? "";
            string refresh = (string)json["refresh_token"];
            if (!string.IsNullOrEmpty(refresh)) S["GoogleRefreshToken"] = refresh;
            long expiresIn = (long?)json["expires_in"] ?? 3600;
            S["GoogleTokenExpiry"] = ToUnix(DateTime.UtcNow.AddSeconds(expiresIn - 60));
            S["GoogleScope"] = scope;
        }

        private static double ToUnix(DateTime utc)
        {
            return utc.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        /// <summary>Valid access token (refreshed when expired), or null when signed out / refresh failed.</summary>
        public static Task<string> GetAccessTokenAsync()
        {
            string token = Get("GoogleAccessToken");
            if (string.IsNullOrEmpty(token) && string.IsNullOrEmpty(Get("GoogleRefreshToken"))) return Task.FromResult<string>(null);
            double expiry = 0;
            try { object v; if (S.TryGetValue("GoogleTokenExpiry", out v)) expiry = Convert.ToDouble(v); } catch { }
            if (!string.IsNullOrEmpty(token) && ToUnix(DateTime.UtcNow) < expiry) return Task.FromResult(token);
            return RefreshAsync();
        }

        /// <summary>Refreshes the access token (one refresh at a time; concurrent callers share it).</summary>
        public static Task<string> RefreshAsync()
        {
            var running = _refreshing;
            if (running != null) return running;
            var t = RefreshCoreAsync();
            _refreshing = t;
            t.ContinueWith(x => { _refreshing = null; });
            return t;
        }

        private static async Task<string> RefreshCoreAsync()
        {
            string refresh = Get("GoogleRefreshToken");
            if (string.IsNullOrEmpty(refresh)) return null;
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", ClientId),
                    new KeyValuePair<string, string>("client_secret", ClientSecret),
                    new KeyValuePair<string, string>("refresh_token", refresh),
                    new KeyValuePair<string, string>("grant_type", "refresh_token")
                });
                var resp = await _http.PostAsync(TokenUrl, content);
                string body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    DiagLog.Write("refresh " + (int)resp.StatusCode + " " + body);
                    if (body.Contains("invalid_grant")) SignOut();   // revoked / expired refresh token
                    return null;
                }
                var json = JObject.Parse(body);
                S["GoogleAccessToken"] = (string)json["access_token"] ?? "";
                long expiresIn = (long?)json["expires_in"] ?? 3600;
                S["GoogleTokenExpiry"] = ToUnix(DateTime.UtcNow.AddSeconds(expiresIn - 60));
                return (string)json["access_token"];
            }
            catch (Exception ex)
            {
                DiagLog.Write("refresh " + ex.Message);
                return null;
            }
        }

        public static void SignOut()
        {
            string token = Get("GoogleRefreshToken") ?? Get("GoogleAccessToken");
            S.Remove("GoogleAccessToken");
            S.Remove("GoogleRefreshToken");
            S.Remove("GoogleTokenExpiry");
            S.Remove("GoogleScope");
            S.Remove("AccountName");
            S.Remove("AccountEmail");
            S.Remove("AccountPhoto");
            if (!string.IsNullOrEmpty(token))
            {
                var ignored = _http.PostAsync(RevokeUrl, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("token", token) }));
            }
        }
    }
}
