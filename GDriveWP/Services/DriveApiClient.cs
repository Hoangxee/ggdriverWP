using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace GDriveWP.Services
{
    public sealed class DriveApiException : Exception
    {
        public int Status { get; private set; }
        public DriveApiException(int status, string message) : base(message) { Status = status; }
    }

    /// <summary>Thin Google Drive REST v3 client (list / get / create / update / delete / upload / download).</summary>
    public static class DriveApiClient
    {
        private const string Api = "https://www.googleapis.com/drive/v3/";
        private const string UploadApi = "https://www.googleapis.com/upload/drive/v3/files";
        public const string FileFields = "id,name,mimeType,size,modifiedTime,viewedByMeTime,sharedWithMeTime,parents,starred,trashed,shared,ownedByMe,owners(displayName),sharingUser(displayName),thumbnailLink,hasThumbnail,webViewLink,fileExtension,shortcutDetails,capabilities(canEdit,canTrash,canDelete,canRename,canShare)";
        private const string ListFields = "nextPageToken,files(" + FileFields + ")";
        private const int ChunkSize = 1024 * 1024;   // resumable upload chunk (multiple of 256 KB; small for 512 MB phones)

        public static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromMinutes(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("GDriveWP/1.0 (Windows Phone 8.1)");
            return c;
        }

        // ---------------------------------------------------------------
        // core request with token refresh on 401
        // ---------------------------------------------------------------

        private static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, HttpCompletionOption option, CancellationToken ct)
        {
            string token = await GoogleAuth.GetAccessTokenAsync();
            if (token == null) throw new DriveApiException(401, "Chưa đăng nhập");
            var req = make();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await Http.SendAsync(req, option, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                resp.Dispose();
                token = await GoogleAuth.RefreshAsync();
                if (token == null) throw new DriveApiException(401, "Phiên đăng nhập đã hết hạn");
                req = make();
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                resp = await Http.SendAsync(req, option, ct);
            }
            return resp;
        }

        private static async Task<JObject> JsonAsync(Func<HttpRequestMessage> make, CancellationToken ct)
        {
            using (var resp = await SendAsync(make, HttpCompletionOption.ResponseContentRead, ct))
            {
                string body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) throw MakeError(resp, body);
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
        }

        private static DriveApiException MakeError(HttpResponseMessage resp, string body)
        {
            string msg = null;
            try { msg = (string)JObject.Parse(body)["error"]["message"]; } catch { }
            int status = (int)resp.StatusCode;
            if (status == 403 && body.Contains("accessNotConfigured")) msg = "Chưa bật Google Drive API cho dự án Google Cloud này.";
            else if (status == 403 && (body.Contains("insufficientPermissions") || body.Contains("insufficientFilePermissions"))) msg = "Không đủ quyền (phạm vi đăng nhập hạn chế hoặc tệp chỉ đọc).";
            else if (status == 404) msg = "Không tìm thấy tệp.";
            else if (status == 429) msg = "Google đang giới hạn yêu cầu. Thử lại sau.";
            DiagLog.Write("API " + status + " " + (body.Length > 300 ? body.Substring(0, 300) : body));
            return new DriveApiException(status, msg ?? ("Lỗi HTTP " + status));
        }

        private static HttpRequestMessage Json(HttpMethod method, string url, JObject body)
        {
            var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            return req;
        }

        private static readonly HttpMethod Patch = new HttpMethod("PATCH");

        // ---------------------------------------------------------------
        // queries
        // ---------------------------------------------------------------

        public static string EscapeQuery(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        }

        public static async Task<FileListPage> ListAsync(string q, string orderBy, string pageToken, int pageSize, CancellationToken ct)
        {
            var sb = new StringBuilder(Api + "files?pageSize=" + pageSize + "&fields=" + Uri.EscapeDataString(ListFields) + "&spaces=drive");
            if (!string.IsNullOrEmpty(q)) sb.Append("&q=").Append(Uri.EscapeDataString(q));
            if (!string.IsNullOrEmpty(orderBy)) sb.Append("&orderBy=").Append(Uri.EscapeDataString(orderBy));
            if (!string.IsNullOrEmpty(pageToken)) sb.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));
            string url = sb.ToString();
            var json = await JsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            var page = new FileListPage { Files = new List<DriveFile>(), NextPageToken = (string)json["nextPageToken"] };
            var arr = json["files"] as JArray;
            if (arr != null) foreach (var j in arr) page.Files.Add(DriveFile.FromJson(j));
            return page;
        }

        public static async Task<DriveFile> GetAsync(string id, CancellationToken ct)
        {
            string url = Api + "files/" + Uri.EscapeDataString(id) + "?fields=" + Uri.EscapeDataString(FileFields);
            return DriveFile.FromJson(await JsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct));
        }

        public static async Task<AccountInfo> GetAboutAsync(CancellationToken ct)
        {
            string url = Api + "about?fields=" + Uri.EscapeDataString("user(displayName,emailAddress,photoLink),storageQuota(limit,usage,usageInDrive,usageInDriveTrash)");
            var json = await JsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            var u = json["user"] ?? new JObject();
            var q = json["storageQuota"] ?? new JObject();
            return new AccountInfo
            {
                DisplayName = (string)u["displayName"],
                Email = (string)u["emailAddress"],
                PhotoLink = (string)u["photoLink"],
                QuotaLimit = (long?)q["limit"] ?? 0,
                QuotaUsage = (long?)q["usage"] ?? 0,
                QuotaUsageInDrive = (long?)q["usageInDrive"] ?? 0,
                QuotaUsageInTrash = (long?)q["usageInDriveTrash"] ?? 0
            };
        }

        // ---------------------------------------------------------------
        // mutations
        // ---------------------------------------------------------------

        public static async Task<DriveFile> CreateFolderAsync(string name, string parentId, CancellationToken ct)
        {
            var body = new JObject { ["name"] = name, ["mimeType"] = DriveFile.FolderMime };
            if (!string.IsNullOrEmpty(parentId)) body["parents"] = new JArray(parentId);
            string url = Api + "files?fields=" + Uri.EscapeDataString(FileFields);
            return DriveFile.FromJson(await JsonAsync(() => Json(HttpMethod.Post, url, body), ct));
        }

        public static async Task<DriveFile> UpdateAsync(string id, JObject patch, string extraQuery, CancellationToken ct)
        {
            string url = Api + "files/" + Uri.EscapeDataString(id) + "?fields=" + Uri.EscapeDataString(FileFields) + (extraQuery ?? "");
            return DriveFile.FromJson(await JsonAsync(() => Json(Patch, url, patch), ct));
        }

        public static Task<DriveFile> RenameAsync(string id, string name, CancellationToken ct)
        {
            return UpdateAsync(id, new JObject { ["name"] = name }, null, ct);
        }

        public static Task<DriveFile> SetStarredAsync(string id, bool starred, CancellationToken ct)
        {
            return UpdateAsync(id, new JObject { ["starred"] = starred }, null, ct);
        }

        public static Task<DriveFile> SetTrashedAsync(string id, bool trashed, CancellationToken ct)
        {
            return UpdateAsync(id, new JObject { ["trashed"] = trashed }, null, ct);
        }

        public static Task<DriveFile> MoveAsync(string id, string newParent, string oldParent, CancellationToken ct)
        {
            string extra = "&addParents=" + Uri.EscapeDataString(newParent);
            if (!string.IsNullOrEmpty(oldParent)) extra += "&removeParents=" + Uri.EscapeDataString(oldParent);
            return UpdateAsync(id, new JObject(), extra, ct);
        }

        public static async Task DeleteForeverAsync(string id, CancellationToken ct)
        {
            string url = Api + "files/" + Uri.EscapeDataString(id);
            using (var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, url), HttpCompletionOption.ResponseContentRead, ct))
            {
                if (!resp.IsSuccessStatusCode) throw MakeError(resp, await resp.Content.ReadAsStringAsync());
            }
        }

        public static async Task EmptyTrashAsync(CancellationToken ct)
        {
            string url = Api + "files/trash";
            using (var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, url), HttpCompletionOption.ResponseContentRead, ct))
            {
                if (!resp.IsSuccessStatusCode) throw MakeError(resp, await resp.Content.ReadAsStringAsync());
            }
        }

        public static async Task<DriveFile> CopyAsync(string id, string name, CancellationToken ct)
        {
            string url = Api + "files/" + Uri.EscapeDataString(id) + "/copy?fields=" + Uri.EscapeDataString(FileFields);
            return DriveFile.FromJson(await JsonAsync(() => Json(HttpMethod.Post, url, new JObject { ["name"] = name }), ct));
        }

        /// <summary>"Anyone with the link can view" permission (what the Drive app's "Copy link" does).</summary>
        public static async Task ShareAnyoneReaderAsync(string id, CancellationToken ct)
        {
            string url = Api + "files/" + Uri.EscapeDataString(id) + "/permissions";
            await JsonAsync(() => Json(HttpMethod.Post, url, new JObject { ["role"] = "reader", ["type"] = "anyone" }), ct);
        }

        // ---------------------------------------------------------------
        // download (streamed into a StorageFile with progress)
        // ---------------------------------------------------------------

        public static string DownloadUrl(string id, string exportMime)
        {
            if (!string.IsNullOrEmpty(exportMime))
                return Api + "files/" + Uri.EscapeDataString(id) + "/export?mimeType=" + Uri.EscapeDataString(exportMime);
            return Api + "files/" + Uri.EscapeDataString(id) + "?alt=media";
        }

        public static async Task DownloadToFileAsync(string id, string exportMime, StorageFile target, Action<long, long> progress, CancellationToken ct)
        {
            string url = DownloadUrl(id, exportMime);
            using (var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode) throw MakeError(resp, await resp.Content.ReadAsStringAsync());
                long total = resp.Content.Headers.ContentLength ?? -1;
                using (var src = await resp.Content.ReadAsStreamAsync())
                using (var dst = await target.OpenStreamForWriteAsync())
                {
                    dst.SetLength(0);
                    var buffer = new byte[64 * 1024];
                    long done = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, read, ct);
                        done += read;
                        if (progress != null) progress(done, total);
                    }
                    await dst.FlushAsync();
                }
            }
        }

        /// <summary>Small downloads (thumbnails, avatars) straight into memory. Returns null on failure.</summary>
        public static async Task<byte[]> DownloadBytesAsync(string url, bool withAuth, CancellationToken ct)
        {
            try
            {
                HttpResponseMessage resp = withAuth
                    ? await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseContentRead, ct)
                    : await Http.GetAsync(url, ct);
                using (resp)
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return await resp.Content.ReadAsByteArrayAsync();
                }
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------
        // resumable upload (1 MB chunks)
        // ---------------------------------------------------------------

        public static async Task<DriveFile> UploadAsync(StorageFile source, string parentId, Action<long, long> progress, CancellationToken ct)
        {
            var props = await source.GetBasicPropertiesAsync();
            long total = (long)props.Size;
            string contentType = FileTypeInfo.ContentTypeFor(source.Name, source.ContentType);

            var meta = new JObject { ["name"] = source.Name };
            if (!string.IsNullOrEmpty(parentId)) meta["parents"] = new JArray(parentId);

            string sessionUrl;
            string startUrl = UploadApi + "?uploadType=resumable&fields=" + Uri.EscapeDataString(FileFields);
            using (var resp = await SendAsync(() =>
            {
                var r = Json(HttpMethod.Post, startUrl, meta);
                r.Headers.Add("X-Upload-Content-Type", contentType);
                r.Headers.Add("X-Upload-Content-Length", total.ToString());
                return r;
            }, HttpCompletionOption.ResponseContentRead, ct))
            {
                if (!resp.IsSuccessStatusCode) throw MakeError(resp, await resp.Content.ReadAsStringAsync());
                sessionUrl = resp.Headers.Location != null ? resp.Headers.Location.ToString() : null;
                if (sessionUrl == null) throw new DriveApiException(500, "Không nhận được phiên tải lên");
            }

            if (total == 0)
            {
                // Empty file: a single PUT with no body finishes the session
                using (var resp = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Put, sessionUrl) { Content = new ByteArrayContent(new byte[0]) }, ct))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) throw MakeError(resp, body);
                    return DriveFile.FromJson(JObject.Parse(body));
                }
            }

            var buffer = new byte[ChunkSize];
            using (var src = await source.OpenStreamForReadAsync())
            {
                long offset = 0;
                int retries = 0;
                while (offset < total)
                {
                    src.Seek(offset, SeekOrigin.Begin);
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = await src.ReadAsync(buffer, read, buffer.Length - read, ct);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read == 0) throw new DriveApiException(500, "Không đọc được tệp nguồn");

                    var content = new ByteArrayContent(buffer, 0, read);
                    content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + read - 1, total);
                    HttpResponseMessage resp;
                    try
                    {
                        resp = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Put, sessionUrl) { Content = content }, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        if (++retries > 3) throw new DriveApiException(0, "Mất kết nối khi tải lên: " + ex.Message);
                        await Task.Delay(2000 * retries, ct);
                        continue;
                    }
                    using (resp)
                    {
                        int status = (int)resp.StatusCode;
                        if (status == 308)
                        {
                            // Resume Incomplete: Range: bytes=0-N tells us what the server actually has
                            IEnumerable<string> ranges;
                            long confirmed = offset + read;
                            if (resp.Headers.TryGetValues("Range", out ranges))
                            {
                                foreach (var r in ranges)
                                {
                                    int dash = r.LastIndexOf('-');
                                    long end;
                                    if (dash > 0 && long.TryParse(r.Substring(dash + 1), out end)) confirmed = end + 1;
                                }
                            }
                            offset = confirmed;
                            retries = 0;
                            if (progress != null) progress(offset, total);
                            continue;
                        }
                        string body = await resp.Content.ReadAsStringAsync();
                        if (status == 200 || status == 201)
                        {
                            if (progress != null) progress(total, total);
                            return DriveFile.FromJson(JObject.Parse(body));
                        }
                        if (status == 401)
                        {
                            // Session URLs are pre-authorised, a 401 here means the token used for the start expired: refresh and retry the chunk
                            if (await GoogleAuth.RefreshAsync() == null || ++retries > 3) throw MakeError(resp, body);
                            continue;
                        }
                        if ((status >= 500 || status == 429) && ++retries <= 3)
                        {
                            await Task.Delay(2000 * retries, ct);
                            continue;
                        }
                        throw MakeError(resp, body);
                    }
                }
            }
            throw new DriveApiException(500, "Tải lên không hoàn tất");
        }
    }
}
