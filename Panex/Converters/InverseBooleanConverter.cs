using System;
using Microsoft.UI.Xaml.Data;

namespace Panex
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b) return !b;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // Avoids infinite recursive loop issues in some WinUI 3 edge cases
            if (value is bool b) return !b;
            return false;
        }
    }
}