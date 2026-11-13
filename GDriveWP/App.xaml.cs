using System;
using System.Collections.Generic;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace GDriveWP
{
    public sealed partial class App : Application
    {
        private TransitionCollection transitions;

        /// <summary>Files picked for upload (FileOpenPicker continuation) / received through Share, consumed by MainPage.ConsumeContinuation.</summary>
        internal static List<StorageFile> PendingUploadFiles;
        /// <summary>Target file chosen in the save picker (download continuation).</summary>
        internal static StorageFile PendingSaveFile;
        internal static bool PendingSaveCancelled;

        public App()
        {
            ApplyThemeSetting();
            this.InitializeComponent();
            this.Suspending += this.OnSuspending;
            this.UnhandledException += App_UnhandledException;
        }

        /// <summary>LocalSettings "Theme": "system" (default), "light" or "dark". Takes effect on the next launch.</summary>
        private void ApplyThemeSetting()
        {
            string theme = "system";
            try
            {
                object value;
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue("Theme", out value) && value is string)
                    theme = (string)value;
            }
            catch { }

            if (theme == "dark") this.RequestedTheme = ApplicationTheme.Dark;
            else if (theme == "light") this.RequestedTheme = ApplicationTheme.Light;
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine("[CRASH PREVENTED] " + (e.Exception != null ? e.Exception.Message : "Unknown error"));
            DiagLog.Write("[UNHANDLED] " + e.Message + " " + DiagLog.Describe(e.Exception));
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            ShowMainPage(e.Arguments);
        }

        /// <summary>Another app shared files with GG Drive ("Share" -> GG Drive): upload them to My Drive.</summary>
        protected override async void OnShareTargetActivated(ShareTargetActivatedEventArgs args)
        {
            try
            {
                var operation = args.ShareOperation;
                if (operation.Data.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await operation.Data.GetStorageItemsAsync();
                    var files = new List<StorageFile>();
                    foreach (var item in items)
                    {
                        var f = item as StorageFile;
                        if (f != null) files.Add(f);
                    }
                    if (files.Count > 0) PendingUploadFiles = files;
                }
                operation.ReportDataRetrieved();
            }
            catch (Exception ex)
            {
                DiagLog.Write("ShareTarget " + ex.Message);
            }

            var page = ShowMainPage(null);
            if (page != null) page.ConsumeContinuation();
        }

        /// <summary>Return from the file pickers (upload: open picker, download: save picker).</summary>
        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);

            var open = args as FileOpenPickerContinuationEventArgs;
            if (open != null)
            {
                var files = new List<StorageFile>();
                if (open.Files != null) foreach (var f in open.Files) files.Add(f);
                PendingUploadFiles = files.Count > 0 ? files : null;
            }
            var save = args as FileSavePickerContinuationEventArgs;
            if (save != null)
            {
                PendingSaveFile = save.File;
                PendingSaveCancelled = save.File == null;
            }
            if (open == null && save == null) return;

            var page = ShowMainPage(null);
            if (page != null) page.ConsumeContinuation();
        }

        private MainPage ShowMainPage(object arguments)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.CacheSize = 1;
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                if (rootFrame.ContentTransitions != null)
                {
                    this.transitions = new TransitionCollection();
                    foreach (var c in rootFrame.ContentTransitions) this.transitions.Add(c);
                }
                rootFrame.ContentTransitions = null;
                rootFrame.Navigated += this.RootFrame_FirstNavigated;

                if (!rootFrame.Navigate(typeof(MainPage), arguments))
                    throw new Exception("Failed to create initial page");
                Window.Current.Activate();
                return null;
            }

            Window.Current.Activate();
            return rootFrame.Content as MainPage;
        }

        private void RootFrame_FirstNavigated(object sender, NavigationEventArgs e)
        {
            var rootFrame = sender as Frame;
            if (rootFrame != null)
            {
                rootFrame.ContentTransitions = this.transitions ?? new TransitionCollection() { new NavigationThemeTransition() };
                rootFrame.Navigated -= this.RootFrame_FirstNavigated;
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
