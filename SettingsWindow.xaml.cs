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
        CodexCheck.IsChecked = _preferences.ShowCodex;
        ClaudeCheck.IsChecked = _preferences.ShowClaude;
        StickyToggle.IsOn = _preferences.Sticky;
        AutoHideToggle.IsOn = _preferences.AutoHideEdges;
        AutoHideToggle.IsEnabled = _preferences.Sticky;
        LayoutCombo.IsEnabled = !_preferences.SeparatePanels;
        LayoutCombo.SelectedIndex = _preferences.Vertical ? 1 : 0;
        TaskbarToggle.IsOn = _preferences.ShowTaskbar;
        TaskbarAlignCombo.SelectedIndex = _preferences.TaskbarAlign == "Left" ? 1 : 0;
        TaskbarTextSlider.Value = Math.Clamp(_preferences.TaskbarTextSize, 6, 16);
        LanguageCombo.SelectedIndex = _preferences.Language switch { "zh-Hans" => 1, "en" => 2, _ => 0 };
        ThemeCombo.SelectedIndex = _preferences.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        DeepSeekToggle.IsOn = _preferences.ShowDeepSeek;
        KimiToggle.IsOn = _preferences.ShowKimi;
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
    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (!StartupRegistration.Set(StartupToggle.IsOn)) StartupToggle.IsOn = StartupRegistration.IsEnabled(); // could not write the Run key
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
        _preferences.ShowCodex = CodexCheck.IsChecked == true;
        _preferences.ShowClaude = ClaudeCheck.IsChecked == true;
        _preferences.Sticky = StickyToggle.IsOn;
        _preferences.AutoHideEdges = AutoHideToggle.IsOn;
        _changed();
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
    // One short line per provider; the full details are in the panel tooltip.
    private static string Status(Usage.UsageSnapshot snapshot, DateTimeOffset now)
    {
        string text = $"{snapshot.Provider} · {Loc.T(snapshot.State.ToString())}";
        if (snapshot.Windows.Count > 0) text += " · " + snapshot.Summary(now).TrimStart('~', ' ');
        if (snapshot.State != Usage.UsageState.Live && snapshot.Message.Length > 0) text += "\n" + Loc.T(snapshot.Message);
        return text;
    }
    public void UpdateUsage(Usage.UsageSnapshot codex, Usage.UsageSnapshot claude)
    {
        var now = DateTimeOffset.UtcNow;
        CodexStatusText.Text = Status(codex, now);
        ClaudeStatusText.Text = Status(claude, now);
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
    private void Experimental_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preferences.ShowDeepSeek = DeepSeekToggle.IsOn;
        _preferences.ShowKimi = KimiToggle.IsOn;
        _changed();
        if (_preferences.ShowDeepSeek || _preferences.ShowKimi) RefreshUsageRequested?.Invoke();
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
}
