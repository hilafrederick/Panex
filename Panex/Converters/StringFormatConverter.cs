// File: \Converters\StringFormatConverter.cs
using Microsoft.UI.Xaml.Data;
using System;

namespace Panex.Converters
{
    public class StringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null) return string.Empty;
            if (parameter is string formatString)
            {
                return string.Format(formatString, value);
            }
            return value.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}