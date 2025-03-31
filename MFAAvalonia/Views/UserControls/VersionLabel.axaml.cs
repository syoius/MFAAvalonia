using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MFAAvalonia.Views.UserControls
{
    public partial class VersionLabel : UserControl
    {
        public static readonly StyledProperty<string> TitleProperty = 
            AvaloniaProperty.Register<VersionLabel, string>(nameof(Title));

        public static readonly StyledProperty<string> VersionProperty = 
            AvaloniaProperty.Register<VersionLabel, string>(nameof(Version));

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Version
        {
            get => GetValue(VersionProperty);
            set => SetValue(VersionProperty, value);
        }

        public VersionLabel()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
} 