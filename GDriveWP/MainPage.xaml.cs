using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Networking.Connectivity;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using GDriveWP.Services;

namespace GDriveWP
{
    public sealed partial class MainPage : Page
    {
        // ==========================================
        // SHARED STATE (declared only here)
        // ==========================================
        private readonly ObservableCollection<DriveFile> items = new ObservableCollection<DriveFile>();          // current list (tab / folder)
        private readonly ObservableCollection<DriveFile> searchItems = new ObservableCollection<DriveFile>();
        private readonly ObservableCollection<DriveFile> moveItems = new ObservableCollection<DriveFile>();      // folders of the move picker
        private readonly ObservableCollection<TransferItem> transfers = new ObservableCollection<TransferItem>();

        private AccountInfo _account;
        private string _themeSetting = "system";
        private CancellationTokenSource _toastCts;
        private bool _initialLoadDone;

        public MainPage()
        {
            this.InitializeComponent();

            FileListView.ItemsSource = items;
            SearchListView.ItemsSource = searchItems;
            MoveListView.ItemsSource = moveItems;
            TransfersList.ItemsSource = transfers;
            transfers.CollectionChanged += (s, e) => UpdateTransfersPanel();

            LoadSettings();
            UpdateNavVisuals();
            UpdateTopBar();

            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
            Application.Current.Suspending += Current_Suspending;
            ApplyStatusBar();
        }

        // ==========================================
        // LIFECYCLE
        // ==========================================

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                var dtm = DataTransferManager.GetForCurrentView();
                dtm.DataRequested -= MainPage_DataRequested;
                dtm.DataRequested += MainPage_DataRequested;
            }
            catch { }

            if (_initialLoadDone) return;
            _initialLoadDone = true;
            var v = Package.Current.Id.Version;
            DiagLog.Write("=== start " + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision + " theme=" + _themeSetting);

            var ignored = CleanPreviewCacheAsync(80L * 1024 * 1024);

