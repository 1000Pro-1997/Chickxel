using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>Shared stop editor for the gradient tool and spatial effect colors.</summary>
public sealed class GradientEditor : StackPanel
{
    private readonly GradientBar _bar = new() { Margin = new Thickness(0, 2, 0, 2) };
    private readonly NumericUpDown _position;
    private readonly NumericUpDown _alpha;
    private readonly ColorField _color;
    private readonly StackPanel _colorRow;
    private readonly StackPanel _alphaRow;
    private readonly Button _delete;
    private readonly ComboBox _interpolation;
    private readonly ComboBox _dither;
    private readonly NumericUpDown? _angleEditor;
    private bool _updating;
    private IReadOnlyList<GradientStop> _stops = EditorContext.DefaultGradientStops;
    private IReadOnlyList<GradientAlphaStop> _alphaStops = EditorContext.DefaultGradientAlphaStops;
    private GradientInterpolation _mode;
    private GradientDither _ditherMode;
    private int _angle;

    public GradientEditor(Func<Color32>? primaryColor = null, bool spatialAngle = false)
    {
        Spacing = 4;
        _bar.Tip(UiStrings.S_GradientEditor_GradientEditor_5D23258D24);
        _bar.StopsChanged += value => { _stops = value; Changed?.Invoke(); RefreshSelected(); };
        _bar.AlphaStopsChanged += value => { _alphaStops = value; Changed?.Invoke(); RefreshSelected(); };
        _bar.SelectionChanged += (_, _) => RefreshSelected();

        _position = Ui.Num(0, 100, 0, 56, 1, 1m).Presets(0, 10, 25, 33.3m, 50, 66.7m, 75, 90, 100).Unit("%").RecentKey("gradient.position");
        _position.FormatString = "0.#";
        _position.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            double value = (double)_position.NumValue() / 100;
            if (_bar.SelectedIsAlpha) ChangeAlpha(s => s with { Position = value });
            else ChangeColor(s => s with { Position = value });
        };
        _color = new ColorField(Color32.Black);
        _color.ColorChanged += (_, _) => { if (!_updating) ChangeColor(s => s with { Color = _color.Color }); };
        _alpha = Ui.Num(0, 100, 100, 56, 1, 1m).Presets(Ui.Percents).Unit("%").RecentKey("opacity");
        _alpha.FormatString = "0.#";
        _alpha.ValueChanged += (_, _) =>
        {
            if (!_updating) ChangeAlpha(s => s with { Alpha = (byte)Math.Round((double)_alpha.NumValue() * 255 / 100) });
        };
        _colorRow = Ui.Row(Ui.Label(UiStrings.S_GradientEditor_GradientEditor_ABB8BC9619), _color);
        if (primaryColor is not null)
            _colorRow.Children.Add(Ui.Button(UiStrings.S_GradientEditor_GradientEditor_9E5B25FE7A, (_, _) => ChangeColor(s => s with { Color = primaryColor() })));
        _alphaRow = Ui.Row(Ui.Label(UiStrings.S_GradientEditor_GradientEditor_D945F5766C), _alpha, Ui.Label("%"));
        _delete = Ui.Button(UiStrings.S_GradientEditor_GradientEditor_01174C479B, (_, _) => _bar.DeleteSelected()).Tip(UiStrings.S_GradientEditor_GradientEditor_303D5EC437);
        _interpolation = Ui.Combo(150, UiStrings.S_GradientEditor_GradientEditor_E0FC6F50AE, UiStrings.S_GradientEditor_GradientEditor_07C7B1752B);
        _dither = Ui.Combo(150, UiStrings.S_GradientEditor_GradientEditor_C103727A20, "2×2 Bayer", "4×4 Bayer");
        _interpolation.SelectionChanged += (_, _) =>
        {
            if (_updating || _interpolation.SelectedIndex < 0) return;
            _mode = (GradientInterpolation)_interpolation.SelectedIndex;
            _bar.Mode = _mode;
            _dither.IsEnabled = _mode == GradientInterpolation.Smooth;
            Changed?.Invoke();
        };
        _dither.SelectionChanged += (_, _) =>
        {
            if (_updating || _dither.SelectedIndex < 0) return;
            _ditherMode = (GradientDither)_dither.SelectedIndex;
            Changed?.Invoke();
        };
        Children.Add(_bar);
        Children.Add(Ui.Row(Ui.Label(UiStrings.S_GradientEditor_GradientEditor_3C9416C632), _position, Ui.Label("%"), _delete));
        Children.Add(_colorRow);
        Children.Add(_alphaRow);
        Children.Add(Ui.Row(Ui.Label(UiStrings.S_GradientEditor_GradientEditor_554C6D3CFC), _interpolation));
        Children.Add(Ui.Row(Ui.Label(UiStrings.S_GradientEditor_GradientEditor_75C017194C), _dither));
        if (spatialAngle)
        {
            _angleEditor = Ui.Num(0, 359, 0, 56, 1, 1m).Unit("°").RecentKey("effect.gradient.angle");
            _angleEditor.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _angle = (int)_angleEditor.NumValue();
                Changed?.Invoke();
            };
            Children.Add(Ui.Row(Ui.Label(UiStrings.S_GradientEditor_GradientEditor_D5C20D5832), _angleEditor, Ui.Label(UiStrings.S_GradientEditor_GradientEditor_DEFF285651)));
        }
        Load(_stops, _alphaStops, _mode, _ditherMode);
    }

    public event Action? Changed;
    public IReadOnlyList<GradientStop> Stops => _stops;
    public IReadOnlyList<GradientAlphaStop> AlphaStops => _alphaStops;
    public GradientInterpolation Interpolation => _mode;
    public GradientDither Dither => _ditherMode;
    public int Angle => _angle;

    /// <summary>Synchronizes externally owned state without firing Changed.</summary>
    public void Load(IReadOnlyList<GradientStop> stops, IReadOnlyList<GradientAlphaStop> alphaStops,
        GradientInterpolation interpolation, GradientDither dither, int angle = 0)
    {
        _updating = true;
        try
        {
            _stops = stops;
            _alphaStops = alphaStops.Count < 2 ? EditorContext.DefaultGradientAlphaStops : alphaStops;
            _mode = interpolation;
            _ditherMode = dither;
            _angle = angle;
            if (_angleEditor is not null) _angleEditor.Value = angle;
            _bar.Stops = stops;
            _bar.AlphaStops = _alphaStops;
            _bar.Mode = interpolation;
            _interpolation.SelectedIndex = (int)interpolation;
            _dither.SelectedIndex = (int)dither;
            _dither.IsEnabled = interpolation == GradientInterpolation.Smooth;
            RefreshSelected();
        }
        finally { _updating = false; }
    }

    private void ChangeColor(Func<GradientStop, GradientStop> change)
    {
        if (_bar.SelectedIsAlpha || _stops.Count == 0) return;
        var list = _stops.ToArray();
        int index = Math.Clamp(_bar.SelectedIndex, 0, list.Length - 1);
        list[index] = change(list[index]);
        _stops = list;
        _bar.Stops = list;
        Changed?.Invoke();
        RefreshSelected();
    }

    private void ChangeAlpha(Func<GradientAlphaStop, GradientAlphaStop> change)
    {
        if (!_bar.SelectedIsAlpha || _alphaStops.Count == 0) return;
        var list = _alphaStops.ToArray();
        int index = Math.Clamp(_bar.SelectedIndex, 0, list.Length - 1);
        list[index] = change(list[index]);
        _alphaStops = list;
        _bar.AlphaStops = list;
        Changed?.Invoke();
        RefreshSelected();
    }

    private void RefreshSelected()
    {
        bool previous = _updating;
        _updating = true;
        try
        {
            bool alpha = _bar.SelectedIsAlpha;
            _colorRow.IsVisible = !alpha;
            _alphaRow.IsVisible = alpha;
            _delete.IsEnabled = alpha ? _alphaStops.Count > 2 : _stops.Count > 2;
            if (alpha && _alphaStops.Count > 0)
            {
                var stop = _alphaStops[Math.Clamp(_bar.SelectedIndex, 0, _alphaStops.Count - 1)];
                _position.Value = (decimal)Math.Round(stop.Position * 100, 1);
                _alpha.Value = (decimal)Math.Round(stop.Alpha * 100 / 255.0, 1);
            }
            else if (!alpha && _stops.Count > 0)
            {
                var stop = _stops[Math.Clamp(_bar.SelectedIndex, 0, _stops.Count - 1)];
                _position.Value = (decimal)Math.Round(stop.Position * 100, 1);
                _color.Color = stop.Color;
            }
        }
        finally { _updating = previous; }
    }
}
