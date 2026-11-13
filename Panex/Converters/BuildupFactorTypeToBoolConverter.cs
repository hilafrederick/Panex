// File: \Converters\BuildupFactorTypeToBoolConverter.cs
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Panex
{
    public class BuildupFactorTypeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || parameter == null)
                return false;

            string checkValue = value.ToString() ?? string.Empty;
            string targetValue = parameter.ToString() ?? string.Empty;

            return checkValue.Equals(targetValue, StringComparison.InvariantCultureIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            bool isChecked = (bool)value;
            if (isChecked)
            {
                // Fixes the WinUI 3 XAML Compiler Boxing Crash
                return Enum.Parse(typeof(Panex.Models.BuildupFactorType), parameter.ToString()!);
            }

            return DependencyProperty.UnsetValue;
        }
    }
}