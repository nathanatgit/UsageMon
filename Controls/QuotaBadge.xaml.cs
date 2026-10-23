using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using UsageMon.Usage;
using Windows.Foundation;

namespace UsageMon.Controls;

public enum LabelMode { Hover, Always, Never }

public sealed partial class QuotaBadge : UserControl
{
    private const double Center = 48;
    private string? _provider;
    private bool _hovered;
    private readonly ToolTip _tip;
    public LabelMode Labels { get => _labels; set { if (_labels == value) return; _labels = value; UpdateLabels(); } }
    private LabelMode _labels = LabelMode.Hover;
    public void SetTheme(ElementTheme theme) => _tip.RequestedTheme = theme;
    private bool _live;
    private UsageWindow? _shortWindow;
    private UsageWindow? _weeklyWindow;
    private readonly StackPanel _tooltipPanel = new() { Spacing = 10, Width = 340 };
    private string _summary = "";

    public QuotaBadge()
    {
        InitializeComponent();
        PointerEntered += (_, _) => { _hovered = true; UpdateLabels(); };
        PointerExited += (_, _) => { _hovered = false; UpdateLabels(); };
        PointerCanceled += (_, _) => { _hovered = false; UpdateLabels(); };
        _tip = new ToolTip { Content = _tooltipPanel, Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Top };
        ToolTipService.SetToolTip(this, _tip);
    }

