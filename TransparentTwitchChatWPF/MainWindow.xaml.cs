﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Collections.Specialized;
using Jot;
using Jot.DefaultInitializer;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;
using Mayerch1.GithubUpdateCheck;

/*
 * v0.10.0
 * > Allow interaction fix (when off)
 * > Redo the startup image (for first time launch)
 * > BTTV and FFZ get unset when switching chats
 * > KapChat customCSS gets unset when switching chats
 * > Popout CSS Editors
 * > Custom Javascript
 * > Global hotkey to force app to be topmost
 * > Update Zoom level values to match with webView2
 * > Deal with popup windows
 * > Reset settings button
 * > Tips and hints in the chat window, turn off in settings
 * 
 * v0.9.5
 * - The uninstaller will now remove stored settings/data
 * - Audio for browser enabled by default
 * - Audio device selection (for sound clips)
 * - Volume control for sounds clips
 * - Can now change the folder for sound clips
 * - Added an entry in the context menu to open Dev Tools
 * - CefSharp (Chromium) updated to 117.2.20
 * 
 * v0.9.4
 * - Filtering: can block users now
 * - Automatically checks for updates on start (Can be disabled in settings)
 * 
 * v0.9.3
 * - Added a setting to allow multiple instances
 * - Fix for crash when adding a widget with a long URL
 * - Updated CEFsharp (Chromium) and Newtsonsoft.Json to latest versions
 * ~ Known Issues:
 * ~ Jump lists no longer work if you allow multiple instances (it just launches another instance)
 * ~ Can't login to the popout Twitch, this is caused by Twitch blocking login for unsupported browsers
 * 
 * v0.9.2
 * - jChat support (this allows BetterTTV, FrankerFaceZ and 7TV emotes)
 * - CefSharp (Chromium) updated to 99.2.9
 * - Twitch popout with BTTV and FFZ emotes support - PR by: github.com/r-o-b-o-t-o
 * - Twitch popout will keep your login session - PR by: github.com/r-o-b-o-t-o
 * 
 * v0.9.1
 * - TwitchLib support for points redemption
 * - Filter settings will let you highlight certain usernames/mods/vip
 * - Possible bug fix for startup crash Load() issue.
 * - System tray icon will always be enabled for now (to prevent no interaction with app)
 * 
 * v0.9.0
 * - Chat filter settings for KapChat version
 * - Filter by allowed usernames, all mods, or all VIPs
 * - You can configure the filter settings under Chat settings and click the Open Chat Filter Settings button
 *
 * 
 * TODO:
 * > Custom CSS for widgets
 * Add opacity and zoom levels into the General settings
 * Save and load different css files
 * Fade out the chat when there's not been a message for awhile (useful if opacity is set high)
 * Cascade the windows when creating a new one
 * Cascade the windows when resetting their positions
 * Easier/better size grip, kinda hard to see it, or click on it right now
 * Hotkey and/or menu item to hide the chat and make it visible again
 * Allowing you to chat from the app, quickly (hotkey to enable it?)
 * Custom javascript
 *
 * 
 */

namespace TransparentTwitchChatWPF
{
    using Chats;
    using Microsoft.Web.WebView2.Core;
    using NAudio.Wave;
    using System.Diagnostics;
    using System.IO;
    using System.Windows.Controls;
    using System.Runtime.InteropServices;
    using System.Collections;
    using System.Speech.Synthesis;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Collections.Concurrent;
    using System.Security.Policy;
    using System.Net.Http;
    using System.Text;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, BrowserWindow
    {
        private System.Timers.Timer _timer;

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        /// 

        SolidColorBrush bgColor;
        Thickness noBorderThickness = new Thickness(0);
        Thickness borderThickness = new Thickness(4);
        int cOpacity = 0;
        bool hiddenBorders = false;
        //GeneralSettings genSettings;
        TrackingConfiguration genSettingsTrackingConfig;
        JsCallbackFunctions jsCallbackFunctions;

        //StringCollection custom_windows = new StringCollection();
        List<BrowserWindow> windows = new List<BrowserWindow>();

        private TwitchPubSub _pubSub;
        private bool _isPubSubConnected = false;

        private Chat currentChat;

        /*public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName]string propertyName = null)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }*/

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            this.currentChat = new CustomURLChat(ChatTypes.CustomURL); // TODO: initializing here needed?

            Services.Tracker.Configure(this).IdentifyAs("State").Apply();
            this.genSettingsTrackingConfig = Services.Tracker.Configure(SettingsSingleton.Instance.genSettings);
            this.genSettingsTrackingConfig.IdentifyAs("MainWindow").Apply();

            //_timer = new System.Timers.Timer(5000);
            //_timer.Elapsed += _timer_Elapsed;
            //_timer.Start();

            InitializeWebViewAsync();
        }

        async void InitializeWebViewAsync()
        {
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TransparentTwitchChatWPF");
            CoreWebView2Environment cwv2Environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await webView.EnsureCoreWebView2Async(cwv2Environment);

            this.jsCallbackFunctions = new JsCallbackFunctions();
            webView.CoreWebView2.AddHostObjectToScript("jsCallbackFunctions", this.jsCallbackFunctions);

            SetupBrowser();
        }

        private void webView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                MessageBox.Show(GetWindow(this), "Failed to initialize WebView2:\n" + e.InitializationException.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //this.Dispatcher.Invoke(new Action(CheckForegroundWindow));
        }

        private void CheckForegroundWindow()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            var hwnd = new WindowInteropHelper(this).Handle;

