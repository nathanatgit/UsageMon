using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace UsageMon;

// A small always-on-top strip placed over the Windows 11 taskbar, just left of the notification area.
public sealed partial class TaskbarWindow : Window
{
    private readonly Preferences _preferences;
    private readonly nint _hwnd;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CompactWindowSizing _compactSizing;
    private bool _lightTaskbar;
    private int _ticks;
    public event Action? SettingsRequested;
    public event Action? RefreshUsageRequested;
    public event Action? ExitRequested;

    public TaskbarWindow(Preferences preferences)
    {
        InitializeComponent();
        Loc.Apply(Root);
        _preferences = preferences;
        Title = "UsageMon taskbar";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        SystemBackdrop = new TransparentPanelBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _compactSizing = new CompactWindowSizing(_hwnd);
        NativeMethods.ShapePanel(_hwnd, 1, 1);
        NativeMethods.MakeToolWindow(_hwnd);
        // Explorer can raise the taskbar above us; reassert position, z-order and theme.
        _timer.Tick += (_, _) =>
        {
            if (!AppWindow.IsVisible) return;
            if (++_ticks % 3 == 0) TaskbarLayout.RefreshInBackground();
            ApplyTheme();
            Reposition();
        };
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    public void UpdateUsage(Usage.UsageSnapshot codex, Usage.UsageSnapshot claude, Usage.BalanceSnapshot deepSeek, Usage.BalanceSnapshot kimi)
    {
        var now = DateTimeOffset.UtcNow;
        CodexReadout.Update(codex, now);
        ClaudeReadout.Update(claude, now);
        CodexText.Text = Note(codex, now);
        ClaudeText.Text = Note(claude, now);
        CodexText.Opacity = codex.State == Usage.UsageState.Live ? 1 : .6;
        ClaudeText.Opacity = claude.State == Usage.UsageState.Live ? 1 : .6;
        DeepSeekReadout.Update(deepSeek);
        KimiReadout.Update(kimi);
    }

    // Two short lines beside the circle, e.g. "5h: 20%" and "7d: 10%".
    private static string Note(Usage.UsageSnapshot snapshot, DateTimeOffset now)
    {
        string Line(string name) => snapshot.FindWindow(name, now) is { } window ? $"{name}: {window.UsedPercent:0}%" : $"{name}: –";
        return Line("5h") + "\n" + Line("7d");
    }

    public void ApplyLanguage() => Loc.Apply(Root);

    public void ApplyPreferences()
    {
        CodexItem.Visibility = _preferences.ShowCodex ? Visibility.Visible : Visibility.Collapsed;
        ClaudeItem.Visibility = _preferences.ShowClaude ? Visibility.Visible : Visibility.Collapsed;
        DeepSeekBox.Visibility = _preferences.ShowDeepSeek ? Visibility.Visible : Visibility.Collapsed;
        KimiBox.Visibility = _preferences.ShowKimi ? Visibility.Visible : Visibility.Collapsed;
        Strip.Children.Clear();
        var items = new Dictionary<string, UIElement> { ["Codex"] = CodexItem, ["Claude"] = ClaudeItem, ["DeepSeek"] = DeepSeekBox, ["Kimi"] = KimiBox };
        foreach (string key in _preferences.NormalizedOrder()) Strip.Children.Add(items[key]);
        ApplyTheme();
        CodexReadout.Labels = ClaudeReadout.Labels = Controls.LabelMode.Never;
        TaskbarLayout.RefreshInBackground();
        Reposition();
    }

    // "Follow Windows" uses the taskbar's own (system) theme, which can differ from the apps theme.
    private void ApplyTheme()
    {
        var theme = _preferences.ThemeValue;
        if (theme == ElementTheme.Default)
        {
            _lightTaskbar = NativeMethods.SystemUsesLightTheme();
            theme = _lightTaskbar ? ElementTheme.Light : ElementTheme.Dark;
        }
        if (Root.RequestedTheme != theme) Root.RequestedTheme = theme;
        foreach (var badge in new[] { CodexReadout, ClaudeReadout }) badge.SetTheme(theme);
        foreach (var badge in new[] { DeepSeekReadout, KimiReadout }) badge.SetTheme(theme);
    }

    private void Reposition()
    {
        if (!NativeMethods.GetTaskbarRects(out var taskbar, out var notify)) return;
        int taskbarHeight = taskbar.Bottom - taskbar.Top;
        if (taskbarHeight <= 0 || taskbar.Right - taskbar.Left < taskbarHeight) { AppWindow.Hide(); return; } // vertical taskbars are not supported
        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(_hwnd) / 96d);
        double side = Math.Max(20, taskbarHeight / scale - 8);
        int count = Strip.Children.Count(c => c.Visibility == Visibility.Visible);
        if (count == 0) { AppWindow.Hide(); return; }
        foreach (var box in new[] { CodexBox, ClaudeBox, DeepSeekBox, KimiBox }) { box.Width = side; box.Height = side; }
        // Balance badges draw their amount inside the circle; rescale so it renders at the chosen DIP size.
        DeepSeekReadout.TextSize = KimiReadout.TextSize = _preferences.TaskbarTextSize * 96 / side;
        foreach (var text in new[] { CodexText, ClaudeText })
        {
            text.FontSize = _preferences.TaskbarTextSize;
            text.LineHeight = _preferences.TaskbarTextSize * 1.15;
        }
        double width = 0;
        foreach (var item in Strip.Children.Where(c => c.Visibility == Visibility.Visible))
        {
            item.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            width += item.DesiredSize.Width;
        }
        width += Strip.Spacing * (count - 1);
        int pxWidth = (int)Math.Ceiling(width * scale), pxHeight = (int)Math.Ceiling(side * scale);
        int margin = (int)Math.Ceiling(8 * scale);
        int x = notify.Left - pxWidth - margin; // right: next to the notification area
        if (_preferences.TaskbarAlign == "Left")
        {
            // Left: after the widgets button, before the (possibly centered) taskbar icons; otherwise after the icons.
            var layout = TaskbarLayout.Current;
            int left = taskbar.Left + margin;
            if (layout.Widgets is { } widgets) left = Math.Max(left, widgets.Right + margin);
            if (layout.Icons is { } icons && left + pxWidth + margin > icons.Left) left = icons.Right + margin;
            if (left + pxWidth + margin <= notify.Left) x = left;
        }
        int y = taskbar.Top + (taskbarHeight - pxHeight) / 2;
        NativeMethods.PlaceTopmost(_hwnd, x, y, pxWidth, pxHeight);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshUsageRequested?.Invoke();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
