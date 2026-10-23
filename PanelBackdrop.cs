using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace UsageMon;

// Keep the material active when another app receives keyboard focus. System policies
// (for example High Contrast or disabled transparency) still control material support.
public sealed class PanelBackdrop : SystemBackdrop
{
    private OpacityBackdropTarget? _target;
    private double _opacity = 1;
    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0, 1);
        _target?.SetOpacity(_opacity);
    }
    private FrameworkElement? _root;
    private MicaController? _mica;
    private DesktopAcrylicController? _acrylic;
    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private string _material = "Mica";

    public void Configure(Preferences preferences)
    {
        string material = preferences.Material == "Acrylic" ? "Acrylic" : "Mica";
        bool changed = material != _material;
        _material = material;
        if (changed && _target is not null) CreateController();
        UpdateAppearance();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        _target = new OpacityBackdropTarget(connectedTarget);
        _target.SetOpacity(_opacity);
        _root = xamlRoot.Content as FrameworkElement;
        if (_root is not null) _root.ActualThemeChanged += ThemeChanged;
        CreateController();
    }

    private void CreateController()
    {
        _mica?.Dispose(); _mica = null;
        _acrylic?.Dispose(); _acrylic = null;
        if (_target is null) return;
        if (_material == "Acrylic" && DesktopAcrylicController.IsSupported())
        {
            _acrylic = new DesktopAcrylicController();
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(_target);
        }
        else if (MicaController.IsSupported())
        {
            _mica = new MicaController { Kind = MicaKind.Base };
            _mica.SetSystemBackdropConfiguration(_configuration);
            _mica.AddSystemBackdropTarget(_target);
        }
        UpdateAppearance();
    }

    private void ThemeChanged(FrameworkElement sender, object args) => UpdateAppearance();
    private void UpdateAppearance()
    {
        bool dark = _root?.ActualTheme == ElementTheme.Dark;
        _configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _configuration.IsInputActive = true;
        var tint = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        if (_mica is not null)
        {
            _mica.TintColor = _mica.FallbackColor = tint;
            _mica.TintOpacity = .65f;
            _mica.LuminosityOpacity = .85f;
        }
        if (_acrylic is not null)
        {
            _acrylic.TintColor = _acrylic.FallbackColor = tint;
            _acrylic.TintOpacity = .35f;
            _acrylic.LuminosityOpacity = .4f;
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (_root is not null) _root.ActualThemeChanged -= ThemeChanged;
        _mica?.Dispose(); _mica = null;
        _acrylic?.Dispose(); _acrylic = null;
        _target?.Dispose();
        _root = null; _target = null;
    }
}

// SystemBackdropElement renders through its own native target, bypassing XAML
// Opacity. Fade the actual native material brush to control its output alpha.
internal sealed class OpacityBackdropTarget(ICompositionSupportsSystemBackdrop target) : ICompositionSupportsSystemBackdrop, IDisposable
{
    private Windows.UI.Composition.CompositionBrush? _source;
    private Windows.UI.Composition.CompositionEffectBrush? _effectBrush;
    private Windows.UI.Composition.CompositionEffectFactory? _factory;
    private double _opacity = 1;
    public Windows.UI.Composition.CompositionBrush SystemBackdrop
    {
        get => _source!;
        set
        {
            _source = value;
            if (value is null)
            {
                target.SystemBackdrop = null;
                ReleaseEffect();
                return;
            }
            var effect = new Microsoft.Graphics.Canvas.Effects.OpacityEffect
            {
                Name = "Fade", Opacity = (float)_opacity,
                Source = new Windows.UI.Composition.CompositionEffectSourceParameter("Material")
            };
            var factory = value.Compositor.CreateEffectFactory(effect, new[] { "Fade.Opacity" });
            var masked = factory.CreateBrush();
            masked.SetSourceParameter("Material", value);
            target.SystemBackdrop = masked;
            ReleaseEffect();
            _factory = factory;
            _effectBrush = masked;
        }
    }
    internal void SetOpacity(double opacity)
    {
        _opacity = opacity;
        _effectBrush?.Properties.InsertScalar("Fade.Opacity", (float)_opacity);
    }
    private void ReleaseEffect()
    {
        _effectBrush?.Dispose(); _effectBrush = null;
        _factory?.Dispose(); _factory = null;
    }
    public void Dispose()
    {
        target.SystemBackdrop = null;
        ReleaseEffect();
        _source = null;
    }
}

