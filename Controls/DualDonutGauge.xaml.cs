using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace nvGPUMonitor.Controls
{
    public partial class DualDonutGauge : UserControl
    {
        // First value (outer ring - TX)
        public static readonly DependencyProperty Value1Property =
            DependencyProperty.Register(nameof(Value1), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        
        // Second value (inner ring - RX)
        public static readonly DependencyProperty Value2Property =
            DependencyProperty.Register(nameof(Value2), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));

        // Labels for each value
        public static readonly DependencyProperty Label1Property =
            DependencyProperty.Register(nameof(Label1), typeof(string), typeof(DualDonutGauge),
                new PropertyMetadata("TX", OnAnyChanged));
        
        public static readonly DependencyProperty Label2Property =
            DependencyProperty.Register(nameof(Label2), typeof(string), typeof(DualDonutGauge),
                new PropertyMetadata("RX", OnAnyChanged));

        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(DualDonutGauge),
                new PropertyMetadata(string.Empty, OnAnyChanged));
        
        public static readonly DependencyProperty DetailProperty =
            DependencyProperty.Register(nameof(Detail), typeof(string), typeof(DualDonutGauge),
                new PropertyMetadata(string.Empty, OnAnyChanged));

        // Brushes for each arc
        public static readonly DependencyProperty Arc1BrushProperty =
            DependencyProperty.Register(nameof(Arc1Brush), typeof(Brush), typeof(DualDonutGauge),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35)), OnAnyChanged)); // Orange
        
        public static readonly DependencyProperty Arc2BrushProperty =
            DependencyProperty.Register(nameof(Arc2Brush), typeof(Brush), typeof(DualDonutGauge),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x96)), OnAnyChanged)); // Green

        // Offsets
        public static readonly DependencyProperty CaptionOffsetXProperty =
            DependencyProperty.Register(nameof(CaptionOffsetX), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty CaptionOffsetYProperty =
            DependencyProperty.Register(nameof(CaptionOffsetY), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty ValueOffsetXProperty =
            DependencyProperty.Register(nameof(ValueOffsetX), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty ValueOffsetYProperty =
            DependencyProperty.Register(nameof(ValueOffsetY), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty DetailOffsetXProperty =
            DependencyProperty.Register(nameof(DetailOffsetX), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));
        public static readonly DependencyProperty DetailOffsetYProperty =
            DependencyProperty.Register(nameof(DetailOffsetY), typeof(double), typeof(DualDonutGauge),
                new PropertyMetadata(0.0, OnAnyChanged));

        public double Value1 { get => (double)GetValue(Value1Property); set => SetValue(Value1Property, value); }
        public double Value2 { get => (double)GetValue(Value2Property); set => SetValue(Value2Property, value); }
        public string Label1 { get => (string)GetValue(Label1Property); set => SetValue(Label1Property, value); }
        public string Label2 { get => (string)GetValue(Label2Property); set => SetValue(Label2Property, value); }
        public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
        public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
        public Brush Arc1Brush { get => (Brush)GetValue(Arc1BrushProperty); set => SetValue(Arc1BrushProperty, value); }
        public Brush Arc2Brush { get => (Brush)GetValue(Arc2BrushProperty); set => SetValue(Arc2BrushProperty, value); }

        public double CaptionOffsetX { get => (double)GetValue(CaptionOffsetXProperty); set => SetValue(CaptionOffsetXProperty, value); }
        public double CaptionOffsetY { get => (double)GetValue(CaptionOffsetYProperty); set => SetValue(CaptionOffsetYProperty, value); }
        public double ValueOffsetX { get => (double)GetValue(ValueOffsetXProperty); set => SetValue(ValueOffsetXProperty, value); }
        public double ValueOffsetY { get => (double)GetValue(ValueOffsetYProperty); set => SetValue(ValueOffsetYProperty, value); }
        public double DetailOffsetX { get => (double)GetValue(DetailOffsetXProperty); set => SetValue(DetailOffsetXProperty, value); }
        public double DetailOffsetY { get => (double)GetValue(DetailOffsetYProperty); set => SetValue(DetailOffsetYProperty, value); }

        public DualDonutGauge()
        {
            InitializeComponent();
            SizeChanged += (_, __) => Redraw();
            Loaded += (_, __) => Redraw();
        }

        private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DualDonutGauge g) g.Redraw();
        }

        private void Redraw()
        {
            double v1 = Math.Max(0, Math.Min(100, Value1));
            double v2 = Math.Max(0, Math.Min(100, Value2));
            
            // Hide the individual TX/RX percentage lines
            Value1Text.Visibility = Visibility.Collapsed;
            Value2Text.Visibility = Visibility.Collapsed;
            
            CaptionText.Text = string.IsNullOrWhiteSpace(Caption) ? "—" : Caption;
            DetailText.Text = Detail ?? "";

            double w = ActualWidth > 0 ? ActualWidth : 192;
            double h = ActualHeight > 0 ? ActualHeight : 192;
            double size = Math.Min(w, h);
            double bgStroke = 24.0;
            double arcStroke = 12.0;

            Overlay.Width = size;
            Overlay.Height = size;
            var center = new Point(size / 2, size / 2);

            // Background: full circle
            double rBg = (size / 2) - (bgStroke / 2) - 1;
            if (rBg <= 0) return;

            var pStartBg = Polar(center, rBg, -90);
            var pMidBg = Polar(center, rBg, 90);
            var pEndBg = Polar(center, rBg, 270);
            var figBg = new PathFigure { StartPoint = pStartBg };
            figBg.Segments.Add(new ArcSegment { Point = pMidBg, Size = new Size(rBg, rBg), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
            figBg.Segments.Add(new ArcSegment { Point = pEndBg, Size = new Size(rBg, rBg), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
            ArcBg.Data = new PathGeometry(new[] { figBg });

            // Outer ring (Value1 - TX in orange) - offset outward
            double r1 = rBg + (arcStroke / 2) - 1;
            ArcPath1.Stroke = Arc1Brush;
            ArcPath1.StrokeThickness = arcStroke;
            ArcPath1.Data = CreateArc(center, r1, v1);

            // Inner ring (Value2 - RX in green) - offset inward
            double r2 = rBg - (arcStroke / 2) + 1;
            ArcPath2.Stroke = Arc2Brush;
            ArcPath2.StrokeThickness = arcStroke;
            ArcPath2.Data = CreateArc(center, r2, v2);

            // Apply offsets
            Overlay.RenderTransform = Transform.Identity;
            CaptionText.RenderTransform = new TranslateTransform(CaptionOffsetX, CaptionOffsetY);
            Value1Text.RenderTransform = new TranslateTransform(ValueOffsetX, ValueOffsetY);
            Value2Text.RenderTransform = new TranslateTransform(ValueOffsetX, ValueOffsetY);
            DetailText.RenderTransform = new TranslateTransform(DetailOffsetX, DetailOffsetY);
        }

        private PathGeometry CreateArc(Point center, double radius, double percentValue)
        {
            double startAngle = -90.0;
            double sweep = (percentValue / 100.0) * 360.0;

            if (sweep <= 0.0)
            {
                return new PathGeometry();
            }

            if (sweep >= 360.0)
            {
                // Full circle - draw as two semicircles
                var pStart = Polar(center, radius, -90);
                var pMid = Polar(center, radius, 90);
                var pEnd = Polar(center, radius, 270);
                var figFull = new PathFigure { StartPoint = pStart };
                figFull.Segments.Add(new ArcSegment { Point = pMid, Size = new Size(radius, radius), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
                figFull.Segments.Add(new ArcSegment { Point = pEnd, Size = new Size(radius, radius), IsLargeArc = false, SweepDirection = SweepDirection.Clockwise });
                return new PathGeometry(new[] { figFull });
            }
            else
            {
                // Normal arc
                double endAngle = startAngle + sweep;
                var p0 = Polar(center, radius, startAngle);
                var p1 = Polar(center, radius, endAngle);
                bool isLarge = sweep >= 180.0;
                var seg = new ArcSegment { Point = p1, Size = new Size(radius, radius), IsLargeArc = isLarge, SweepDirection = SweepDirection.Clockwise };
                var fig = new PathFigure { StartPoint = p0 };
                fig.Segments.Add(seg);
                return new PathGeometry(new[] { fig });
            }
        }

        private static Point Polar(Point c, double radius, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point(c.X + radius * Math.Cos(rad), c.Y + radius * Math.Sin(rad));
        }
    }
}