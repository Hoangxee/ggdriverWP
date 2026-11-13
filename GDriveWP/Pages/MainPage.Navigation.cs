using System;
using System.Collections.Generic;
using Windows.Phone.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Anim = Windows.UI.Xaml.Media.Animation;

namespace GDriveWP
{
    public enum DriveTab { Home, Starred, Shared, Files, Trash }

    public sealed partial class MainPage
    {
        private DriveTab _tab = DriveTab.Home;

        /// <summary>Folder path of the Files tab, [0] = My Drive root.</summary>
        private readonly List<DriveFile> _folderStack = new List<DriveFile>();

        private static DriveFile RootFolder()
        {
            return new DriveFile { Id = "root", Name = "Drive của tôi", MimeType = DriveFile.FolderMime, OwnedByMe = true };
        }

        private DriveFile CurrentFolder
        {
            get { return _folderStack.Count > 0 ? _folderStack[_folderStack.Count - 1] : null; }
        }

        private bool InFolder { get { return _tab == DriveTab.Files && _folderStack.Count > 1; } }

        // Pushed overlays (search, move picker, details, settings, viewer), top = last
        private readonly List<Grid> _subPageStack = new List<Grid>();
        private readonly Dictionary<Grid, Anim.Storyboard> _subPageAnimations = new Dictionary<Grid, Anim.Storyboard>();
        private const int SUB_PAGE_ANIMATION_MS = 220;

        private bool IsSubPageOpen { get { return _subPageStack.Count > 0; } }
        private Grid TopSubPage { get { return _subPageStack.Count > 0 ? _subPageStack[_subPageStack.Count - 1] : null; } }

        private static Visibility ToVisibility(bool visible)
        {
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==========================================
        // BOTTOM NAVIGATION
        // ==========================================

        private void Nav_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            string tag = fe != null && fe.Tag != null ? fe.Tag.ToString() : "home";
            DriveTab tab = DriveTab.Home;
            if (tag == "starred") tab = DriveTab.Starred;
            else if (tag == "shared") tab = DriveTab.Shared;
            else if (tag == "files") tab = DriveTab.Files;
            if (tab == _tab && !(tab == DriveTab.Files && _folderStack.Count > 1))
            {
                ScrollListToTop();
                return;
            }
            SelectTab(tab);
        }

        private void SelectTab(DriveTab tab)
        {
            _tab = tab;
            if (tab == DriveTab.Files)
            {
                _folderStack.Clear();
                _folderStack.Add(RootFolder());
            }
            _sortByName = tab == DriveTab.Files || tab == DriveTab.Starred;
            UpdateNavVisuals();
            UpdateTopBar();
            var ignored = LoadCurrentAsync();
        }

        private void UpdateNavVisuals()
        {
            SetNav(NavHomePill, NavHomeIcon, NavHomeText, _tab == DriveTab.Home);
            SetNav(NavStarredPill, NavStarredIcon, NavStarredText, _tab == DriveTab.Starred);
            SetNav(NavSharedPill, NavSharedIcon, NavSharedText, _tab == DriveTab.Shared);
            SetNav(NavFilesPill, NavFilesIcon, NavFilesText, _tab == DriveTab.Files || _tab == DriveTab.Trash);
        }

        private void SetNav(Border pill, Windows.UI.Xaml.Shapes.Path icon, TextBlock text, bool selected)
        {
            pill.Background = selected ? (Brush)Resources2("SecondaryContainerBrush") : new SolidColorBrush(Windows.UI.Colors.Transparent);
            icon.Fill = (Brush)Resources2(selected ? "OnSecondaryContainerBrush" : "OnSurfaceVariantBrush");
            text.Foreground = (Brush)Resources2(selected ? "OnSurfaceBrush" : "OnSurfaceVariantBrush");
            text.FontFamily = (FontFamily)Application.Current.Resources[selected ? "AppFontMedium" : "AppFont"];
        }

        /// <summary>Theme brush of the active theme (ThemeResource lookup from code).</summary>
        private object Resources2(string key)
        {
            try
            {
                var themes = Application.Current.Resources.ThemeDictionaries;
                string[] dictKeys = IsLightTheme ? new[] { "Light" } : new[] { "Default", "Dark" };
                foreach (var k in dictKeys)
                {
                    object d;
                    if (themes.TryGetValue(k, out d))
                    {
                        var dict = d as ResourceDictionary;
                        if (dict != null && dict.ContainsKey(key)) return dict[key];
                    }
                }
            }
            catch { }
            return new SolidColorBrush(Windows.UI.Colors.Gray);
        }

