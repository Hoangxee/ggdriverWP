using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        private const long MaxImagePreviewBytes = 25L * 1024 * 1024;

        private DriveFile _viewerFile;
        private CancellationTokenSource _viewerCts;
        private StorageFile _previewFile;        // downloaded copy of the open file (LocalCacheFolder\preview)

        // ==========================================
        // OPEN / CLOSE
        // ==========================================

        private void OpenViewer(DriveFile file)
        {
            if (file == null) return;
            if (TopSubPage == ViewerPage) TeardownViewer();
            _viewerFile = file;
            ViewerTitle.Text = file.Name;
            ViewerBar.Visibility = Visibility.Visible;
            HideViewerPanes();
            OpenSubPage(ViewerPage);
            ApplyViewerStatusBar(true);
            var ignored = LoadPreviewAsync(file);
        }

        private void HideViewerPanes()
        {
            ImageViewerScroll.Visibility = Visibility.Collapsed;
            VideoPlayer.Visibility = Visibility.Collapsed;
            ViewerPdfBar.Visibility = Visibility.Collapsed;
            ViewerStatusPanel.Visibility = Visibility.Collapsed;
            ViewerUnsupportedPanel.Visibility = Visibility.Collapsed;
        }

        private void TeardownViewer()
        {
            var cts = _viewerCts;
            _viewerCts = null;
            if (cts != null) { try { cts.Cancel(); } catch { } }
            StopVideo();
            ViewerImage.Source = null;
            _previewFile = null;
            _viewerFile = null;
            HideViewerPanes();
            ApplyViewerStatusBar(false);
        }

        private void StopVideo()
        {
            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
            }
            catch { }
        }

        private void ViewerBackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSubPage();
        }

        private void ImageViewerScroll_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ViewerBar.Visibility = ViewerBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ViewerMoreButton_Click(object sender, RoutedEventArgs e)
        {
            var file = _viewerFile;
            if (file == null) return;
            try
            {
                var menu = new MenuFlyout();
                Add(menu, "Tải xuống", () => StartDownload(file));
                Add(menu, "Mở bằng ứng dụng khác", () => { var ignored = OpenExternalAsync(file); });
                if (!string.IsNullOrEmpty(file.WebViewLink)) Add(menu, "Mở trên web", () => { var ignored = Launcher.LaunchUriAsync(new Uri(file.WebViewLink)); });
                Add(menu, "Chia sẻ liên kết", () => { var ignored = ShareLinkAsync(file); });
                Add(menu, file.Starred ? "Bỏ dấu sao" : "Gắn dấu sao", () => { var ignored = ToggleStarAsync(file); });
                if (file.CanRename) Add(menu, "Đổi tên", () => { var ignored = RenameAsync(file); });
                Add(menu, "Chi tiết", () => ShowDetails(file));
                if (file.CanTrash) Add(menu, "Xoá", () => { var ignored = TrashAsync(file); });
                menu.ShowAt(ViewerMoreButton);
            }
            catch { }
        }

        private void OpenExternalButton_Click(object sender, RoutedEventArgs e)
        {
            var ignored = OpenExternalAsync(_viewerFile);
        }

        private void ViewerPdfOpenButton_Click(object sender, RoutedEventArgs e)
        {
            var ignored = OpenExternalAsync(_viewerFile);
        }

        private void ViewerDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            StartDownload(_viewerFile);
        }

        private async void ViewerWebButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewerFile == null || string.IsNullOrEmpty(_viewerFile.WebViewLink)) return;
            try { await Launcher.LaunchUriAsync(new Uri(_viewerFile.WebViewLink)); } catch { }
        }

        // ==========================================
        // PREVIEW PIPELINE
        // ==========================================

        private async Task LoadPreviewAsync(DriveFile file)
        {
            var cts = new CancellationTokenSource();
            _viewerCts = cts;
            var ct = cts.Token;
            try
            {
                string mime = file.EffectiveMime ?? "";
                bool isImage = mime.StartsWith("image/");
                bool isVideo = mime.StartsWith("video/");
                bool isAudio = mime.StartsWith("audio/");
                bool isPdf = mime == "application/pdf";
                bool isGoogleDoc = FileTypeInfo.IsGoogleDoc(mime) && mime != FileTypeInfo.GForm;

                if (isImage && file.Size > MaxImagePreviewBytes)
                {
                    ShowUnsupported(file, "Ảnh quá lớn để xem trước trên điện thoại (" + FileTypeInfo.FormatSize(file.Size) + ").");
                    return;
                }
                if (isPdf || isGoogleDoc)
                {
                    await ShowDocumentPreviewAsync(file, ct);
                    return;
                }
                if (!(isImage || isVideo || isAudio))
                {
                    ShowUnsupported(file, "Không xem trước được " + file.TypeName.ToLowerInvariant() + " trong ứng dụng.");
                    return;
                }

                var local = await EnsurePreviewFileAsync(file, null, null, ct);
                if (ct.IsCancellationRequested || local == null) return;
                _previewFile = local;

                if (isImage) await ShowImageAsync(local, file, ct);
                else await ShowMediaAsync(local, mime);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                DiagLog.Write("preview " + DiagLog.Describe(ex));
                if (HandleAuthFailure(ex)) return;
                ShowUnsupported(file, "Không tải được: " + Describe(ex));
            }
        }

        private void ShowUnsupported(DriveFile file, string text)
        {
            HideViewerPanes();
            ViewerTypeBadge.Background = file.TypeColor;
            ViewerTypeLabel.Text = file.TypeLabel;
            ViewerUnsupportedText.Text = text;
            ViewerWebButton.Visibility = ToVisibility(!string.IsNullOrEmpty(file.WebViewLink));
            ViewerUnsupportedPanel.Visibility = Visibility.Visible;
        }

        private static string SafeFileName(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// <summary>Preview cache name: id + modified time, so an edited file is fetched again.</summary>
        private static string PreviewName(DriveFile file, string ext)
        {
            string stamp = file.ModifiedTime != null ? file.ModifiedTime.Value.Ticks.ToString() : "0";
            if (string.IsNullOrEmpty(ext))
            {
                int dot = (file.Name ?? "").LastIndexOf('.');
                ext = dot >= 0 ? file.Name.Substring(dot + 1) : "bin";
            }
            return SafeFileName(file.Id) + "_" + stamp + "." + SafeFileName(ext);
        }

        private async Task<StorageFile> EnsurePreviewFileAsync(DriveFile file, string exportMime, string ext, CancellationToken ct)
        {
            var folder = await GetPreviewFolderAsync();
            string name = PreviewName(file, ext);
            try
            {
                var existing = await folder.GetFileAsync(name);
                if ((await existing.GetBasicPropertiesAsync()).Size > 0) return existing;
            }
            catch { }

            HideViewerPanes();
            ViewerProgress.IsIndeterminate = true;
            ViewerStatus.Text = "Đang tải xuống…";
            ViewerStatusPanel.Visibility = Visibility.Visible;

            var temp = await folder.CreateFileAsync(name + ".part", CreationCollisionOption.ReplaceExisting);
            try
            {
                string id = file.IsShortcut && !string.IsNullOrEmpty(file.ShortcutTargetId) ? file.ShortcutTargetId : file.Id;
                await DriveApiClient.DownloadToFileAsync(id, exportMime, temp, (done, total) =>
                {
                    if (!Dispatcher.HasThreadAccess) return;
                    if (total > 0)
                    {
                        ViewerProgress.IsIndeterminate = false;
                        ViewerProgress.Value = 100.0 * done / total;
                        ViewerStatus.Text = "Đang tải xuống… " + FileTypeInfo.FormatSize(done) + " / " + FileTypeInfo.FormatSize(total);
                    }
                    else
                    {
                        ViewerStatus.Text = "Đang tải xuống… " + FileTypeInfo.FormatSize(done);
                    }
                }, ct);
                await temp.RenameAsync(name, NameCollisionOption.ReplaceExisting);
                return temp;
            }
            catch
            {
                try { await temp.DeleteAsync(); } catch { }
                throw;
            }
        }

        // ---- image ----

        private async Task ShowImageAsync(StorageFile local, DriveFile file, CancellationToken ct)
        {
            ViewerStatus.Text = "Đang mở ảnh…";
            var bmp = new BitmapImage();
            if (file.Size > 3L * 1024 * 1024 || file.Size < 0) bmp.DecodePixelWidth = 1600;   // bound memory for big photos (Lumia 1020 shots are 5-10 MB)
            using (var stream = await local.OpenAsync(FileAccessMode.Read))
            {
                await bmp.SetSourceAsync(stream);
            }
            if (ct.IsCancellationRequested) return;
            ViewerImage.Source = bmp;
            HideViewerPanes();
            ImageViewerScroll.Visibility = Visibility.Visible;
            try { ImageViewerScroll.ChangeView(0, 0, 1.0f, true); } catch { }
        }

        // ---- video / audio ----

        private async Task ShowMediaAsync(StorageFile local, string mime)
        {
            var stream = await local.OpenAsync(FileAccessMode.Read);
            HideViewerPanes();
            VideoPlayer.Visibility = Visibility.Visible;
            VideoPlayer.SetSource(stream, mime);
            VideoPlayer.Play();
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            var file = _viewerFile;
            if (file == null) return;
            DiagLog.Write("media failed " + e.ErrorMessage);
            ShowUnsupported(file, "Không phát được tệp này (định dạng không được hỗ trợ trên Windows Phone).");
        }

        // ---- pdf / Google Docs ----

        /// <summary>
        /// Windows Phone 8.1 has no PDF renderer (Windows.Data.Pdf is desktop-only): show Google's first-page
        /// preview image in-app and offer the full document to the system PDF reader.
        /// </summary>
        private async Task ShowDocumentPreviewAsync(DriveFile file, CancellationToken ct)
        {
            HideViewerPanes();
            ViewerProgress.IsIndeterminate = true;
            ViewerStatus.Text = "Đang tải trang đầu…";
            ViewerStatusPanel.Visibility = Visibility.Visible;

            BitmapImage bmp = null;
            if (!string.IsNullOrEmpty(file.ThumbnailLink))
            {
                string link = file.ThumbnailLink;
                int eq = link.LastIndexOf("=s", StringComparison.Ordinal);
                if (eq > 0 && link.IndexOf('/', eq) < 0) link = link.Substring(0, eq) + "=s1200";
                byte[] bytes = await DriveApiClient.DownloadBytesAsync(link, true, ct);
                if (ct.IsCancellationRequested) return;
                bmp = await ThumbnailCache.LoadBitmapAsync(bytes, 0);
            }
            if (ct.IsCancellationRequested) return;
            if (bmp == null)
            {
                ShowUnsupported(file, "Không có bản xem trước. Mở toàn bộ tài liệu bằng trình đọc PDF của điện thoại.");
                return;
            }
            ViewerImage.Source = bmp;
            HideViewerPanes();
            ImageViewerScroll.Visibility = Visibility.Visible;
            ViewerPdfBar.Visibility = Visibility.Visible;
            ViewerPdfText.Text = file.IsPdf ? "Xem trước trang đầu" : "Xem trước trang đầu (xuất PDF)";
            try { ImageViewerScroll.ChangeView(0, 0, 1.0f, true); } catch { }
        }

        // ---- other apps ----

        /// <summary>Downloads (PDF export for Google Docs when previewing, Office formats otherwise) into the cache and hands the file to the system.</summary>
        private async Task OpenExternalAsync(DriveFile file)
        {
            if (file == null) return;
            var cts = new CancellationTokenSource();
            var old = _viewerCts;
            _viewerCts = cts;
            if (old != null) { try { old.Cancel(); } catch { } }
            bool wasImage = ImageViewerScroll.Visibility == Visibility.Visible;
            bool wasPdfBar = ViewerPdfBar.Visibility == Visibility.Visible;
            bool wasVideo = VideoPlayer.Visibility == Visibility.Visible;
            try
            {
                string exportMime = null, ext = null;
                FileTypeInfo.GetExport(file.EffectiveMime, wasPdfBar, out exportMime, out ext);
                var local = await EnsurePreviewFileAsync(file, exportMime, ext, cts.Token);
                if (local == null) return;
                HideViewerPanes();
                if (wasImage) { ImageViewerScroll.Visibility = Visibility.Visible; ViewerPdfBar.Visibility = ToVisibility(wasPdfBar); }
                else if (wasVideo) VideoPlayer.Visibility = Visibility.Visible;
                else ShowUnsupported(file, "Đã tải xuống. Đang mở bằng ứng dụng khác…");
                bool ok = await Launcher.LaunchFileAsync(local);
                if (!ok) ShowToast("Không có ứng dụng nào mở được loại tệp này. Cài \"PDF Reader\" từ Store.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowToast(Describe(ex));
            }
        }
    }
}
