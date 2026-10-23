using Microsoft.UI.Xaml;

namespace UsageMon;

public partial class App : Application
{
    private FloatingWindow _floating = null!;
    private FloatingWindow[] _panels = [];
    private bool _panelsHidden;
    private SettingsWindow? _settings;
    private TaskbarWindow _taskbar = null!;
    private TrayIcon _tray = null!;
    private readonly Preferences _preferences = Preferences.Load();
    private bool _exiting;
    private string _language = "System";
    private Usage.UsageMonitor _usage = null!;
    private readonly DispatcherTimer _usageDisplayTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public App()
    {
        // Diagnostics: USAGEMON_TRACE=1 logs every managed exception, including ones swallowed by WinRT/XAML.
        if (Environment.GetEnvironmentVariable("USAGEMON_TRACE") == "1")
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "usagemon-trace.log"), e.Exception + Environment.NewLine + Environment.StackTrace + Environment.NewLine + "----" + Environment.NewLine); }
                catch (IOException) { }
            };
        UnhandledException += (_, e) =>
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), e.Exception.ToString()); }
            catch (IOException) { }
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Loc.Init(_preferences.Language);
        _language = _preferences.Language;
        Usage.ClaudeBridgeInstaller.RepairScript();
        _usage = new(_preferences);
        _panels = [new(_preferences, PanelKind.Joined), new(_preferences, PanelKind.Codex), new(_preferences, PanelKind.Claude)];
        _floating = _panels[0]; // Stable HWND owns the tray even while joined mode is hidden.
        foreach (var panel in _panels)
        {
            panel.SettingsRequested += ShowSettings;
            panel.RefreshUsageRequested += () => _ = _usage.RefreshAsync();
            panel.HideRequested += () => panel.AppWindow.Hide();
            panel.ExitRequested += ExitApp;
            panel.PreferencesChanged += ApplyPanelPreferences;
            panel.Moving += SnapPanel;
            panel.DragCompleted += p => { SnapPanel(p); p.UpdateDocking(); };
            panel.Closed += (_, e) =>
            {
                if (!_exiting) { e.Handled = true; panel.AppWindow.Hide(); }
            };
        }
        _taskbar = new TaskbarWindow(_preferences);
        _taskbar.SettingsRequested += ShowSettings;
        _taskbar.RefreshUsageRequested += () => _ = _usage.RefreshAsync();
        _taskbar.ExitRequested += ExitApp;
        _taskbar.Closed += (_, e) => { if (!_exiting) { e.Handled = true; _taskbar.AppWindow.Hide(); } };
        _tray = new TrayIcon(_floating, ShowSettings, TogglePanel, ExitApp);
        ApplyPanelPreferences();
        _usage.Updated += () => _floating.DispatcherQueue.TryEnqueue(UpdateUsageDisplay);
        _usageDisplayTimer.Tick += (_, _) => UpdateUsageDisplay();
        _usageDisplayTimer.Start();
        UpdateUsageDisplay();
        _usage.Start();
        if (Environment.GetCommandLineArgs().Contains("--settings")) ShowSettings();
    }

    public void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(_preferences, () =>
            {
                ApplyPanelPreferences();
            }, TogglePanel, () =>
            {
                _preferences.Save();
                _settings?.ApplyTheme();
                if (_language != _preferences.Language) { _language = _preferences.Language; Loc.Init(_language); ApplyLanguage(); }
                foreach (var panel in _panels) panel.ApplyAppearance();
                if (_preferences.ShowTaskbar) _taskbar.ApplyPreferences();
            });
            _settings.Closed += (_, _) => _settings = null;
            _settings.RefreshUsageRequested += async () => await _usage.RefreshAsync();
        }
        _settings.Activate();
        _settings.AppWindow.Show();
        UpdateUsageDisplay();
    }
    private void UpdateUsageDisplay()
    {
        if (_exiting) return;
        _taskbar.UpdateUsage(_usage.Codex, _usage.Claude, _usage.DeepSeek, _usage.Kimi);
        foreach (var panel in _panels) { panel.UpdateUsage(_usage.Codex, _usage.Claude); panel.UpdateBalances(_usage.DeepSeek, _usage.Kimi); }
        _settings?.UpdateUsage(_usage.Codex, _usage.Claude);
    }

    private void ApplyLanguage()
    {
        foreach (var panel in _panels) panel.ApplyLanguage();
        _taskbar.ApplyLanguage();
        _settings?.ApplyLanguage();
        UpdateUsageDisplay();
    }

    private void TogglePanel()
    {
        _panelsHidden = _panels.Any(p => p.AppWindow.IsVisible);
        ApplyPanelPreferences();
    }

    private void ApplyPanelPreferences()
    {
        foreach (var panel in _panels)
        {
            panel.ApplyPreferences();
            bool enabled = panel.Kind switch
            {
                PanelKind.Joined => (!_preferences.SeparatePanels && (_preferences.ShowCodex || _preferences.ShowClaude)) || _preferences.ShowDeepSeek || _preferences.ShowKimi,
                PanelKind.Codex => _preferences.SeparatePanels && _preferences.ShowCodex,
                _ => _preferences.SeparatePanels && _preferences.ShowClaude
            };
            if (enabled && !_panelsHidden)
            {
                if (!panel.AppWindow.IsVisible) { panel.Activate(); panel.AppWindow.Show(); }
                panel.UpdateDocking();
            }
            else panel.AppWindow.Hide();
        }
        if (_preferences.ShowTaskbar)
        {
            _taskbar.ApplyPreferences();
            if (!_taskbar.AppWindow.IsVisible) { _taskbar.Activate(); _taskbar.AppWindow.Show(); }
        }
        else _taskbar.AppWindow.Hide();
        _preferences.Save();
        _settings?.ReloadPreferences();
    }

    private void SnapPanel(FloatingWindow panel)
    {
        if (!_preferences.Sticky) return;
        var bounds = panel.ScreenBounds;
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(panel.AppWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        var others = _panels.Where(p => p != panel && p.AppWindow.IsVisible && !p.IsAutoHidden).Select(p => p.ScreenBounds);
        panel.MoveTo(PanelSnapping.Snap(bounds, work, others, (int)Math.Ceiling(14 * panel.Scale)));
    }

    private void ExitApp()
    {
        _exiting = true;
        _usageDisplayTimer.Stop();
        _usage.Dispose();
        _preferences.Save();
        _tray.Dispose();
        _settings?.Close();
        _taskbar.Close();
        foreach (var panel in _panels) panel.Close();
        Exit();
    }
}
