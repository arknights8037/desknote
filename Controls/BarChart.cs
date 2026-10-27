using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DestNote.Controls;

public sealed class BarChart : FrameworkElement
{
    private IReadOnlyList<ChartBar> _bars = Array.Empty<ChartBar>();

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentDeepBrushProperty = DependencyProperty.Register(
        nameof(AccentDeepBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.DarkGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public BarChart()
    {
        SetResourceReference(AccentBrushProperty, "AccentBrush");
        SetResourceReference(AccentDeepBrushProperty, "AccentDeepBrush");
        SetResourceReference(TrackBrushProperty, "PaperLineBrush");
        SetResourceReference(TextBrushProperty, "MutedBrush");
        SnapsToDevicePixels = true;
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush AccentDeepBrush
    {
        get => (Brush)GetValue(AccentDeepBrushProperty);
        set => SetValue(AccentDeepBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public void SetBars(IReadOnlyList<ChartBar> bars)
    {
        _bars = bars;
        ToolTip = bars.Count == 0
            ? null
            : string.Join("\n", bars.Where(x => x.Amount > 0)
                .Select(x => $"{x.Label}  ¥{x.Amount:#,##0.00}"));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth < 20 || ActualHeight < 30) return;

        var fontFamily = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Microsoft YaHei UI");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var width = ActualWidth;
        var height = ActualHeight;
        const double top = 20;
        const double bottom = 22;
        var plotHeight = Math.Max(1, height - top - bottom);
        var max = Math.Max(1m, _bars.Count == 0 ? 0 : _bars.Max(x => x.Amount));
        var slot = width / Math.Max(1, _bars.Count);

        var gridPen = new Pen(TrackBrush, 1) { DashStyle = new DashStyle(new double[] { 1, 4 }, 0) };
        gridPen.Freeze();
        for (var i = 0; i < 3; i++)
        {
            var y = top + plotHeight * i / 2.0;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }

        var baseline = top + plotHeight;
        drawingContext.DrawLine(new Pen(TrackBrush, 1), new Point(0, baseline), new Point(width, baseline));

        if (_bars.Count == 0) return;

        for (var i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            var value = (double)(bar.Amount / max);
            var barHeight = bar.Amount <= 0 ? 0 : Math.Max(4, plotHeight * value);
            var barWidth = Math.Min(30, slot * 0.5);
            var x = slot * i + (slot - barWidth) / 2;
            var y = baseline - barHeight;
            var isMax = bar.Amount > 0 && bar.Amount == _bars.Max(v => v.Amount);
            var brush = isMax ? AccentDeepBrush : AccentBrush;

            if (barHeight > 0)
            {
                drawingContext.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, barHeight), 4, 4);
            }
            else
            {
                drawingContext.DrawEllipse(TrackBrush, null, new Point(slot * i + slot / 2, baseline - 1.5), 2, 2);
            }

            var label = MakeText(bar.Label, 9, TextBrush, fontFamily, dpi);
            drawingContext.DrawText(label,
                new Point(slot * i + (slot - label.Width) / 2, baseline + 6));

            if (bar.Amount > 0)
            {
                var shortValue = bar.Amount >= 1000
                    ? (bar.Amount / 1000).ToString("0.#", CultureInfo.GetCultureInfo("zh-CN")) + "k"
                    : bar.Amount.ToString("0.#", CultureInfo.GetCultureInfo("zh-CN"));
                var valueText = MakeText(shortValue, 8, isMax ? AccentDeepBrush : TextBrush, fontFamily, dpi);
                var valueY = Math.Max(0, y - valueText.Height - 2);
                drawingContext.DrawText(valueText, new Point(slot * i + (slot - valueText.Width) / 2, valueY));
            }
        }
    }

    private static FormattedText MakeText(string text, double size, Brush brush, FontFamily family, double dpi)
        => new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, brush, dpi);
}
