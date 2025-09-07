using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Windows.Shell;
using System.IO;

namespace ComfyUIClientWrapper
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<TabItem, WebView2> _tabContentMapping = new();

        private readonly string[] _tabOrdinalNames =
        [
            "Primary", "Secondary", "Tertiary", "Quaternary", "Quinary", "Senary", "Septenary", "Octonary", "Nonary",
            "Denary"
        ];

        private bool _isFullscreen;

        private AppSettings _settings = null!;
        private WindowState _storedWindowState;
        private WindowStyle _storedWindowStyle;

        public MainWindow()
        {
            InitializeComponent();
            LoadAndApplyTheme();
            TabControl.SelectionChanged += TabControl_SelectionChanged;
        }
        
        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                // Store the current state and enter fullscreen
                _storedWindowState = WindowState;
                _storedWindowStyle = WindowStyle;

                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }
                
                WindowStyle = WindowStyle.None;
                // We set WindowState to Maximized to fill the screen, not Normal.
                WindowState = WindowState.Maximized;
                
                _isFullscreen = true;
            }
            else
            {
                // Restore the previous state and exit fullscreen
                WindowState = _storedWindowState;
                WindowStyle = _storedWindowStyle;
                
                _isFullscreen = false;
            }
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11) ToggleFullscreen();
            if (e.Key == Key.F5) RefreshButton_Click(sender, e);
        }

        private void LoadAndApplyTheme()
        {
            _settings = SettingsManager.LoadSettings();
            var themeToLoad = _settings.Theme;

            if (themeToLoad == "System Detected") themeToLoad = IsWindowsInLightMode() ? "LightTheme" : "DarkTheme";

            ApplyTheme(themeToLoad);
        }

        private void ApplyTheme(string themeName)
        {
            // Find and remove the old theme dictionary if it exists
            var oldTheme = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.EndsWith(".xaml"));

            if (oldTheme != null) Application.Current.Resources.MergedDictionaries.Remove(oldTheme);

            // Add the new theme dictionary
            var uri = new Uri($"{themeName}.xaml", UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = uri };
            Application.Current.Resources.MergedDictionaries.Add(resourceDict);

            // Update existing WebView2 instances with the new background color
            var wpfColor = (Color)Application.Current.Resources["WebViewBackgroundColor"];
            var drawingColor = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
            foreach (var webView in _tabContentMapping.Values) webView.DefaultBackgroundColor = drawingColor;
        }

        private bool IsWindowsInLightMode()
        {
            try
            {
                return (int)Registry.GetValue(
                    @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1)! == 1;
            }
            catch
            {
                return true; // Default to light mode on error
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AddNewTab();
        }

        private void UpdateTabHeaders()
        {
            for (var i = 0; i < TabControl.Items.Count; i++)
            {
                if (TabControl.Items[i] is TabItem tab)
                {
                    var tabName = i < _tabOrdinalNames.Length ? _tabOrdinalNames[i] : $"Tab {i + 1}";
                    if (tab.Header is StackPanel headerPanel && headerPanel.Children[0] is TextBlock textBlock)
                    {
                        // Store the base name in the Tag property for later use
                        tab.Tag = tabName;
                        // The text is now updated dynamically by the WebView2 event, so we just set the base name here.
                        textBlock.Text = tabName;
                    }
                }
            }
        }

        private void UpdateRefreshButtonVisibility()
        {
            RefreshButton.Visibility = TabControl.HasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void AddNewTab()
        {
            var newTab = new TabItem();
            var webView = new WebView2();

            var wpfColor = (Color)Application.Current.Resources["WebViewBackgroundColor"];
            webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);

            _tabContentMapping[newTab] = webView;

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });

            var closeButton = new Button { Style = (Style)FindResource("TabCloseButton"), Tag = newTab };
            closeButton.Click += CloseButton_Click;
            headerPanel.Children.Add(closeButton);

            newTab.Header = headerPanel;

            webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

            TabControl.Items.Add(newTab);
            UpdateTabHeaders();

            TabControl.SelectedItem = newTab;

            try
            {
                // Define a user-writable path in AppData for WebView2's user data.
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Draekz\\ComfyUI Client Wrapper");

                // Create the environment and initialize the WebView2 control.
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(environment);

                // Subscribe to title changes after WebView2 is ready.
                webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    var title = webView.CoreWebView2.DocumentTitle;
                    var progressString = GetProgressStringFromTitle(title);
                    
                    // Only update the taskbar if the change happened in the currently visible WebView2
                    if (!ReferenceEquals(MainContentPresenter.Content, webView))
                        return;
                    
                    this.Title = $"ComfyUI {progressString}".Trim();
                    UpdateTaskbarProgressFromTitle(webView.CoreWebView2.DocumentTitle);
                };
                    
                // Set the source AFTER initialization is successful.
                webView.Source = new Uri("http://127.0.0.1:8188/");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"WebView2 core initialization failed. Please ensure the WebView2 Runtime is installed. Error: {ex.Message}",
                    "Core Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            webView.Source = new Uri("http://127.0.0.1:8188/");

            UpdateRefreshButtonVisibility();
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl && TabControl.SelectedItem != null)
            {
                if (_tabContentMapping.TryGetValue(
                        TabControl.SelectedItem as TabItem ?? throw new InvalidOperationException(),
                        out var webViewToShow))
                {
                    MainContentPresenter.Content = webViewToShow;

                    var title = webViewToShow.CoreWebView2?.DocumentTitle ?? string.Empty;
                    var progressString = GetProgressStringFromTitle(title);
                    this.Title = $"ComfyUI {progressString}".Trim();
                    UpdateTaskbarProgressFromTitle(webViewToShow.CoreWebView2?.DocumentTitle ?? string.Empty);
                }
            }
            else
            {
                MainContentPresenter.Content = null;
                this.Title = "ComfyUI";
                UpdateTaskbarProgressFromTitle(string.Empty);

            }
        }

        // 1. ADDED: The new, single source of truth for progress logic.
        private Match? GetPrioritizedProgressMatch(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
    
            var matches = Regex.Matches(title, @"\[(\d+)%\]");

            // Find the last match that is not "[0%]"
            return matches.LastOrDefault(m => m.Value != "[0%]");
        }
        
        private string GetProgressStringFromTitle(string title)
        {
            var match = GetPrioritizedProgressMatch(title);
    
            // Return the match's formatted value (e.g., "[55%]") or an empty string.
            return match?.Value ?? string.Empty;
        }
        
        private void UpdateTaskbarProgressFromTitle(string title)
        {
            var match = GetPrioritizedProgressMatch(title);

            // If a valid match was found and we can parse its number...
            if (match != null && int.TryParse(match.Groups[1].Value, out int percentage))
            {
                // ...update the taskbar progress.
                TaskBarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                TaskBarItemInfo.ProgressValue = percentage / 100.0;
            }
            else
            {
                // ...otherwise, reset the progress.
                TaskBarItemInfo.ProgressState = TaskbarItemProgressState.None;
            }
        }
        
        private void WebView_CoreWebView2InitializationCompleted(object? sender,
            CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                MessageBox.Show(
                    $"WebView2 core initialization failed. Please ensure the WebView2 Runtime is installed. Error: {e.InitializationException.Message}",
                    "Core Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void UpdateThemeCheckmarks()
        {
            if (settingsButton.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "Theme") is not { } themeParentMenu)
            {
                return;
            }
            
            foreach (var item in themeParentMenu.Items)
            {
                if (item is MenuItem { Tag: string themeTag } themeItem)
                {
                    themeItem.IsChecked = (themeTag == _settings.Theme);
                }
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.ContextMenu != null)
            {
                UpdateThemeCheckmarks();
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var selectedTheme = menuItem?.Tag as string;

            if (string.IsNullOrEmpty(selectedTheme)) return;

            _settings.Theme = selectedTheme;
            SettingsManager.SaveSettings(_settings);

            var themeToLoad = selectedTheme;
            if (themeToLoad == "System Detected") themeToLoad = IsWindowsInLightMode() ? "LightTheme" : "DarkTheme";

            ApplyTheme(themeToLoad);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var tabToClose = (TabItem)button.Tag;

            var closingTabIndex = TabControl.Items.IndexOf(tabToClose);

            if (_tabContentMapping.TryGetValue(tabToClose, out var webViewToDispose))
            {
                webViewToDispose.Dispose();
                _tabContentMapping.Remove(tabToClose);
            }

            TabControl.SelectionChanged -= TabControl_SelectionChanged;

            TabControl.Items.Remove(tabToClose);
            UpdateTabHeaders();

            if (TabControl.Items.Count > 0)
            {
                var newIndex = Math.Min(closingTabIndex, TabControl.Items.Count - 1);
                TabControl.SelectedIndex = newIndex;

                if (_tabContentMapping.TryGetValue(TabControl.SelectedItem as TabItem ?? throw new InvalidOperationException(), out var webViewToShow))
                    MainContentPresenter.Content = webViewToShow;
            }
            else
            {
                MainContentPresenter.Content = null;
            }

            TabControl.SelectionChanged += TabControl_SelectionChanged;
            UpdateRefreshButtonVisibility();
        }

        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (TabControl.SelectedItem is TabItem selectedTab &&
                _tabContentMapping.TryGetValue(selectedTab, out var currentWebView))
                currentWebView.Reload();
        }
        
        private void DevToolsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TabControl.SelectedItem is TabItem selectedTab &&
                _tabContentMapping.TryGetValue(selectedTab, out var currentWebView))
            {
                // This command opens the DevTools window for the selected tab's WebView2 instance
                currentWebView.CoreWebView2?.OpenDevToolsWindow();
            }
        }
        
        private void CloseOtherTabs_Click(object sender, RoutedEventArgs e)
        {
            if (TabControl.SelectedItem is not TabItem selectedTab)
                return;

            var tabsToRemove = _tabContentMapping.Keys.Where(tab => tab != selectedTab).ToList();

            foreach (var tab in tabsToRemove)
            {
                if (_tabContentMapping.TryGetValue(tab, out var webViewToDispose))
                {
                    webViewToDispose.Dispose();
                    _tabContentMapping.Remove(tab);
                }

                TabControl.Items.Remove(tab);
            }

            UpdateTabHeaders();
            UpdateRefreshButtonVisibility();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
