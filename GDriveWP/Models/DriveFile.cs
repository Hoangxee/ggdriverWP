using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using GDriveWP.Services;

namespace GDriveWP
{
    /// <summary>One Drive v3 "files" resource as shown in the lists (folders included).</summary>
    public sealed class DriveFile : INotifyPropertyChanged
    {
        public const string FolderMime = "application/vnd.google-apps.folder";
        public const string ShortcutMime = "application/vnd.google-apps.shortcut";

        public string Id { get; set; }
        public string Name { get; set; }
        public string MimeType { get; set; }
        public long Size { get; set; }                 // -1 when unknown (Google Docs, folders)
        public DateTime? ModifiedTime { get; set; }
        public DateTime? ViewedByMeTime { get; set; }
        public DateTime? SharedWithMeTime { get; set; }
        public List<string> Parents { get; set; }
        public bool Trashed { get; set; }
        public bool Shared { get; set; }
        public bool OwnedByMe { get; set; }
        public string OwnerName { get; set; }
        public string SharingUserName { get; set; }
        public string ThumbnailLink { get; set; }
        public bool HasThumbnail { get; set; }
        public string WebViewLink { get; set; }
        public string FileExtension { get; set; }
        public string ShortcutTargetId { get; set; }
        public string ShortcutTargetMime { get; set; }
        public bool CanEdit { get; set; }
        public bool CanTrash { get; set; }
        public bool CanDelete { get; set; }
        public bool CanRename { get; set; }
        public bool CanShare { get; set; }

        private bool _starred;
        public bool Starred
        {
            get { return _starred; }
            set { if (_starred != value) { _starred = value; Raise("Starred"); Raise("StarVisibility"); } }
        }

        private BitmapImage _thumbnail;
        public BitmapImage Thumbnail
        {
            get { return _thumbnail; }
            set { _thumbnail = value; Raise("Thumbnail"); Raise("ThumbVisibility"); Raise("TypeIconVisibility"); }
        }
        public bool ThumbnailRequested { get; set; }

        public bool IsFolder { get { return MimeType == FolderMime; } }
        public bool IsShortcut { get { return MimeType == ShortcutMime; } }
        public bool IsGoogleDoc { get { return FileTypeInfo.IsGoogleDoc(MimeType); } }
        public bool IsImage { get { return MimeType != null && MimeType.StartsWith("image/"); } }
        public bool IsVideo { get { return MimeType != null && MimeType.StartsWith("video/"); } }
        public bool IsAudio { get { return MimeType != null && MimeType.StartsWith("audio/"); } }
        public bool IsPdf { get { return MimeType == "application/pdf"; } }

        /// <summary>Mime used for icons / preview (a shortcut shows as its target).</summary>
        public string EffectiveMime { get { return IsShortcut && !string.IsNullOrEmpty(ShortcutTargetMime) ? ShortcutTargetMime : MimeType; } }

        // ---- list row bindings ----
        public string TypeLabel { get { return FileTypeInfo.Label(EffectiveMime, Name); } }
        public SolidColorBrush TypeColor { get { return FileTypeInfo.Color(EffectiveMime, Name); } }
        public Visibility FolderIconVisibility { get { return IsFolder || EffectiveMime == FolderMime ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ThumbVisibility { get { return _thumbnail != null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility TypeIconVisibility { get { return !IsFolder && EffectiveMime != FolderMime && _thumbnail == null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility StarVisibility { get { return _starred ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility SharedVisibility { get { return Shared ? Visibility.Visible : Visibility.Collapsed; } }

        public string SizeText { get { return Size >= 0 ? FileTypeInfo.FormatSize(Size) : ""; } }

        /// <summary>"Bạn đã sửa • 12 thg 10" / "Nguyễn Văn A đã chia sẻ • 3,2 MB" like the Android app.</summary>
        public string SubText
        {
            get
            {
                string who;
                if (SharingUserName != null && !OwnedByMe) who = SharingUserName + " đã chia sẻ";
                else if (!OwnedByMe && !string.IsNullOrEmpty(OwnerName)) who = OwnerName + " đã sửa";
                else who = "Bạn đã sửa";
                string when = FileTypeInfo.FormatDate(ModifiedTime);
                string s = when.Length > 0 ? who + " • " + when : who;
                if (Size > 0 && !IsFolder) s += " • " + SizeText;
                return s;
            }
        }

        public string ModifiedText { get { return FileTypeInfo.FormatDateTime(ModifiedTime); } }
        public string TypeName { get { return FileTypeInfo.TypeName(EffectiveMime, Name); } }

        public static DriveFile FromJson(JToken j)
        {
            if (j == null) return null;
            var f = new DriveFile
            {
                Id = (string)j["id"],
                Name = (string)j["name"] ?? "",
                MimeType = (string)j["mimeType"] ?? "application/octet-stream",
                Size = ParseLong(j["size"]),
                ModifiedTime = ParseDate(j["modifiedTime"]),
                ViewedByMeTime = ParseDate(j["viewedByMeTime"]),
                SharedWithMeTime = ParseDate(j["sharedWithMeTime"]),
                Trashed = (bool?)j["trashed"] ?? false,
                Shared = (bool?)j["shared"] ?? false,
                OwnedByMe = (bool?)j["ownedByMe"] ?? true,
                ThumbnailLink = (string)j["thumbnailLink"],
                HasThumbnail = (bool?)j["hasThumbnail"] ?? false,
                WebViewLink = (string)j["webViewLink"],
                FileExtension = (string)j["fileExtension"],
                Parents = new List<string>()
            };
            f._starred = (bool?)j["starred"] ?? false;
            var parents = j["parents"] as JArray;
            if (parents != null) foreach (var p in parents) f.Parents.Add((string)p);
            var owners = j["owners"] as JArray;
            if (owners != null && owners.Count > 0) f.OwnerName = (string)owners[0]["displayName"];
            var su = j["sharingUser"];
            if (su != null) f.SharingUserName = (string)su["displayName"];
            var sc = j["shortcutDetails"];
            if (sc != null) { f.ShortcutTargetId = (string)sc["targetId"]; f.ShortcutTargetMime = (string)sc["targetMimeType"]; }
            var cap = j["capabilities"];
            if (cap != null)
            {
                f.CanEdit = (bool?)cap["canEdit"] ?? true;
                f.CanTrash = (bool?)cap["canTrash"] ?? true;
                f.CanDelete = (bool?)cap["canDelete"] ?? true;
                f.CanRename = (bool?)cap["canRename"] ?? true;
                f.CanShare = (bool?)cap["canShare"] ?? true;
            }
            else { f.CanEdit = f.CanTrash = f.CanDelete = f.CanRename = f.CanShare = true; }
            return f;
        }

        private static long ParseLong(JToken t)
        {
            if (t == null) return -1;
            long v;
            return long.TryParse(t.ToString(), out v) ? v : -1;
        }

        private static DateTime? ParseDate(JToken t)
        {
            if (t == null) return null;
            DateTime d;
            if (DateTime.TryParse(t.ToString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out d))
                return d.ToLocalTime();
            return null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