        // ==========================================
        // TOP BAR + HEADER
        // ==========================================

        private void UpdateTopBar()
        {
            bool folderBar = InFolder || _tab == DriveTab.Trash;
            HomeBar.Visibility = ToVisibility(!folderBar);
            FolderBar.Visibility = ToVisibility(folderBar);
            if (folderBar)
            {
                FolderTitle.Text = _tab == DriveTab.Trash ? "Thùng rác" : (CurrentFolder != null ? CurrentFolder.Name : "");
            }

            switch (_tab)
            {
                case DriveTab.Home: TabTitle.Text = "Gần đây"; break;
                case DriveTab.Starred: TabTitle.Text = "Có gắn dấu sao"; break;
                case DriveTab.Shared: TabTitle.Text = "Đã chia sẻ với tôi"; break;
                case DriveTab.Trash: TabTitle.Text = "Tệp trong thùng rác"; break;
                default: TabTitle.Text = InFolder ? "Thư mục" : "Drive của tôi"; break;
            }
            TrashButton.Visibility = ToVisibility(_tab == DriveTab.Files && !InFolder);
            SortText.Text = _sortByName ? "Tên" : "Sửa gần đây";
            FabButton.Visibility = ToVisibility(_tab == DriveTab.Files);
            BottomNav.Visibility = ToVisibility(_tab != DriveTab.Trash);
        }

