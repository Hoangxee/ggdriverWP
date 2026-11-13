using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace GDriveWP.Services
{
    /// <summary>
    /// Drive thumbnails need the Bearer token, so BitmapImage cannot load them directly: they are fetched with the
    /// API client, written to LocalCacheFolder\thumbs\&lt;id&gt; and decoded small (2 downloads at a time, 512 MB phones).
    /// </summary>
    public static class ThumbnailCache
    {
        private const string FolderName = "thumbs";
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(2, 2);
        private static readonly Dictionary<string, BitmapImage> _memory = new Dictionary<string, BitmapImage>();
        private static StorageFolder _folder;

        private static async Task<StorageFolder> GetFolderAsync()
        {
            if (_folder == null)
                _folder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(FolderName, CreationCollisionOption.OpenIfExists);
            return _folder;
        }

        private static string SafeName(string id)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// <summary>Returns the row thumbnail (decoded to decodeWidth px) or null. Never throws.</summary>
        public static async Task<BitmapImage> GetAsync(DriveFile file, int decodeWidth, CancellationToken ct)
        {
            if (file == null || string.IsNullOrEmpty(file.Id) || string.IsNullOrEmpty(file.ThumbnailLink)) return null;
            BitmapImage cached;
            lock (_memory)
            {
                if (_memory.TryGetValue(file.Id, out cached)) return cached;
            }
            await _gate.WaitAsync(ct);
            try
            {
                var folder = await GetFolderAsync();
                string name = SafeName(file.Id);
                StorageFile f = null;
                try { f = await folder.GetFileAsync(name); } catch { }
                if (f == null)
                {
                    // Ask Google for a small variant: "=s220" suffix -> "=s128"
                    string link = file.ThumbnailLink;
                    int eq = link.LastIndexOf("=s", StringComparison.Ordinal);
                    if (eq > 0 && link.IndexOf('/', eq) < 0) link = link.Substring(0, eq) + "=s" + Math.Max(decodeWidth * 2, 128);
                    byte[] bytes = await DriveApiClient.DownloadBytesAsync(link, true, ct);
                    if (bytes == null || bytes.Length == 0) return null;
                    f = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(f, bytes);
                }
                var bmp = new BitmapImage { DecodePixelWidth = decodeWidth };
                using (var stream = await f.OpenAsync(FileAccessMode.Read))
                {
                    await bmp.SetSourceAsync(stream);
                }
                lock (_memory)
                {
                    if (_memory.Count > 300) _memory.Clear();
                    _memory[file.Id] = bmp;
                }
                return bmp;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                DiagLog.Write("thumb " + ex.Message);
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        public static async Task<BitmapImage> LoadBitmapAsync(byte[] bytes, int decodeWidth)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var bmp = new BitmapImage();
                if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                using (var ms = new InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }
                return bmp;
            }
            catch { return null; }
        }

        public static void ClearMemory()
        {
            lock (_memory) _memory.Clear();
        }

        public static async Task<ulong> GetSizeAsync()
        {
            ulong total = 0;
            try
            {
                var folder = await GetFolderAsync();
                foreach (var f in await folder.GetFilesAsync()) total += (await f.GetBasicPropertiesAsync()).Size;
            }
            catch { }
            return total;
        }

        public static async Task<int> ClearAsync()
        {
            int n = 0;
            ClearMemory();
            try
            {
                var folder = await GetFolderAsync();
                foreach (var f in await folder.GetFilesAsync())
                {
                    try { await f.DeleteAsync(); n++; } catch { }
                }
            }
            catch { }
            return n;
        }
    }
}
