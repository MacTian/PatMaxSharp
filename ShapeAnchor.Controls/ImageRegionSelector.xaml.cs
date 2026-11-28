using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ShapeAnchor.Controls;

/// <summary>
/// Image viewer with rectangular region selection.
///
/// One rule, everywhere: the canvas is the image scaled by <see cref="Zoom"/>, so
///   canvas position  = image position * Zoom      (drawing)
///   image position   = canvas position / Zoom     (mouse)
/// The bitmap is the canvas background, so there is no second coordinate space to reconcile.
/// </summary>
public partial class ImageRegionSelector : UserControl
{
    private Canvas _canvas = null!;
    private ImageBrush _brush = null!;

    private Image _maskImage = null!;
    private WriteableBitmap _maskBitmap;
    private bool _paintingMask;

    private Rectangle _trainRect = null!;
    private Rectangle _searchRect = null!;
    private Rectangle _dragRect = null!;
    private Point _dragStart;      // image pixels
    private Point _dragLast;       // image pixels
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
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).Redraw()));

    /// <summary>Training region in image pixels.</summary>
    public Rect? TrainRegion
    {
        get => (Rect?)GetValue(TrainRegionProperty);
        set => SetValue(TrainRegionProperty, value);
    }

    public static readonly DependencyProperty SearchRegionProperty =
        DependencyProperty.Register(nameof(SearchRegion), typeof(Rect?), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).Redraw()));

    /// <summary>Search region in image pixels.</summary>
    public Rect? SearchRegion
    {
        get => (Rect?)GetValue(SearchRegionProperty);
        set => SetValue(SearchRegionProperty, value);
    }

    public static readonly DependencyProperty MarkersProperty =
        DependencyProperty.Register(nameof(Markers), typeof(List<MatchMarker>), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).Redraw()));

    public List<MatchMarker> Markers
    {
        get => (List<MatchMarker>)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(ImageRegionSelector),
            new PropertyMetadata(-1, (d, e) => ((ImageRegionSelector)d).Redraw()));

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Model overlay: feature position around the pattern origin plus its edge normal.</summary>
    public static readonly DependencyProperty PatternOutlineProperty =
        DependencyProperty.Register(nameof(PatternOutline), typeof(List<OutlineFeature>), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).Redraw()));

    public List<OutlineFeature> PatternOutline
    {
        get => (List<OutlineFeature>)GetValue(PatternOutlineProperty);
        set => SetValue(PatternOutlineProperty, value);
    }

    /// <summary>Care / Don't-care mask over the image, one byte per pixel, 0 = Don't care.</summary>
    public static readonly DependencyProperty MaskBytesProperty =
        DependencyProperty.Register(nameof(MaskBytes), typeof(byte[]), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).RebuildMaskOverlay()));

    public byte[] MaskBytes
    {
        get => (byte[])GetValue(MaskBytesProperty);
        set => SetValue(MaskBytesProperty, value);
    }

    /// <summary>When true, dragging paints the mask instead of selecting a region.</summary>
    public static readonly DependencyProperty MaskEditingProperty =
        DependencyProperty.Register(nameof(MaskEditing), typeof(bool), typeof(ImageRegionSelector),
            new PropertyMetadata(false));

    public bool MaskEditing
    {
        get => (bool)GetValue(MaskEditingProperty);
        set => SetValue(MaskEditingProperty, value);
    }

    /// <summary>Brush radius in image pixels.</summary>
    public static readonly DependencyProperty BrushRadiusProperty =
        DependencyProperty.Register(nameof(BrushRadius), typeof(double), typeof(ImageRegionSelector),
            new PropertyMetadata(12.0));

    public double BrushRadius
    {
        get => (double)GetValue(BrushRadiusProperty);
        set => SetValue(BrushRadiusProperty, value);
    }

    /// <summary>True erases (marks Don't care), false marks Care.</summary>
    public static readonly DependencyProperty MaskEraseProperty =
        DependencyProperty.Register(nameof(MaskErase), typeof(bool), typeof(ImageRegionSelector),
            new PropertyMetadata(true));

    public bool MaskErase
    {
        get => (bool)GetValue(MaskEraseProperty);
        set => SetValue(MaskEraseProperty, value);
    }

    /// <summary>Training origin in image pixels, drawn as a cross so it is visible while framing.</summary>
    public static readonly DependencyProperty OriginPointProperty =
        DependencyProperty.Register(nameof(OriginPoint), typeof(Point?), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).Redraw()));

    public Point? OriginPoint
    {
        get => (Point?)GetValue(OriginPointProperty);
        set => SetValue(OriginPointProperty, value);
    }

    /// <summary>Whether the green training rectangle is drawn (hidden again after a run).</summary>
    public static readonly DependencyProperty ShowTrainRegionProperty =
        DependencyProperty.Register(nameof(ShowTrainRegion), typeof(bool), typeof(ImageRegionSelector),
            new PropertyMetadata(true, (d, e) => ((ImageRegionSelector)d).Redraw()));

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

    public int ImageWidth
    {
        get => (int)GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    public static readonly DependencyProperty ImageHeightProperty =
        DependencyProperty.Register(nameof(ImageHeight), typeof(int), typeof(ImageRegionSelector),
            new PropertyMetadata(0));

    public int ImageHeight
    {
        get => (int)GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(ImageRegionSelector),
            new PropertyMetadata(1.0, (d, e) => ((ImageRegionSelector)d).ApplyZoom()));

    /// <summary>Canvas pixels per image pixel.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }
    #endregion

    public event Action<Rect?> RegionChanged;

    // ------------------------------------------------------------------
    //  Setup
    // ------------------------------------------------------------------
    private void Setup()
    {
        if (_canvas != null) return;
        _canvas = canvas;
        _brush = (ImageBrush)_canvas.Background;

        _trainRect = MakeRect(Brushes.Lime, Color.FromArgb(40, 0, 255, 0));
        _searchRect = MakeRect(Brushes.Cyan, Color.FromArgb(40, 0, 255, 255));
        _dragRect = new Rectangle
        {
            Stroke = Brushes.Yellow, StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed
        };
        // mask overlay sits under the region rectangles: translucent red where Don't care
        _maskImage = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(_maskImage);

        _canvas.Children.Add(_trainRect);
        _canvas.Children.Add(_searchRect);
        _canvas.Children.Add(_dragRect);

        LoadImage();
    }

    private void OnSourceChanged()
    {
        if (_canvas == null) { Setup(); return; }
        LoadImage();
    }

    private void LoadImage()
    {
        _brush.ImageSource = Source;
        if (Source is System.Windows.Media.Imaging.BitmapSource bs)
        {
            ImageWidth = bs.PixelWidth;
            ImageHeight = bs.PixelHeight;
            FitZoom();
        }
        ApplyZoom();
    }

    /// <summary>Zoom so the whole frame is visible (never above 1:1).</summary>
    private void FitZoom()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0) return;
        double w = ActualWidth - 8, h = ActualHeight - 8;
        if (w <= 0 || h <= 0) return;
        Zoom = Math.Clamp(Math.Min(1.0, Math.Min(w / ImageWidth, h / ImageHeight)), 0.05, 20.0);
    }

    private void ApplyZoom()
    {
        if (_canvas == null || ImageWidth <= 0 || ImageHeight <= 0) return;
        _canvas.Width = ImageWidth * Zoom;
        _canvas.Height = ImageHeight * Zoom;
        if (_maskImage != null)
        {
            _maskImage.Width = _canvas.Width;
            _maskImage.Height = _canvas.Height;
        }
        Redraw();
    }

    private static Rectangle MakeRect(Brush stroke, Color fill) => new()
    {
        Stroke = stroke,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(fill),
        Visibility = Visibility.Collapsed
    };

    // ------------------------------------------------------------------
    //  Drawing: image pixels * Zoom
    // ------------------------------------------------------------------
    private void Redraw()
    {
        if (_trainRect == null) return;

        if (TrainRegion is Rect tr && ShowTrainRegion) SetRect(_trainRect, tr);
        else _trainRect.Visibility = Visibility.Collapsed;

        if (SearchRegion is Rect sr) SetRect(_searchRect, sr);
        else _searchRect.Visibility = Visibility.Collapsed;

        // training origin: a cross + ring so the point the model is defined around is visible
        foreach (var el in _canvas.Children.OfType<FrameworkElement>().Where(fe => fe.Tag is OriginTag).ToList())
            _canvas.Children.Remove(el);
        if (OriginPoint is Point op)
        {
            double ox = op.X * Zoom, oy = op.Y * Zoom;
            var ob = Brushes.DeepSkyBlue;
            var crossH = new Line { X1 = ox - 10, Y1 = oy, X2 = ox + 10, Y2 = oy, Stroke = ob, StrokeThickness = 1.5, Tag = new OriginTag() };
            var crossV = new Line { X1 = ox, Y1 = oy - 10, X2 = ox, Y2 = oy + 10, Stroke = ob, StrokeThickness = 1.5, Tag = new OriginTag() };
            var ring = new Ellipse { Width = 14, Height = 14, Stroke = ob, StrokeThickness = 1.5, Fill = Brushes.Transparent, Tag = new OriginTag() };
            Canvas.SetLeft(ring, ox - 7);
            Canvas.SetTop(ring, oy - 7);
            _canvas.Children.Add(crossH);
            _canvas.Children.Add(crossV);
            _canvas.Children.Add(ring);
        }

        var old = _canvas.Children.OfType<FrameworkElement>()
            .Where(fe => fe.Tag is MatchMarkerTag).ToList();
        foreach (var el in old) _canvas.Children.Remove(el);

        if (Markers == null) return;
        for (int i = 0; i < Markers.Count; i++) DrawMarker(Markers[i], i == SelectedIndex);
    }

    private void SetRect(Rectangle target, Rect imageRect)
    {
        target.Visibility = Visibility.Visible;
        target.Width = imageRect.Width * Zoom;
        target.Height = imageRect.Height * Zoom;
        Canvas.SetLeft(target, imageRect.X * Zoom);
        Canvas.SetTop(target, imageRect.Y * Zoom);
    }

    private void DrawMarker(MatchMarker m, bool selected)
    {
        var brush = selected ? Brushes.Yellow : (m.Accepted ? Brushes.Lime : Brushes.Orange);
        double thickness = selected ? 4.0 : 2.0;

        double px = m.X * Zoom, py = m.Y * Zoom;
        double size = m.Size * Zoom * m.Scale;
        double angle = m.AngleDeg * Math.PI / 180.0;

        var crossH = new Line { X1 = px - 8, Y1 = py, X2 = px + 8, Y2 = py, Stroke = brush, StrokeThickness = thickness };
        var crossV = new Line { X1 = px, Y1 = py - 8, X2 = px, Y2 = py + 8, Stroke = brush, StrokeThickness = thickness };
        var arrow = new Line
        {
            X1 = px, Y1 = py,
            X2 = px + Math.Cos(angle) * size, Y2 = py + Math.Sin(angle) * size,
            Stroke = brush, StrokeThickness = thickness
        };
        var box = new Rectangle
        {
            Width = size * 2, Height = size * 2,
            Stroke = brush, StrokeThickness = thickness,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = Brushes.Transparent,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(m.AngleDeg)
        };
        Canvas.SetLeft(box, px - size);
        Canvas.SetTop(box, py - size);

        // The model as one path of ticks along each feature edge normal, rotated and scaled
        // onto the found pose.
        if (PatternOutline is { Count: > 0 })
        {
            double ca = Math.Cos(angle), sa = Math.Sin(angle);
            const double tickHalf = 1.6;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                foreach (var f in PatternOutline)
                {
                    double fx = m.X + m.Scale * (ca * f.X - sa * f.Y);
                    double fy = m.Y + m.Scale * (sa * f.X + ca * f.Y);
                    double nx = m.Scale * (ca * f.Nx - sa * f.Ny) * tickHalf;
                    double ny = m.Scale * (sa * f.Nx + ca * f.Ny) * tickHalf;

                    ctx.BeginFigure(new Point((fx - nx) * Zoom, (fy - ny) * Zoom), false, false);
                    ctx.LineTo(new Point((fx + nx) * Zoom, (fy + ny) * Zoom), true, false);
                }
            }
            geo.Freeze();

            _canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = selected ? Brushes.Yellow : Brushes.Cyan,
                StrokeThickness = Math.Max(1.0, thickness * 0.55),
                Tag = new MatchMarkerTag()
            });
        }

        foreach (var el in new FrameworkElement[] { box, crossH, crossV, arrow })
            el.Tag = new MatchMarkerTag();
        _canvas.Children.Add(box);
        _canvas.Children.Add(crossH);
        _canvas.Children.Add(crossV);
        _canvas.Children.Add(arrow);
    }

    /// <summary>Raised when the user finishes a mask stroke, so the view model can mirror it.</summary>
    public event Action MaskPainted;

    /// <summary>Paint (or erase) a filled circle of <see cref="BrushRadius"/> at a canvas point.</summary>
    private void PaintMask(Point canvasPoint)
    {
        var mask = MaskBytes;
        if (mask == null || ImageWidth <= 0 || ImageHeight <= 0) return;

        var p = ToImage(canvasPoint);
        double r = Math.Max(1.0, BrushRadius);
        int cx = (int)Math.Round(p.X), cy = (int)Math.Round(p.Y);
        int ri = (int)Math.Ceiling(r);
        byte value = MaskErase ? (byte)0 : (byte)1;

        for (int y = cy - ri; y <= cy + ri; y++)
        {
            if (y < 0 || y >= ImageHeight) continue;
            for (int x = cx - ri; x <= cx + ri; x++)
            {
                if (x < 0 || x >= ImageWidth) continue;
                double dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r * r) continue;
                mask[y * ImageWidth + x] = value;
            }
        }
        RebuildMaskOverlay();
    }

    /// <summary>Draw the Don''t-care area as translucent red; Care is left untouched.</summary>
    private void RebuildMaskOverlay()
    {
        if (_maskImage == null) return;
        var mask = MaskBytes;
        if (mask == null || ImageWidth <= 0 || ImageHeight <= 0)
        {
            _maskImage.Source = null;
            return;
        }

        if (_maskBitmap == null || _maskBitmap.PixelWidth != ImageWidth || _maskBitmap.PixelHeight != ImageHeight)
            _maskBitmap = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);

        var pixels = new int[ImageWidth * ImageHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = mask[i] == 0 ? unchecked((int)0x66FF2020) : 0;   // ARGB: red, ~40% alpha
        _maskBitmap.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), pixels, ImageWidth * 4, 0);
        _maskImage.Source = _maskBitmap;
    }
    // ------------------------------------------------------------------
    //  Mouse: canvas / Zoom = image pixels
    // ------------------------------------------------------------------
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Source == null) return;
        Zoom = Math.Clamp(Zoom * (e.Delta > 0 ? 1.1 : 0.9), 0.05, 20.0);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Source == null) return;
        if (MaskEditing) { _paintingMask = true; PaintMask(e.GetPosition(_canvas)); _canvas.CaptureMouse(); return; }
        _isDragging = true;
        _dragStart = _dragLast = ToImage(e.GetPosition(_canvas));
        _dragRect.Visibility = Visibility.Visible;
        _dragRect.Width = 0;
        _dragRect.Height = 0;
        _canvas.CaptureMouse();
    }

    /// <summary>Mouse position in image pixels (may be outside the image).</summary>
    public event Action<double, double> CursorMoved;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Source != null && CursorMoved != null)
        {
            var p = ToImage(e.GetPosition(_canvas));
            CursorMoved(p.X, p.Y);
        }
        if (_paintingMask) { PaintMask(e.GetPosition(_canvas)); return; }
        if (!_isDragging) return;
        _dragLast = ToImage(e.GetPosition(_canvas));
        UpdateDragRect();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_paintingMask)
        {
            _paintingMask = false;
            _canvas.ReleaseMouseCapture();
            RebuildMaskOverlay();
            MaskPainted?.Invoke();
            return;
        }
        if (!_isDragging) return;
        _isDragging = false;
        _canvas.ReleaseMouseCapture();
        _dragRect.Visibility = Visibility.Collapsed;

        _dragLast = ToImage(e.GetPosition(_canvas));
        var rect = new Rect(
            Math.Min(_dragStart.X, _dragLast.X),
            Math.Min(_dragStart.Y, _dragLast.Y),
            Math.Abs(_dragLast.X - _dragStart.X),
            Math.Abs(_dragLast.Y - _dragStart.Y));

        if (rect.Width < 4 || rect.Height < 4) return;

        if (SelectMode == RegionKind.Train) TrainRegion = rect;
        else SearchRegion = rect;

        RegionChanged?.Invoke(rect);
        Redraw();
    }

    private void UpdateDragRect()
    {
        var r = new Rect(
            Math.Min(_dragStart.X, _dragLast.X),
            Math.Min(_dragStart.Y, _dragLast.Y),
            Math.Abs(_dragLast.X - _dragStart.X),
            Math.Abs(_dragLast.Y - _dragStart.Y));
        SetRect(_dragRect, r);
    }

    /// <summary>Canvas point to image pixel.</summary>
    private Point ToImage(Point canvasPoint) => new(canvasPoint.X / Zoom, canvasPoint.Y / Zoom);

    private sealed class MatchMarkerTag { }
    private sealed class OriginTag { }
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

/// <summary>One entry of the model overlay: position relative to the pattern origin plus normal.</summary>
public sealed class OutlineFeature
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Nx { get; set; }
    public double Ny { get; set; }
}

public enum RegionKind { Train, Search }