        private void BackFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tab == DriveTab.Trash) { SelectTab(DriveTab.Files); return; }
            GoUpFolder();
        }

        private void GoUpFolder()
        {
            if (_folderStack.Count <= 1) return;
            _folderStack.RemoveAt(_folderStack.Count - 1);
            UpdateTopBar();
            var ignored = LoadCurrentAsync();
        }

        private void PushFolder(DriveFile folder)
        {
            if (folder == null) return;
            if (_tab != DriveTab.Files)
            {
                // A folder opened from Home / Starred / Shared / search: continue in the Files tab
                _tab = DriveTab.Files;
                _folderStack.Clear();
                _folderStack.Add(RootFolder());
                _sortByName = true;
                UpdateNavVisuals();
            }
            _folderStack.Add(folder);
            UpdateTopBar();
            var ignored = LoadCurrentAsync();
        }

        private void Avatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            OpenSettings();
        }

        private void TrashButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _tab = DriveTab.Trash;
            _sortByName = false;
            UpdateNavVisuals();
            UpdateTopBar();
            var ignored = LoadCurrentAsync();
        }

        private void FolderMoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menu = new MenuFlyout();
                var refresh = new MenuFlyoutItem { Text = "Làm mới" };
                refresh.Click += (s, a) => { var ignored = LoadCurrentAsync(); };
                menu.Items.Add(refresh);

                if (_tab == DriveTab.Trash)
                {
                    var empty = new MenuFlyoutItem { Text = "Dọn sạch thùng rác" };
                    empty.Click += (s, a) => { var ignored = EmptyTrashAsync(); };
                    menu.Items.Add(empty);
                }
                else if (CurrentFolder != null)
                {
                    var folder = CurrentFolder;
                    var newFolder = new MenuFlyoutItem { Text = "Thư mục mới" };
                    newFolder.Click += (s, a) => { var ignored = NewFolderAsync(); };
                    menu.Items.Add(newFolder);
                    var upload = new MenuFlyoutItem { Text = "Tải tệp lên" };
                    upload.Click += (s, a) => StartUploadPicker();
                    menu.Items.Add(upload);
                    var star = new MenuFlyoutItem { Text = folder.Starred ? "Bỏ dấu sao" : "Gắn dấu sao" };
                    star.Click += (s, a) => { var ignored = ToggleStarAsync(folder); };
                    menu.Items.Add(star);
                    var rename = new MenuFlyoutItem { Text = "Đổi tên" };
                    rename.Click += (s, a) => { var ignored = RenameAsync(folder); };
                    menu.Items.Add(rename);
                    var share = new MenuFlyoutItem { Text = "Chia sẻ liên kết" };
                    share.Click += (s, a) => { var ignored = ShareLinkAsync(folder); };
                    menu.Items.Add(share);
                    var details = new MenuFlyoutItem { Text = "Chi tiết" };
                    details.Click += (s, a) => ShowDetails(folder);
                    menu.Items.Add(details);
                }
                menu.ShowAt(FolderMoreButton);
            }
            catch { }
        }

        private void FabButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                var menu = new MenuFlyout();
                var upload = new MenuFlyoutItem { Text = "Tải tệp lên" };
                upload.Click += (s, a) => StartUploadPicker();
                menu.Items.Add(upload);
                var newFolder = new MenuFlyoutItem { Text = "Thư mục mới" };
                newFolder.Click += (s, a) => { var ignored = NewFolderAsync(); };
                menu.Items.Add(newFolder);
                menu.ShowAt(FabButton);
            }
            catch { }
        }

        private void SubPageBack_Click(object sender, RoutedEventArgs e)
        {
            CloseSubPage();
        }

        // ==========================================
        // SUB-PAGES (push / pop with slide)
        // ==========================================

        private void OpenSubPage(Grid page)
        {
            if (page == null) return;
            try
            {
                if (_subPageStack.Count > 0 && _subPageStack[_subPageStack.Count - 1] == page && page.Visibility == Visibility.Visible)
                    return;

                _subPageStack.Remove(page);
                _subPageStack.Add(page);
                Canvas.SetZIndex(page, 100 + _subPageStack.Count);
                page.Visibility = Visibility.Visible;
                AnimateSubPage(page, GetOverlayWidth(), 0, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SubPage] open: " + ex.Message);
                page.Visibility = Visibility.Visible;
            }
        }

        private void CloseSubPage()
        {
            if (_subPageStack.Count == 0) return;
            var page = _subPageStack[_subPageStack.Count - 1];
            _subPageStack.RemoveAt(_subPageStack.Count - 1);
            OnSubPageClosing(page);
            try
            {
                AnimateSubPage(page, 0, GetOverlayWidth(), true);
            }
            catch
            {
                page.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseAllSubPages()
        {
            while (_subPageStack.Count > 0) CloseSubPage();
        }

        /// <summary>Per-page teardown when it is popped (any way).</summary>
        private void OnSubPageClosing(Grid page)
        {
            if (page == ViewerPage) TeardownViewer();
            else if (page == SearchPage) OnSearchClosing();
            else if (page == MovePage) _moveTarget = null;
        }

        private double GetOverlayWidth()
        {
            double w = RootGrid.ActualWidth;
            if (w <= 0)
            {
                try { w = Window.Current.Bounds.Width; } catch { w = 400; }
            }
            return w;
        }

        private void AnimateSubPage(Grid page, double from, double to, bool closing)
        {
            var transform = page.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                page.RenderTransform = transform;
            }

            Anim.Storyboard running;
            if (_subPageAnimations.TryGetValue(page, out running))
            {
                running.Stop();
                _subPageAnimations.Remove(page);
            }

            var anim = new Anim.DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(SUB_PAGE_ANIMATION_MS)),
                EasingFunction = new Anim.CubicEase { EasingMode = closing ? Anim.EasingMode.EaseIn : Anim.EasingMode.EaseOut }
            };
            Anim.Storyboard.SetTarget(anim, transform);
            Anim.Storyboard.SetTargetProperty(anim, "X");

            var sb = new Anim.Storyboard();
            sb.Children.Add(anim);
            sb.Completed += (s, a) =>
            {
                Anim.Storyboard current;
                if (_subPageAnimations.TryGetValue(page, out current) && current == sb)
                    _subPageAnimations.Remove(page);
                if (closing && !_subPageStack.Contains(page))
                    page.Visibility = Visibility.Collapsed;
            };
            _subPageAnimations[page] = sb;
            sb.Begin();
        }

        // ==========================================
        // HARDWARE BACK KEY
        // ==========================================

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            try
            {
                if (InputDialog.Visibility == Visibility.Visible)
                {
                    e.Handled = true;
                    FinishInputDialog(null);
                }
                else if (IsSubPageOpen)
                {
                    e.Handled = true;
                    CloseSubPage();
                }
                else if (LoginPage.Visibility == Visibility.Visible)
                {
                    if (IsLoginSubPanelOpen)
                    {
                        e.Handled = true;
                        ShowLoginChooser();
                    }
                    // else: exit the app
                }
                else if (_tab == DriveTab.Trash)
                {
                    e.Handled = true;
                    SelectTab(DriveTab.Files);
                }
                else if (InFolder)
                {
                    e.Handled = true;
                    GoUpFolder();
                }
                else if (_tab != DriveTab.Home)
                {
                    e.Handled = true;
                    SelectTab(DriveTab.Home);
                }
                // else: Handled stays false -> the system suspends / exits the app
            }
            catch { }
        }
    }
}
