using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;
using Avalonia;

namespace MFAAvalonia.Views.Pages;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        if (!Design.IsDesignMode)
        {
            DataContext = Instances.MonitorViewModel;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!Design.IsDesignMode)
        {
            Instances.MonitorViewModel.Activate();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (!Design.IsDesignMode)
        {
            Instances.MonitorViewModel.Deactivate();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && !Design.IsDesignMode)
        {
            var isVisible = change.NewValue is true;
            if (isVisible)
            {
                Instances.MonitorViewModel.Activate();
            }
            else
            {
                Instances.MonitorViewModel.Deactivate();
            }
        }
    }
}
