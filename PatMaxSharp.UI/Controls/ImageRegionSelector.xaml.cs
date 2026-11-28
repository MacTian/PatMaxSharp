using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PatMaxSharp.UI.Controls;

/// <summary>
/// Image viewer with zoom/pan and rectangular region selection.
/// Renders the image plus train/search region overlays and match markers.
/// </summary>
public partial class ImageRegionSelector : UserControl
{
    private Canvas _canvas = null!;
    private ImageBrush _brush = null!;

    private Rectangle _trainRect = null!;
    private Rectangle _searchRect = null!;
    private Rectangle _dragRect = null!;
    private Point _dragStart;
    private Point _dragStartImg;
    private bool _isDragging;

    public ImageRegionSelector()
    {
        InitializeComponent();
        Loaded += (_, _) => Setup();
    }

    #region Dependency properties
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).OnSourceChanged()));

    public ImageSource Source
    {
        get => (ImageSource)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty TrainRegionProperty =
        DependencyProperty.Register(nameof(TrainRegion), typeof(Rect?), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).RedrawRegions()));

    public Rect? TrainRegion
    {
        get => (Rect?)GetValue(TrainRegionProperty);
        set => SetValue(TrainRegionProperty, value);
    }

    public static readonly DependencyProperty SearchRegionProperty =
        DependencyProperty.Register(nameof(SearchRegion), typeof(Rect?), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).RedrawRegions()));

    public Rect? SearchRegion
    {
        get => (Rect?)GetValue(SearchRegionProperty);
        set => SetValue(SearchRegionProperty, value);
    }

    public static readonly DependencyProperty MarkersProperty =
        DependencyProperty.Register(nameof(Markers), typeof(List<MatchMarker>), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).RedrawRegions()));

    public List<MatchMarker> Markers
    {
        get => (List<MatchMarker>)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(ImageRegionSelector),
            new PropertyMetadata(-1, (d, e) => ((ImageRegionSelector)d).RedrawRegions()));

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Pattern outline entries: feature position relative to the pattern origin plus its
    /// unit edge normal, so the overlay can be drawn as oriented ticks at the matched pose.
    /// A bare point list cannot be rotated meaningfully — the old overlay simply added the
    /// model offset to the match position, which is why rotated matches drew wrong.
    /// </summary>
    public static readonly DependencyProperty PatternOutlineProperty =
        DependencyProperty.Register(nameof(PatternOutline), typeof(List<OutlineFeature>), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).RedrawRegions()));

    public List<OutlineFeature> PatternOutline
    {
        get => (List<OutlineFeature>)GetValue(PatternOutlineProperty);
        set => SetValue(PatternOutlineProperty, value);
    }

    /// <summary>Whether the green training rectangle is shown (hidden again after a run).</summary>
    public static readonly DependencyProperty ShowTrainRegionProperty =
        DependencyProperty.Register(nameof(ShowTrainRegion), typeof(bool), typeof(ImageRegionSelector),
            new PropertyMetadata(true, (d, e) => ((ImageRegionSelector)d).RedrawRegions()));

    public bool ShowTrainRegion
    {
        get => (bool)GetValue(ShowTrainRegionProperty);
        set => SetValue(ShowTrainRegionProperty, value);
    }

    public static readonly DependencyProperty SelectModeProperty =
        DependencyProperty.Register(nameof(SelectMode), typeof(RegionKind), typeof(ImageRegionSelector),
            new PropertyMetadata(RegionKind.Train));

    public RegionKind SelectMode
    {
        get => (RegionKind)GetValue(SelectModeProperty);
        set => SetValue(SelectModeProperty, value);
    }

    public static readonly DependencyProperty ImageWidthProperty =
        DependencyProperty.Register(nameof(ImageWidth), typeof(int), typeof(ImageRegionSelector),
            new PropertyMetadata(0));
    public int ImageWidth { get => (int)GetValue(ImageWidthProperty); set => SetValue(ImageWidthProperty, value); }

    public static readonly DependencyProperty ImageHeightProperty =
        DependencyProperty.Register(nameof(ImageHeight), typeof(int), typeof(ImageRegionSelector),
            new PropertyMetadata(0));
    public int ImageHeight { get => (int)GetValue(ImageHeightProperty); set => SetValue(ImageHeightProperty, value); }

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(ImageRegionSelector),
            new PropertyMetadata(1.0));
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    #endregion

    public event Action<Rect?> RegionChanged;

    /// <summary>Temporary diagnostic: the transform chain as the control sees it.</summary>
    public string DescribeGeometry()
    {
        return $"canvas={_canvas.ActualWidth:F0}x{_canvas.ActualHeight:F0} " +
               $"image={ImageWidth}x{ImageHeight} zoom={Zoom:F3} (canvas = image x zoom)";
    }

    public event Action<string> GeometryReported;

    private bool _userZoomed;

    /// <summary>
    /// Fit the whole image into the viewport. Without this the canvas can be shorter than the
    /// image, the bottom of the frame is clipped, and a box dragged on the visible part lands on
    /// a region the user never saw.
    /// </summary>
    private void FitToViewport()
    {
        if (_brush == null || ImageWidth <= 0 || ImageHeight <= 0) return;
        double w = ActualWidth - 8, h = ActualHeight - 8;
        if (w <= 0 || h <= 0) return;
        Zoom = Math.Clamp(Math.Min(1.0, Math.Min(w / ImageWidth, h / ImageHeight)), 0.05, 20.0);
        ApplyZoom();
    }

    private void OnSourceChanged()
    {
        // If already initialized, just update the image source/size
        if (_canvas != null)
        {
            _brush.ImageSource = Source;
            if (Source is System.Windows.Media.Imaging.BitmapSource bs)
            {
                ImageWidth = bs.PixelWidth;
                ImageHeight = bs.PixelHeight;
                _userZoomed = false;
                FitToViewport();
            }
            return;
        }
        Setup();
    }

    public void Setup()
    {
        if (_canvas != null) return;
        _canvas = canvas;
        _brush = brush;

        _trainRect = MakeRect(Brushes.Lime, Color.FromArgb(40, 0, 255, 0));
        _searchRect = MakeRect(Brushes.Cyan, Color.FromArgb(40, 0, 255, 255));
        _dragRect = new Rectangle
        {
            Stroke = Brushes.Yellow, StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_trainRect);
        _canvas.Children.Add(_searchRect);
        _canvas.Children.Add(_dragRect);

        if (Source != null)
        {
            _brush.ImageSource = Source;
            if (Source is System.Windows.Media.Imaging.BitmapSource bs)
            {
                ImageWidth = bs.PixelWidth;
                ImageHeight = bs.PixelHeight;
            }
        }
        ApplyZoom();
    }

    private static Rectangle MakeRect(Brush stroke, Color fill)
    {
        return new Rectangle
        {
            Stroke = stroke, StrokeThickness = 2,
            Fill = new SolidColorBrush(fill),
            Visibility = Visibility.Collapsed
        };
    }

    // ---- Coordinate mapping -------------------------------------------------
    // Everything is expressed in image pixels and converted through the visual tree, so the
    // rectangle you drag and the rectangle that is drawn come from the same transform. The
    // previous code multiplied positions by Zoom when drawing and divided mouse positions by
    // Zoom when hit-testing; once the image element had any transform of its own the two
    // disagreed and a box dragged over the part produced an empty training region.

    /// <summary>
    /// Mapping between the image element space and image pixels. The element paints the bitmap
    /// with Stretch=Uniform, i.e. scaled to fit and *centred*, so the content does not start at
    /// the element origin. Ignoring that letterbox offset is what shifted every dragged region
    /// by a constant amount.
    /// </summary>
    private (double Sx, double Sy, double Ox, double Oy) ContentMapping()
    {
        double z = Zoom <= 0 ? 1.0 : Zoom;
        return (z, z, 0, 0);
    }

    /// <summary>Canvas point to image pixel coordinates.</summary>
    private Point CanvasToImage(Point canvasPoint)
    {
        var m = ContentMapping();
        return new Point(canvasPoint.X / m.Sx, canvasPoint.Y / m.Sy);
    }

    /// <summary>Image pixel to canvas point (inverse of <see cref="CanvasToImage"/>).</summary>
    private Point ImageToCanvas(Point imagePoint) => new(imagePoint.X * Zoom, imagePoint.Y * Zoom);



    private double CanvasScale
    {
        get
        {
            var a = ImageToCanvas(new Point(0, 0));
            var b = ImageToCanvas(new Point(100, 0));
            return Math.Max(1e-6, (b.X - a.X) / 100.0);
        }
    }

    // ---- Mouse handlers (wired in XAML) ----
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Source == null) return;
        double factor = e.Delta > 0 ? 1.1 : 0.9;
        _userZoomed = true;
        Zoom = Math.Clamp(Zoom * factor, 0.1, 20);
        ApplyZoom();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Source == null) return;
        _isDragging = true;
        // Take the point in the image element own space: one unit is one image pixel there,
        // independent of the element offset or scale inside the canvas.
        _dragStartImg = CanvasToImage(e.GetPosition(_canvas));
        _dragStart = new Point(_dragStartImg.X * Zoom, _dragStartImg.Y * Zoom);
        _dragRect.Visibility = Visibility.Visible;
        _dragRect.Width = 0; _dragRect.Height = 0;
        Canvas.SetLeft(_dragRect, _dragStart.X);
        Canvas.SetTop(_dragRect, _dragStart.Y);
        _canvas.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var cur = CanvasToImage(e.GetPosition(_canvas));
        var p = new Point(cur.X * Zoom, cur.Y * Zoom);
        _dragRect.Width = Math.Abs(p.X - _dragStart.X);
        _dragRect.Height = Math.Abs(p.Y - _dragStart.Y);
        Canvas.SetLeft(_dragRect, Math.Min(_dragStart.X, p.X));
        Canvas.SetTop(_dragRect, Math.Min(_dragStart.Y, p.Y));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _canvas.ReleaseMouseCapture();
        _dragRect.Visibility = Visibility.Collapsed;

        var b = CanvasToImage(e.GetPosition(_canvas));
        var a = _dragStartImg;
        var rect = new Rect(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y));

        if (rect.Width < 4 || rect.Height < 4) return;

        if (SelectMode == RegionKind.Train) TrainRegion = rect;
        else SearchRegion = rect;
        RegionChanged?.Invoke(rect);
        RedrawRegions();
    }

    private void ApplyZoom()
    {
        // The canvas is exactly the image at the requested zoom; children live in image pixels,
        // so every position is multiplied by Zoom on the way out and divided on the way in.
        if (ImageWidth > 0 && ImageHeight > 0)
        {
            _canvas.Width = ImageWidth * Zoom;
            _canvas.Height = ImageHeight * Zoom;
        }
        RedrawRegions();
    }

    private void RedrawRegions()
    {
        if (_trainRect == null) return;

        if (TrainRegion is Rect tr && ShowTrainRegion)
        {
            _trainRect.Visibility = Visibility.Visible;
            var p0 = ImageToCanvas(new Point(tr.X, tr.Y));
            var p1 = ImageToCanvas(new Point(tr.X + tr.Width, tr.Y + tr.Height));
            _trainRect.Width = Math.Abs(p1.X - p0.X); _trainRect.Height = Math.Abs(p1.Y - p0.Y);
            Canvas.SetLeft(_trainRect, Math.Min(p0.X, p1.X)); Canvas.SetTop(_trainRect, Math.Min(p0.Y, p1.Y));
        }
        else _trainRect.Visibility = Visibility.Collapsed;

        if (SearchRegion is Rect sr)
        {
            _searchRect.Visibility = Visibility.Visible;
            var s0 = ImageToCanvas(new Point(sr.X, sr.Y));
            var s1 = ImageToCanvas(new Point(sr.X + sr.Width, sr.Y + sr.Height));
            _searchRect.Width = Math.Abs(s1.X - s0.X); _searchRect.Height = Math.Abs(s1.Y - s0.Y);
            Canvas.SetLeft(_searchRect, Math.Min(s0.X, s1.X)); Canvas.SetTop(_searchRect, Math.Min(s0.Y, s1.Y));
        }
        else _searchRect.Visibility = Visibility.Collapsed;

        // match markers
        var old = _canvas.Children
            .OfType<FrameworkElement>()
            .Where(fe => fe.Tag is MatchMarkerTag)
            .ToList();
        foreach (var el in old) _canvas.Children.Remove(el);

        if (Markers == null) return;
        for (int mi = 0; mi < Markers.Count; mi++)
        {
            var m = Markers[mi];
            bool selected = (mi == SelectedIndex);
            var brush = selected ? Brushes.Yellow : (m.Accepted ? Brushes.Lime : Brushes.Orange);
            double thickness = selected ? 4.0 : 2.0;
            var pos = ImageToCanvas(new Point(m.X, m.Y));
            double k = CanvasScale;
            double s = m.Size * k * m.Scale;
            double a = m.AngleDeg * Math.PI / 180.0;

            var crossH = new Line { X1 = pos.X - 8 * k, Y1 = pos.Y, X2 = pos.X + 8 * k, Y2 = pos.Y, Stroke = brush, StrokeThickness = thickness };
            var crossV = new Line { X1 = pos.X, Y1 = pos.Y - 8 * k, X2 = pos.X, Y2 = pos.Y + 8 * k, Stroke = brush, StrokeThickness = thickness };
            var arrow = new Line { X1 = pos.X, Y1 = pos.Y, X2 = pos.X + Math.Cos(a) * s, Y2 = pos.Y + Math.Sin(a) * s, Stroke = brush, StrokeThickness = thickness };
            var box = new Rectangle
            {
                Width = s * 2, Height = s * 2,
                Stroke = brush, StrokeThickness = thickness,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(m.AngleDeg)
            };
            Canvas.SetLeft(box, pos.X - s);
            Canvas.SetTop(box, pos.Y - s);

            // Matched model overlay: each feature drawn as a tick along its own edge normal,
            // rotated and scaled by the found pose. One Path for the whole model keeps the
            // element count at one per match instead of one per feature.
            if (PatternOutline != null && PatternOutline.Count > 0)
            {
                var outlineBrush = selected ? Brushes.Yellow : Brushes.Cyan;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                const double tickHalf = 1.6;   // model-space half length of a tick

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    foreach (var f in PatternOutline)
                    {
                        double px = m.X + m.Scale * (ca * f.X - sa * f.Y);
                        double py = m.Y + m.Scale * (sa * f.X + ca * f.Y);
                        double nx = m.Scale * (ca * f.Nx - sa * f.Ny) * tickHalf;
                        double ny = m.Scale * (sa * f.Nx + ca * f.Ny) * tickHalf;

                        var tick0 = ImageToCanvas(new Point(px - nx, py - ny));
                        var tick1 = ImageToCanvas(new Point(px + nx, py + ny));
                        ctx.BeginFigure(tick0, false, false);
                        ctx.LineTo(tick1, true, false);
                    }
                }
                geo.Freeze();

                var overlay = new System.Windows.Shapes.Path
                {
                    Data = geo,
                    Stroke = outlineBrush,
                    StrokeThickness = Math.Max(1.0, thickness * 0.55),
                    Tag = new MatchMarkerTag()
                };
                _canvas.Children.Add(overlay);
            }

            foreach (var el in new FrameworkElement[] { box, crossH, crossV, arrow })
                el.Tag = new MatchMarkerTag();
            _canvas.Children.Add(box);
            _canvas.Children.Add(crossH);
            _canvas.Children.Add(crossV);
            _canvas.Children.Add(arrow);
        }
    }

    private sealed class MatchMarkerTag { }
}

/// <summary>A match result marker drawn on the image.</summary>
public sealed class MatchMarker
{
    public double X { get; set; }
    public double Y { get; set; }
    public double AngleDeg { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Score { get; set; }
    public bool Accepted { get; set; }
    public double Size { get; set; } = 24;
}

/// <summary>
/// One entry of the model overlay: position relative to the pattern origin and the unit
/// edge normal at that position.
/// </summary>
public sealed class OutlineFeature
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Nx { get; set; }
    public double Ny { get; set; }
}

public enum RegionKind { Train, Search }