            if (GoogleAuth.IsSignedIn && GoogleAuth.HasClient)
            {
                await StartSessionAsync();
            }
            else
            {
                ShowLogin();
            }
            ConsumeContinuation();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            try { DataTransferManager.GetForCurrentView().DataRequested -= MainPage_DataRequested; } catch { }
        }

        private void Current_Suspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                StopVideo();
            }
            catch { }
            finally
            {
                deferral.Complete();
            }
        }

        // ==========================================
        // TOAST (snackbar)
        // ==========================================

        private async void ShowToast(string message, int durationMs = 2500)
        {
            var oldCts = _toastCts;
            _toastCts = new CancellationTokenSource();
            if (oldCts != null)
            {
                try { oldCts.Cancel(); } catch { }
            }
            var token = _toastCts.Token;

            try
            {
                ToastFadeOutStoryboard.Stop();
                ToastText.Text = message ?? "";
                ToastNotification.Visibility = Visibility.Visible;
                ToastFadeInStoryboard.Begin();
            }
            catch { }

            try
            {
                await Task.Delay(durationMs, token);
                ToastFadeOutStoryboard.Begin();
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private void ToastFadeOutStoryboard_Completed(object sender, object e)
        {
            ToastNotification.Visibility = Visibility.Collapsed;
        }

        // ==========================================
        // DIALOGS
        // ==========================================

        private async Task<bool> ConfirmAsync(string title, string message, string okText)
        {
            try
            {
                var dialog = new MessageDialog(message ?? "", title ?? "");
                bool ok = false;
                dialog.Commands.Add(new UICommand(okText, c => ok = true));
                dialog.Commands.Add(new UICommand("Huỷ"));
                dialog.DefaultCommandIndex = 0;
                dialog.CancelCommandIndex = 1;
                await dialog.ShowAsync();
                return ok;
            }
            catch { return false; }
        }

        private async Task AlertAsync(string title, string message)
        {
            try
            {
                var dialog = new MessageDialog(message ?? "", title ?? "");
                dialog.Commands.Add(new UICommand("OK"));
                await dialog.ShowAsync();
            }
            catch { }
        }

        /// <summary>Error text for the user from any exception (API message, network, cancelled).</summary>
        private static string Describe(Exception ex)
        {
            if (ex is OperationCanceledException) return "Đã huỷ";
            var api = ex as DriveApiException;
            if (api != null) return api.Message;
            if (ex is System.Net.Http.HttpRequestException) return "Không kết nối được tới Google. Kiểm tra mạng.";
            return ex != null ? ex.Message : "Lỗi không xác định";
        }

        // ==========================================
        // NETWORK
        // ==========================================

        private bool IsInternetAvailable()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return (profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
            }
            catch
            {
                return true;
            }
        }

        // ==========================================
        // PREVIEW CACHE (LocalCacheFolder\preview)
        // ==========================================

        private static async Task<StorageFolder> GetPreviewFolderAsync()
        {
            return await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("preview", CreationCollisionOption.OpenIfExists);
        }

        /// <summary>Deletes the whole preview cache when it grows past maxBytes (old phones have little storage).</summary>
        private static async Task CleanPreviewCacheAsync(long maxBytes)
        {
            try
            {
                var folder = await GetPreviewFolderAsync();
                var files = await folder.GetFilesAsync();
                ulong total = 0;
                foreach (var f in files) total += (await f.GetBasicPropertiesAsync()).Size;
                if ((long)total > maxBytes)
                {
                    foreach (var f in files) { try { await f.DeleteAsync(); } catch { } }
                }
            }
            catch { }
        }

        private static async Task<ulong> GetPreviewCacheSizeAsync()
        {
            ulong total = 0;
            try
            {
                var folder = await GetPreviewFolderAsync();
                foreach (var f in await folder.GetFilesAsync()) total += (await f.GetBasicPropertiesAsync()).Size;
            }
            catch { }
            return total;
        }

        // ==========================================
        // THEME + STATUS BAR
        // ==========================================

        private static bool IsLightTheme
        {
            get
            {
                try { return Application.Current.RequestedTheme == ApplicationTheme.Light; }
                catch { return true; }
            }
        }

        private Color GetThemeColor(string brushKey)
        {
            bool light = IsLightTheme;
            try
            {
                var themes = Application.Current.Resources.ThemeDictionaries;
                string[] dictKeys = light ? new[] { "Light" } : new[] { "Default", "Dark" };
                foreach (var dictKey in dictKeys)
                {
                    object dictObj;
                    if (themes.TryGetValue(dictKey, out dictObj))
                    {
                        var dict = dictObj as ResourceDictionary;
                        if (dict != null && dict.ContainsKey(brushKey))
                        {
                            var brush = dict[brushKey] as SolidColorBrush;
                            if (brush != null) return brush.Color;
                        }
                    }
                }
            }
            catch { }
            return light ? Colors.White : Color.FromArgb(255, 0x13, 0x13, 0x14);
        }

        private void ApplyStatusBar()
        {
            try
            {
                var statusBar = StatusBar.GetForCurrentView();
                statusBar.BackgroundOpacity = 1;
                statusBar.BackgroundColor = GetThemeColor("SurfaceBrush");
                statusBar.ForegroundColor = GetThemeColor("OnSurfaceVariantBrush");
            }
            catch { }
        }

        private void ApplyViewerStatusBar(bool viewer)
        {
            try
            {
                var statusBar = StatusBar.GetForCurrentView();
                statusBar.BackgroundOpacity = 1;
                statusBar.BackgroundColor = viewer ? Colors.Black : GetThemeColor("SurfaceBrush");
                statusBar.ForegroundColor = viewer ? Colors.White : GetThemeColor("OnSurfaceVariantBrush");
            }
            catch { }
        }
    }
}
