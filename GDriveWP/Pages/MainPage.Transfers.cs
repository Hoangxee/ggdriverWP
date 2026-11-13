using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        // ==========================================
        // UPLOAD (FileOpenPicker ... AndContinue -> App.OnActivated -> ConsumeContinuation)
        // ==========================================

        private void StartUploadPicker()
        {
            try
            {
                string parent = _tab == DriveTab.Files && CurrentFolder != null ? CurrentFolder.Id : "root";
                ApplicationData.Current.LocalSettings.Values["PendingUploadParent"] = parent;
                ApplicationData.Current.LocalSettings.Values["PendingUploadParentName"] = _tab == DriveTab.Files && CurrentFolder != null ? CurrentFolder.Name : "Drive của tôi";
                var picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add("*");
                picker.PickMultipleFilesAndContinue();
            }
            catch (Exception ex)
            {
                DiagLog.Write("picker " + ex.Message);
                ShowToast("Không mở được bộ chọn tệp.");
            }
        }

        /// <summary>Called by App after a picker / share activation (and once at startup).</summary>
        public void ConsumeContinuation()
        {
            var uploads = App.PendingUploadFiles;
            App.PendingUploadFiles = null;
            var save = App.PendingSaveFile;
            bool saveCancelled = App.PendingSaveCancelled;
            App.PendingSaveFile = null;
            App.PendingSaveCancelled = false;

            if (uploads != null && uploads.Count > 0)
            {
                if (!GoogleAuth.IsSignedIn) { ShowToast("Đăng nhập để tải tệp lên."); }
                else
                {
                    var s = ApplicationData.Current.LocalSettings.Values;
                    object p; object pn;
                    string parent = s.TryGetValue("PendingUploadParent", out p) && p is string ? (string)p : "root";
                    string parentName = s.TryGetValue("PendingUploadParentName", out pn) && pn is string ? (string)pn : "Drive của tôi";
                    s.Remove("PendingUploadParent");
                    s.Remove("PendingUploadParentName");
                    var ignored = UploadFilesAsync(uploads, parent, parentName);
                }
            }

            if (save != null)
            {
                var s = ApplicationData.Current.LocalSettings.Values;
                object id, name, export;
                string fileId = s.TryGetValue("PendingDlId", out id) ? id as string : null;
                string fileName = s.TryGetValue("PendingDlName", out name) ? name as string : save.Name;
                string exportMime = s.TryGetValue("PendingDlExport", out export) ? export as string : null;
                if (!string.IsNullOrEmpty(fileId))
                {
                    var ignored = DownloadAsync(save, fileId, fileName, string.IsNullOrEmpty(exportMime) ? null : exportMime);
                }
            }
            if (save != null || saveCancelled)
            {
                var s = ApplicationData.Current.LocalSettings.Values;
                s.Remove("PendingDlId"); s.Remove("PendingDlName"); s.Remove("PendingDlExport");
            }
        }

        private async Task UploadFilesAsync(List<StorageFile> files, string parent, string parentName)
        {
            int ok = 0;
            foreach (var file in files)
            {
                var t = new TransferItem { Kind = TransferKind.Upload, Name = file.Name, Cts = new CancellationTokenSource() };
                try { t.Total = (long)(await file.GetBasicPropertiesAsync()).Size; } catch { }
                transfers.Add(t);
                try
                {
                    var created = await DriveApiClient.UploadAsync(file, parent, (done, total) => ReportProgress(t, done, total), t.Cts.Token);
                    t.Done = t.Total;
                    t.Status = "Đã tải lên " + parentName;
                    LearnRootId(created, parent);
                    InsertIfVisible(created, parent);
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    t.Status = "Đã huỷ";
                }
                catch (Exception ex)
                {
                    DiagLog.Write("upload " + DiagLog.Describe(ex));
                    t.Status = "Lỗi: " + Describe(ex);
                }
                var ignored = RemoveTransferLaterAsync(t);
            }
            if (files.Count > 1) ShowToast("Đã tải lên " + ok + "/" + files.Count + " tệp");
            else if (ok == 1) ShowToast("Đã tải lên \"" + files[0].Name + "\"");
        }

        private void ReportProgress(TransferItem t, long done, long total)
        {
            if (Dispatcher.HasThreadAccess)
            {
                if (total > 0) t.Total = total;
                t.Done = done;
            }
            else
            {
                var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { if (total > 0) t.Total = total; t.Done = done; });
            }
        }

        private async Task RemoveTransferLaterAsync(TransferItem t)
        {
            await Task.Delay(t.Status != null && t.Status.StartsWith("Lỗi") ? 12000 : 5000);
            if (!t.IsRunning) transfers.Remove(t);
        }

        // ==========================================
        // DOWNLOAD (FileSavePicker ... AndContinue)
        // ==========================================

        private void StartDownload(DriveFile file)
        {
            if (file == null || file.IsFolder) return;
            try
            {
                string exportMime, ext;
                string name = file.Name;
                if (FileTypeInfo.GetExport(file.EffectiveMime, false, out exportMime, out ext))
                {
                    name = file.Name + "." + ext;
                }
                else
                {
                    exportMime = null;
                    int dot = name.LastIndexOf('.');
                    ext = dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : (file.FileExtension ?? "");
                    if (ext.Length == 0) { ext = "dat"; name += ".dat"; }
                }
                var s = ApplicationData.Current.LocalSettings.Values;
                s["PendingDlId"] = file.IsShortcut ? file.ShortcutTargetId : file.Id;
                s["PendingDlName"] = name;
                s["PendingDlExport"] = exportMime ?? "";

                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = file.IsImage ? PickerLocationId.PicturesLibrary : (file.IsVideo ? PickerLocationId.VideosLibrary : PickerLocationId.Downloads);
                picker.SuggestedFileName = name;
                picker.FileTypeChoices.Add("Tệp " + ext.ToUpperInvariant(), new List<string> { "." + ext });
                picker.PickSaveFileAndContinue();
            }
            catch (Exception ex)
            {
                DiagLog.Write("savepicker " + ex.Message);
                ShowToast("Không mở được hộp thoại lưu tệp.");
            }
        }

        private async Task DownloadAsync(StorageFile target, string fileId, string name, string exportMime)
        {
            var t = new TransferItem { Kind = TransferKind.Download, Name = name, Cts = new CancellationTokenSource() };
            transfers.Add(t);
            try
            {
                await DriveApiClient.DownloadToFileAsync(fileId, exportMime, target, (done, total) => ReportProgress(t, done, total), t.Cts.Token);
                t.Total = t.Done;
                t.Status = "Đã lưu vào " + target.Path;
                ShowToast("Đã tải xuống \"" + target.Name + "\"");
            }
            catch (OperationCanceledException)
            {
                t.Status = "Đã huỷ";
                try { await target.DeleteAsync(); } catch { }
            }
            catch (Exception ex)
            {
                DiagLog.Write("download " + DiagLog.Describe(ex));
                t.Status = "Lỗi: " + Describe(ex);
                try { await target.DeleteAsync(); } catch { }
            }
            var ignored = RemoveTransferLaterAsync(t);
        }

        // ==========================================
        // ACTIVITY PANEL
        // ==========================================

        private void UpdateTransfersPanel()
        {
            TransfersPanel.Visibility = ToVisibility(transfers.Count > 0);
        }

        private void TransferClose_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var t = fe != null ? fe.Tag as TransferItem : null;
            if (t == null) return;
            if (t.IsRunning)
            {
                try { t.Cts.Cancel(); } catch { }
            }
            else
            {
                transfers.Remove(t);
            }
        }
    }
}
