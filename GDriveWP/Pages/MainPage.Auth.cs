using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage
    {
        private CancellationTokenSource _deviceCts;
        private BitmapImage _avatarBitmap;

        // ==========================================
        // LOGIN SCREEN
        // ==========================================

        private void ShowLogin()
        {
            CancelDeviceFlow();
            LoginPage.Visibility = Visibility.Visible;
            ShowLoginChooser();
        }

        private void HideLogin()
        {
            CancelDeviceFlow();
            LoginPage.Visibility = Visibility.Collapsed;
        }

        private void ShowLoginChooser()
        {
            CancelDeviceFlow();
            LoginSetupHint.Visibility = ToVisibility(!GoogleAuth.HasClient);
            LoginChooser.Visibility = Visibility.Visible;
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            PasteCodePanel.Visibility = Visibility.Collapsed;
            PasteCodeStatus.Text = "";
        }

        private bool IsLoginSubPanelOpen
        {
            get { return DeviceCodePanel.Visibility == Visibility.Visible || PasteCodePanel.Visibility == Visibility.Visible; }
        }

        private void LoginSetupButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }

        private bool EnsureClientConfigured()
        {
            if (GoogleAuth.HasClient) return true;
            ShowToast("Nhập Client ID và Client secret trước.");
            OpenSettings();
            return false;
        }

        // ==========================================
        // DEVICE-CODE FLOW (QR), like YTMusicWP
        // ==========================================

        private void LoginQrButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConfigured()) return;
            LoginChooser.Visibility = Visibility.Collapsed;
            DeviceCodePanel.Visibility = Visibility.Visible;
            DeviceCodeText.Text = "----";
            DeviceCodeQrImage.Source = null;
            DeviceCodeQrLoading.Visibility = Visibility.Visible;
            DeviceCodeStatus.Text = "Đang lấy mã…";
            DeviceCodeProgress.Visibility = Visibility.Visible;
            var ignored = RunDeviceFlowAsync();
        }

        private string _deviceVerificationUrl;

        private async Task RunDeviceFlowAsync()
        {
            CancelDeviceFlow();
            _deviceCts = new CancellationTokenSource();
            var ct = _deviceCts.Token;
            try
            {
                var info = await GoogleAuth.StartDeviceFlowAsync();
                if (ct.IsCancellationRequested) return;

                DeviceCodeText.Text = info.UserCode ?? "ERROR";
                DeviceCodeStatus.Text = "Đang chờ bạn đăng nhập…";
                _deviceVerificationUrl = "https://www.google.com/device?user_code=" + Uri.EscapeDataString(info.UserCode ?? "");
                var qr = QrCodeGenerator.GenerateQrBitmap(_deviceVerificationUrl, 4, 3);
                if (qr != null) DeviceCodeQrImage.Source = qr;
                DeviceCodeQrLoading.Visibility = Visibility.Collapsed;

                int interval = Math.Max(3, info.Interval);
                int elapsed = 0;
                while (!ct.IsCancellationRequested && elapsed < info.ExpiresIn)
                {
                    await Task.Delay(interval * 1000, ct);
                    elapsed += interval;
                    DevicePollResult result;
                    try { result = await GoogleAuth.PollDeviceOnceAsync(info.DeviceCode); }
                    catch { DeviceCodeStatus.Text = "Lỗi mạng, đang thử lại…"; continue; }
                    if (ct.IsCancellationRequested) return;

                    if (result == DevicePollResult.Pending) continue;
                    if (result == DevicePollResult.SlowDown) { interval += 2; continue; }
                    if (result == DevicePollResult.Success)
                    {
                        DeviceCodeStatus.Text = "Đăng nhập thành công!";
                        DeviceCodeProgress.Visibility = Visibility.Collapsed;
                        await OnSignedInAsync();
                        return;
                    }
                    DeviceCodeStatus.Text = result == DevicePollResult.Expired ? "Mã đã hết hạn. Thử lại." : "Đăng nhập thất bại: " + (GoogleAuth.LastError ?? "");
                    DeviceCodeProgress.Visibility = Visibility.Collapsed;
                    return;
                }
                if (!ct.IsCancellationRequested)
                {
                    DeviceCodeStatus.Text = "Mã đã hết hạn. Thử lại.";
                    DeviceCodeProgress.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DiagLog.Write("device flow " + DiagLog.Describe(ex));
                DeviceCodeText.Text = "ERROR";
                DeviceCodeQrLoading.Visibility = Visibility.Collapsed;
                DeviceCodeProgress.Visibility = Visibility.Collapsed;
                DeviceCodeStatus.Text = "Không lấy được mã: " + Describe(ex) + "\nClient phải thuộc loại \"TVs and Limited Input devices\".";
            }
        }

        private void CancelDeviceFlow()
        {
            var cts = _deviceCts;
            _deviceCts = null;
            if (cts != null) { try { cts.Cancel(); } catch { } }
        }

        private async void OpenDeviceBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            try { await Launcher.LaunchUriAsync(new Uri(_deviceVerificationUrl ?? "https://www.google.com/device")); } catch { }
        }

        private void CancelDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLoginChooser();
        }

        // ==========================================
        // PASTE-THE-CODE FLOW (full Drive scope)
        // ==========================================

        private void LoginCodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConfigured()) return;
            LoginChooser.Visibility = Visibility.Collapsed;
            PasteCodePanel.Visibility = Visibility.Visible;
            AuthCodeBox.Text = "";
            PasteCodeStatus.Text = "";
            try { AuthQrImage.Source = QrCodeGenerator.GenerateQrBitmap(GoogleAuth.BuildAuthUrl(), 3, 2); } catch { }
        }

        private void ShareAuthLinkButton_Click(object sender, RoutedEventArgs e)
        {
            _shareTitle = "Đăng nhập GG Drive";
            _shareText = GoogleAuth.BuildAuthUrl();
            try { Windows.ApplicationModel.DataTransfer.DataTransferManager.ShowShareUI(); }
            catch { ShowToast("Không mở được bảng chia sẻ."); }
        }

        private async void OpenAuthLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try { await Launcher.LaunchUriAsync(new Uri(GoogleAuth.BuildAuthUrl())); } catch { ShowToast("Không mở được trình duyệt."); }
        }

        private async void SubmitAuthCodeButton_Click(object sender, RoutedEventArgs e)
        {
            string code = GoogleAuth.ExtractCode(AuthCodeBox.Text);
            if (string.IsNullOrEmpty(code))
            {
                PasteCodeStatus.Text = "Hãy dán địa chỉ hoặc mã nhận được.";
                return;
            }
            SubmitAuthCodeButton.IsEnabled = false;
            PasteCodeStatus.Text = "Đang xác thực…";
            try
            {
                await GoogleAuth.ExchangeCodeAsync(code);
                PasteCodeStatus.Text = "Đăng nhập thành công!";
                await OnSignedInAsync();
            }
            catch (Exception ex)
            {
                PasteCodeStatus.Text = "Không xác thực được: " + Describe(ex);
            }
            finally
            {
                SubmitAuthCodeButton.IsEnabled = true;
            }
        }

        private void CancelPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLoginChooser();
        }

        // ==========================================
        // SESSION
        // ==========================================

        private async Task OnSignedInAsync()
        {
            HideLogin();
            CloseAllSubPages();
            await StartSessionAsync();
            ShowToast(GoogleAuth.FullAccess ? "Đã đăng nhập (toàn quyền Drive)" : "Đã đăng nhập (chỉ tệp của ứng dụng)");
        }

        /// <summary>Signed in: show cached account, load the Home tab, refresh account info in the background.</summary>
        private async Task StartSessionAsync()
        {
            ApplyAccountUi();
            SelectTab(DriveTab.Home);
            await LoadAvatarFromDiskAsync();
            var ignored = RefreshAccountAsync();
        }

        private async Task RefreshAccountAsync()
        {
            try
            {
                var about = await DriveApiClient.GetAboutAsync(CancellationToken.None);
                _account = about;
                var s = ApplicationData.Current.LocalSettings.Values;
                s["AccountName"] = about.DisplayName ?? "";
                s["AccountEmail"] = about.Email ?? "";
                s["AccountPhoto"] = about.PhotoLink ?? "";
                ApplyAccountUi();

                if (!string.IsNullOrEmpty(about.PhotoLink))
                {
                    string link = about.PhotoLink;
                    int eq = link.LastIndexOf("=s", StringComparison.Ordinal);
                    if (eq > 0 && link.IndexOf('/', eq) < 0) link = link.Substring(0, eq) + "=s128";
                    byte[] bytes = await DriveApiClient.DownloadBytesAsync(link, false, CancellationToken.None);
                    if (bytes == null) bytes = await DriveApiClient.DownloadBytesAsync(link, true, CancellationToken.None);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var f = await ApplicationData.Current.LocalFolder.CreateFileAsync("avatar.jpg", CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteBytesAsync(f, bytes);
                        _avatarBitmap = await ThumbnailCache.LoadBitmapAsync(bytes, 96);
                        ApplyAccountUi();
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("about " + DiagLog.Describe(ex));
                if (SettingsPage.Visibility == Visibility.Visible) StorageText.Text = Describe(ex);
            }
        }

        private async Task LoadAvatarFromDiskAsync()
        {
            if (_avatarBitmap != null) return;
            try
            {
                var f = await ApplicationData.Current.LocalFolder.GetFileAsync("avatar.jpg");
                var bmp = new BitmapImage { DecodePixelWidth = 96 };
                using (var stream = await f.OpenAsync(FileAccessMode.Read))
                {
                    await bmp.SetSourceAsync(stream);
                }
                _avatarBitmap = bmp;
                ApplyAccountUi();
            }
            catch { }
        }

        private static string CachedAccountName
        {
            get
            {
                try { object v; return ApplicationData.Current.LocalSettings.Values.TryGetValue("AccountName", out v) ? v as string : null; }
                catch { return null; }
            }
        }

        private static string CachedAccountEmail
        {
            get
            {
                try { object v; return ApplicationData.Current.LocalSettings.Values.TryGetValue("AccountEmail", out v) ? v as string : null; }
                catch { return null; }
            }
        }

        /// <summary>Avatar / name everywhere (search pill, settings card).</summary>
        private void ApplyAccountUi()
        {
            string name = CachedAccountName;
            string letter = !string.IsNullOrEmpty(name) ? name.Substring(0, 1).ToUpperInvariant() : "G";
            AvatarLetter.Text = letter;
            if (_avatarBitmap != null)
            {
                AvatarImageBrush.ImageSource = _avatarBitmap;
                AvatarEllipse.Visibility = Visibility.Visible;
            }
            else
            {
                AvatarEllipse.Visibility = Visibility.Collapsed;
            }
            if (SettingsPage.Visibility == Visibility.Visible) InitSettingsUi();
        }
    }
}
