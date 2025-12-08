using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace nvGPUMonitor.Controls
{
    public partial class DonutGauge : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(DonutGauge),
                new PropertyMetadata(string.Empty, OnAnyChanged));
        public static readonly DependencyProperty DetailProperty =
            DependencyProperty.Register(nameof(Detail), typeof(string), typeof(DonutGauge),
                new PropertyMetadata(string.Empty, OnAnyChanged));
        public static readonly DependencyProperty ArcBrushProperty =
            DependencyProperty.Register(nameof(ArcBrush), typeof(Brush), typeof(DonutGauge),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A,0x7B,0xFF)), OnAnyChanged));

        // Caption-only offsets
        public static readonly DependencyProperty CaptionOffsetXProperty =
            DependencyProperty.Register(nameof(CaptionOffsetX), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty CaptionOffsetYProperty =
            DependencyProperty.Register(nameof(CaptionOffsetY), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));

        // Value / Detail offsets
        public static readonly DependencyProperty ValueOffsetXProperty =
            DependencyProperty.Register(nameof(ValueOffsetX), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty ValueOffsetYProperty =
            DependencyProperty.Register(nameof(ValueOffsetY), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty DetailOffsetXProperty =
            DependencyProperty.Register(nameof(DetailOffsetX), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty DetailOffsetYProperty =
            DependencyProperty.Register(nameof(DetailOffsetY), typeof(double), typeof(DonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
        public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
        public Brush ArcBrush { get => (Brush)GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }

        public double CaptionOffsetX { get => (double)GetValue(CaptionOffsetXProperty); set => SetValue(CaptionOffsetXProperty, value); }
        public double CaptionOffsetY { get => (double)GetValue(CaptionOffsetYProperty); set => SetValue(CaptionOffsetYProperty, value); }
        public double ValueOffsetX   { get => (double)GetValue(ValueOffsetXProperty); set => SetValue(ValueOffsetXProperty, value); }
        public double ValueOffsetY   { get => (double)GetValue(ValueOffsetYProperty); set => SetValue(ValueOffsetYProperty, value); }
        public double DetailOffsetX  { get => (double)GetValue(DetailOffsetXProperty); set => SetValue(DetailOffsetXProperty, value); }
        public double DetailOffsetY  { get => (double)GetValue(DetailOffsetYProperty); set => SetValue(DetailOffsetYProperty, value); }

        public DonutGauge()
        {
            InitializeComponent();
            SizeChanged += (_, __) => Redraw();
            Loaded += (_, __) => Redraw();
        }

        private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DonutGauge g) g.Redraw();
        }

        private void Redraw()
        {
            double v = Math.Max(0, Math.Min(100, Value));
            ValueText.Text = $"{v:0}%";
            CaptionText.Text = string.IsNullOrWhiteSpace(Caption) ? "—" : Caption;
            DetailText.Text = Detail ?? "";
            ArcPath.Stroke = ArcBrush;

            double w = ActualWidth > 0 ? ActualWidth : 192;
            double h = ActualHeight > 0 ? ActualHeight : 192;
            double size = Math.Min(w, h);
            double stroke = ArcPath.StrokeThickness;
            double r = (size / 2) - (stroke / 2) - 1;
            if (r <= 0) return;

            Overlay.Width = size;
            Overlay.Height = size;
            var center = new Point(size/2, size/2);

            // Background: full circle via two arcs
            var pStartBg = Polar(center, r, -90);
            var pMidBg = Polar(center, r, 90);
            var pEndBg = Polar(center, r, 270);
            var figBg = new PathFigure { StartPoint = pStartBg };
            figBg.Segments.Add(new ArcSegment { Point = pMidBg, Size = new Size(r, r), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
            figBg.Segments.Add(new ArcSegment { Point = pEndBg, Size = new Size(r, r), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
            ArcBg.Data = new PathGeometry(new [] { figBg });

            // Foreground arc
            double startAngle = -90.0;
            double sweep = (v / 100.0) * 360.0;
            
            // Handle edge cases
            if (sweep <= 0.0)
            {
                // No arc to draw
                ArcPath.Data = new PathGeometry();
                // Don't return - we still need to apply offsets below
            }
            else if (sweep >= 360.0)
            {
                // Full circle - draw as two semicircles to avoid arc issues
                var pStart = Polar(center, r, -90);
                var pMid = Polar(center, r, 90);
                var pEnd = Polar(center, r, 270);
                var figFull = new PathFigure { StartPoint = pStart };
                figFull.Segments.Add(new ArcSegment { Point = pMid, Size = new Size(r, r), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
                figFull.Segments.Add(new ArcSegment { Point = pEnd, Size = new Size(r, r), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
                ArcPath.Data = new PathGeometry(new [] { figFull });
            }
            else
            {
                // Normal arc
                double endAngle = startAngle + sweep;
                var p0 = Polar(center, r, startAngle);
                var p1 = Polar(center, r, endAngle);
                bool isLarge = sweep >= 180.0;
                var seg = new ArcSegment { Point = p1, Size = new Size(r, r), IsLargeArc = isLarge, SweepDirection = SweepDirection.Clockwise };
                var fig = new PathFigure { StartPoint = p0 };
                fig.Segments.Add(seg);
                ArcPath.Data = new PathGeometry(new [] { fig });
            }

            // Optical-centering tweeks: apply per-line offsets
            Overlay.RenderTransform = Transform.Identity;
            CaptionText.RenderTransform = new TranslateTransform(CaptionOffsetX, CaptionOffsetY);
            ValueText.RenderTransform   = new TranslateTransform(ValueOffsetX,   ValueOffsetY);
            DetailText.RenderTransform  = new TranslateTransform(DetailOffsetX,  DetailOffsetY);
        }

        private static Point Polar(Point c, double radius, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point(c.X + radius * Math.Cos(rad), c.Y + radius * Math.Sin(rad));
        }
    }
}