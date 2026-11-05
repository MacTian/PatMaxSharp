using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PatMaxSharp.UI.Controls;

/// <summary>
/// Image viewer with zoom/pan and rectangular region selection.
/// Region selection is used for the training region and the search region.
/// </summary>
public sealed class ImageRegionSelector : Control
{
    private Canvas _canvas = null!;
    private Image _image = null!;
    private ScaleTransform _scale = null!;
    private TranslateTransform _translate = null!;
    private Rectangle _trainRect = null!;
    private Rectangle _searchRect = null!;
    private Rectangle _dragRect = null!;
    private Point _dragStart;
    private bool _isDragging;

    static ImageRegionSelector()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageRegionSelector),
            new FrameworkPropertyMetadata(typeof(ImageRegionSelector)));
    }

    public ImageRegionSelector()
    {
        Loaded += (_, _) => Setup();
    }

    #region Dependency properties
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(ImageRegionSelector),
            new PropertyMetadata(null, (d, e) => ((ImageRegionSelector)d).Setup()));

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

    /// <summary>Which region the user is drawing: Train / Search.</summary>
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

    public void Setup()
    {
        if (_canvas != null) return;
        _canvas = new Canvas { Background = Brushes.Black };
        _scale = new ScaleTransform(1, 1);
        _translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_translate);

        _image = new Image
        {
            Source = Source,
            Stretch = Stretch.Uniform,
            RenderTransform = group,
            RenderTransformOrigin = new Point(0, 0)
        };
        _canvas.Children.Add(_image);

        _trainRect = new Rectangle
        {
            Stroke = Brushes.Lime, StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)),
            Visibility = Visibility.Collapsed
        };
        _searchRect = new Rectangle
        {
            Stroke = Brushes.Cyan, StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 255)),
            Visibility = Visibility.Collapsed
        };
        _dragRect = new Rectangle
        {
            Stroke = Brushes.Yellow, StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_trainRect);
        _canvas.Children.Add(_searchRect);
        _canvas.Children.Add(_dragRect);

        _canvas.MouseWheel += OnMouseWheel;
        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;

        if (Source != null)
        {
            _image.Source = Source;
            var w = (Source as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth ?? 0;
            var h = (Source as System.Windows.Media.Imaging.BitmapSource)?.PixelHeight ?? 0;
            ImageWidth = w; ImageHeight = h;
        }
        RedrawRegions();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Source == null) return;
        double factor = e.Delta > 0 ? 1.1 : 0.9;
        Zoom = Math.Clamp(Zoom * factor, 0.1, 20);
        ApplyTransform();
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
        double x = Math.Min(_dragStart.X, p.X), y = Math.Min(_dragStart.Y, p.Y);
        _dragRect.Width = Math.Abs(p.X - _dragStart.X);
        _dragRect.Height = Math.Abs(p.Y - _dragStart.Y);
        Canvas.SetLeft(_dragRect, x); Canvas.SetTop(_dragRect, y);
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

    private void ApplyTransform()
    {
        _scale.ScaleX = Zoom; _scale.ScaleY = Zoom;
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
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Setup();
    }
}

public enum RegionKind { Train, Search }
