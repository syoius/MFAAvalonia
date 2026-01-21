using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace MFAAvalonia.Views.Windows;

public partial class RecordingOverlayView : Window
{
    public RecordingOverlayView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}

