using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        private const int PageSize = 60;
        private string _nextPageToken;
        private bool _loadingMore;
        private bool _sortByName = false;
        private CancellationTokenSource _listCts;
        private ScrollViewer _listScroll;

        // ==========================================
        // QUERIES
        // ==========================================

        private string BuildQuery()
        {
            switch (_tab)
            {
                case DriveTab.Home:
                    return "trashed=false and mimeType!='" + DriveFile.FolderMime + "'";
                case DriveTab.Starred:
                    return "starred=true and trashed=false";
                case DriveTab.Shared:
                    return "sharedWithMe=true and trashed=false";
                case DriveTab.Trash:
                    return "trashed=true";
                default:
                    string parent = CurrentFolder != null ? CurrentFolder.Id : "root";
                    return "'" + DriveApiClient.EscapeQuery(parent) + "' in parents and trashed=false";
            }
        }

        private string BuildOrderBy()
        {
            switch (_tab)
            {
                case DriveTab.Home: return "recency desc";
                case DriveTab.Shared: return _sortByName ? "folder,name_natural" : "sharedWithMeTime desc";
                case DriveTab.Trash: return "modifiedTime desc";
                default: return _sortByName ? "folder,name_natural" : "folder,modifiedTime desc";
            }
        }

        // ==========================================
        // LOADING
        // ==========================================

        private async Task LoadCurrentAsync()
        {
            var old = _listCts;
            _listCts = new CancellationTokenSource();
            if (old != null) { try { old.Cancel(); } catch { } }
            var ct = _listCts.Token;

            _nextPageToken = null;
            _loadingMore = false;
            items.Clear();
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            LoadMoreBar.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            ScrollListToTop();

            try
            {
                var page = await DriveApiClient.ListAsync(BuildQuery(), BuildOrderBy(), null, PageSize, ct);
                if (ct.IsCancellationRequested) return;
                foreach (var f in page.Files) items.Add(f);
                _nextPageToken = page.NextPageToken;
                UpdateEmptyState();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                DiagLog.Write("LoadCurrent " + DiagLog.Describe(ex));
                if (HandleAuthFailure(ex)) return;
                ErrorText.Text = Describe(ex);
                ErrorPanel.Visibility = Visibility.Visible;
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task LoadMoreAsync()
        {
            if (_loadingMore || string.IsNullOrEmpty(_nextPageToken) || _listCts == null) return;
            var ct = _listCts.Token;
            _loadingMore = true;
            LoadMoreBar.Visibility = Visibility.Visible;
            try
            {
                var page = await DriveApiClient.ListAsync(BuildQuery(), BuildOrderBy(), _nextPageToken, PageSize, ct);
                if (ct.IsCancellationRequested) return;
                foreach (var f in page.Files) items.Add(f);
                _nextPageToken = page.NextPageToken;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) ShowToast(Describe(ex));
            }
            finally
            {
                _loadingMore = false;
                if (!ct.IsCancellationRequested) LoadMoreBar.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>401 that survived a refresh: the session is gone, back to the login screen.</summary>
        private bool HandleAuthFailure(Exception ex)
        {
            var api = ex as DriveApiException;
            if (api == null || api.Status != 401) return false;
            ShowToast("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.");
            ShowLogin();
            return true;
        }

        private void UpdateEmptyState()
        {
            bool empty = items.Count == 0;
            EmptyPanel.Visibility = ToVisibility(empty);
            if (!empty) return;
            switch (_tab)
            {
                case DriveTab.Home:
                    EmptyTitle.Text = "Chưa có tệp nào";
                    EmptyText.Text = GoogleAuth.FullAccess ? "Các tệp bạn mở gần đây sẽ xuất hiện ở đây." : "Đăng nhập bằng mã thiết bị chỉ thấy tệp do ứng dụng này tải lên. Dùng \"Toàn quyền Drive\" để xem tất cả.";
                    break;
                case DriveTab.Starred:
                    EmptyTitle.Text = "Chưa có mục gắn dấu sao";
                    EmptyText.Text = "Gắn dấu sao các tệp quan trọng để tìm nhanh.";
                    break;
                case DriveTab.Shared:
                    EmptyTitle.Text = "Chưa có tệp được chia sẻ";
                    EmptyText.Text = "Tệp và thư mục người khác chia sẻ với bạn sẽ xuất hiện ở đây.";
                    break;
                case DriveTab.Trash:
                    EmptyTitle.Text = "Thùng rác trống";
                    EmptyText.Text = "Các mục trong thùng rác bị xoá vĩnh viễn sau 30 ngày.";
                    break;
                default:
                    EmptyTitle.Text = InFolder ? "Thư mục trống" : "Drive của tôi trống";
                    EmptyText.Text = "Nhấn + để tải tệp lên hoặc tạo thư mục.";
                    break;
            }
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            var ignored = LoadCurrentAsync();
        }

        private void SortButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_tab == DriveTab.Home || _tab == DriveTab.Trash) return;
            _sortByName = !_sortByName;
            SortText.Text = _sortByName ? "Tên" : "Sửa gần đây";
            var ignored = LoadCurrentAsync();
        }

        // ==========================================
        // LIST EVENTS
        // ==========================================

        private void FileListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_listScroll != null) return;
            _listScroll = FindScrollViewer(FileListView);
            if (_listScroll != null) _listScroll.ViewChanged += ListScroll_ViewChanged;
        }

        private void ListScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            try
            {
                if (_listScroll.VerticalOffset >= _listScroll.ScrollableHeight - 400)
                {
                    var ignored = LoadMoreAsync();
                }
            }
            catch { }
        }

        private void ScrollListToTop()
        {
            try { if (_listScroll != null) _listScroll.ChangeView(null, 0, null, true); } catch { }
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var sv = child as ScrollViewer;
                if (sv != null) return sv;
                sv = FindScrollViewer(child);
                if (sv != null) return sv;
            }
            return null;
        }

        /// <summary>Lazy row thumbnails (images / videos only) for both the main and the search list.</summary>
        private void FileListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            var file = args.Item as DriveFile;
            if (file == null || args.InRecycleQueue) return;
            if (file.ThumbnailRequested || string.IsNullOrEmpty(file.ThumbnailLink)) return;
            if (!(file.IsImage || file.IsVideo || file.IsPdf)) return;
            file.ThumbnailRequested = true;
            var ignored = LoadRowThumbnailAsync(file);
        }

        private async Task LoadRowThumbnailAsync(DriveFile file)
        {
            try
            {
                var bmp = await ThumbnailCache.GetAsync(file, 80, CancellationToken.None);
                if (bmp != null) file.Thumbnail = bmp;
            }
            catch { }
        }

        private void FileListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var file = e.ClickedItem as DriveFile;
            if (file != null) OpenItem(file);
        }

        private async void OpenItem(DriveFile file)
        {
            if (file == null) return;
            if (_tab == DriveTab.Trash)
            {
                ShowDetails(file);
                return;
            }
            if (file.IsFolder)
            {
                PushFolder(file);
                return;
            }
            if (file.IsShortcut)
            {
                if (string.IsNullOrEmpty(file.ShortcutTargetId)) { ShowToast("Lối tắt không có đích."); return; }
                if (file.ShortcutTargetMime == DriveFile.FolderMime)
                {
                    PushFolder(new DriveFile { Id = file.ShortcutTargetId, Name = file.Name, MimeType = DriveFile.FolderMime, OwnedByMe = file.OwnedByMe });
                    return;
                }
                try
                {
                    var target = await DriveApiClient.GetAsync(file.ShortcutTargetId, CancellationToken.None);
                    OpenViewer(target);
                }
                catch (Exception ex) { ShowToast(Describe(ex)); }
                return;
            }
            OpenViewer(file);
        }

        // ==========================================
        // LIST MAINTENANCE AFTER OPERATIONS
        // ==========================================

        private void RemoveFromLists(string id)
        {
            for (int i = items.Count - 1; i >= 0; i--) if (items[i].Id == id) items.RemoveAt(i);
            for (int i = searchItems.Count - 1; i >= 0; i--) if (searchItems[i].Id == id) searchItems.RemoveAt(i);
            if (_tab != DriveTab.Trash) UpdateEmptyState();
        }

        private void ReplaceInLists(DriveFile updated)
        {
            if (updated == null) return;
            for (int i = 0; i < items.Count; i++) if (items[i].Id == updated.Id) { updated.Thumbnail = items[i].Thumbnail; updated.ThumbnailRequested = items[i].ThumbnailRequested; items[i] = updated; }
            for (int i = 0; i < searchItems.Count; i++) if (searchItems[i].Id == updated.Id) { updated.Thumbnail = searchItems[i].Thumbnail; updated.ThumbnailRequested = searchItems[i].ThumbnailRequested; searchItems[i] = updated; }
            if (_viewerFile != null && _viewerFile.Id == updated.Id) _viewerFile = updated;
            for (int i = 0; i < _folderStack.Count; i++) if (_folderStack[i].Id == updated.Id) _folderStack[i] = updated;
            if (InFolder || _tab == DriveTab.Trash) UpdateTopBar();
        }

        /// <summary>A freshly created / uploaded item belongs to the folder on screen: show it at the top.</summary>
        private void InsertIfVisible(DriveFile created, string parentId)
        {
            if (created == null || _tab != DriveTab.Files) return;
            string current = CurrentFolder != null ? CurrentFolder.Id : "root";
            bool matches = current == parentId || (current == "root" && created.Parents != null && created.Parents.Count > 0 && created.Parents[0] == _rootId);
            if (!matches && created.Parents != null) foreach (var p in created.Parents) if (p == current) matches = true;
            if (!matches) return;
            int index = 0;
            if (!created.IsFolder) while (index < items.Count && items[index].IsFolder) index++;
            items.Insert(index, created);
            UpdateEmptyState();
        }

        private string _rootId;   // real id of "root" (learned from created items' parents)
    }
}
