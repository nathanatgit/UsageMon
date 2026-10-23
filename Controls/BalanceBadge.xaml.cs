using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UsageMon.Usage;

namespace UsageMon.Controls;

public sealed partial class BalanceBadge : UserControl
{
    private readonly StackPanel _tooltipPanel = new() { Spacing = 8, MinWidth = 220 };
    private readonly ToolTip _tip;
    private string? _provider;
    public void SetTheme(ElementTheme theme) => _tip.RequestedTheme = theme;
    public double TextSize
    {
        get => Amount.FontSize;
        set { value = Math.Clamp(value, 6, 60); Amount.FontSize = value; }
    }

    public BalanceBadge()
    {
        InitializeComponent();
        _tip = new ToolTip { Content = _tooltipPanel, Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Top };
        ToolTipService.SetToolTip(this, _tip);
    }

    private static TextBlock Secondary(string text, bool wrap = false) => new()
    {
        Text = text, FontSize = 12, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxWidth = 300, Opacity = .72
    };

    public void Update(BalanceSnapshot snapshot)
    {
        bool live = snapshot.State == UsageState.Live;
        bool known = live || snapshot.State == UsageState.Stale;
        Amount.Text = known ? snapshot.Amount : "–";
        if (_provider != snapshot.Provider)
        {
            _provider = snapshot.Provider;
            string data = snapshot.Provider == "Kimi" ? ProviderLogos.Kimi : ProviderLogos.DeepSeek;
            var shape = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{data}\" />");
            var geometry = shape.Data;
            shape.Data = null; // WinUI geometry can only have one owning Path.
            Logo.Data = geometry;
        }
        Logo.Opacity = known ? 1 : .55;
        Amount.Opacity = live ? 1 : .55;
        Ring.Opacity = live ? .9 : .25;
        Ring.StrokeDashArray = known ? new DoubleCollection() : new DoubleCollection { 1, 2 };

        _tooltipPanel.Children.Clear();
        _tooltipPanel.Children.Add(new TextBlock { Text = snapshot.Provider + " " + Loc.T("balance"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (known)
            foreach (var (name, value) in snapshot.Details)
            {
                var row = new Grid { ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = Loc.T(name) });
                var amount = new TextBlock { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(amount, 1);
                row.Children.Add(amount);
                _tooltipPanel.Children.Add(row);
            }
        _tooltipPanel.Children.Add(Secondary(Loc.T(snapshot.Message), true));
        if (snapshot.UpdatedAt is { } updated) _tooltipPanel.Children.Add(Secondary(Loc.T("Last received ") + updated.ToLocalTime().ToString("g")));
        if (snapshot.State == UsageState.Stale) _tooltipPanel.Children.Add(Secondary(Loc.T("Cached reading · awaiting a successful refresh")));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, snapshot.Summary);
    }
}