            if (foregroundWindow != hwnd)
                WindowHelper.SetWindowPosTopMost(hwnd);
        }

        public bool ProcessCommandLineArgs(IList<string> args)
        {
            if (args == null || args.Count == 0)
                return true;
            if ((args.Count > 1))
            {
                //the first index always contains the location of the exe so we need to check the second index
                if ((args[1].ToLowerInvariant() == "/toggleborders"))
                {
                    ToggleBorderVisibility();
                }
                else if ((args[1].ToLowerInvariant() == "/settings"))
                {
                    ShowSettingsWindow();
                }
                else if ((args[1].ToLowerInvariant() == "/resetwindow"))
                {
                    ResetWindowPosition();

                    if (MessageBox.Show("Show settings folder?", "Settings Folder", MessageBoxButton.YesNo, MessageBoxImage.Question)
                        == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start((Services.Tracker.StoreFactory as Jot.Storage.JsonFileStoreFactory).StoreFolderPath);
                    }
                }
            }

            return true;
        }

        public void drawBorders()
        {
            this.ShowInTaskbar = true;

            var hwnd = new WindowInteropHelper(this).Handle;
            WindowHelper.SetWindowExDefault(hwnd);

            // show minimize, maximize, and close buttons
            //btnHide.Visibility = Visibility.Visible;
            //btnSettings.Visibility = Visibility.Visible;

            this.AppTitleBar.Visibility = Visibility.Visible;
            this.FooterBar.Visibility = Visibility.Visible;
            this.webView.SetValue(Grid.RowSpanProperty, 1);
            this.BorderBrush = Brushes.Black;
            this.BorderThickness = this.borderThickness;
            this.ResizeMode = System.Windows.ResizeMode.CanResizeWithGrip;

            hiddenBorders = false;

            this.Topmost = false;
            this.Activate();
            this.Topmost = true;

            this.webView.IsEnabled = SettingsSingleton.Instance.genSettings.AllowInteraction;
        }

        public void hideBorders()
        {
            if (SettingsSingleton.Instance.genSettings.HideTaskbarIcon)
                this.ShowInTaskbar = false;

            var hwnd = new WindowInteropHelper(this).Handle;
            WindowHelper.SetWindowExTransparent(hwnd);

            // hide minimize, maximize, and close buttons
            //btnHide.Visibility = Visibility.Hidden;
            //btnSettings.Visibility = Visibility.Hidden;

            this.AppTitleBar.Visibility = Visibility.Collapsed;
            this.FooterBar.Visibility = Visibility.Collapsed;
            this.webView.SetValue(Grid.RowSpanProperty, 2);
            this.BorderBrush = Brushes.Transparent;
            this.BorderThickness = this.noBorderThickness;
            this.ResizeMode = System.Windows.ResizeMode.NoResize;

            hiddenBorders = true;

            this.Topmost = false;
            this.Activate();
            this.Topmost = true;

            this.webView.IsEnabled = false;
        }

        public void ToggleBorderVisibility()
        {
            if (hiddenBorders)
                DrawBordersForAllWindows();
            else
                HideBordersForAllWindows();
        }

        public void DrawBordersForAllWindows()
        {
            drawBorders();
            foreach (BrowserWindow win in this.windows)
                win.drawBorders();
        }

        public void HideBordersForAllWindows()
        {
            hideBorders();
            foreach (BrowserWindow win in this.windows)
                win.hideBorders();
        }

        public void ResetWindowPosition()
        {
            this.ResetWindowState();
            foreach (BrowserWindow win in this.windows)
                win.ResetWindowState();
        }

        public void ResetWindowState()
        {
            drawBorders();
            this.WindowState = WindowState.Normal;
            this.Left = 10;
            this.Top = 10;
            this.Height = 500;
            this.Width = 320;
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            //if (e.KeyCode == Key.F9)
            //{
            //    ToggleBorderVisibility();
            //}
            //else if (e.Key == Key.F8)
            //{
            //    ShowSettingsWindow();
            //}
        }

        private void SetCustomChatAddress(string url)
        {
            this.webView.CoreWebView2.Navigate(url);
        }

        private void SetChatAddress(string chatChannel)
        {
            string username = chatChannel;
            if (chatChannel.Contains("/"))
                username = chatChannel.Split('/').Last();

            string fade = SettingsSingleton.Instance.genSettings.FadeTime;
            if (!SettingsSingleton.Instance.genSettings.FadeChat) { fade = "false"; }

            string theme = string.Empty;
            if ((SettingsSingleton.Instance.genSettings.ThemeIndex >= 0) && (SettingsSingleton.Instance.genSettings.ThemeIndex < KapChat.Themes.Count))
                theme = KapChat.Themes[SettingsSingleton.Instance.genSettings.ThemeIndex];

            string url = @"https://www.nightdev.com/hosted/obschat/?";
            url += @"theme=" + theme;
            url += @"&channel=" + username;
            url += @"&fade=" + fade;
            url += @"&bot_activity=" + (!SettingsSingleton.Instance.genSettings.BlockBotActivity).ToString();
            url += @"&prevent_clipping=false";
            this.webView.CoreWebView2.Navigate(url);
        }

        public void ShowInputFadeDialogBox()
        {
            Input_Fade inputDialog = new Input_Fade();
            if (inputDialog.ShowDialog() == true)
            {
                string fadeTime = inputDialog.Channel;
                int fadeTimeInt = 0;

                int.TryParse(fadeTime, out fadeTimeInt);

                SettingsSingleton.Instance.genSettings.FadeChat = (fadeTimeInt > 0);
                SettingsSingleton.Instance.genSettings.FadeTime = fadeTime;
            }
        }

        public void ExitApplication()
        {
            App.IsShuttingDown = true;
            Application.Current.Shutdown();

            // Removing the 'are you sure' for now
            /*if (SettingsSingleton.Instance.genSettings.ConfirmClose)
            {
                var msgBoxResult = MessageBox.Show("Sure you want to exit the application?", "Exit",
                                                    MessageBoxButton.YesNo,
                                                    MessageBoxImage.Question,
                                                    MessageBoxResult.No,
                                                    MessageBoxOptions.DefaultDesktopOnly);
                if (msgBoxResult == MessageBoxResult.Yes)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }*/
            //SystemCommands.CloseWindow(this);
        }

        private void MenuItem_ToggleBorderVisible(object sender, RoutedEventArgs e)
        {
            foreach (BrowserWindow window in this.windows)
            {
                if (window != null)
                {
                    if (this.hiddenBorders)
                        window.drawBorders();
                    else
                        window.hideBorders();
                }
            }
            ToggleBorderVisibility();
        }

        private void MenuItem_ShowSettings(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void MenuItem_VisitWebsite(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/baffler/Transparent-Twitch-Chat-Overlay/releases/latest");
        }

        private void btnHide_Click(object sender, RoutedEventArgs e)
        {
            hideBorders();
        }

        private void MenuItem_ZoomIn(object sender, RoutedEventArgs e)
        {

            if (SettingsSingleton.Instance.genSettings.ZoomLevel < 4.0)
            {
                this.webView.ZoomFactor = SettingsSingleton.Instance.genSettings.ZoomLevel + 0.1;
                SettingsSingleton.Instance.genSettings.ZoomLevel = this.webView.ZoomFactor;
            }
        }

        private void MenuItem_ZoomOut(object sender, RoutedEventArgs e)
        {
            if (SettingsSingleton.Instance.genSettings.ZoomLevel > 0.1)
            {
                this.webView.ZoomFactor = SettingsSingleton.Instance.genSettings.ZoomLevel - 0.1;
                SettingsSingleton.Instance.genSettings.ZoomLevel = this.webView.ZoomFactor;
            }
        }

        private void MenuItem_ZoomReset(object sender, RoutedEventArgs e)
        {
            this.webView.ZoomFactor = 1.0;
            SettingsSingleton.Instance.genSettings.ZoomLevel = 1.0;
        }

        private void webView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            System.Console.WriteLine("UHHH");
        }

        private async void webView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                return;
            }

            this.webView.Dispatcher.Invoke(new Action(() =>
            {
                if (SettingsSingleton.Instance.genSettings.ZoomLevel > 0)
                    this.webView.ZoomFactor = SettingsSingleton.Instance.genSettings.ZoomLevel;
            }));

            if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.TwitchPopout)
                TwitchPopoutSetup();

            string js = this.currentChat.SetupJavascript();
            if (!string.IsNullOrEmpty(js))
                await this.webView.ExecuteScriptAsync(js);

            // Custom CSS
            string script = string.Empty;
            string css = this.currentChat.SetupCustomCSS();

            if (!string.IsNullOrEmpty(css))
                script = InsertCustomCSS2(css);

            if (!string.IsNullOrEmpty(script))
                await this.webView.ExecuteScriptAsync(script);

            this.PushNewMessage("Loading...");
        }

        private void TwitchPopoutSetup()
        {
            if (SettingsSingleton.Instance.genSettings.BetterTtv)
            {
                InsertCustomJavaScriptFromUrl("https://cdn.betterttv.net/betterttv.js");
            }
            if (SettingsSingleton.Instance.genSettings.FrankerFaceZ)
            {
                // Observe for FrankerFaceZ's reskin stylesheet
                // that breaks the transparency and remove it
                InsertCustomJavaScript(@"
(function() {
    const head = document.getElementsByTagName(""head"")[0];
    const observer = new MutationObserver((mutations, observer) => {
        for (const mut of mutations) {
            if (mut.type === ""childList"") {
                for (const node of mut.addedNodes) {
                    if (node.tagName.toLowerCase() === ""link"" && node.href.includes(""color_normalizer"")) {
                        node.remove();
                    }
                }
            }
        }
    });
    observer.observe(head, {
        attributes: false,
        childList: true,
        subtree: false,
    });
})();
                        ");

                InsertCustomJavaScriptFromUrl("https://cdn.frankerfacez.com/static/script.min.js");
            }
        }

        private void webView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.TwitchPopout)
            {
                try
                {
                    var message = e.TryGetWebMessageAsString();
                    Debug.WriteLine(message);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
            if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.jChat)
            {
                try
                {
                    var message = e.TryGetWebMessageAsString();
                    PushNewMessage(message);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        }

        //private void InsertCustomCSS_old(string CSS)
        //{
        //    string base64CSS = Utilities.Base64Encode(CSS.Replace("\r\n", "").Replace("\t", ""));

        //    string href = "data:text/css;charset=utf-8;base64," + base64CSS;

        //    string script = "var link = document.createElement('link');";
        //    script += "link.setAttribute('rel', 'stylesheet');";
        //    script += "link.setAttribute('type', 'text/css');";
        //    script += "link.setAttribute('href', '" + href + "');";
        //    script += "document.getElementsByTagName('head')[0].appendChild(link);";

        //    this.Browser1.ExecuteScriptAsync(script);
        //}

        private string InsertCustomCSS2(string CSS)
        {
            string uriEncodedCSS = Uri.EscapeDataString(CSS);
            string script = "const ttcCSS = document.createElement('style');";
            script += "ttcCSS.innerHTML = decodeURIComponent(\"" + uriEncodedCSS + "\");";
            script += "document.querySelector('head').appendChild(ttcCSS);";
            return script;
        }

        private async void InsertCustomJavaScript(string JS)
        {
            try
            {
                await this.webView.ExecuteScriptAsync(JS);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertCustomJavaScriptFromUrl(string scriptUrl)
        {
            InsertCustomJavaScript(@"
(function() {
    const script = document.createElement(""script"");
    script.src = """ + scriptUrl + @""";
    document.getElementsByTagName(""head"")[0].appendChild(script);
})();
            ");
        }

        public void CreateNewWindow(string URL, string CustomCSS)
        {
            if (SettingsSingleton.Instance.genSettings.CustomWindows.Contains(URL))
            {
                MessageBox.Show("That URL already exists as a window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                SettingsSingleton.Instance.genSettings.CustomWindows.Add(URL);
                OpenNewCustomWindow(URL, CustomCSS);
            }
        }

        private void CreateNewWindowDialog()
        {
            Input_Custom inputDialog = new Input_Custom();
            if (inputDialog.ShowDialog() == true)
            {
                CreateNewWindow(inputDialog.Url, inputDialog.CustomCSS);
            }
        }

        private void MenuItem_ClickNewWindow(object sender, RoutedEventArgs e)
        {
            CreateNewWindowDialog();
        }

        private string GetSoundClipsFolder()
        {
            string path = SettingsSingleton.Instance.genSettings.SoundClipsFolder;
            if (path == "Default")
            {
                path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\assets\\";
            }
            else if (!Directory.Exists(path))
            {
                path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\assets\\";
            }

            if (!path.EndsWith("\\")) path += "\\";

            return path;
        }

        private void ShowSettingsWindow()
        {
            WindowSettings config = new WindowSettings
            {
                Title = "Main Window",
                ChatType = SettingsSingleton.Instance.genSettings.ChatType,
                URL = SettingsSingleton.Instance.genSettings.CustomURL,
                jChatURL = SettingsSingleton.Instance.genSettings.jChatURL,
                Username = SettingsSingleton.Instance.genSettings.Username,
                ChatFade = SettingsSingleton.Instance.genSettings.FadeChat,
                FadeTime = SettingsSingleton.Instance.genSettings.FadeTime,
                //ShowBotActivity = SettingsSingleton.Instance.genSettings.ShowBotActivity,
                ChatNotificationSound = SettingsSingleton.Instance.genSettings.ChatNotificationSound,
                Theme = SettingsSingleton.Instance.genSettings.ThemeIndex,
                CustomCSS = SettingsSingleton.Instance.genSettings.CustomCSS,
                AutoHideBorders = SettingsSingleton.Instance.genSettings.AutoHideBorders,
                ConfirmClose = SettingsSingleton.Instance.genSettings.ConfirmClose,
                EnableTrayIcon = SettingsSingleton.Instance.genSettings.EnableTrayIcon,
                HideTaskbarIcon = SettingsSingleton.Instance.genSettings.HideTaskbarIcon,
                AllowInteraction = SettingsSingleton.Instance.genSettings.AllowInteraction,
                RedemptionsEnabled = SettingsSingleton.Instance.genSettings.RedemptionsEnabled,
                ChannelID = SettingsSingleton.Instance.genSettings.ChannelID,
                BetterTtv = SettingsSingleton.Instance.genSettings.BetterTtv,
                FrankerFaceZ = SettingsSingleton.Instance.genSettings.FrankerFaceZ,
            };

            SettingsWindow settingsWindow = new SettingsWindow(this, config);

            if (settingsWindow.ShowDialog() == true)
            {
                SettingsSingleton.Instance.genSettings.ChatType = config.ChatType;

                if (config.ChatType == (int)ChatTypes.CustomURL)
                {
                    this.currentChat = new CustomURLChat(ChatTypes.CustomURL);
                    SettingsSingleton.Instance.genSettings.CustomURL = config.URL;
                    SettingsSingleton.Instance.genSettings.CustomCSS = config.CustomCSS;

                    if (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.CustomURL))
                        SetCustomChatAddress(SettingsSingleton.Instance.genSettings.CustomURL);
                }
                else if (config.ChatType == (int)ChatTypes.TwitchPopout)
                {
                    this.currentChat = new CustomURLChat(ChatTypes.TwitchPopout);
                    SettingsSingleton.Instance.genSettings.Username = config.Username;

                    if (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.Username))
                        SetCustomChatAddress("https://www.twitch.tv/popout/" + SettingsSingleton.Instance.genSettings.Username + "/chat?popout=");

                    SettingsSingleton.Instance.genSettings.BetterTtv = config.BetterTtv;
                    SettingsSingleton.Instance.genSettings.FrankerFaceZ = config.FrankerFaceZ;
                }
                else if (config.ChatType == (int)ChatTypes.KapChat)
                {
                    this.currentChat = new Chats.KapChat();
                    SettingsSingleton.Instance.genSettings.Username = config.Username;
                    SettingsSingleton.Instance.genSettings.FadeChat = config.ChatFade;
                    SettingsSingleton.Instance.genSettings.FadeTime = config.FadeTime;
                    //SettingsSingleton.Instance.genSettings.ShowBotActivity = config.ShowBotActivity;
                    SettingsSingleton.Instance.genSettings.ChatNotificationSound = config.ChatNotificationSound;
                    SettingsSingleton.Instance.genSettings.ThemeIndex = config.Theme;

                    if (SettingsSingleton.Instance.genSettings.ThemeIndex == 0)
                        SettingsSingleton.Instance.genSettings.CustomCSS = config.CustomCSS;
                    else
                        SettingsSingleton.Instance.genSettings.CustomCSS = string.Empty;


                    if (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.Username))
                    {
                        SetChatAddress(SettingsSingleton.Instance.genSettings.Username);

                        if (config.RedemptionsEnabled)
                            SetupPubSubRedemptions();
                        else
                            DisablePubSubRedemptions();
                    }


                    if (SettingsSingleton.Instance.genSettings.ChatNotificationSound.ToLower() == "none")
                        this.jsCallbackFunctions.MediaFile = string.Empty;
                    else
                    {
                        string file = GetSoundClipsFolder() + SettingsSingleton.Instance.genSettings.ChatNotificationSound;
                        if (System.IO.File.Exists(file))
                        {
                            this.jsCallbackFunctions.MediaFile = file;
                        }
                        else
                        {
                            this.jsCallbackFunctions.MediaFile = string.Empty;
                        }
                    }
                }

                else if (config.ChatType == (int)ChatTypes.jChat)
                {
                    this.currentChat = new jChat();
                    SettingsSingleton.Instance.genSettings.jChatURL = config.jChatURL;
                    SettingsSingleton.Instance.genSettings.ChatNotificationSound = config.ChatNotificationSound;

                    if (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.Username))
                    {
                        SetCustomChatAddress(SettingsSingleton.Instance.genSettings.jChatURL);

                        if (config.RedemptionsEnabled)
                            SetupPubSubRedemptions();
                        else
                            DisablePubSubRedemptions();
                    }
                    else
                        SetCustomChatAddress(SettingsSingleton.Instance.genSettings.jChatURL);


                    if (SettingsSingleton.Instance.genSettings.ChatNotificationSound.ToLower() == "none")
                        this.jsCallbackFunctions.MediaFile = string.Empty;
                    else
                    {
                        string file = GetSoundClipsFolder() + SettingsSingleton.Instance.genSettings.ChatNotificationSound;
                        if (System.IO.File.Exists(file))
                        {
                            this.jsCallbackFunctions.MediaFile = file;
                        }
                        else
                        {
                            this.jsCallbackFunctions.MediaFile = string.Empty;
                        }
                    }
                }

                SettingsSingleton.Instance.genSettings.AutoHideBorders = config.AutoHideBorders;
                SettingsSingleton.Instance.genSettings.ConfirmClose = config.ConfirmClose;
                SettingsSingleton.Instance.genSettings.EnableTrayIcon = config.EnableTrayIcon;
                SettingsSingleton.Instance.genSettings.HideTaskbarIcon = config.HideTaskbarIcon;
                SettingsSingleton.Instance.genSettings.AllowInteraction = config.AllowInteraction;
                SettingsSingleton.Instance.genSettings.RedemptionsEnabled = config.RedemptionsEnabled;
                SettingsSingleton.Instance.genSettings.ChannelID = config.ChannelID;

                this.taskbarControl.Visibility = Visibility.Visible; //config.EnableTrayIcon ? Visibility.Visible : Visibility.Hidden;
                this.ShowInTaskbar = !config.HideTaskbarIcon;

                if (!this.hiddenBorders) this.webView.IsEnabled = config.AllowInteraction;

                // Save the new changes for settings
                this.genSettingsTrackingConfig.Persist();
            }
        }

        private void OpenNewCustomWindow(string url, string CustomCSS, bool hideBorder = false)
        {
            CustomWindow newWindow = new CustomWindow(this, url, CustomCSS);
            windows.Add(newWindow);
            newWindow.Show();

            if (hideBorder)
                newWindow.hideBorders();
        }

        public void RemoveCustomWindow(string url)
        {
            if (SettingsSingleton.Instance.genSettings.CustomWindows.Contains(url))
            {
                SettingsSingleton.Instance.genSettings.CustomWindows.Remove(url);
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Window window = (Window)sender;
            window.Topmost = true;
        }

        private void Window_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Window window = (Window)sender;
            window.Topmost = true;
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            Point screenPos = btnSettings.PointToScreen(new Point(0, btnSettings.ActualHeight));
            settingsBtnContextMenu.IsOpen = false;
            //settingsBtnContextMenu.PlacementTarget = this.btnSettings;
            settingsBtnContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
            settingsBtnContextMenu.HorizontalOffset = screenPos.X;
            settingsBtnContextMenu.VerticalOffset = screenPos.Y;
            settingsBtnContextMenu.IsOpen = true;
        }

        private void btnSettings_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (SettingsSingleton.Instance.genSettings.VersionTracker <= 0.95)
            {
                SettingsSingleton.Instance.genSettings.ZoomLevel = 1;
                SettingsSingleton.Instance.genSettings.VersionTracker = 0.96;
            }

            this.taskbarControl.Visibility = Visibility.Visible;

            if (SettingsSingleton.Instance.genSettings.CheckForUpdates)
            {
                Task.Run(() => CheckForUpdateAsync());
            }
        }

        private async void CheckForUpdateAsync()
        {
            GithubUpdateCheck update = new GithubUpdateCheck("baffler", "Transparent-Twitch-Chat-Overlay");
            //bool isUpdate = update.IsUpdateAvailable(SettingsSingleton.Version, VersionChange.Build);
            bool isUpdate = await update.IsUpdateAvailableAsync(SettingsSingleton.Version, VersionChange.Build);

            if (isUpdate)
            {
                if (MessageBox.Show($"Transparent Twitch Chat Overlay\nVersion {update.Version()} is available. Would you like to download it now?\n(Opens in your default browser)",
                    "New Version Available",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start("https://github.com/baffler/Transparent-Twitch-Chat-Overlay/releases/latest");
                }
            }
        }
        private void webView_ContentLoading(object sender, Microsoft.Web.WebView2.Core.CoreWebView2ContentLoadingEventArgs e)
        {

        }

        private void SetupBrowser()
        {
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            this.bgColor = new SolidColorBrush(Color.FromArgb(SettingsSingleton.Instance.genSettings.OpacityLevel, 0, 0, 0));
            this.Background = this.bgColor;
            this.cOpacity = SettingsSingleton.Instance.genSettings.OpacityLevel;

            if (SettingsSingleton.Instance.genSettings.AutoHideBorders)
                hideBorders();
            else
                drawBorders();

            if (SettingsSingleton.Instance.genSettings.ChatNotificationSound.ToLower() != "none")
            {
                string file = GetSoundClipsFolder() + SettingsSingleton.Instance.genSettings.ChatNotificationSound;
                if (System.IO.File.Exists(file))
                {
                    this.jsCallbackFunctions.MediaFile = file;
                }
                else
                {
                    this.jsCallbackFunctions.MediaFile = string.Empty;
                }
            }

            if (SettingsSingleton.Instance.genSettings.CustomWindows != null)
            {
                foreach (string url in SettingsSingleton.Instance.genSettings.CustomWindows)
                    OpenNewCustomWindow(url, "", SettingsSingleton.Instance.genSettings.AutoHideBorders);
            }

            if ((SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.CustomURL) && (!string.IsNullOrWhiteSpace(SettingsSingleton.Instance.genSettings.CustomURL)))
            {
                this.currentChat = new CustomURLChat(ChatTypes.CustomURL);
                SetCustomChatAddress(SettingsSingleton.Instance.genSettings.CustomURL);
            }
            else if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.YoutubeLive) 
            {
                this.currentChat = new YoutubeChat();
                SetCustomChatAddress(SettingsSingleton.Instance.genSettings.CustomURL);
            }
            else if ((SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.TwitchPopout) && (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.Username)))
            {
                this.currentChat = new CustomURLChat(ChatTypes.TwitchPopout);
                SetCustomChatAddress("https://www.twitch.tv/popout/" + SettingsSingleton.Instance.genSettings.Username + "/chat?popout=");
            }
            else if (!string.IsNullOrWhiteSpace(SettingsSingleton.Instance.genSettings.Username))
            { // TODO: need to clean this up to determine which type of chat to load better
                if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.KapChat)
                {
                    this.currentChat = new Chats.KapChat();
                    SetChatAddress(SettingsSingleton.Instance.genSettings.Username);
                }
                else if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.jChat)
                {
                    this.currentChat = new jChat();
                    SetCustomChatAddress(SettingsSingleton.Instance.genSettings.CustomURL);
                }
                else
                {
                    Uri startupPath = new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);
                    string address = System.IO.Path.GetDirectoryName(startupPath.LocalPath) + "\\index.html";
                    webView.CoreWebView2.Navigate(address);
                }

                if (SettingsSingleton.Instance.genSettings.RedemptionsEnabled)
                {
                    SetupPubSubRedemptions();
                }
            }
            else if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.jChat)
            { // TODO: need to clean this up to determine which type of chat to load better
                this.currentChat = new jChat();
                SetCustomChatAddress(SettingsSingleton.Instance.genSettings.jChatURL);
            }
            else if (SettingsSingleton.Instance.genSettings.ChatType == (int)ChatTypes.YoutubeLive)
            {
                this.currentChat = new YoutubeChat();
                SetCustomChatAddress(SettingsSingleton.Instance.genSettings.CustomURL);
            }
            else
            {
                Uri startupPath = new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);
                string address = System.IO.Path.GetDirectoryName(startupPath.LocalPath) + "\\index.html";
                webView.CoreWebView2.Navigate(address);

                //CefSharp.WebBrowserExtensions.LoadHtml(Browser1,
                //"<html><body style=\"font-size: x-large; color: white; text-shadow: -1px -1px 0 #000, 1px -1px 0 #000, -1px 1px 0 #000, 1px 1px 0 #000; \">Load a channel to connect to by right-clicking the tray icon.<br /><br />You can move and resize the window, then press the [o] button to hide borders, or use the tray icon menu.</body></html>");
            }
        }

        private void MenuItem_SettingsClick(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void MenuItem_Exit(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private void MenuItem_ResetWindowClick(object sender, RoutedEventArgs e)
        {
            ResetWindowPosition();

            if (MessageBox.Show("Show settings folder?", "Settings Folder", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start((Services.Tracker.StoreFactory as Jot.Storage.JsonFileStoreFactory).StoreFolderPath);
            }
        }

        private void MenuItem_IncOpacity(object sender, RoutedEventArgs e)
        {
            this.cOpacity += 15;
            if (this.cOpacity > 255) this.cOpacity = 255;
            SettingsSingleton.Instance.genSettings.OpacityLevel = (byte)this.cOpacity;
            this.bgColor.Color = Color.FromArgb((byte)this.cOpacity, 0, 0, 0);
        }

        private void MenuItem_DecOpacity(object sender, RoutedEventArgs e)
        {
            this.cOpacity -= 15;
            if (this.cOpacity < 0) this.cOpacity = 0;
            SettingsSingleton.Instance.genSettings.OpacityLevel = (byte)this.cOpacity;
            this.bgColor.Color = Color.FromArgb((byte)this.cOpacity, 0, 0, 0);
        }

        private void MenuItem_ResetOpacity(object sender, RoutedEventArgs e)
        {
            this.cOpacity = 0;
            SettingsSingleton.Instance.genSettings.OpacityLevel = 0;
            this.bgColor.Color = Color.FromArgb(0, 0, 0, 0);
        }

        private void PushNewMessage(string message = "")
        {
            string js = this.currentChat.PushNewMessage(message);

            if (!string.IsNullOrEmpty(js))
            {
                this.webView.ExecuteScriptAsync(js);
            }
        }

        private void PushNewChatMessage(string message = "", string nick = "", string color = "")
        {
            //this.Browser1.Dispatcher.Invoke(new Action(() => { });

            string js = this.currentChat.PushNewChatMessage(message, nick, color);
            if (!string.IsNullOrEmpty(js))
                this.webView.ExecuteScriptAsync(js);
        }

        public void SetupPubSubRedemptions()
        {
            DisablePubSubRedemptions();

            if (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.ChannelID)
                && !string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.OAuthToken))
            {
                _pubSub = new TwitchPubSub();
                _pubSub.OnPubSubServiceConnected += _pubSub_OnPubSubServiceConnected;
                _pubSub.OnPubSubServiceClosed += _pubSub_OnPubSubServiceClosed;
                _pubSub.OnPubSubServiceError += _pubSub_OnPubSubServiceError;
                _pubSub.OnListenResponse += _pubSub_OnListenResponse;
                _pubSub.OnChannelPointsRewardRedeemed += _pubSub_OnChannelPointsRewardRedeemed;

                _pubSub.ListenToChannelPoints(SettingsSingleton.Instance.genSettings.ChannelID);
                _pubSub.Connect();
            }
        }

        public void DisablePubSubRedemptions()
        {
            if (_pubSub != null)
            {
                try { _pubSub.OnPubSubServiceConnected -= _pubSub_OnPubSubServiceConnected; }
                catch { }
                try { _pubSub.OnPubSubServiceClosed -= _pubSub_OnPubSubServiceClosed; }
                catch { }
                try { _pubSub.OnPubSubServiceError -= _pubSub_OnPubSubServiceError; }
                catch { }
                try { _pubSub.OnListenResponse -= _pubSub_OnListenResponse; }
                catch { }
                try { _pubSub.OnChannelPointsRewardRedeemed -= _pubSub_OnChannelPointsRewardRedeemed; }
                catch { }

                try
                {
                    _isPubSubConnected = false;
                    if (_isPubSubConnected)
                        _pubSub.Disconnect();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }
        }

        private void _pubSub_OnPubSubServiceConnected(object sender, System.EventArgs e)
        {
            _isPubSubConnected = true;
            if (!string.IsNullOrEmpty(SettingsSingleton.Instance.genSettings.OAuthToken))
            {
                PushNewMessage("PubSub Service Connected");
                _pubSub.SendTopics(SettingsSingleton.Instance.genSettings.OAuthToken);
            }
        }

        private void _pubSub_OnPubSubServiceError(object sender, OnPubSubServiceErrorArgs e)
        {
            PushNewMessage($"PubSub Service Error: {e.Exception.Message}");
        }

        private void _pubSub_OnPubSubServiceClosed(object sender, EventArgs e)
        {
            _isPubSubConnected = false;
            PushNewMessage("PubSub Service Closed");
        }

        private void _pubSub_OnListenResponse(object sender, OnListenResponseArgs e)
        {
            if (!e.Successful)
            {
                PushNewMessage($"Failed to listen! Response: {e.Response.Error}");
            }
            else
                PushNewMessage($"Success! Listening to topic: {e.Topic}");
        }

        private void _pubSub_OnChannelPointsRewardRedeemed(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            if (SettingsSingleton.Instance.genSettings.RedemptionsEnabled)
            {
                var redeem = e.RewardRedeemed.Redemption;

                PushNewChatMessage(
                    $"redeemed '{redeem.Reward.Title}' ({redeem.Reward.Cost} points)", // ~ {e.ChannelId}",
                    redeem.User.DisplayName, "#a1b3c4");

                if (!string.IsNullOrEmpty(redeem.UserInput) && !string.IsNullOrWhiteSpace(redeem.UserInput))
                {
                    PushNewChatMessage($"\"{redeem.UserInput}\"", redeem.User.DisplayName, "#a1b3c4");
                }
            }
        }

        private void MenuItem_DevToolsClick(object sender, RoutedEventArgs e)
        {
            this.webView.CoreWebView2.OpenDevToolsWindow();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (!App.IsShuttingDown)
                ExitApplication();
        }
    }
    static class RandomExtensions
    {
        public static void Shuffle<T>(this Random random, T[] array)
        {
            int n = array.Length;
            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = array[n];
                array[n] = array[k];
                array[k] = temp;
            }
        }
    }

    static class StringExtension
    {
        public static bool isRussian(this string text)
        {
            int numValue;
            var isNumber = int.TryParse(text, out numValue);
            if (isNumber)
            {
                return true;
            }
            Regex regex = new Regex(@"[\p{IsCyrillic}]");
            Match match = regex.Match(text);
            var isSuccess = match.Success;
            return isSuccess;
        }

        public static bool isValidUrl(this string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp
                  || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        public static bool isReadable(this string text)
        {
            return text.Length >= 20 && text.Length <= 60;
        }
    }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class JsCallbackFunctions
    {
        private ConcurrentDictionary<String, String> nickToSound = new ConcurrentDictionary<String, String>();
        private ConcurrentDictionary<String, String> soundToNick = new ConcurrentDictionary<String, String>();
        private string _mediaFile;
        private AudioFileReader _audioFileReader;
        private WaveOutEvent _waveOutDevice;

        public string MediaFile
        {
            get { return _mediaFile; }
            set
            {
                _mediaFile = value;
                InitAudio();
            }
        }

        public JsCallbackFunctions()
        {
            this._mediaFile = "";
        }

        private void VerifyOutputDevice()
        {
            var deviceId = SettingsSingleton.Instance.genSettings.DeviceID;

            if (deviceId < 0)
            {
                SettingsSingleton.Instance.genSettings.DeviceName = "Default";
                return;
            }

            if (deviceId >= WaveOut.DeviceCount)
            {
                SettingsSingleton.Instance.genSettings.DeviceID = -1;
                SettingsSingleton.Instance.genSettings.DeviceName = "Default";
                return;
            }

            var capabilities = WaveOut.GetCapabilities(deviceId);
            if (!SettingsSingleton.Instance.genSettings.DeviceName.StartsWith(capabilities.ProductName))
            {
                SettingsSingleton.Instance.genSettings.DeviceID = -1;
                SettingsSingleton.Instance.genSettings.DeviceName = "Default";
            }
        }

        private void InitAudio()
        {
            if (_waveOutDevice != null)
            {
                _waveOutDevice.Stop();
                _waveOutDevice = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }

            if (string.IsNullOrEmpty(_mediaFile) || string.Equals(_mediaFile.ToLower(), "none")) return;

            _audioFileReader = new AudioFileReader(_mediaFile);
            _audioFileReader.Volume = SettingsSingleton.Instance.genSettings.OutputVolume;
            _waveOutDevice = new WaveOutEvent();

            VerifyOutputDevice();
            if (SettingsSingleton.Instance.genSettings.DeviceID >= 0)
                _waveOutDevice.DeviceNumber = SettingsSingleton.Instance.genSettings.DeviceID;

            _waveOutDevice.Init(_audioFileReader);
        }

        string soundsDirectory = SettingsSingleton.Instance.genSettings.SoundClipsFolder;

        private string? AssignFileForNick(String nick)
        {
            var allFiles = Directory.GetFiles(soundsDirectory);
            new Random().Shuffle(allFiles);
            var randomFiles = allFiles.ToList();

            while (randomFiles.Count > 0)
            {
                var file = randomFiles.First();
                if (soundToNick.ContainsKey(file))
                {
                    randomFiles.RemoveAt(0);
                    continue;
                }
                nickToSound[nick] = file;
                soundToNick[file] = nick;
                return file;
            }

            if (allFiles.Length > 0)
            {
                var randomFile = allFiles.First();
                return randomFile;
            }
            else
            {
                return null;
            }
        }
        private string? GetSoundForNick(String nick)
        {
            nickToSound["inspectorl2"] = soundsDirectory + "\\\\" + "icq.mp3";

            if (nickToSound.ContainsKey(nick))
            {
                var soundPath = nickToSound[nick];
                if  (File.Exists(soundPath)) {
                    return soundPath;
                }
            }

            return AssignFileForNick(nick);
        }

        object isReadingLock = new object();
        protected bool _isReading;
        public bool isReading
        {
            get
            {
                lock (isReadingLock)
                    return _isReading;
            }
            set
            {
                lock (isReadingLock)
                    _isReading = value;
            }
        }

        BlockingCollection<Tuple<string, string>> messagesToRead = new BlockingCollection<Tuple<string, string>>();

        void PopAndRead()
        {
            if (messagesToRead.Count < 1) { 
                return; 
            }
            Tuple<string, string> message;
            messagesToRead.TryTake(out message);

            Task.Run(() =>
            {
                var nick = message.Item1;
                var text = message.Item2;
                read(nick, text);
            });
        }

        private void ScheduleOrRead(string nick, string message)
        {
            messagesToRead.Add(new Tuple<string, string>(nick, message));
            if (isReading != true)
            {
                PopAndRead();
            }
        }

        StreamWriter writer = new StreamWriter("chatLog.txt");

        public void PlaySound(string nick, string message)
        {
            writer.WriteLine(message);
            writer.Flush();

            if (message.isReadable())
            {
                generateAudio(message);
                //ScheduleOrRead(nick, message);
            }
            else
            {
                Task.Run(() =>
                {
                    var soundPath = this.GetSoundForNick(nick);
                    if (soundPath != null)
                    {
                        var mediaPlayer = new MediaPlayer();
                        mediaPlayer.Open(new Uri(soundPath));
                        mediaPlayer.Play();
                    }
                });
            }
        }

        enum TextTokenType
        {
            None,
            Russian,
            English
        };

        private async void generateAudio(string message)
        {
            string url = "http://127.0.0.1:5000/generateTTS"; // Убедитесь, что замените это на ваш реальный URL

            var data = new
            {
                message = message,
                is_russian = true
            };

            using (var httpClient = new HttpClient())
            {
                var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();

                    try
                    {
                        Guid uuid = Guid.NewGuid();
                        string uuidString = uuid.ToString();


                        File.WriteAllBytes(uuidString + ".mp3", bytes);

                        Console.WriteLine("Файл успешно сохранен как output.mp3");

                        var mediaPlayer = new MediaPlayer();
                        mediaPlayer.Open(new Uri("C:\\Users\\Demensdeum\\Documents\\Sources\\Demens-Chat-Overlay\\TransparentTwitchChatWPF\\bin\\x86\\Debug\\"+ uuidString +".mp3"));
                        mediaPlayer.Play();
                        mediaPlayer.MediaEnded += (sender, e) =>
                        {
                            File.Delete("C:\\Users\\Demensdeum\\Documents\\Sources\\Demens-Chat-Overlay\\TransparentTwitchChatWPF\\bin\\x86\\Debug\\" + uuidString + ".mp3");
                        };
                    }
                    catch
                    {
                        Console.WriteLine("AAAAAAAAAAAAA!!!!!!!!!");
                    }

                }
                else
                {
                    Console.WriteLine($"Ошибка: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                }
            }
        }


        private void read(string nick, string message)
        {
           isReading = true;

            var speechSynthesizer = new SpeechSynthesizer();
            speechSynthesizer.Rate = 3;
            var delimiters = new char[] { ' ' };
            var tokens = message.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

            var tokensByGroups = new List<String>();
            var currentGroup = "";
            var previousTokenType = TextTokenType.None;

            var sameTokenCount = 0;
            var previousToken = "";

            foreach (var token in tokens)
            {
                var tokenType = token.isRussian() ? TextTokenType.Russian : TextTokenType.English;

                if (previousTokenType != tokenType && !token.isValidUrl())
                {
                    previousTokenType = tokenType;
                    tokensByGroups.Add(currentGroup);
                    currentGroup = token;
                }
                else
                {
                    if (!token.isValidUrl())
                    {
                            if (previousToken == token)
                            {
                                sameTokenCount += 1;
                            }
                            else
                            {
                            previousToken = token;
                                sameTokenCount = 1;
                            }
                        if (sameTokenCount <= 3)
                        {
                            currentGroup += " " + token.Replace("_", " ");
                        }
                    }
                }
            }

            tokensByGroups.Add(currentGroup);

            foreach (var token in tokensByGroups)
            {
                try
                {
                    var voice = token.isRussian() ? "Microsoft Irina Desktop" : "Microsoft Zira Desktop";
                    speechSynthesizer.SelectVoice(voice);
                    speechSynthesizer.Speak(token);
                }
                catch { // ??
                }
            }

            isReading = false;
            PopAndRead();
        }

        public void showMessageBox(string msg)
        {
            try
            {
                MessageBox.Show(msg);
            }
            catch { }
        }
    }

    public class GeneralSettings
    {
        [Trackable]
        public StringCollection CustomWindows { get; set; }
        [Trackable]
        public string Username { get; set; }
        [Trackable]
        public bool FadeChat { get; set; }
        [Trackable]
        public string FadeTime { get; set; } // fade time in seconds or "false"
        [Trackable]
        public bool BlockBotActivity { get; set; }
        [Trackable]
        public string ChatNotificationSound { get; set; }
        [Trackable]
        public int ThemeIndex { get; set; }
        [Trackable]
        public string CustomCSS { get; set; }
        [Trackable]
        public string TwitchPopoutCSS { get; set; }
        [Trackable]
        public int ChatType { get; set; }
        [Trackable]
        public string CustomURL { get; set; }
        [Trackable]
        public double ZoomLevel { get; set; }
        [Trackable]
        public byte OpacityLevel { get; set; }
        [Trackable]
        public bool AutoHideBorders { get; set; }
        [Trackable]
        public bool EnableTrayIcon { get; set; }
        [Trackable]
        public bool ConfirmClose { get; set; }
        [Trackable]
        public bool HideTaskbarIcon { get; set; }
        [Trackable]
        public bool AllowInteraction { get; set; }
        [Trackable]
        public double VersionTracker { get; set; }
        [Trackable]
        public bool HighlightUsersChat { get; set; }
        [Trackable]
        public bool AllowedUsersOnlyChat { get; set; }
        [Trackable]
        public bool FilterAllowAllMods { get; set; }
        [Trackable]
        public bool FilterAllowAllVIPs { get; set; }
        [Trackable]
        public StringCollection AllowedUsersList { get; set; }
        [Trackable]
        public StringCollection BlockedUsersList { get; set; }
        [Trackable]
        public bool RedemptionsEnabled { get; set; }
        [Trackable]
        public string ChannelID { get; set; }
        [Trackable]
        public string OAuthToken { get; set; }
        [Trackable]
        public bool BetterTtv { get; set; }
        [Trackable]
        public bool FrankerFaceZ { get; set; }
        [Trackable]
        public string jChatURL { get; set; }
        [Trackable]
        public bool CheckForUpdates { get; set; }
        [Trackable]
        public Color ChatHighlightColor { get; set; }
        [Trackable]
        public Color ChatHighlightModsColor { get; set; }
        [Trackable]
        public Color ChatHighlightVIPsColor { get; set; }
        [Trackable]
        public float OutputVolume { get; set; }
        [Trackable]
        public string DeviceName { get; set; }
        [Trackable]
        public int DeviceID { get; set; }
        [Trackable]
        public string SoundClipsFolder { get; set; }
    }
}

#nullable disable