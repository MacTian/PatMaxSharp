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
    private Image _image = null!;
    private ScaleTransform _scale = null!;
    private Rectangle _trainRect = null!;
    private Rectangle _searchRect = null!;
    private Rectangle _dragRect = null!;
    private Point _dragStart;
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

    public event Action<Rect?>? RegionChanged;

    private void OnSourceChanged() => Setup();

    public void Setup()
    {
        if (_canvas != null) return;
        _canvas = canvas;

        _scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(_scale);

        _image = new Image
        {
            Source = Source,
            Stretch = Stretch.Uniform,
            RenderTransform = group,
            RenderTransformOrigin = new Point(0, 0)
        };
        _canvas.Children.Add(_image);

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
            _image.Source = Source;
            var bs = Source as System.Windows.Media.Imaging.BitmapSource;
            if (bs != null)
            {
                ImageWidth = bs.PixelWidth;
                ImageHeight = bs.PixelHeight;
                _image.Width = bs.PixelWidth;
                _image.Height = bs.PixelHeight;
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

    // ---- Mouse handlers (wired in XAML) ----
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Source == null) return;
        double factor = e.Delta > 0 ? 1.1 : 0.9;
        Zoom = Math.Clamp(Zoom * factor, 0.1, 20);
        ApplyZoom();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Source == null) return;
        _isDragging = true;
        _dragStart = e.GetPosition(_canvas);
        _dragRect.Visibility = Visibility.Visible;
        _dragRect.Width = 0; _dragRect.Height = 0;
        Canvas.SetLeft(_dragRect, _dragStart.X);
        Canvas.SetTop(_dragRect, _dragStart.Y);
        _canvas.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var p = e.GetPosition(_canvas);
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

        var p = e.GetPosition(_canvas);
        var rect = new Rect(
            Math.Min(_dragStart.X, p.X) / Zoom,
            Math.Min(_dragStart.Y, p.Y) / Zoom,
            Math.Abs(p.X - _dragStart.X) / Zoom,
            Math.Abs(p.Y - _dragStart.Y) / Zoom);

        if (rect.Width < 4 || rect.Height < 4) return;

        if (SelectMode == RegionKind.Train) TrainRegion = rect;
        else SearchRegion = rect;
        RegionChanged?.Invoke(rect);
        RedrawRegions();
    }

    private void ApplyZoom()
    {
        _scale.ScaleX = Zoom;
        _scale.ScaleY = Zoom;
        if (_image.Source is System.Windows.Media.Imaging.BitmapSource bs)
        {
            _image.Width = bs.PixelWidth;
            _image.Height = bs.PixelHeight;
        }
        RedrawRegions();
    }

    private void RedrawRegions()
    {
        if (_trainRect == null) return;

        if (TrainRegion is Rect tr)
        {
            _trainRect.Visibility = Visibility.Visible;
            _trainRect.Width = tr.Width * Zoom; _trainRect.Height = tr.Height * Zoom;
            Canvas.SetLeft(_trainRect, tr.X * Zoom); Canvas.SetTop(_trainRect, tr.Y * Zoom);
        }
        else _trainRect.Visibility = Visibility.Collapsed;

        if (SearchRegion is Rect sr)
        {
            _searchRect.Visibility = Visibility.Visible;
            _searchRect.Width = sr.Width * Zoom; _searchRect.Height = sr.Height * Zoom;
            Canvas.SetLeft(_searchRect, sr.X * Zoom); Canvas.SetTop(_searchRect, sr.Y * Zoom);
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
            var pos = new Point(m.X * Zoom, m.Y * Zoom);
            double s = m.Size * Zoom * m.Scale;
            double a = m.AngleDeg * Math.PI / 180.0;

            var crossH = new Line { X1 = pos.X - 8 * Zoom, Y1 = pos.Y, X2 = pos.X + 8 * Zoom, Y2 = pos.Y, Stroke = brush, StrokeThickness = thickness };
            var crossV = new Line { X1 = pos.X, Y1 = pos.Y - 8 * Zoom, X2 = pos.X, Y2 = pos.Y + 8 * Zoom, Stroke = brush, StrokeThickness = thickness };
            var arrow = new Line { X1 = pos.X, Y1 = pos.Y, X2 = pos.X + Math.Cos(a) * s, Y2 = pos.Y + Math.Sin(a) * s, Stroke = brush, StrokeThickness = thickness };
            var box = new Rectangle
            {
                Width = s * 2, Height = s * 2,
                Stroke = brush, StrokeThickness = thickness,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(box, pos.X - s);
            Canvas.SetTop(box, pos.Y - s);

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

public enum RegionKind { Train, Search }
