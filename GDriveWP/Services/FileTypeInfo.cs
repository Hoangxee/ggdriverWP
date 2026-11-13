using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace GDriveWP.Services
{
    /// <summary>Mime type -> label / colour / display name / export format, plus size and date formatting.</summary>
    public static class FileTypeInfo
    {
        public const string GDoc = "application/vnd.google-apps.document";
        public const string GSheet = "application/vnd.google-apps.spreadsheet";
        public const string GSlides = "application/vnd.google-apps.presentation";
        public const string GDrawing = "application/vnd.google-apps.drawing";
        public const string GForm = "application/vnd.google-apps.form";

        private static readonly Dictionary<string, SolidColorBrush> _brushes = new Dictionary<string, SolidColorBrush>();

        private static SolidColorBrush Brush(string hex)
        {
            SolidColorBrush b;
            if (!_brushes.TryGetValue(hex, out b))
            {
                uint v = Convert.ToUInt32(hex.TrimStart('#'), 16);
                b = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v));
                _brushes[hex] = b;
            }
            return b;
        }

        public static bool IsGoogleDoc(string mime)
        {
            return mime != null && mime.StartsWith("application/vnd.google-apps.")
                && mime != DriveFile.FolderMime && mime != DriveFile.ShortcutMime;
        }

        private static string Ext(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.LastIndexOf('.');
            return i >= 0 && i < name.Length - 1 ? name.Substring(i + 1).ToLowerInvariant() : "";
        }

        /// <summary>Short text drawn inside the coloured type square ("PDF", "DOC", ...).</summary>
        public static string Label(string mime, string name)
        {
            mime = mime ?? "";
            string ext = Ext(name);
            if (mime == GDoc) return "DOC";
            if (mime == GSheet) return "SHT";
            if (mime == GSlides) return "SLD";
            if (mime == GForm) return "FRM";
            if (mime == GDrawing) return "DRW";
            if (mime == "application/pdf") return "PDF";
            if (mime.StartsWith("image/")) return "IMG";
            if (mime.StartsWith("video/")) return "VID";
            if (mime.StartsWith("audio/")) return "AUD";
            if (mime.StartsWith("text/") || ext == "txt" || ext == "md") return "TXT";
            if (mime.Contains("word") || ext == "doc" || ext == "docx" || ext == "odt") return "DOC";
            if (mime.Contains("sheet") || mime.Contains("excel") || ext == "xls" || ext == "xlsx" || ext == "csv") return "XLS";
            if (mime.Contains("presentation") || mime.Contains("powerpoint") || ext == "ppt" || ext == "pptx") return "PPT";
            if (mime.Contains("zip") || mime.Contains("rar") || mime.Contains("7z") || mime.Contains("compressed") || ext == "zip" || ext == "rar" || ext == "7z") return "ZIP";
            if (ext == "apk" || ext == "xap" || ext == "appx" || ext == "exe") return "APP";
            if (ext.Length > 0 && ext.Length <= 4) return ext.ToUpperInvariant();
            return "FILE";
        }

        /// <summary>Google Drive's per-type colours (Docs blue, Sheets green, Slides yellow, PDF red, ...).</summary>
        public static SolidColorBrush Color(string mime, string name)
        {
            string l = Label(mime, name);
            switch (l)
            {
                case "DOC": return Brush("#4285F4");
                case "SHT": case "XLS": return Brush("#0F9D58");
                case "SLD": case "PPT": return Brush("#F4B400");
                case "FRM": return Brush("#673AB7");
                case "DRW": return Brush("#DB4437");
                case "PDF": return Brush("#EA4335");
                case "IMG": return Brush("#D93025");
                case "VID": return Brush("#C5221F");
                case "AUD": return Brush("#E37400");
                case "TXT": return Brush("#5F6368");
                case "ZIP": return Brush("#7B1FA2");
                case "APP": return Brush("#009688");
                default: return Brush("#5F6368");
            }
        }

        public static string TypeName(string mime, string name)
        {
            mime = mime ?? "";
            if (mime == DriveFile.FolderMime) return "Thư mục";
            if (mime == GDoc) return "Google Tài liệu";
            if (mime == GSheet) return "Google Trang tính";
            if (mime == GSlides) return "Google Trang trình bày";
            if (mime == GForm) return "Google Biểu mẫu";
            if (mime == GDrawing) return "Google Bản vẽ";
            if (mime == DriveFile.ShortcutMime) return "Lối tắt";
            if (mime == "application/pdf") return "PDF";
            if (mime.StartsWith("image/")) return "Hình ảnh (" + mime.Substring(6).ToUpperInvariant() + ")";
            if (mime.StartsWith("video/")) return "Video";
            if (mime.StartsWith("audio/")) return "Âm thanh";
            string ext = Ext(name);
            return ext.Length > 0 ? "Tệp " + ext.ToUpperInvariant() : mime;
        }

        /// <summary>Export mime + extension for Google Docs types (download). Returns false for binary files.</summary>
        public static bool GetExport(string mime, bool forPreview, out string exportMime, out string extension)
        {
            exportMime = null; extension = null;
            if (!IsGoogleDoc(mime)) return false;
            if (forPreview) { exportMime = "application/pdf"; extension = "pdf"; return true; }
            switch (mime)
            {
                case GDoc: exportMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"; extension = "docx"; return true;
                case GSheet: exportMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"; extension = "xlsx"; return true;
                case GSlides: exportMime = "application/vnd.openxmlformats-officedocument.presentationml.presentation"; extension = "pptx"; return true;
                case GDrawing: exportMime = "image/png"; extension = "png"; return true;
                default: exportMime = "application/pdf"; extension = "pdf"; return true;
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "";
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            double gb = mb / 1024.0;
            if (gb < 1024) return gb.ToString("0.##") + " GB";
            return (gb / 1024.0).ToString("0.##") + " TB";
        }

        private static readonly string[] _months = { "thg 1", "thg 2", "thg 3", "thg 4", "thg 5", "thg 6", "thg 7", "thg 8", "thg 9", "thg 10", "thg 11", "thg 12" };

        /// <summary>Short relative date like the Drive app: "14:05" (today), "Hôm qua", "12 thg 10", "3 thg 2, 2024".</summary>
        public static string FormatDate(DateTime? d)
        {
            if (d == null) return "";
            var t = d.Value;
            var now = DateTime.Now;
            if (t.Date == now.Date) return t.ToString("HH:mm");
            if (t.Date == now.Date.AddDays(-1)) return "Hôm qua";
            if (t.Year == now.Year) return t.Day + " " + _months[t.Month - 1];
            return t.Day + " " + _months[t.Month - 1] + ", " + t.Year;
        }

        public static string FormatDateTime(DateTime? d)
        {
            if (d == null) return "";
            var t = d.Value;
            return t.Day + " " + _months[t.Month - 1] + ", " + t.Year + " " + t.ToString("HH:mm");
        }

        public static string ContentTypeFor(string name, string fallback)
        {
            switch (Ext(name))
            {
                case "jpg": case "jpeg": return "image/jpeg";
                case "png": return "image/png";
                case "gif": return "image/gif";
                case "bmp": return "image/bmp";
                case "mp4": case "m4v": return "video/mp4";
                case "3gp": return "video/3gpp";
                case "wmv": return "video/x-ms-wmv";
                case "mp3": return "audio/mpeg";
                case "m4a": return "audio/mp4";
                case "wav": return "audio/wav";
                case "pdf": return "application/pdf";
                case "txt": return "text/plain";
                case "zip": return "application/zip";
                case "docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case "xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case "pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                default: return string.IsNullOrEmpty(fallback) ? "application/octet-stream" : fallback;
            }
        }
    }
}
