using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PcMonitor.Services;

namespace PcMonitor.Controls;

/// <summary>Одна линия на графике. BrushKey — ключ кисти из активной темы (например "CpuBrush").</summary>
public sealed class ChartSeries
{
    public string Name { get; init; } = "";
    public string BrushKey { get; init; } = "AccentBrush";
    public string Unit { get; init; } = "%";
    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
}

/// <summary>
/// Лёгкий линейный график: сетка, подписи осей, заливка под линией, подсказка при наведении.
/// Цвета берёт из ресурсов темы при каждой отрисовке, поэтому перекрашивается при смене темы.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    private static readonly Typeface Face = new("Segoe UI");
    private double? _hoverX;

    public static readonly DependencyProperty SeriesProperty = Register<IReadOnlyList<ChartSeries>?>(nameof(Series), null);
    public static readonly DependencyProperty MaxPointsProperty = Register(nameof(MaxPoints), 60);
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 100.0);
    public static readonly DependencyProperty ShowGridProperty = Register(nameof(ShowGrid), true);
    public static readonly DependencyProperty FillAreaProperty = Register(nameof(FillArea), true);
    public static readonly DependencyProperty XLabelsProperty = Register<IReadOnlyList<string>?>(nameof(XLabels), null);

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public int MaxPoints { get => (int)GetValue(MaxPointsProperty); set => SetValue(MaxPointsProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public bool FillArea { get => (bool)GetValue(FillAreaProperty); set => SetValue(FillAreaProperty, value); }
    public IReadOnlyList<string>? XLabels { get => (IReadOnlyList<string>?)GetValue(XLabelsProperty); set => SetValue(XLabelsProperty, value); }

    public LineChart()
    {
        SnapsToDevicePixels = true;
        Loaded += (_, _) => ThemeService.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private static DependencyProperty Register<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(LineChart),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    private void OnThemeChanged() => InvalidateVisual();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!ShowGrid) return;
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = null;
        InvalidateVisual();
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private static Color ColorOf(Brush b) => b is SolidColorBrush s ? s.Color : Colors.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // чтобы работало наведение

        var labels = XLabels;
        var pad = ShowGrid
            ? new Thickness(40, 10, 10, labels is { Count: > 1 } ? 26 : 10)
            : new Thickness(0, 3, 0, 3);
        var plot = new Rect(pad.Left, pad.Top, Math.Max(1, w - pad.Left - pad.Right), Math.Max(1, h - pad.Top - pad.Bottom));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double range = Maximum - Minimum;
        if (range <= 0) range = 1;
        int n = Math.Max(2, MaxPoints);

        double XAt(int slot) => plot.Left + plot.Width * slot / (n - 1);
        double YAt(double v) => plot.Bottom - plot.Height * Math.Clamp((v - Minimum) / range, 0, 1);

        if (ShowGrid) DrawGrid(dc, plot, labels, dpi);

        var series = Series;
        if (series is null) return;

        foreach (var s in series)
        {
            var vals = s.Values;
            int skip = Math.Max(0, vals.Count - n);
            int count = vals.Count - skip;
            if (count < 1) continue;

            var brush = Res(s.BrushKey);
            var color = ColorOf(brush);
            var pen = new Pen(brush, ShowGrid ? 2.25 : 2)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();

            // Непрерывные участки: NaN означает «данных нет» и разрывает линию
            var segment = new List<Point>();
            for (int i = 0; i <= count; i++)
            {
                if (i < count && double.IsFinite(vals[skip + i]))
                {
                    segment.Add(new Point(XAt(i + (n - count)), YAt(vals[skip + i])));
                    continue;
                }
                DrawSegment(dc, segment, plot, brush, color, pen);
                segment.Clear();
            }
        }

        if (ShowGrid && _hoverX is double hx && plot.Contains(new Point(hx, plot.Top + 1)))
            DrawHover(dc, series, plot, hx, n, dpi, XAt, YAt);
    }

    private void DrawSegment(DrawingContext dc, List<Point> pts, Rect plot, Brush brush, Color color, Pen pen)
    {
        if (pts.Count == 0) return;
        if (pts.Count == 1)
        {
            dc.DrawEllipse(brush, null, pts[0], 3, 3);
            return;
        }

        if (FillArea)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, plot.Bottom), true, true);
                ctx.LineTo(pts[0], false, false);
                ctx.PolyLineTo(pts.Skip(1).ToArray(), false, false);
                ctx.LineTo(new Point(pts[^1].X, plot.Bottom), false, false);
            }
            area.Freeze();
            var fill = new LinearGradientBrush(
                Color.FromArgb(70, color.R, color.G, color.B),
                Color.FromArgb(0, color.R, color.G, color.B),
                new Point(0, 0), new Point(0, 1));
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToArray(), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    private void DrawGrid(DrawingContext dc, Rect plot, IReadOnlyList<string>? labels, double dpi)
    {
        var border = Res("BorderBrush");
        var muted = Res("TextMutedBrush");
        var pen = new Pen(border, 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) };
        pen.Freeze();

        for (int i = 0; i <= 4; i++)
        {
            double y = Math.Round(plot.Bottom - plot.Height * i / 4) + 0.5;
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            double v = Minimum + (Maximum - Minimum) * i / 4;
            var ft = Text(v.ToString("0", CultureInfo.CurrentCulture), 11, muted, dpi);
            dc.DrawText(ft, new Point(plot.Left - 8 - ft.Width, y - ft.Height / 2));
        }

        if (labels is { Count: > 1 })
        {
            for (int i = 0; i < labels.Count; i++)
            {
                var ft = Text(labels[i], 11, muted, dpi);
                double cx = plot.Left + plot.Width * i / (labels.Count - 1);
                double x = Math.Clamp(cx - ft.Width / 2, 0, ActualWidth - ft.Width);
                dc.DrawText(ft, new Point(x, plot.Bottom + 7));
            }
        }
    }

    private void DrawHover(DrawingContext dc, IReadOnlyList<ChartSeries>? series, Rect plot, double hx, int n, double dpi,
                           Func<int, double> xAt, Func<double, double> yAt)
    {
        if (series is null) return;
        int slot = (int)Math.Round((hx - plot.Left) / plot.Width * (n - 1));
        slot = Math.Clamp(slot, 0, n - 1);
        double x = xAt(slot);

        var guide = new Pen(Res("TextMutedBrush"), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        guide.Freeze();
        dc.DrawLine(guide, new Point(x, plot.Top), new Point(x, plot.Bottom));

        var rows = new List<(Brush brush, string text)>();
        foreach (var s in series)
        {
            int skip = Math.Max(0, s.Values.Count - n);
            int count = s.Values.Count - skip;
            int idx = slot - (n - count);
            if (idx < 0 || idx >= count) continue;

            double v = s.Values[skip + idx];
            if (!double.IsFinite(v)) continue;
            var brush = Res(s.BrushKey);
            dc.DrawEllipse(Res("SurfaceBrush"), new Pen(brush, 2), new Point(x, yAt(v)), 4, 4);
            rows.Add((brush, $"{s.Name}  {v.ToString("0.#", CultureInfo.CurrentCulture)} {s.Unit}"));
        }
        if (rows.Count == 0) return;

        var fg = Res("TextBrush");
        var texts = rows.Select(r => Text(r.text, 12, fg, dpi)).ToList();
        double boxW = texts.Max(t => t.Width) + 14 + 22;
        double boxH = texts.Count * 20 + 12;
        double bx = x + 14 + boxW > plot.Right + 10 ? x - 14 - boxW : x + 14;
        double by = plot.Top + 4;

        dc.DrawRoundedRectangle(Res("Surface2Brush"), new Pen(Res("BorderBrush"), 1), new Rect(bx, by, boxW, boxH), 8, 8);
        for (int i = 0; i < texts.Count; i++)
        {
            double ry = by + 6 + i * 20;
            dc.DrawEllipse(rows[i].brush, null, new Point(bx + 14, ry + 10), 4, 4);
            dc.DrawText(texts[i], new Point(bx + 26, ry + 10 - texts[i].Height / 2));
        }
    }

    private static FormattedText Text(string s, double size, Brush brush, double dpi) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush, dpi);
}
