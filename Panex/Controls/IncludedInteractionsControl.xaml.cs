// File: \Controls\IncludedInteractionsControl.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Panex.Controls
{
    public sealed partial class IncludedInteractionsControl : UserControl
    {
        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register(
                nameof(LabelText),
                typeof(string),
                typeof(IncludedInteractionsControl),
                new PropertyMetadata(string.Empty, OnLabelTextChanged));

        public static readonly DependencyProperty FlyoutTitleProperty =
            DependencyProperty.Register(
                nameof(FlyoutTitle),
                typeof(string),
                typeof(IncludedInteractionsControl),
                new PropertyMetadata("Included Components:"));

        public string LabelText
        {
            get => (string)GetValue(LabelTextProperty);
            set => SetValue(LabelTextProperty, value);
        }

        public string FlyoutTitle
        {
            get => (string)GetValue(FlyoutTitleProperty);
            set => SetValue(FlyoutTitleProperty, value);
        }

        public Visibility HasLabelText =>
            string.IsNullOrWhiteSpace(LabelText) ? Visibility.Collapsed : Visibility.Visible;

        private static void OnLabelTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IncludedInteractionsControl control)
            {
                control.Bindings.Update();
            }
        }

        public IncludedInteractionsControl()
        {
            this.InitializeComponent();
        }
    }
}
