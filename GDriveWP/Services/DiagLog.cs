using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace GDriveWP
{
    /// <summary>
    /// Diagnostics: appends timestamped lines to LocalFolder\diag.log (read from the PC with
    /// ISETool ts appxfolder:Local de &lt;product-id&gt; &lt;dir&gt;). Writes are serialized and never throw.
    /// </summary>
    public static class DiagLog
    {
        private const string FileName = "diag.log";
        private const ulong MaxSize = 512 * 1024;
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public static void Write(string message)
        {
            var ignored = WriteAsync(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
        }

        private static async Task WriteAsync(string line)
        {
            await _lock.WaitAsync();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.OpenIfExists);
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    if (stream.Size > MaxSize) stream.Size = 0;
                    stream.Seek(stream.Size);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\r\n").AsBuffer());
                    await stream.FlushAsync();
                }
            }
            catch { }
            finally
            {
                _lock.Release();
            }
        }

        public static string Describe(Exception ex)
        {
            if (ex == null) return "null";
            var sb = new StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.Append(e.GetType().FullName).Append(": ").Append(e.Message).Append(" | ").Append(e.StackTrace).Append(" || ");
            }
            return sb.ToString();
        }
    }
}
