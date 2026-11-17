using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace UsageMon;

public sealed partial class SettingsWindow : Window
{
    private readonly Preferences _preferences;
    private readonly Action _changed;
    private readonly Action _togglePanel;
    private readonly Action _appearanceChanged;
    private bool _ready;
    public event Action? RefreshUsageRequested;

    public SettingsWindow(Preferences preferences, Action changed, Action togglePanel, Action appearanceChanged)
    {
        _preferences = preferences;
        _changed = changed;
        _togglePanel = togglePanel;
        _appearanceChanged = appearanceChanged;
        InitializeComponent();
        BuildExperimental();
        BuildExtraHandleRows();
        BuildProviderHandleRows();
        Title = Loc.T("UsageMon Settings");
        Loc.Apply(Root);
        // Immersive title bar: our own drag area, with caption buttons coloured to match the theme.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionColors();
        Root.SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "UsageMon.ico"));
        SystemBackdrop = new MicaBackdrop();
        double scale = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)Math.Ceiling(1000 * scale), area.Width);
        int height = Math.Min((int)Math.Ceiling(690 * scale), area.Height);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
        ReloadPreferences();
        ApplyTheme();
    }
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.Language = LanguageCombo.SelectedIndex switch { 1 => "zh-Hans", 2 => "en", _ => "System" };
        _appearanceChanged();
    }
    public void ApplyLanguage()
    {
        Title = Loc.T("UsageMon Settings");
        Loc.Apply(Root);
        ReloadPreferences();
    }
    // Two columns of tiles when the window is wide; a single column when it is narrowed.
    private bool _singleColumn;
    private void ApplyLayout(double width)
    {
        bool single = width < 720;
        if (single == _singleColumn && Columns.RowDefinitions.Count > 0 && Grid.GetRow(RightColumn) == (single ? 1 : 0)) return;
        _singleColumn = single;
        Columns.ColumnDefinitions[1].Width = single ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Columns.ColumnSpacing = single ? 0 : 16;
        Columns.RowSpacing = single ? 16 : 0;
        Grid.SetColumn(RightColumn, single ? 0 : 1);
        Grid.SetRow(RightColumn, single ? 1 : 0);
    }
    public void ApplyTheme()
    {
        Root.RequestedTheme = _preferences.ThemeValue;
        UpdateCaptionColors();
    }
    private void UpdateCaptionColors()
    {
        var titleBar = AppWindow.TitleBar;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        byte shade = dark ? (byte)255 : (byte)0;
        titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, shade, shade, shade);
        titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(dark ? (byte)120 : (byte)110, shade, shade, shade);
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(dark ? (byte)30 : (byte)20, shade, shade, shade);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, shade, shade, shade);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(dark ? (byte)50 : (byte)35, shade, shade, shade);
        titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, shade, shade, shade);
    }
    public void ReloadPreferences()
    {
        _ready = false;
        OnTopToggle.IsOn = _preferences.AlwaysOnTop;
        StartupToggle.IsOn = StartupRegistration.IsEnabled();
        ModeCombo.SelectedIndex = _preferences.SeparatePanels ? 1 : 0;
        StickyToggle.IsOn = _preferences.Sticky;
        AutoHideToggle.IsOn = _preferences.AutoHideEdges;
        AutoHideToggle.IsEnabled = _preferences.Sticky;
        LayoutCombo.IsEnabled = !_preferences.SeparatePanels;
        LayoutCombo.SelectedIndex = _preferences.Vertical ? 1 : 0;
        TaskbarToggle.IsOn = _preferences.ShowTaskbar;
        TaskbarAlignCombo.SelectedIndex = _preferences.TaskbarAlign == "Left" ? 1 : 0;
        TaskbarTextSlider.Value = Math.Clamp(_preferences.TaskbarTextSize, 6, 16);
        foreach (var (box, swatch, key) in HandleControls())
        {
            box.IsChecked = _preferences.HandleBars.Contains(key);
            swatch.Background = new SolidColorBrush(ParseColor(_preferences.HandleColor(key)));
        }
        HandleModeCombo.SelectedIndex = _preferences.HandleMode switch { "Combined" => 1, "Parallel" => 2, _ => 0 };
        foreach (var (provider, (_, providerBox)) in _providerHandleRows) providerBox.IsChecked = _preferences.HandleBars.Any(k => k.StartsWith(provider + ":"));
        foreach (var (key, swatch) in _providerSwatches) swatch.Background = new SolidColorBrush(ParseColor(_preferences.HandleColor(key)));
        HandleThicknessSlider.Value = Math.Clamp(_preferences.HandleThickness, 2, 16);
        HandleLengthSlider.Value = Math.Clamp(_preferences.HandleLength, 24, 200);
        LanguageCombo.SelectedIndex = _preferences.Language switch { "zh-Hans" => 1, "en" => 2, _ => 0 };
        ThemeCombo.SelectedIndex = _preferences.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        foreach (var (key, toggle) in _experimentalToggles) toggle.IsOn = _preferences.IsShown(key);
        UpdateExtraHandleRows();
        MaterialCombo.SelectedIndex = _preferences.Material == "Acrylic" ? 1 : 0;
        TransparencySlider.Value = Math.Clamp(_preferences.Transparency, 0, 100);
        PanelSizeSlider.Value = Math.Clamp(_preferences.PanelSize, 16, 192);
        LabelModeCombo.SelectedIndex = _preferences.LabelMode switch { "Always" => 1, "Never" => 2, _ => 0 };
        LabelSizeSlider.Value = Math.Clamp(_preferences.LabelSize, 8, 24);
        ImageStatus.Text = string.IsNullOrEmpty(_preferences.BackgroundImagePath) ? Loc.T("No background image") : Path.GetFileName(_preferences.BackgroundImagePath);
        ClearImageButton.IsEnabled = !string.IsNullOrEmpty(_preferences.BackgroundImagePath);
        RefreshIntervalCombo.SelectedIndex = _preferences.UsageRefreshSeconds switch { 30 => 0, 120 => 2, 300 => 3, _ => 1 };
        _ready = true;
    }
    private async void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (await StartupRegistration.SetAsync(StartupToggle.IsOn)) return;
        _ready = false; // could not enable it (write failure, or blocked in Windows Startup settings): show the real state
        StartupToggle.IsOn = StartupRegistration.IsEnabled();
        _ready = true;
    }
    private void Preference_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preferences.AlwaysOnTop = OnTopToggle.IsOn;
        _changed();
    }
    private void Layout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.Vertical = LayoutCombo.SelectedIndex == 1;
        _changed();
    }
    private void PanelOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preferences.SeparatePanels = ModeCombo.SelectedIndex == 1;
        _preferences.Sticky = StickyToggle.IsOn;
        _preferences.AutoHideEdges = AutoHideToggle.IsOn;
        _changed();
    }
    // The panel checkboxes, the Experimental toggles and the handle rows all edit the same ShowGrok/ShowCopilot flags.
    private void SyncExperimentalControls()
    {
        bool was = _ready;
        _ready = false; // setting IsOn raises Toggled
        foreach (var (key, toggle) in _experimentalToggles) toggle.IsOn = _preferences.IsShown(key);
        UpdateExtraHandleRows();
        _ready = was;
    }
    // One handle row per reading these providers can report.
    private static readonly (string Key, string Label)[] ExtraHandleBars =
    [
        ("Kimi:5h", "Kimi 5h"), ("Kimi:weekly", "Kimi weekly"), ("Grok:used", "Grok"),
        ("Copilot:premium", "Copilot premium"), ("Copilot:chat", "Copilot chat"), ("Copilot:completions", "Copilot completions")
    ];
    private readonly Dictionary<string, (Grid Row, CheckBox Box, Border Swatch)> _extraHandleRows = [];
    private void BuildExtraHandleRows()
    {
        int index = 4; // after the four Codex/Claude rows, before "Multiple bars"
        foreach (var (key, label) in ExtraHandleBars)
        {
            var box = new CheckBox { Content = Loc.T(label), Tag = key, MinWidth = 0 };
            box.Click += Handle_Changed;
            var swatch = new Border { Width = 44, Height = 16, CornerRadius = new CornerRadius(3) };
            var color = new Button { Tag = key, Content = swatch, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(4) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(color, label + " colour");
            color.Click += HandleColor_Click;
            var row = new Grid { Style = (Style)Root.Resources["Row"], Visibility = Visibility.Collapsed };
            row.Children.Add(box);
            row.Children.Add(color);
            HandleCard.Children.Insert(index++, row);
            _extraHandleRows[key] = (row, box, swatch);
        }
    }
    // Parallel mode picks whole providers (both windows); the other modes pick single windows.
    private readonly Dictionary<string, (Grid Row, CheckBox Box)> _providerHandleRows = [];
    private readonly List<(string Key, Border Swatch)> _providerSwatches = [];
    private void BuildProviderHandleRows()
    {
        int index = 0;
        foreach (string provider in new[] { "Codex", "Claude", "Kimi", "Grok", "Copilot" })
        {
            var box = new CheckBox { Content = provider, Tag = provider, MinWidth = 0 };
            box.Click += HandleProvider_Changed;
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (string key in Preferences.HandleKeys.Where(k => k.StartsWith(provider + ":")))
            {
                var swatch = new Border { Width = 44, Height = 16, CornerRadius = new CornerRadius(3) };
                var button = new Button { Tag = key, Content = swatch, Padding = new Thickness(4) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, key + " colour");
                button.Click += HandleColor_Click;
                colors.Children.Add(button);
                _providerSwatches.Add((key, swatch));
            }
            var row = new Grid { Style = (Style)Root.Resources["Row"], Visibility = Visibility.Collapsed };
            row.Children.Add(box);
            row.Children.Add(colors);
            HandleCard.Children.Insert(index++, row);
            _providerHandleRows[provider] = (row, box);
        }
    }
    private void HandleProvider_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not CheckBox { Tag: string provider } box) return;
        _preferences.HandleBars = _preferences.HandleBars.Where(k => !k.StartsWith(provider + ":"))
            .Concat(box.IsChecked == true ? Preferences.HandleKeys.Where(k => k.StartsWith(provider + ":")) : []).ToList();
        _changed();
    }
    private void UpdateExtraHandleRows()
    {
        bool parallel = _preferences.HandleMode == "Parallel";
        foreach (var (barKey, (row, _, _)) in _extraHandleRows)
            row.Visibility = !parallel && _preferences.IsShown(barKey[..barKey.IndexOf(':')]) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in new FrameworkElement[] { Codex5Row, Codex7Row, Claude5Row, Claude7Row })
            row.Visibility = parallel ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (provider, (row, _)) in _providerHandleRows)
            row.Visibility = parallel && (provider is "Codex" or "Claude" || _preferences.IsShown(provider)) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void TogglePanel_Click(object sender, RoutedEventArgs e) => _togglePanel();
    private void PanelSize_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.PanelSize = PanelSizeSlider.Value;
        _changed();
    }
    private void LabelMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.LabelMode = LabelModeCombo.SelectedIndex switch { 1 => "Always", 2 => "Never", _ => "Hover" };
        _changed();
    }
    private void LabelSize_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.LabelSize = LabelSizeSlider.Value;
        _changed();
    }
    private static string StateText(Usage.UsageState state) => Loc.T(state == Usage.UsageState.SignInRequired ? "Sign-in required" : state.ToString());
    // One short line per provider; the full details are in the panel tooltip.
    private static string Status(Usage.UsageSnapshot snapshot, DateTimeOffset now)
    {
        string text = $"{snapshot.Provider} · {StateText(snapshot.State)}";
        if (snapshot.Windows.Count > 0) text += " · " + snapshot.Summary(now).TrimStart('~', ' ');
        if (snapshot.State != Usage.UsageState.Live && snapshot.Message.Length > 0) text += "\n" + Loc.T(snapshot.Message);
        return text;
    }
    private static string Status(Usage.BalanceSnapshot snapshot)
    {
        string text = $"{snapshot.Provider} · {StateText(snapshot.State)}";
        if (snapshot.State is Usage.UsageState.Live or Usage.UsageState.Stale) text += " · " + snapshot.Summary.Replace(snapshot.Provider + " ", "");
        if (snapshot.State != Usage.UsageState.Live && snapshot.Message.Length > 0) text += "\n" + Loc.T(snapshot.Message);
        return text;
    }
    private void ShowStatus(TextBlock box, string key, Usage.BalanceSnapshot snapshot)
    {
        bool shown = _preferences.IsShown(key);
        box.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown) box.Text = Status(snapshot);
    }
    public void UpdateUsage(Usage.UsageSnapshot codex, Usage.UsageSnapshot claude, Usage.BalanceSnapshot grok, Usage.BalanceSnapshot copilot, Usage.BalanceSnapshot kimi, Usage.BalanceSnapshot deepSeek)
    {
        var now = DateTimeOffset.UtcNow;
        CodexStatusText.Text = Status(codex, now);
        ClaudeStatusText.Text = Status(claude, now);
        ShowStatus(GrokStatusText, "Grok", grok);
        ShowStatus(CopilotStatusText, "Copilot", copilot);
        ShowStatus(KimiStatusText, "Kimi", kimi);
        ShowStatus(DeepSeekStatusText, "DeepSeek", deepSeek);
    }
    private void RefreshUsage_Click(object sender, RoutedEventArgs e) => RefreshUsageRequested?.Invoke();
    private void RefreshInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.UsageRefreshSeconds = RefreshIntervalCombo.SelectedIndex switch { 0 => 30, 2 => 120, 3 => 300, _ => 60 };
        _preferences.Save();
    }
    private void EnableBridge_Click(object sender, RoutedEventArgs e)
    {
        try { BridgeStatusText.Text = Loc.T(Usage.ClaudeBridgeInstaller.Install()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { BridgeStatusText.Text = Loc.T("Could not install the bridge. Check Claude settings access and try again."); }
    }
    private void Taskbar_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preferences.ShowTaskbar = TaskbarToggle.IsOn;
        _changed();
    }
    private void Taskbar_Selection(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.TaskbarAlign = TaskbarAlignCombo.SelectedIndex == 1 ? "Left" : "Right";
        _changed();
    }
    private void TaskbarText_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.TaskbarTextSize = TaskbarTextSlider.Value;
        _changed();
    }
    // Every provider with its on/off toggle. Variable: null = nothing to set up, "" = reuses its own CLI/editor sign-in,
    // otherwise the environment variable that can supply an API key. Only the pay-per-use balances are still experimental.
    private static readonly (string Key, string Label, string? Variable, bool Experimental)[] ExperimentalProviders =
    [
        ("Codex", "Codex", null, false), ("Claude", "Claude", null, false),
        ("Grok", "Grok", "", false), ("Copilot", "GitHub Copilot", "", false),
        ("DeepSeek", "DeepSeek", "DEEPSEEK_API_KEY", true), ("Kimi", "Kimi", "MOONSHOT_API_KEY", true)
    ];
    // An empty variable means the provider reuses its own CLI/editor sign-in and needs no key.
    private static bool SignInFound(string key) => key == "Grok"
        ? File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "auth.json"))
        : Usage.CopilotSignIn.HasToken || File.Exists(Path.Combine(Usage.BalanceProviders.CopilotFolder, "apps.json")) || File.Exists(Path.Combine(Usage.BalanceProviders.CopilotFolder, "hosts.json"));
    // Grok has no in-app login: open a terminal running the CLI's own, and wait for it to write its auth file.
    private async Task SignInGrokAsync()
    {
        var started = DateTime.UtcNow;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/k grok login") { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return; }
        // The file may already exist with an expired token: wait until the CLI rewrites it.
        string auth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "auth.json");
        for (int i = 0; i < 100 && !(File.Exists(auth) && File.GetLastWriteTimeUtc(auth) > started); i++) await Task.Delay(3000);
    }
    private async Task SignInCopilotAsync()
    {
        var cancel = new CancellationTokenSource(); // not disposed: the dialog's Closed event can still fire after this method returns
        var challenge = await Usage.CopilotSignIn.StartAsync(cancel.Token);
        if (challenge is null) { await ShowMessageAsync("Could not reach GitHub. Check your connection and try again."); return; }
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(challenge.UserCode);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(challenge.VerificationUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = Loc.T("Enter this code at ") + challenge.VerificationUri + Loc.T(" (copied to the clipboard). Waiting for you to approve…"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = challenge.UserCode, FontSize = 28, FontFamily = new FontFamily("Consolas"), IsTextSelectionEnabled = true });
        var dialog = new ContentDialog { Title = Loc.T("Sign in to GitHub Copilot"), Content = body, CloseButtonText = Loc.T("Cancel"), XamlRoot = Root.XamlRoot };
        dialog.Closed += (_, _) => cancel.Cancel();
        _ = dialog.ShowAsync();
        bool ok = false;
        try { ok = await Usage.CopilotSignIn.WaitAsync(challenge, cancel.Token); }
        catch (OperationCanceledException) { }
        dialog.Hide();
        if (!ok && !cancel.IsCancellationRequested) await ShowMessageAsync("Sign-in did not complete. Try again.");
    }
    // Kimi Code device login: pick the account region, approve in the browser, wait for the token.
    private async Task SignInKimiAsync()
    {
        var picker = new ContentDialog
        {
            Title = Loc.T("Sign in to Kimi Code"), Content = new TextBlock { Text = Loc.T("Which Kimi site is your account on?"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Usage.KimiSignIn.China.Name, SecondaryButtonText = Usage.KimiSignIn.Global.Name, CloseButtonText = Loc.T("Cancel"), XamlRoot = Root.XamlRoot
        };
        var choice = await picker.ShowAsync();
        if (choice == ContentDialogResult.None) return;
        var cancel = new CancellationTokenSource(); // not disposed: the dialog's Closed event can still fire after this method returns
        var challenge = await Usage.KimiSignIn.StartAsync(choice == ContentDialogResult.Primary ? Usage.KimiSignIn.China : Usage.KimiSignIn.Global, cancel.Token);
        if (challenge is null) { await ShowMessageAsync("Could not reach Kimi. Check your connection and try again."); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(challenge.VerificationUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = Loc.T("Approve the sign-in in your browser. If asked for a code, enter:"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = challenge.UserCode, FontSize = 28, FontFamily = new FontFamily("Consolas"), IsTextSelectionEnabled = true });
        var dialog = new ContentDialog { Title = Loc.T("Sign in to Kimi Code"), Content = body, CloseButtonText = Loc.T("Cancel"), XamlRoot = Root.XamlRoot };
        dialog.Closed += (_, _) => cancel.Cancel();
        _ = dialog.ShowAsync();
        bool ok = false;
        try { ok = await Usage.KimiSignIn.WaitAsync(challenge, cancel.Token); }
        catch (OperationCanceledException) { }
        dialog.Hide();
        if (!ok && !cancel.IsCancellationRequested) await ShowMessageAsync("Sign-in did not complete. Try again.");
    }
    private async Task ShowMessageAsync(string message) =>
        await new ContentDialog { Content = new TextBlock { Text = Loc.T(message), TextWrapping = TextWrapping.Wrap }, CloseButtonText = Loc.T("OK"), XamlRoot = Root.XamlRoot }.ShowAsync();
    private readonly Dictionary<string, ToggleSwitch> _experimentalToggles = [];
    private void BuildExperimental()
    {
        foreach (var (key, label, variable, experimental) in ExperimentalProviders)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(0, 6, 12, 6) };
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            name.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            if (experimental)
                name.Children.Add((Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" CornerRadius=\"4\" Padding=\"6,1,6,1\" BorderThickness=\"1\" VerticalAlignment=\"Center\" BorderBrush=\"{ThemeResource ControlStrokeColorDefaultBrush}\">"
                    + $"<TextBlock Text=\"{Loc.T("Experimental")}\" FontSize=\"11\" Opacity=\"0.72\" /></Border>"));
            text.Children.Add(name);
            if (variable is null) { AddExperimentalRow(text, key); continue; }
            bool signIn = variable.Length == 0;
            bool found = signIn ? SignInFound(key) : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
            if (signIn)
            {
                var status = new TextBlock { Style = (Style)Root.Resources["Caption"], TextWrapping = TextWrapping.Wrap };
                var button = new Button { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 4, 0, 0) };
                void Refresh()
                {
                    bool now = SignInFound(key);
                    status.Text = Loc.T(key == "Grok"
                        ? (now ? "Grok sign-in detected. Uses its saved login; no key needed." : "Not signed in. Sign in with the Grok CLI (opens a terminal).")
                        : (now ? "GitHub Copilot sign-in detected. Uses its saved login; no key needed." : "Not signed in. Sign in with GitHub (device code), or sign in to Copilot in VS Code."));
                    button.Content = Loc.T(now ? "Sign in again" : "Sign in");
                }
                Refresh();
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try { if (key == "Grok") await SignInGrokAsync(); else await SignInCopilotAsync(); }
                    finally { button.IsEnabled = true; Refresh(); if (SignInFound(key) && _preferences.IsShown(key)) RefreshUsageRequested?.Invoke(); }
                };
                text.Children.Add(status);
                text.Children.Add(button);
                AddExperimentalRow(text, key);
                continue;
            }
            // API-key providers: paste the key here (stored DPAPI-protected, readable only by this Windows user); the environment variable still works.
            string secret = key == "DeepSeek" ? "deepseek-key" : "moonshot-key";
            var keyStatus = new TextBlock { Style = (Style)Root.Resources["Caption"], TextWrapping = TextWrapping.Wrap };
            var keyBox = new PasswordBox { PlaceholderText = Loc.T("Paste API key"), Width = 230, VerticalAlignment = VerticalAlignment.Center };
            var save = new Button { Content = Loc.T("Save"), Padding = new Thickness(10, 3, 10, 3) };
            var remove = new Button { Content = Loc.T("Remove"), Padding = new Thickness(10, 3, 10, 3) };
            var kimiStatus = new TextBlock { Style = (Style)Root.Resources["Caption"], TextWrapping = TextWrapping.Wrap };
            var kimiButton = new Button { Padding = new Thickness(10, 3, 10, 3) };
            var kimiOut = new Button { Content = Loc.T("Sign out"), Padding = new Thickness(10, 3, 10, 3) };
            void RefreshKey()
            {
                bool stored = Usage.SecretStore.Has(secret);
                keyStatus.Text = Loc.T(stored ? "API key saved. Paste a new one to replace it."
                    : found ? "Using the API key from the environment variable. Paste one here to override it." : "Paste your API key to show the balance.");
                remove.Visibility = stored ? Visibility.Visible : Visibility.Collapsed;
                if (key != "Kimi") return;
                bool login = Usage.KimiSignIn.HasLogin;
                kimiStatus.Text = Loc.T(login ? "Signed in to Kimi Code: the plan's 5h and weekly quota are added." : "Optional: sign in with your browser to add the Kimi Code plan quota.");
                kimiButton.Content = Loc.T(login ? "Sign in again" : "Sign in with browser");
                kimiOut.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
            }
            void Changed() { RefreshKey(); if (_preferences.IsShown(key)) RefreshUsageRequested?.Invoke(); }
            save.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(keyBox.Password)) return;
                Usage.SecretStore.Save(secret, keyBox.Password.Trim());
                keyBox.Password = "";
                Changed();
            };
            remove.Click += (_, _) => { Usage.SecretStore.Delete(secret); Changed(); };
            kimiOut.Click += (_, _) => { Usage.KimiSignIn.SignOut(); Changed(); };
            kimiButton.Click += async (_, _) =>
            {
                kimiButton.IsEnabled = false;
                try { await SignInKimiAsync(); }
                finally { kimiButton.IsEnabled = true; Changed(); }
            };
            text.Children.Add(keyStatus);
            var keyLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            keyLine.Children.Add(keyBox);
            keyLine.Children.Add(save);
            keyLine.Children.Add(remove);
            text.Children.Add(keyLine);
            if (key == "Kimi")
            {
                kimiStatus.Margin = new Thickness(0, 6, 0, 0);
                text.Children.Add(kimiStatus);
                var kimiLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                kimiLine.Children.Add(kimiButton);
                kimiLine.Children.Add(kimiOut);
                text.Children.Add(kimiLine);
            }
            RefreshKey();
            AddExperimentalRow(text, key);
        }
    }
    private void AddExperimentalRow(StackPanel text, string key)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Tag = key };
        toggle.Toggled += Experimental_Changed;
        _experimentalToggles[key] = toggle;
        var row = new Grid { Style = (Style)Root.Resources["Row"] };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        ExperimentalCard.Children.Add(row);
    }
    private void Experimental_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        foreach (var (key, toggle) in _experimentalToggles) _preferences.SetShown(key, toggle.IsOn);
        SyncExperimentalControls();
        _changed();
        if (ExperimentalProviders.Any(p => _preferences.IsShown(p.Key))) RefreshUsageRequested?.Invoke();
    }

    private void Appearance_Changed(object sender, SelectionChangedEventArgs e) => UpdateAppearance();
    private void Transparency_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => UpdateAppearance();
    private void UpdateAppearance()
    {
        if (!_ready) return;
        _preferences.Theme = ThemeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _preferences.Material = MaterialCombo.SelectedIndex == 1 ? "Acrylic" : "Mica";
        _preferences.Transparency = TransparencySlider.Value;
        _appearanceChanged();
    }
    private async void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        ChooseImageButton.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMon", "Backgrounds");
            _preferences.BackgroundImagePath = await BackgroundImageStore.ImportAsync(file, folder);
            ImageStatus.Text = file.Name;
            ClearImageButton.IsEnabled = true;
            _appearanceChanged();
        }
        catch (Exception error) { ImageStatus.Text = Loc.T("Could not load image: ") + error.Message; }
        finally { ChooseImageButton.IsEnabled = true; }
    }
    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        _preferences.BackgroundImagePath = null;
        ImageStatus.Text = Loc.T("No background image");
        ClearImageButton.IsEnabled = false;
        _appearanceChanged();
    }
    private IEnumerable<(CheckBox Box, Border Swatch, string Key)> HandleControls() =>
    [
        (Codex5Check, Codex5Swatch, "Codex:5h"), (Codex7Check, Codex7Swatch, "Codex:7d"),
        (Claude5Check, Claude5Swatch, "Claude:5h"), (Claude7Check, Claude7Swatch, "Claude:7d"),
        .. _extraHandleRows.Select(r => (r.Value.Box, r.Value.Swatch, r.Key))
    ];
    private static Windows.UI.Color ParseColor(string hex) => Windows.UI.Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    private void Handle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        // Keep choices for providers whose rows are hidden right now.
        var present = HandleControls().Select(h => h.Key).ToHashSet();
        _preferences.HandleBars = _preferences.HandleBars.Where(k => !present.Contains(k))
            .Concat(HandleControls().Where(h => h.Box.IsChecked == true).Select(h => h.Key)).ToList();
        _changed();
    }
    private void Handle_Selection(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.HandleMode = HandleModeCombo.SelectedIndex switch { 1 => "Combined", 2 => "Parallel", _ => "Stacked" };
        UpdateExtraHandleRows();
        _changed();
    }
    private void HandleSize_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _preferences.HandleThickness = HandleThicknessSlider.Value;
        _preferences.HandleLength = HandleLengthSlider.Value;
        _changed();
    }
    private void HandleColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } button) return;
        var picker = new ColorPicker
        {
            Color = ParseColor(_preferences.HandleColor(key)), IsAlphaEnabled = false, IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false, IsHexInputVisible = true
        };
        picker.ColorChanged += (_, args) =>
        {
            var c = args.NewColor;
            _preferences.HandleColors[key] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var swatch = HandleControls().First(h => h.Key == key).Swatch;
            swatch.Background = new SolidColorBrush(c);
            foreach (var (swatchKey, other) in _providerSwatches) if (swatchKey == key) other.Background = new SolidColorBrush(c);
            _changed();
        };
        new Flyout { Content = picker }.ShowAt(button);
    }
}
