using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace TPC.WinUI;

public sealed class TrafficChart : Grid
{
    private readonly Canvas _canvas = new();
    private readonly TextBlock _emptyText = new()
    {
        Text = "等待流量数据",
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 255, 255, 255)),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly List<(double Up, double Down)> _samples = new();
    private bool _glowEnabled = true;

    public bool GlowEnabled
    {
        get => _glowEnabled;
        set
        {
            if (_glowEnabled == value)
            {
                return;
            }

            _glowEnabled = value;
            Redraw();
        }
    }

    public TrafficChart()
    {
        MinHeight = 220;
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255));
        CornerRadius = new CornerRadius(8);
        Children.Add(_canvas);
        Children.Add(_emptyText);
        SizeChanged += (_, _) => Redraw();
    }

    public void SetSamples(IEnumerable<(double Up, double Down)> samples)
    {
        _samples.Clear();
        _samples.AddRange(samples.TakeLast(80));
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        _emptyText.Visibility = _samples.Count < 2 ? Visibility.Visible : Visibility.Collapsed;

        if (_samples.Count < 2 || ActualWidth <= 4 || ActualHeight <= 4)
        {
            return;
        }

        var max = Math.Max(1, _samples.Max(x => Math.Max(x.Up, x.Down)));
        DrawSeries(_samples.Select(x => x.Down).ToArray(), max, Windows.UI.Color.FromArgb(255, 255, 77, 90));
        DrawSeries(_samples.Select(x => x.Up).ToArray(), max, Windows.UI.Color.FromArgb(255, 55, 214, 122));
    }

    private void DrawSeries(IReadOnlyList<double> values, double max, Windows.UI.Color color)
    {
        if (_glowEnabled)
        {
            _canvas.Children.Add(CreateLine(values, max, Windows.UI.Color.FromArgb(76, color.R, color.G, color.B), 8.0));
            _canvas.Children.Add(CreateLine(values, max, Windows.UI.Color.FromArgb(100, color.R, color.G, color.B), 4.6));
        }

        _canvas.Children.Add(CreateLine(values, max, color, 2.2));
    }

    private Polyline CreateLine(IReadOnlyList<double> values, double max, Windows.UI.Color color, double thickness)
    {
        var line = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round
        };

        for (var i = 0; i < values.Count; i++)
        {
            var x = ActualWidth * i / Math.Max(1, values.Count - 1);
            var y = ActualHeight - ActualHeight * values[i] / max;
            line.Points.Add(new Windows.Foundation.Point(x, y));
        }

        return line;
    }
}
