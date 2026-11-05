using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AdvCamera.Controls;

/// <summary>A slider with a title, a value read-out and a reset button.</summary>
public partial class LabeledSlider : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(LabeledSlider), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(LabeledSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(100.0));

    public static readonly DependencyProperty DefaultValueProperty =
        DependencyProperty.Register(nameof(DefaultValue), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(LabeledSlider), new PropertyMetadata(1.0));

    public static readonly DependencyProperty LargeStepProperty =
        DependencyProperty.Register(nameof(LargeStep), typeof(double), typeof(LabeledSlider), new PropertyMetadata(10.0));

    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.Register(nameof(Format), typeof(string), typeof(LabeledSlider), new PropertyMetadata("0", OnDisplayChanged));

    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(LabeledSlider), new PropertyMetadata("", OnDisplayChanged));

    public LabeledSlider()
    {
        InitializeComponent();
        UpdateText();
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double DefaultValue { get => (double)GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double LargeStep { get => (double)GetValue(LargeStepProperty); set => SetValue(LargeStepProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public string Suffix { get => (string)GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }

    private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LabeledSlider)d).UpdateText();

    private void UpdateText()
    {
        if (ValueText != null)
            ValueText.Text = Value.ToString(Format, CultureInfo.CurrentCulture) + Suffix;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Value = DefaultValue;

    private void Slider_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Value = DefaultValue;
}
