using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        private bool _settingsLoading;

        private void LoadSettings()
        {
            _settingsLoading = true;
            try
            {
                var s = ApplicationData.Current.LocalSettings.Values;
                object v;
                if (s.TryGetValue("Theme", out v) && v is string) _themeSetting = (string)v;
                ClientIdBox.Text = GoogleAuth.ClientId;
                ClientSecretBox.Text = GoogleAuth.ClientSecret;
                int idx = _themeSetting == "dark" ? 1 : (_themeSetting == "system" ? 2 : 0);
                ThemeCombo.SelectedIndex = idx;
                var ver = Package.Current.Id.Version;
                VersionText.Text = "GG Drive " + ver.Major + "." + ver.Minor + "." + ver.Build + "." + ver.Revision;
            }
            catch { }
            finally { _settingsLoading = false; }
        }

        private void OpenSettings()
        {
            InitSettingsUi();
            OpenSubPage(SettingsPage);
        }

        private void InitSettingsUi()
        {
            bool signedIn = GoogleAuth.IsSignedIn;
            string name = CachedAccountName;
            string email = CachedAccountEmail;
            SettingsUserName.Text = signedIn ? (!string.IsNullOrEmpty(name) ? name : "Tài khoản Google") : "Chưa đăng nhập";
            SettingsUserEmail.Text = signedIn ? (email ?? "") : "";
            SettingsAvatarLetter.Text = !string.IsNullOrEmpty(name) ? name.Substring(0, 1).ToUpperInvariant() : "G";
            if (signedIn && _avatarBitmap != null)
            {
                SettingsAvatarBrush.ImageSource = _avatarBitmap;
                SettingsAvatarEllipse.Visibility = Visibility.Visible;
            }
            else
            {
                SettingsAvatarEllipse.Visibility = Visibility.Collapsed;
            }
            AccessModeText.Text = !signedIn ? "" : (GoogleAuth.FullAccess
                ? "Toàn quyền Drive: xem và quản lý mọi tệp trong Drive của tôi."
                : "Phạm vi hạn chế (drive.file): chỉ thấy tệp do ứng dụng này tải lên. Đăng xuất và chọn \"Toàn quyền Drive\" để xem tất cả.");

            if (signedIn && _account != null)
            {
                if (_account.QuotaLimit > 0)
                {
                    double pct = Math.Min(100.0, 100.0 * _account.QuotaUsage / _account.QuotaLimit);
                    StorageBar.Value = pct;
                    StorageBar.Visibility = Visibility.Visible;
                    StorageText.Text = "Đã dùng " + FileTypeInfo.FormatSize(_account.QuotaUsage) + " / " + FileTypeInfo.FormatSize(_account.QuotaLimit)
                        + " (thùng rác " + FileTypeInfo.FormatSize(_account.QuotaUsageInTrash) + ")";
                }
                else
                {
                    StorageBar.Visibility = Visibility.Collapsed;
                    StorageText.Text = "Đã dùng " + FileTypeInfo.FormatSize(_account.QuotaUsage) + " (không giới hạn)";
                }
            }
            else
            {
                StorageBar.Visibility = Visibility.Collapsed;
                StorageText.Text = signedIn ? "Đang tải thông tin bộ nhớ…" : "";
            }
            LogoutButton.Visibility = ToVisibility(signedIn);
            SettingsLoginButton.Visibility = ToVisibility(!signedIn);
            var ignored = UpdateCacheSizeAsync();
        }

        private async Task UpdateCacheSizeAsync()
        {
            try
            {
                ulong total = await ThumbnailCache.GetSizeAsync() + await GetPreviewCacheSizeAsync();
                CacheSizeText.Text = "Hình thu nhỏ và tệp xem trước: " + FileTypeInfo.FormatSize((long)total);
            }
            catch { }
        }

        private void SaveClientButton_Click(object sender, RoutedEventArgs e)
        {
            GoogleAuth.ClientId = ClientIdBox.Text.Trim();
            GoogleAuth.ClientSecret = ClientSecretBox.Text.Trim();
            LoginSetupHint.Visibility = ToVisibility(!GoogleAuth.HasClient);
            ShowToast(GoogleAuth.HasClient ? "Đã lưu Client ID" : "Cần cả Client ID và Client secret");
            try { ClientSecretBox.IsEnabled = false; ClientSecretBox.IsEnabled = true; } catch { }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingsLoading) return;
            var item = ThemeCombo.SelectedItem as ComboBoxItem;
            if (item == null || item.Tag == null) return;
            string theme = item.Tag.ToString();
            if (theme == _themeSetting) return;
            _themeSetting = theme;
            ApplicationData.Current.LocalSettings.Values["Theme"] = theme;
            ShowToast("Khởi động lại ứng dụng để áp dụng giao diện.");
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            ClearCacheButton.IsEnabled = false;
            try
            {
                int n = await ThumbnailCache.ClearAsync();
                await CleanPreviewCacheAsync(0);
                foreach (var f in items) { f.Thumbnail = null; f.ThumbnailRequested = false; }
                foreach (var f in searchItems) { f.Thumbnail = null; f.ThumbnailRequested = false; }
                await UpdateCacheSizeAsync();
                ShowToast("Đã xoá bộ nhớ đệm (" + n + " hình thu nhỏ)");
            }
            catch { ShowToast("Không xoá được bộ nhớ đệm"); }
            finally { ClearCacheButton.IsEnabled = true; }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmAsync("Đăng xuất?", "Ứng dụng sẽ không còn truy cập Google Drive của bạn cho đến khi đăng nhập lại.", "Đăng xuất")) return;
            GoogleAuth.SignOut();
            _account = null;
            _avatarBitmap = null;
            items.Clear();
            searchItems.Clear();
            ThumbnailCache.ClearMemory();
            try
            {
                var f = await ApplicationData.Current.LocalFolder.GetFileAsync("avatar.jpg");
                await f.DeleteAsync();
            }
            catch { }
            ApplyAccountUi();
            CloseAllSubPages();
            _tab = DriveTab.Home;
            UpdateNavVisuals();
            UpdateTopBar();
            ShowLogin();
            ShowToast("Đã đăng xuất");
        }

        private void SettingsLoginButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSubPage();
            ShowLogin();
        }
    }
}