    public void Update(UsageSnapshot snapshot, DateTimeOffset now)
    {
        if (_provider != snapshot.Provider)
        {
            _provider = snapshot.Provider;
            string data = snapshot.Provider == "Claude" ? ProviderLogos.Claude : ProviderLogos.OpenAI;
            var shape = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{data}\" />");
            var geometry = shape.Data;
            shape.Data = null; // WinUI geometry can only have one owning Path.
            Logo.Data = geometry;
        }
        var shortWindow = snapshot.FindWindow("5h", now);
        var weeklyWindow = snapshot.FindWindow("7d", now);
        bool live = snapshot.State == UsageState.Live;
        _shortWindow = shortWindow;
        _weeklyWindow = weeklyWindow;
        _live = live;
        UpdateLabels();
        Logo.Opacity = shortWindow is not null || weeklyWindow is not null ? 1 : .55;
        BuildTooltip(snapshot, shortWindow, weeklyWindow, live);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, snapshot.Provider + " · " + snapshot.Summary(now));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(this, _summary);
    }

    private static TextBlock Caption(string text, bool wrap = false) => new()
    {
        Text = text, FontSize = 12, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, Opacity = .72
    };

    private void BuildTooltip(UsageSnapshot snapshot, UsageWindow? shortWindow, UsageWindow? weeklyWindow, bool live)
    {
        _tooltipPanel.Children.Clear();
        var header = new TextBlock { Text = snapshot.Provider, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _tooltipPanel.Children.Add(header);
        var lines = new List<string> { snapshot.Provider };
        _tooltipPanel.Children.Add(WindowRow(Loc.T("Current session"), Loc.T("Outer ring · 5 hours"), shortWindow, live, lines));
        _tooltipPanel.Children.Add(WindowRow(Loc.T("This week"), Loc.T("Inner ring · 7 days"), weeklyWindow, live, lines));
        var footer = new StackPanel { Spacing = 2 };
        footer.Children.Add(Caption(Loc.T(snapshot.Message), true));
        if (snapshot.UpdatedAt is { } updated) footer.Children.Add(Caption(Loc.T("Last received ") + updated.ToLocalTime().ToString("g")));
        if (snapshot.State == UsageState.Stale) footer.Children.Add(Caption(Loc.T("Cached reading · awaiting a successful refresh")));
        if (snapshot.Provider == "Claude" && !live)
            footer.Children.Add(new TextBlock
            {
                Text = Loc.T("To fix: run claude in a terminal and sign in (/login), then send a message. The desktop app doesn't feed this ring."),
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
        _tooltipPanel.Children.Add(footer);
        _summary = string.Join(Environment.NewLine, lines) + Environment.NewLine + snapshot.Message;
    }

    private static Grid WindowRow(string title, string ring, UsageWindow? window, bool live, List<string> lines)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 56 });
        var labels = new StackPanel { Spacing = 1 };
        labels.Children.Add(new TextBlock { Text = title });
        string reset = window?.ResetsAt is { } at
            ? (Loc.Chinese ? "重置于 " : "Resets ") + (at.ToLocalTime().Date == DateTime.Today ? (Loc.Chinese ? "" : "at ") + at.ToLocalTime().ToString("t") : at.ToLocalTime().ToString("ddd t"))
            : ring;
        labels.Children.Add(Caption(window is null ? Loc.T("Unavailable") : reset));
        grid.Children.Add(labels);
        var bar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Value = window is null ? 0 : Math.Clamp(window.UsedPercent, 0, 100),
            VerticalAlignment = VerticalAlignment.Center, Opacity = window is null ? .4 : live ? 1 : .55,
            CornerRadius = new CornerRadius(2)
        };
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);
        var percent = Caption(window is null ? "–" : $"{window.UsedPercent:0.#}% used");
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        percent.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(percent, 2);
        grid.Children.Add(percent);
        lines.Add($"{title}: " + (window is null ? "unavailable" : $"{window.UsedPercent:0.#}% used"));
        return grid;
    }

    private static void DrawRing(Microsoft.UI.Xaml.Shapes.Path track, Microsoft.UI.Xaml.Shapes.Path arc, UsageWindow? window, double radius, bool live, double opacity, List<Rect> gaps)
    {
        bool known = window is not null;
        track.StrokeDashArray = known ? new DoubleCollection() : new DoubleCollection { 1, 2 };
        track.Opacity = known ? .12 : .25;
        track.Data = RingGeometry(radius, 100, gaps);
        arc.Opacity = opacity * (live ? 1 : .45);
        arc.StrokeDashArray = live ? new DoubleCollection() : new DoubleCollection { 1, 1 };
        if (window is null || window.UsedPercent <= 0) { arc.Data = null; return; }
        arc.Data = RingGeometry(radius, Math.Clamp(window.UsedPercent, 0, 100), gaps);
    }

    public double LabelSize
    {
        get => OuterPercent.FontSize;
        set
        {
            value = Math.Clamp(value, 6, 60);
            if (OuterPercent.FontSize == value) return;
            OuterPercent.FontSize = value;
            InnerPercent.FontSize = Math.Round(value * .875, 1);
            UpdateLabels();
        }
    }
    private bool ShowLabels => _labels == LabelMode.Always || (_labels == LabelMode.Hover && _hovered);
    private void UpdateLabels()
    {
        OuterPercent.Visibility = InnerPercent.Visibility = Visibility.Collapsed;
        Rect? outer = ShowLabels ? PlaceLabel(OuterPercent, _shortWindow, 39) : null;
        Rect? inner = ShowLabels ? PlaceLabel(InnerPercent, _weeklyWindow, 31) : null;
        // Prefer the five-hour reading when the padded label bounds are too close.
        if (outer is { } a && inner is { } b &&
            a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top)
        {
            InnerPercent.Visibility = Visibility.Collapsed;
            inner = null;
        }
        // Omit stroke sections behind labels, preserving the transparent material.
        var gaps = new List<Rect>();
        if (outer is { } outerBounds) gaps.Add(outerBounds);
        if (inner is { } innerBounds) gaps.Add(innerBounds);
        DrawRing(OuterTrack, OuterArc, _shortWindow, 39, _live, 1, gaps);
        DrawRing(InnerTrack, InnerArc, _weeklyWindow, 31, _live, .55, gaps);
    }

    private static PathGeometry RingGeometry(double radius, double percent, List<Rect> gaps)
    {
        double end = percent / 100 * Math.PI * 2;
        var cuts = new List<double> { 0, end };
        // Split at quadrant boundaries and every rectangle/circle intersection.
        for (int i = 1; i < 4; i++) cuts.Add(i * Math.PI / 2);
        foreach (var gap in gaps)
        {
            foreach (double edge in new[] { gap.Left, gap.Right })
            {
                double value = (edge - Center) / radius;
                if (Math.Abs(value) > 1) continue;
                double angle = Math.Asin(value);
                cuts.Add((angle + Math.PI * 2) % (Math.PI * 2));
                cuts.Add(Math.PI - angle);
            }
            foreach (double edge in new[] { gap.Top, gap.Bottom })
            {
                double value = (Center - edge) / radius;
                if (Math.Abs(value) > 1) continue;
                double angle = Math.Acos(value);
                cuts.Add(angle);
                cuts.Add(Math.PI * 2 - angle);
            }
        }
        var angles = cuts.Where(a => a >= 0 && a <= end).Distinct().OrderBy(a => a).ToArray();
        Point At(double angle) => new(Center + radius * Math.Sin(angle), Center - radius * Math.Cos(angle));
        var geometry = new PathGeometry();
        for (int i = 1; i < angles.Length; i++)
        {
            double start = angles[i - 1], stop = angles[i];
            if (stop - start < .00001 || gaps.Any(gap => gap.Contains(At((start + stop) / 2)))) continue;
            var figure = new PathFigure { StartPoint = At(start), IsClosed = false, IsFilled = false };
            figure.Segments.Add(Segment(At(stop), radius, false));
            geometry.Figures.Add(figure);
        }
        return geometry;
    }

    private Rect? PlaceLabel(TextBlock label, UsageWindow? window, double radius)
    {
        if (window is null) return null;
        label.Text = $"{window.UsedPercent:0}%";
        label.Visibility = Visibility.Visible;
        label.Opacity = 1;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = label.DesiredSize;
        double angle = Math.Clamp(window.UsedPercent, 0, 100) / 100 * Math.PI * 2;
        double left = Math.Clamp(Center + radius * Math.Sin(angle) - size.Width / 2, 0, 96 - size.Width);
        double top = Math.Clamp(Center - radius * Math.Cos(angle) - size.Height / 2, 0, 96 - size.Height);
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        const double padding = 2;
        return new Rect(left - padding, top - padding, size.Width + padding * 2, size.Height + padding * 2);
    }

    private static ArcSegment Segment(Point end, double radius, bool large) => new()
    {
        Point = end, Size = new Size(radius, radius), IsLargeArc = large, SweepDirection = SweepDirection.Clockwise
    };
}
