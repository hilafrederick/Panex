using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace Panex.Controls
{
    public sealed partial class PhysicsDataTableControl : UserControl
    {
        public static readonly DependencyProperty HeadersSourceProperty =
            DependencyProperty.Register(
                nameof(HeadersSource),
                typeof(object),
                typeof(PhysicsDataTableControl),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(object),
                typeof(PhysicsDataTableControl),
                new PropertyMetadata(null));

        public static readonly DependencyProperty CopyCommandProperty =
            DependencyProperty.Register(
                nameof(CopyCommand),
                typeof(ICommand),
                typeof(PhysicsDataTableControl),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ShowEnergyGridConfigProperty =
            DependencyProperty.Register(
                nameof(ShowEnergyGridConfig),
                typeof(bool),
                typeof(PhysicsDataTableControl),
                new PropertyMetadata(true));

        public object? HeadersSource
        {
            get => GetValue(HeadersSourceProperty);
            set => SetValue(HeadersSourceProperty, value);
        }

        public object? ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public ICommand? CopyCommand
        {
            get => (ICommand?)GetValue(CopyCommandProperty);
            set => SetValue(CopyCommandProperty, value);
        }

        public bool ShowEnergyGridConfig
        {
            get => (bool)GetValue(ShowEnergyGridConfigProperty);
            set => SetValue(ShowEnergyGridConfigProperty, value);
        }

        public PhysicsDataTableControl()
        {
            this.InitializeComponent();
        }
    }
}
