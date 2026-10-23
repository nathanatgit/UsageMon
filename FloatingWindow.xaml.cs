using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace UsageMon;

public sealed partial class FloatingWindow : Window
{
    private readonly Preferences _preferences;
    private bool _placing, _dragging, _resizeQueued;
    private double _scale;
    private XamlRoot? _xamlRoot;
    private PointInt32 _dragWindowOrigin;
    private NativeMethods.Point _dragPointerOrigin;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private string? _dockedEdge;
    private bool _menuOpen;
    private RectInt32 _expandedBounds;
    private readonly PanelBackdrop _backdrop = new();
    private readonly PanelBackdrop _edgeBackdrop = new();
    private readonly PanelBackdrop _featherBackdrop = new();
    private string? _imagePath;
    private readonly CompactWindowSizing _compactSizing;
    public PanelKind Kind { get; }
    public bool IsAutoHidden { get; private set; }
    public double Scale => _scale;
    public RectInt32 ScreenBounds => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
    public event Action? SettingsRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action? PreferencesChanged;
    public event Action? RefreshUsageRequested;
    public event Action<FloatingWindow>? Moving;
    public event Action<FloatingWindow>? DragCompleted;

    public FloatingWindow(Preferences preferences, PanelKind kind = PanelKind.Joined)
    {
        InitializeComponent();
        Loc.Apply(Surface);
        _preferences = preferences;
        Kind = kind;
        Title = kind == PanelKind.Joined ? "UsageMon" : $"UsageMon · {kind}";
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_menuOpen && !_dragging && !PointerInside()) CollapseToEdge();
        };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "UsageMon.ico"));
        _backdrop.Configure(preferences);
        _edgeBackdrop.Configure(preferences);
        _featherBackdrop.Configure(preferences);
        MaterialEdge.SystemBackdrop = _edgeBackdrop;
        MaterialFeather.SystemBackdrop = _featherBackdrop;
        MaterialSurface.SystemBackdrop = _backdrop;
        Surface.ActualThemeChanged += (_, _) => UpdateMaterialRim();
        UpdateMaterialRim();
        SystemBackdrop = new TransparentPanelBackdrop();
        BackgroundImage.ImageFailed += (_, _) => BackgroundImage.Visibility = Visibility.Collapsed;
        BackgroundImage.SizeChanged += (_, _) => ClipBackgroundImage();
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        _compactSizing = new CompactWindowSizing(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange) UpdateShape();
            if (args.DidPositionChange && !_placing && !_dragging && !IsAutoHidden) SavePosition();
        };
        Surface.Loaded += (_, _) =>
        {
            if (_xamlRoot is not null) return;
            _xamlRoot = Surface.XamlRoot;
            _xamlRoot.Changed += XamlRoot_Changed;
            ResizeForDpi();
            ClampToScreen();
        };
        Surface.Unloaded += (_, _) =>
        {
            if (_xamlRoot is not null) _xamlRoot.Changed -= XamlRoot_Changed;
            _xamlRoot = null;
            _hideTimer.Stop();
        };
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (Math.Abs(sender.RasterizationScale - _scale) < .001 || _resizeQueued) return;
        _resizeQueued = true;
        // Run after WinUI has finished processing the monitor's DPI transition.
        DispatcherQueue.TryEnqueue(() =>
        {
            _resizeQueued = false;
            if (_xamlRoot is null) return;
            Reveal();
            ResizeForDpi();
            if (!_dragging) { ClampToScreen(); UpdateDocking(); }
        });
    }

    private void ResizeForDpi()
    {
        _scale = Surface.XamlRoot?.RasterizationScale
            ?? NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        // XAML uses DIPs; AppWindow client sizing takes physical pixels.
        int count = Math.Max(1, Panel.Children.Count(c => c.Visibility == Visibility.Visible));
        double diameter = double.IsFinite(_preferences.PanelSize) ? Math.Clamp(_preferences.PanelSize, 16, 192) : 104;
        double width = !_preferences.Vertical ? diameter * count : diameter;
        double height = _preferences.Vertical ? diameter * count : diameter;
        PanelScale.Width = width;
        PanelScale.Height = height;
        SwapLayer.Width = width;
        SwapLayer.Height = height;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(width * _scale), (int)Math.Ceiling(height * _scale)));
        UpdateShape();
    }

    private void UpdateShape()
    {
        var size = AppWindow.Size;
        NativeMethods.ShapePanel(WinRT.Interop.WindowNative.GetWindowHandle(this), size.Width, size.Height);
        Surface.CornerRadius = new CornerRadius(Math.Min(size.Width, size.Height) / (2 * Math.Max(_scale, 1)));
        MaterialSurface.CornerRadius = Surface.CornerRadius;
        MaterialEdge.CornerRadius = Surface.CornerRadius;
        MaterialFeather.CornerRadius = Surface.CornerRadius;
        MaterialRim.CornerRadius = Surface.CornerRadius;
        double edgeScale = Math.Min(1, Math.Min(size.Width, size.Height) / (104 * Math.Max(_scale, 1)));
        MaterialFeather.Margin = new Thickness(edgeScale);
        MaterialEdge.Margin = new Thickness(2 * edgeScale);
        MaterialSurface.Margin = new Thickness(3 * edgeScale);
        MaterialRim.Margin = new Thickness(3 * edgeScale);
        MaterialRim.BorderThickness = new Thickness(.75 * edgeScale);
        BackgroundImage.Margin = new Thickness(3.5 * edgeScale);
        ClipBackgroundImage();
    }

    private void UpdateMaterialRim()
    {
        bool dark = Surface.ActualTheme == ElementTheme.Dark;
        var rim = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        rim.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
        { Offset = 0, Color = Windows.UI.Color.FromArgb(dark ? (byte)70 : (byte)115, 255, 255, 255) });
        rim.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
        { Offset = .45, Color = Windows.UI.Color.FromArgb(20, 255, 255, 255) });
        rim.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
        { Offset = 1, Color = Windows.UI.Color.FromArgb(dark ? (byte)35 : (byte)20, 0, 0, 0) });
        MaterialRim.BorderBrush = rim;
    }

    private void ClipBackgroundImage()
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(BackgroundImage);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new System.Numerics.Vector2((float)BackgroundImage.ActualWidth, (float)BackgroundImage.ActualHeight);
        float radius = Math.Min(geometry.Size.X, geometry.Size.Y) / 2;
        geometry.CornerRadius = new System.Numerics.Vector2(radius);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }

    public void ApplyPreferences()
    {
        _placing = true;
        try
        {
            ApplyAppearance();
            CodexReadout.LabelSize = ClaudeReadout.LabelSize = double.IsFinite(_preferences.LabelSize) ? _preferences.LabelSize : 16;
            var labels = _preferences.LabelMode switch { "Always" => Controls.LabelMode.Always, "Never" => Controls.LabelMode.Never, _ => Controls.LabelMode.Hover };
            CodexReadout.Labels = ClaudeReadout.Labels = labels;
            Reveal();
            bool joined = Kind == PanelKind.Joined;
            CodexReadout.Visibility = Kind != PanelKind.Claude && _preferences.ShowCodex && !(joined && _preferences.SeparatePanels) ? Visibility.Visible : Visibility.Collapsed;
            ClaudeReadout.Visibility = Kind != PanelKind.Codex && _preferences.ShowClaude && !(joined && _preferences.SeparatePanels) ? Visibility.Visible : Visibility.Collapsed;
            DeepSeekReadout.Visibility = joined && _preferences.ShowDeepSeek ? Visibility.Visible : Visibility.Collapsed;
            KimiReadout.Visibility = joined && _preferences.ShowKimi ? Visibility.Visible : Visibility.Collapsed;
            Panel.Orientation = _preferences.Vertical ? Microsoft.UI.Xaml.Controls.Orientation.Vertical : Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
            Panel.Children.Clear();
            var badges = new Dictionary<string, FrameworkElement>
            { ["Codex"] = CodexReadout, ["Claude"] = ClaudeReadout, ["DeepSeek"] = DeepSeekReadout, ["Kimi"] = KimiReadout };
            _visibleKeys.Clear();
            foreach (string key in _preferences.NormalizedOrder())
            {
                Panel.Children.Add(badges[key]);
                if (badges[key].Visibility == Visibility.Visible) _visibleKeys.Add(key);
            }
            BuildSwapButtons();
            ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = _preferences.AlwaysOnTop;
            ResizeForDpi();
            var (savedX, savedY) = SavedPosition;
            var area = (savedX.HasValue && savedY.HasValue
                ? DisplayArea.GetFromPoint(new PointInt32(savedX.Value, savedY.Value), DisplayAreaFallback.Nearest)
                : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)).WorkArea;
            int margin = (int)Math.Ceiling(16 * _scale);
            int defaultX = area.X + area.Width - AppWindow.Size.Width - margin;
            if (Kind == PanelKind.Codex) defaultX -= AppWindow.Size.Width + margin;
            AppWindow.Move(new PointInt32(savedX ?? defaultX,
                savedY ?? area.Y + area.Height - AppWindow.Size.Height - margin));
            ClampToScreen();
            UpdateDocking();
            ShowSwap(null);
        }
        finally { _placing = false; }
    }

    private void ClampToScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var position = AppWindow.Position;
        int x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.X + area.Width - AppWindow.Size.Width));
        int y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - AppWindow.Size.Height));
        if (x != position.X || y != position.Y) AppWindow.Move(new PointInt32(x, y));
    }

    private void SavePosition()
    {
        if (IsAutoHidden) return;
        var p = AppWindow.Position;
        switch (Kind)
        {
            case PanelKind.Joined: _preferences.X = p.X; _preferences.Y = p.Y; break;
            case PanelKind.Codex: _preferences.CodexX = p.X; _preferences.CodexY = p.Y; break;
            case PanelKind.Claude: _preferences.ClaudeX = p.X; _preferences.ClaudeY = p.Y; break;
        }
        _preferences.Save();
    }

    private (int? X, int? Y) SavedPosition => Kind switch
    {
        PanelKind.Codex => (_preferences.CodexX, _preferences.CodexY),
        PanelKind.Claude => (_preferences.ClaudeX, _preferences.ClaudeY),
        _ => (_preferences.X, _preferences.Y)
    };

    public void MoveTo(PointInt32 point)
    {
        bool wasPlacing = _placing;
        _placing = true;
        try { AppWindow.Move(point); }
        finally { _placing = wasPlacing; }
    }

    public void ApplyLanguage()
    {
        Loc.Apply(Surface);
        BuildSwapButtons();
    }

    public void UpdateDocking()
    {
        _hideTimer.Stop();
        if (!_preferences.Sticky || !_preferences.AutoHideEdges) { _dockedEdge = null; Reveal(); return; }
        if (IsAutoHidden) return;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var b = ScreenBounds;
        _dockedEdge = Math.Abs(b.X - work.X) <= 2 ? "left"
            : Math.Abs(b.X + b.Width - work.X - work.Width) <= 2 ? "right"
            : Math.Abs(b.Y - work.Y) <= 2 ? "top"
            : Math.Abs(b.Y + b.Height - work.Y - work.Height) <= 2 ? "bottom" : null;
        if (_dockedEdge is not null && AppWindow.IsVisible && !_dragging) _hideTimer.Start();
    }

    private bool PointerInside()
    {
        if (!NativeMethods.GetCursorPos(out var pointer)) return true;
        var b = ScreenBounds;
        return pointer.X >= b.X && pointer.X < b.X + b.Width && pointer.Y >= b.Y && pointer.Y < b.Y + b.Height;
    }

    private void CollapseToEdge()
    {
        if (IsAutoHidden || _dockedEdge is null || !AppWindow.IsVisible || !_preferences.Sticky || !_preferences.AutoHideEdges) return;
        _expandedBounds = ScreenBounds;
        IsAutoHidden = true;
        Panel.Visibility = Visibility.Collapsed;
        PanelScale.Visibility = Visibility.Collapsed;
        EdgeTab.Visibility = Visibility.Visible;
        Panel.Margin = new Thickness(0);
        BackgroundImage.Visibility = Visibility.Collapsed;
        bool side = _dockedEdge is "left" or "right";
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling((side ? 8 : 72) * _scale), (int)Math.Ceiling((side ? 72 : 8) * _scale)));
        UpdateShape();
        UpdateEdgeBar();
        int x = side ? _expandedBounds.X : _expandedBounds.X + (_expandedBounds.Width - AppWindow.Size.Width) / 2;
        int y = side ? _expandedBounds.Y + (_expandedBounds.Height - AppWindow.Size.Height) / 2 : _expandedBounds.Y;
        if (_dockedEdge == "right") x = _expandedBounds.X + _expandedBounds.Width - AppWindow.Size.Width;
        if (_dockedEdge == "bottom") y = _expandedBounds.Y + _expandedBounds.Height - AppWindow.Size.Height;
        MoveTo(new PointInt32(x, y));
    }

    // The collapsed handle doubles as a bar for the first visible quota's five-hour usage.
    private Usage.UsageSnapshot? _codexSnapshot, _claudeSnapshot;
    private void UpdateEdgeBar()
    {
        if (!IsAutoHidden || _dockedEdge is null) return;
        bool side = _dockedEdge is "left" or "right";
        // The collapsed handle is 8 x 72 DIP (or 72 x 8) with a 2 DIP inset.
        double length = 68, thickness = 4;
        var now = DateTimeOffset.UtcNow;
        string? first = _visibleKeys.FirstOrDefault(k => k is "Codex" or "Claude");
        var snapshot = first == "Claude" ? _claudeSnapshot : first == "Codex" ? _codexSnapshot : null;
        double percent = snapshot?.FindWindow("5h", now)?.UsedPercent ?? 0;
        double fill = percent <= 0 ? 0 : Math.Clamp(length * percent / 100, thickness, length);
        EdgeTrack.CornerRadius = EdgeFill.CornerRadius = new CornerRadius(thickness / 2);
        EdgeFill.Width = side ? thickness : fill;
        EdgeFill.Height = side ? fill : thickness;
        // Side handles fill from the bottom up; top/bottom handles fill left to right.
        EdgeFill.HorizontalAlignment = HorizontalAlignment.Left;
        EdgeFill.VerticalAlignment = side ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        EdgeFill.Visibility = fill > 0 ? Visibility.Visible : Visibility.Collapsed;
        EdgeFill.Opacity = snapshot?.State == Usage.UsageState.Live ? 1 : .55;
    }

    private void Reveal()
    {
        _hideTimer.Stop();
        if (!IsAutoHidden) return;
        IsAutoHidden = false;
        Panel.Visibility = Visibility.Visible;
        PanelScale.Visibility = Visibility.Visible;
        EdgeTab.Visibility = Visibility.Collapsed;
        Panel.Margin = new Thickness(4);
        ApplyAppearance();
        bool wasPlacing = _placing;
        _placing = true;
        try { ResizeForDpi(); AppWindow.Move(new PointInt32(_expandedBounds.X, _expandedBounds.Y)); }
        finally { _placing = wasPlacing; }
    }

    private readonly List<string> _visibleKeys = [];
    // One swap button between each pair of neighbouring badges, shown on hover.
    private void BuildSwapButtons()
    {
        SwapLayer.Children.Clear();
        SwapLayer.ColumnDefinitions.Clear();
        SwapLayer.RowDefinitions.Clear();
        int count = _visibleKeys.Count;
        if (Kind != PanelKind.Joined || count < 2) return;
        bool vertical = _preferences.Vertical;
        for (int i = 0; i < count; i++)
        {
            if (vertical) SwapLayer.RowDefinitions.Add(new RowDefinition());
            else SwapLayer.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (int i = 0; i < count - 1; i++)
        {
            var button = new Microsoft.UI.Xaml.Controls.Button
            {
                Width = 20, Height = 20, Padding = new Thickness(0), CornerRadius = new CornerRadius(10),
                Opacity = 0, IsHitTestVisible = false, Tag = i,
                Content = new Microsoft.UI.Xaml.Controls.FontIcon
                {
                    Glyph = "\uE8AB", FontSize = 10, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5),
                    RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = vertical ? 90 : 0 }
                },
                OpacityTransition = new ScalarTransition()
            };
            ToolTipService.SetToolTip(button, Loc.T("Swap order"));
            button.Click += Swap_Click;
            if (vertical)
            {
                Grid.SetRow(button, i);
                button.VerticalAlignment = VerticalAlignment.Bottom;
                button.HorizontalAlignment = HorizontalAlignment.Center;
                button.Margin = new Thickness(0, 0, 0, -10);
            }
            else
            {
                Grid.SetColumn(button, i);
                button.HorizontalAlignment = HorizontalAlignment.Right;
                button.VerticalAlignment = VerticalAlignment.Center;
                button.Margin = new Thickness(0, 0, -10, 0);
            }
            SwapLayer.Children.Add(button);
        }
    }
    // A swap button appears only while the pointer is near the gap it sits in.
    private void ShowSwap(Windows.Foundation.Point? pointer)
    {
        int count = _visibleKeys.Count;
        double length = _preferences.Vertical ? SwapLayer.ActualHeight : SwapLayer.ActualWidth;
        double cell = count > 0 ? length / count : 0;
        foreach (var child in SwapLayer.Children)
        {
            bool near = false;
            if (pointer is { } at && child is FrameworkElement { Tag: int index } && cell > 0)
            {
                double along = _preferences.Vertical ? at.Y : at.X;
                near = Math.Abs(along - (index + 1) * cell) <= Math.Max(16, cell * .22);
            }
            child.Opacity = near ? 1 : 0;
            child.IsHitTestVisible = near;
        }
    }
    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index }) _preferences.SwapVisible(index, _visibleKeys);
        ChangePreferences();
    }
    private void Surface_PointerEntered(object sender, PointerRoutedEventArgs e) { Reveal(); ShowSwap(e.GetCurrentPoint(SwapLayer).Position); }
    public void ApplyAppearance()
    {
        Surface.RequestedTheme = _preferences.ThemeValue;
        CodexReadout.SetTheme(_preferences.ThemeValue); ClaudeReadout.SetTheme(_preferences.ThemeValue);
        DeepSeekReadout.SetTheme(_preferences.ThemeValue); KimiReadout.SetTheme(_preferences.ThemeValue);
        _backdrop.Configure(_preferences);
        _edgeBackdrop.Configure(_preferences);
        _featherBackdrop.Configure(_preferences);
        UpdateMaterialRim();
        // TintOpacity only changes the material's color mix. Fade the actual
        // surfaces to expose the desktop while leaving the readouts opaque.
        double opacity = 1 - Math.Clamp(_preferences.Transparency, 0, 100) / 100;
        _backdrop.SetOpacity(opacity);
        _edgeBackdrop.SetOpacity(.42 * opacity);
        _featherBackdrop.SetOpacity(.16 * opacity);
        MaterialRim.Opacity = opacity;
        string? imagePath = _preferences.BackgroundImagePath;
        if (_imagePath != imagePath)
        {
            _imagePath = imagePath;
            BackgroundImage.Source = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
                ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imagePath)) : null;
        }
        BackgroundImage.Opacity = opacity;
        BackgroundImage.Visibility = BackgroundImage.Source is not null && !IsAutoHidden ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Surface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ShowSwap(null);
        if (_dockedEdge is not null && !_dragging && !_menuOpen && !IsAutoHidden) _hideTimer.Start();
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        Reveal();
        if (!NativeMethods.GetCursorPos(out _dragPointerOrigin)) return;
        Surface.Focus(FocusState.Pointer);
        _dragWindowOrigin = AppWindow.Position;
        _dragging = Surface.CapturePointer(e.Pointer);
        e.Handled = true;
    }
    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) { ShowSwap(e.GetCurrentPoint(SwapLayer).Position); return; }
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        ShowSwap(null);
        AppWindow.Move(new PointInt32(_dragWindowOrigin.X + cursor.X - _dragPointerOrigin.X,
            _dragWindowOrigin.Y + cursor.Y - _dragPointerOrigin.Y));
        Moving?.Invoke(this);
        e.Handled = true;
    }
    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return; // Let right-click reach the context flyout.
        FinishDrag();
        Surface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
    private void Surface_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => FinishDrag();
    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        ClampToScreen();
        DragCompleted?.Invoke(this);
        SavePosition();
    }

    private void PanelMenu_Opening(object sender, object e)
    {
        _menuOpen = true;
        _hideTimer.Stop();
        OnTopIcon.Glyph = _preferences.AlwaysOnTop ? "" : ""; // check, or an empty icon slot so the text stays aligned
    }
    private void ChangePreferences()
    {
        _preferences.Save();
        // A mode/visibility change can hide the window owning this flyout.
        DispatcherQueue.TryEnqueue(() => PreferencesChanged?.Invoke());
    }
    private void PanelMenu_Closed(object sender, object e) { _menuOpen = false; UpdateDocking(); }
    private void OnTop_Click(object sender, RoutedEventArgs e) { _preferences.AlwaysOnTop = !_preferences.AlwaysOnTop; ChangePreferences(); }
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void RefreshUsage_Click(object sender, RoutedEventArgs e) => RefreshUsageRequested?.Invoke();
    public void UpdateBalances(Usage.BalanceSnapshot deepSeek, Usage.BalanceSnapshot kimi)
    {
        DeepSeekReadout.Update(deepSeek);
        KimiReadout.Update(kimi);
    }
    public void UpdateUsage(Usage.UsageSnapshot codex, Usage.UsageSnapshot claude)
    {
        var now = DateTimeOffset.UtcNow;
        CodexReadout.Update(codex, now);
        ClaudeReadout.Update(claude, now);
        _codexSnapshot = codex;
        _claudeSnapshot = claude;
        UpdateEdgeBar();
    }
    private void Hide_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
