using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System;

namespace MFAAvalonia.Views.UserControls;

public partial class TooltipBlock : UserControl
{
    public TooltipBlock()
    {
        AvaloniaXamlLoader.Load(this);

        DataContext = this;

        Opacity = NormalOpacity;

        PointerEntered += OnPointerEnter;
        PointerExited += OnPointerLeave;
    }

    public static readonly StyledProperty<string> TooltipTextProperty =
        AvaloniaProperty.Register<TooltipBlock, string>(nameof(TooltipText), string.Empty);

    public static readonly StyledProperty<double> TooltipMaxWidthProperty =
        AvaloniaProperty.Register<TooltipBlock, double>(nameof(TooltipMaxWidth), double.MaxValue);

    public static readonly StyledProperty<double> NormalOpacityProperty =
        AvaloniaProperty.Register<TooltipBlock, double>(
            nameof(NormalOpacity),
            0.7,
            coerce: CoerceOpacity);

    public static readonly StyledProperty<double> HoverOpacityProperty =
        AvaloniaProperty.Register<TooltipBlock, double>(
            nameof(HoverOpacity),
            1.0,
            coerce: CoerceOpacity);

    public static readonly StyledProperty<int> InitialShowDelayProperty =
        AvaloniaProperty.Register<TooltipBlock, int>(nameof(InitialShowDelay), 100);

    public string TooltipText
    {
        get => GetValue(TooltipTextProperty);
        set => SetValue(TooltipTextProperty, value);
    }

    public bool TooltipTextNotEmpty => !string.IsNullOrEmpty(TooltipText);

    public double TooltipMaxWidth
    {
        get => GetValue(TooltipMaxWidthProperty);
        set => SetValue(TooltipMaxWidthProperty, value);
    }

    public double NormalOpacity
    {
        get => GetValue(NormalOpacityProperty);
        set => SetValue(NormalOpacityProperty, value);
    }

    public double HoverOpacity
    {
        get => GetValue(HoverOpacityProperty);
        set => SetValue(HoverOpacityProperty, value);
    }

    public int InitialShowDelay
    {
        get => GetValue(InitialShowDelayProperty);
        set => SetValue(InitialShowDelayProperty, value);
    }

    private static double CoerceOpacity(AvaloniaObject d, double baseValue)
    {
        if (d is TooltipBlock { IsPointerOver: false } tooltipBlock)
        {
            // 如果鼠标不在控件上，直接应用NormalOpacity
            if (tooltipBlock.NormalOpacity.Equals(baseValue))
            {
                tooltipBlock.Opacity = baseValue;
            }
        }
        return baseValue;
    }

    private void OnPointerEnter(object sender, PointerEventArgs e)
    {
        AnimateOpacity(HoverOpacity);
    }

    private void OnPointerLeave(object sender, PointerEventArgs e)
    {
        AnimateOpacity(NormalOpacity);
    }

    private async void AnimateOpacity(double targetOpacity)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(InitialShowDelay),
            Children =
            {
                new KeyFrame
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = OpacityProperty,
                            Value = targetOpacity
                        }
                    },
                    Cue = new Cue(1d)
                }
            }
        };

        await animation.RunAsync(this);
    }
}
