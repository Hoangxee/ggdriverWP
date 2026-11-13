using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        private DispatcherTimer _searchTimer;
        private CancellationTokenSource _searchCts;
        private DriveFile _searchScope;     // folder to search in (null = whole Drive)
        private string _lastSearch;

        private void SearchPill_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenSearch(null);
        }

        private void SearchInFolderButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSearch(InFolder ? CurrentFolder : null);
        }

        private void OpenSearch(DriveFile scope)
        {
            if (_searchTimer == null)
            {
                _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _searchTimer.Tick += (s, a) => { _searchTimer.Stop(); var ignored = RunSearchAsync(); };
            }
            _searchScope = scope;
            _lastSearch = null;
            SearchBox.PlaceholderText = scope != null ? "Tìm trong \"" + scope.Name + "\"" : "Tìm kiếm trong Drive";
            SearchBox.Text = "";
            searchItems.Clear();
            SearchEmptyText.Text = "Nhập tên tệp hoặc thư mục cần tìm.";
            SearchEmptyText.Visibility = Visibility.Visible;
            SearchClearButton.Visibility = Visibility.Collapsed;
            OpenSubPage(SearchPage);
            var ignored2 = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { try { SearchBox.Focus(FocusState.Programmatic); } catch { } });
        }

        private void OnSearchClosing()
        {
            if (_searchTimer != null) _searchTimer.Stop();
            var cts = _searchCts;
            _searchCts = null;
            if (cts != null) { try { cts.Cancel(); } catch { } }
            try { SearchBox.IsEnabled = false; SearchBox.IsEnabled = true; } catch { }   // drops focus -> keyboard closes
        }

        private void SearchBackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSubPage();
        }

        private void SearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            try { SearchBox.Focus(FocusState.Programmatic); } catch { }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchClearButton.Visibility = ToVisibility(SearchBox.Text.Length > 0);
            if (_searchTimer == null) return;
            _searchTimer.Stop();
            if (SearchBox.Text.Trim().Length == 0)
            {
                searchItems.Clear();
                SearchEmptyText.Text = "Nhập tên tệp hoặc thư mục cần tìm.";
                SearchEmptyText.Visibility = Visibility.Visible;
                return;
            }
            _searchTimer.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                if (_searchTimer != null) _searchTimer.Stop();
                var ignored = RunSearchAsync();
                try { SearchBox.IsEnabled = false; SearchBox.IsEnabled = true; } catch { }
            }
        }

        private async Task RunSearchAsync()
        {
            string text = SearchBox.Text.Trim();
            if (text.Length == 0 || text == _lastSearch) return;
            _lastSearch = text;

            var old = _searchCts;
            _searchCts = new CancellationTokenSource();
            if (old != null) { try { old.Cancel(); } catch { } }
            var ct = _searchCts.Token;

            searchItems.Clear();
            SearchEmptyText.Visibility = Visibility.Collapsed;
            SearchLoadingRing.IsActive = true;
            SearchLoadingRing.Visibility = Visibility.Visible;
            try
            {
                string q = "name contains '" + DriveApiClient.EscapeQuery(text) + "' and trashed=false";
                if (_searchScope != null) q += " and '" + DriveApiClient.EscapeQuery(_searchScope.Id) + "' in parents";
                var page = await DriveApiClient.ListAsync(q, "folder,modifiedTime desc", null, 50, ct);
                if (ct.IsCancellationRequested) return;
                foreach (var f in page.Files) searchItems.Add(f);
                if (searchItems.Count == 0)
                {
                    SearchEmptyText.Text = "Không tìm thấy kết quả cho \"" + text + "\".";
                    SearchEmptyText.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                if (HandleAuthFailure(ex)) return;
                SearchEmptyText.Text = Describe(ex);
                SearchEmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    SearchLoadingRing.IsActive = false;
                    SearchLoadingRing.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void SearchListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var file = e.ClickedItem as DriveFile;
            if (file == null) return;
            if (file.IsFolder || (file.IsShortcut && file.ShortcutTargetMime == DriveFile.FolderMime))
            {
                CloseSubPage();
                OpenItem(file);
                return;
            }
            OpenItem(file);
        }
    }
}
