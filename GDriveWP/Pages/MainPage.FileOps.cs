using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        // ==========================================
        // CONTEXT MENU (⋮ / long press)
        // ==========================================

        private void RowMore_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var file = fe != null ? fe.Tag as DriveFile : null;
            if (file != null) ShowFileMenu(file, fe);
        }

        private void Row_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            var fe = sender as FrameworkElement;
            var file = fe != null ? fe.Tag as DriveFile : null;
            if (file != null) { e.Handled = true; ShowFileMenu(file, fe); }
        }

        private void ShowFileMenu(DriveFile file, FrameworkElement anchor)
        {
            try
            {
                var menu = new MenuFlyout();
                if (file.Trashed)
                {
                    Add(menu, "Khôi phục", () => { var ignored = RestoreAsync(file); });
                    Add(menu, "Xoá vĩnh viễn", () => { var ignored = DeleteForeverAsync(file); });
                    Add(menu, "Chi tiết", () => ShowDetails(file));
                    menu.ShowAt(anchor);
                    return;
                }
                Add(menu, file.IsFolder ? "Mở" : "Xem", () => OpenItem(file));
                if (!file.IsFolder && !file.IsShortcut) Add(menu, "Tải xuống", () => StartDownload(file));
                Add(menu, file.Starred ? "Bỏ dấu sao" : "Gắn dấu sao", () => { var ignored = ToggleStarAsync(file); });
                if (file.CanRename) Add(menu, "Đổi tên", () => { var ignored = RenameAsync(file); });
                if (file.CanEdit && file.OwnedByMe) Add(menu, "Di chuyển", () => StartMove(file));
                if (!file.IsFolder && !file.IsShortcut) Add(menu, "Tạo bản sao", () => { var ignored = CopyAsync(file); });
                Add(menu, "Chia sẻ liên kết", () => { var ignored = ShareLinkAsync(file); });
                Add(menu, "Chi tiết", () => ShowDetails(file));
                if (file.CanTrash) Add(menu, "Xoá", () => { var ignored = TrashAsync(file); });
                menu.ShowAt(anchor);
            }
            catch (Exception ex)
            {
                DiagLog.Write("menu " + ex.Message);
            }
        }

        private static void Add(MenuFlyout menu, string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (s, a) => action();
            menu.Items.Add(item);
        }

        // ==========================================
        // OPERATIONS
        // ==========================================

        private async Task NewFolderAsync()
        {
            if (_tab != DriveTab.Files) { SelectTab(DriveTab.Files); }
            string name = await ShowInputDialogAsync("Thư mục mới", "Thư mục không có tiêu đề", "Tạo");
            if (string.IsNullOrWhiteSpace(name)) return;
            string parent = CurrentFolder != null ? CurrentFolder.Id : "root";
            try
            {
                var created = await DriveApiClient.CreateFolderAsync(name.Trim(), parent, CancellationToken.None);
                LearnRootId(created, parent);
                InsertIfVisible(created, parent);
                ShowToast("Đã tạo thư mục \"" + created.Name + "\"");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        private void LearnRootId(DriveFile created, string parent)
        {
            if (parent == "root" && created != null && created.Parents != null && created.Parents.Count > 0) _rootId = created.Parents[0];
        }

        private async Task RenameAsync(DriveFile file)
        {
            string name = await ShowInputDialogAsync("Đổi tên", file.Name, "Đổi tên");
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == file.Name) return;
            try
            {
                var updated = await DriveApiClient.RenameAsync(file.Id, name.Trim(), CancellationToken.None);
                ReplaceInLists(updated);
                if (_viewerFile != null && _viewerFile.Id == updated.Id) ViewerTitle.Text = updated.Name;
                ShowToast("Đã đổi tên");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        private async Task ToggleStarAsync(DriveFile file)
        {
            bool target = !file.Starred;
            file.Starred = target;
            try
            {
                var updated = await DriveApiClient.SetStarredAsync(file.Id, target, CancellationToken.None);
                if (_tab == DriveTab.Starred && !target) RemoveFromLists(file.Id);
                else ReplaceInLists(updated);
                ShowToast(target ? "Đã gắn dấu sao" : "Đã bỏ dấu sao");
            }
            catch (Exception ex)
            {
                file.Starred = !target;
                ShowToast(Describe(ex));
            }
        }

        private async Task TrashAsync(DriveFile file)
        {
            try
            {
                await DriveApiClient.SetTrashedAsync(file.Id, true, CancellationToken.None);
                RemoveFromLists(file.Id);
                if (_viewerFile != null && _viewerFile.Id == file.Id && TopSubPage == ViewerPage) CloseSubPage();
                ShowToast("Đã chuyển \"" + file.Name + "\" vào thùng rác");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        private async Task RestoreAsync(DriveFile file)
        {
            try
            {
                await DriveApiClient.SetTrashedAsync(file.Id, false, CancellationToken.None);
                RemoveFromLists(file.Id);
                UpdateEmptyState();
                ShowToast("Đã khôi phục \"" + file.Name + "\"");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        private async Task DeleteForeverAsync(DriveFile file)
        {
            if (!await ConfirmAsync("Xoá vĩnh viễn?", "\"" + file.Name + "\" sẽ bị xoá vĩnh viễn và không thể khôi phục.", "Xoá vĩnh viễn")) return;
            try
            {
                await DriveApiClient.DeleteForeverAsync(file.Id, CancellationToken.None);
                RemoveFromLists(file.Id);
                UpdateEmptyState();
                ShowToast("Đã xoá vĩnh viễn");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        private async Task EmptyTrashAsync()
        {
            if (!await ConfirmAsync("Dọn sạch thùng rác?", "Tất cả các mục trong thùng rác sẽ bị xoá vĩnh viễn.", "Dọn sạch")) return;
            try
            {
                await DriveApiClient.EmptyTrashAsync(CancellationToken.None);
                items.Clear();
                UpdateEmptyState();
                ShowToast("Đã dọn sạch thùng rác");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        private async Task CopyAsync(DriveFile file)
        {
            try
            {
                ShowToast("Đang tạo bản sao…");
                var copy = await DriveApiClient.CopyAsync(file.Id, "Bản sao của " + file.Name, CancellationToken.None);
                string parent = copy.Parents != null && copy.Parents.Count > 0 ? copy.Parents[0] : "root";
                InsertIfVisible(copy, parent);
                ShowToast("Đã tạo \"" + copy.Name + "\"");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
        }

        // ==========================================
        // SHARE LINK (Windows Phone has no clipboard API: the link goes through the Share charm)
        // ==========================================

        private string _shareText;
        private string _shareTitle;

        private async Task ShareLinkAsync(DriveFile file)
        {
            if (string.IsNullOrEmpty(file.WebViewLink))
            {
                ShowToast("Tệp này không có liên kết web.");
                return;
            }
            if (file.CanShare && file.OwnedByMe && !file.Shared)
            {
                bool makePublic = await ConfirmAsync("Chia sẻ liên kết", "Cho phép bất kỳ ai có liên kết xem \"" + file.Name + "\"? (Chọn Huỷ để chỉ gửi liên kết cho người đã có quyền.)", "Cho phép");
                if (makePublic)
                {
                    try
                    {
                        await DriveApiClient.ShareAnyoneReaderAsync(file.Id, CancellationToken.None);
                        file.Shared = true;
                    }
                    catch (Exception ex) { ShowToast(Describe(ex)); }
                }
            }
            _shareTitle = file.Name;
            _shareText = file.WebViewLink;
            try { DataTransferManager.ShowShareUI(); }
            catch { ShowToast("Không mở được bảng chia sẻ."); }
        }

        private void MainPage_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            try
            {
                if (string.IsNullOrEmpty(_shareText))
                {
                    args.Request.FailWithDisplayText("Không có liên kết để chia sẻ.");
                    return;
                }
                args.Request.Data.Properties.Title = _shareTitle ?? "Google Drive";
                args.Request.Data.Properties.Description = "Liên kết Google Drive";
                args.Request.Data.SetText(_shareText);
                try { args.Request.Data.SetWebLink(new Uri(_shareText)); } catch { }
            }
            catch { }
        }

        // ==========================================
        // DETAILS
        // ==========================================

        private DriveFile _detailsFile;

        private void ShowDetails(DriveFile file)
        {
            if (file == null) return;
            _detailsFile = file;
            bool folder = file.IsFolder || file.EffectiveMime == DriveFile.FolderMime;
            DetailsFolderIcon.Visibility = ToVisibility(folder);
            DetailsTypeBadge.Visibility = ToVisibility(!folder);
            DetailsTypeBadge.Background = file.TypeColor;
            DetailsTypeLabel.Text = file.TypeLabel;
            DetailsName.Text = file.Name;
            DetailsType.Text = file.TypeName + (file.IsShortcut ? " → " + FileTypeInfo.TypeName(file.ShortcutTargetMime, file.Name) : "");
            DetailsSize.Text = file.Size >= 0 ? FileTypeInfo.FormatSize(file.Size) : (folder ? "—" : "Không áp dụng");
            DetailsModified.Text = file.ModifiedText.Length > 0 ? file.ModifiedText : "—";
            DetailsOwner.Text = file.OwnedByMe ? "Tôi" : (file.OwnerName ?? "—");
            DetailsLocation.Text = file.Trashed ? "Thùng rác" : (InFolder && _tab == DriveTab.Files ? CurrentFolder.Name : (file.OwnedByMe ? "Drive của tôi" : "Đã chia sẻ với tôi"));
            DetailsShared.Text = file.Shared ? "Đã chia sẻ" : "Riêng tư";
            DetailsId.Text = file.Id ?? "";
            DetailsOpenWebButton.Visibility = ToVisibility(!string.IsNullOrEmpty(file.WebViewLink));
            OpenSubPage(DetailsPage);
        }

        private async void DetailsOpenWebButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detailsFile == null || string.IsNullOrEmpty(_detailsFile.WebViewLink)) return;
            try { await Launcher.LaunchUriAsync(new Uri(_detailsFile.WebViewLink)); } catch { }
        }

        // ==========================================
        // MOVE PICKER
        // ==========================================

        private DriveFile _moveTarget;
        private readonly System.Collections.Generic.List<DriveFile> _moveStack = new System.Collections.Generic.List<DriveFile>();
        private CancellationTokenSource _moveCts;

        private void StartMove(DriveFile file)
        {
            _moveTarget = file;
            _moveStack.Clear();
            _moveStack.Add(RootFolder());
            MoveTitle.Text = "Di chuyển \"" + file.Name + "\"";
            OpenSubPage(MovePage);
            var ignored = LoadMoveFolderAsync();
        }

        private async Task LoadMoveFolderAsync()
        {
            var old = _moveCts;
            _moveCts = new CancellationTokenSource();
            if (old != null) { try { old.Cancel(); } catch { } }
            var ct = _moveCts.Token;

            var folder = _moveStack[_moveStack.Count - 1];
            MoveSubtitle.Text = folder.Name;
            moveItems.Clear();
            MoveEmptyText.Visibility = Visibility.Collapsed;
            MoveLoadingRing.IsActive = true;
            MoveLoadingRing.Visibility = Visibility.Visible;
            try
            {
                string q = "'" + DriveApiClient.EscapeQuery(folder.Id) + "' in parents and mimeType='" + DriveFile.FolderMime + "' and trashed=false";
                string token = null;
                do
                {
                    var page = await DriveApiClient.ListAsync(q, "name_natural", token, 100, ct);
                    if (ct.IsCancellationRequested) return;
                    foreach (var f in page.Files)
                        if (_moveTarget == null || f.Id != _moveTarget.Id) moveItems.Add(f);
                    token = page.NextPageToken;
                } while (!string.IsNullOrEmpty(token) && moveItems.Count < 500);
                MoveEmptyText.Visibility = ToVisibility(moveItems.Count == 0);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!ct.IsCancellationRequested) ShowToast(Describe(ex)); }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    MoveLoadingRing.IsActive = false;
                    MoveLoadingRing.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void MoveListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var folder = e.ClickedItem as DriveFile;
            if (folder == null) return;
            _moveStack.Add(folder);
            var ignored = LoadMoveFolderAsync();
        }

        private void MoveBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_moveStack.Count > 1 && sender == MoveBackButton)
            {
                _moveStack.RemoveAt(_moveStack.Count - 1);
                var ignored = LoadMoveFolderAsync();
                return;
            }
            CloseSubPage();
        }

        private async void MoveHereButton_Click(object sender, RoutedEventArgs e)
        {
            var file = _moveTarget;
            if (file == null) return;
            var dest = _moveStack[_moveStack.Count - 1];
            string oldParent = file.Parents != null && file.Parents.Count > 0 ? file.Parents[0] : null;
            if (dest.Id == oldParent || (dest.Id == "root" && oldParent == _rootId))
            {
                ShowToast("Tệp đã ở trong thư mục này.");
                return;
            }
            MoveHereButton.IsEnabled = false;
            try
            {
                var updated = await DriveApiClient.MoveAsync(file.Id, dest.Id, oldParent, CancellationToken.None);
                if (_tab == DriveTab.Files) RemoveFromLists(file.Id); else ReplaceInLists(updated);
                CloseSubPage();
                ShowToast("Đã di chuyển vào \"" + dest.Name + "\"");
            }
            catch (Exception ex) { ShowToast(Describe(ex)); }
            finally { MoveHereButton.IsEnabled = true; }
        }

        // ==========================================
        // INPUT DIALOG (await a text)
        // ==========================================

        private TaskCompletionSource<string> _inputTcs;

        private Task<string> ShowInputDialogAsync(string title, string initial, string okText)
        {
            FinishInputDialog(null);
            _inputTcs = new TaskCompletionSource<string>();
            InputDialogTitle.Text = title;
            InputDialogOk.Content = okText;
            InputDialogBox.Text = initial ?? "";
            InputDialog.Visibility = Visibility.Visible;
            try
            {
                InputDialogBox.Focus(FocusState.Programmatic);
                // Select the base name only (like the Drive rename dialog)
                int dot = (initial ?? "").LastIndexOf('.');
                InputDialogBox.Select(0, dot > 0 ? dot : (initial ?? "").Length);
            }
            catch { }
            return _inputTcs.Task;
        }

        private void FinishInputDialog(string result)
        {
            var tcs = _inputTcs;
            _inputTcs = null;
            InputDialog.Visibility = Visibility.Collapsed;
            if (tcs != null) tcs.TrySetResult(result);
        }

        private void InputDialogOk_Click(object sender, RoutedEventArgs e)
        {
            FinishInputDialog(InputDialogBox.Text);
        }

        private void InputDialogCancel_Click(object sender, RoutedEventArgs e)
        {
            FinishInputDialog(null);
        }

        private void InputDialogScrim_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FinishInputDialog(null);
        }

        private void InputDialogCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void InputDialogBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                FinishInputDialog(InputDialogBox.Text);
            }
        }
    }
}
