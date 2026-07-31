using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Edi.Forms;

public partial class RangeSlider : UserControl
{
    private const double ThumbWidth = 16;

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(
            nameof(Minimum),
            typeof(int),
            typeof(RangeSlider),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.AffectsRender,
                ValuesChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(int),
            typeof(RangeSlider),
            new FrameworkPropertyMetadata(
                100,
                FrameworkPropertyMetadataOptions.AffectsRender,
                ValuesChanged));

    public static readonly DependencyProperty LowerValueProperty =
        DependencyProperty.Register(
            nameof(LowerValue),
            typeof(int),
            typeof(RangeSlider),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                ValuesChanged,
                CoerceLowerValue));

    public static readonly DependencyProperty UpperValueProperty =
        DependencyProperty.Register(
            nameof(UpperValue),
            typeof(int),
            typeof(RangeSlider),
            new FrameworkPropertyMetadata(
                100,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                ValuesChanged,
                CoerceUpperValue));

    public RangeSlider()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int LowerValue
    {
        get => (int)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public int UpperValue
    {
        get => (int)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    private static void ValuesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)dependencyObject;
        slider.CoerceValue(LowerValueProperty);
        slider.CoerceValue(UpperValueProperty);
        slider.UpdateVisuals();
    }

    private static object CoerceLowerValue(
        DependencyObject dependencyObject,
        object baseValue)
    {
        var slider = (RangeSlider)dependencyObject;
        return Math.Clamp(
            (int)baseValue,
            slider.Minimum,
            Math.Max(slider.Minimum, slider.UpperValue));
    }

    private static object CoerceUpperValue(
        DependencyObject dependencyObject,
        object baseValue)
    {
        var slider = (RangeSlider)dependencyObject;
        return Math.Clamp(
            (int)baseValue,
            Math.Min(slider.Maximum, slider.LowerValue),
            slider.Maximum);
    }

    private void LowerThumb_DragDelta(
        object sender,
        DragDeltaEventArgs e)
        => SetCurrentValue(
            LowerValueProperty,
            Math.Min(
                UpperValue,
                ValueFromPosition(
                    Canvas.GetLeft(LowerThumb) + e.HorizontalChange)));

    private void UpperThumb_DragDelta(
        object sender,
        DragDeltaEventArgs e)
        => SetCurrentValue(
            UpperValueProperty,
            Math.Max(
                LowerValue,
                ValueFromPosition(
                    Canvas.GetLeft(UpperThumb) + e.HorizontalChange)));

    private void TrackArea_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb)
            return;

        var value = ValueFromPosition(
            e.GetPosition(TrackArea).X - ThumbWidth / 2);
        if (Math.Abs(value - LowerValue)
            <= Math.Abs(value - UpperValue))
        {
            SetCurrentValue(
                LowerValueProperty,
                Math.Min(value, UpperValue));
        }
        else
        {
            SetCurrentValue(
                UpperValueProperty,
                Math.Max(value, LowerValue));
        }
    }

    private int ValueFromPosition(double position)
    {
        var length = Math.Max(1, TrackArea.ActualWidth - ThumbWidth);
        var ratio = Math.Clamp(position / length, 0d, 1d);
        return (int)Math.Round(
            Minimum + (Maximum - Minimum) * ratio,
            MidpointRounding.AwayFromZero);
    }

    private void TrackArea_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
        => UpdateVisuals();

    private void UpdateVisuals()
    {
        if (!IsLoaded
            || LowerThumb is null
            || UpperThumb is null
            || Maximum <= Minimum)
        {
            return;
        }

        var length = Math.Max(0, TrackArea.ActualWidth - ThumbWidth);
        var lowerPosition =
            (LowerValue - Minimum) / (double)(Maximum - Minimum) * length;
        var upperPosition =
            (UpperValue - Minimum) / (double)(Maximum - Minimum) * length;

        Canvas.SetLeft(LowerThumb, lowerPosition);
        Canvas.SetLeft(UpperThumb, upperPosition);
        Canvas.SetLeft(SelectedTrack, lowerPosition + ThumbWidth / 2);
        SelectedTrack.Width = Math.Max(0, upperPosition - lowerPosition);
    }
}
