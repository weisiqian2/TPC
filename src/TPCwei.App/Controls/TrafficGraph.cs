using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TPC.App.Models;

namespace TPC.App.Controls;

public sealed class TrafficGraph : Control
{
    public static readonly StyledProperty<IEnumerable<TrafficSample>?> SamplesProperty =
        AvaloniaProperty.Register<TrafficGraph, IEnumerable<TrafficSample>?>(nameof(Samples));

    public IEnumerable<TrafficSample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = Bounds;
        var background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        context.FillRectangle(background, rect);

        var samples = Samples?.ToList() ?? [];
        if (samples.Count < 2)
        {
            DrawEmpty(context, rect);
            return;
        }

        var max = Math.Max(1, samples.Max(x => Math.Max(x.UpBytesPerSecond, x.DownBytesPerSecond)));
        DrawLine(context, rect, samples.Select(x => x.DownBytesPerSecond).ToList(), max, Colors.Red);
        DrawLine(context, rect, samples.Select(x => x.UpBytesPerSecond).ToList(), max, Colors.LimeGreen);
    }

    private static void DrawEmpty(DrawingContext context, Rect rect)
    {
        var text = new FormattedText(
            "等待流量数据",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            13,
            new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
        context.DrawText(text, new Point(16, Math.Max(12, rect.Height / 2 - 10)));
    }

    private static void DrawLine(DrawingContext context, Rect rect, IReadOnlyList<double> values, double max, Color color)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var x = rect.X + (rect.Width * i / Math.Max(1, values.Count - 1));
                var y = rect.Bottom - (rect.Height * values[i] / max);
                if (i == 0)
                {
                    stream.BeginFigure(new Point(x, y), false);
                }
                else
                {
                    stream.LineTo(new Point(x, y));
                }
            }
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2), geometry);
    }
}
