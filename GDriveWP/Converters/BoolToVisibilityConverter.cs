using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace GDriveWP
{
    /// <summary>bool -> Visibility. Non-bool / null values count as false. Invert="True" flips the result.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool flag = (value is bool) && (bool)value;
            if (Invert) flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            bool visible = (value is Visibility) && (Visibility)value == Visibility.Visible;
            return Invert ? !visible : visible;
        }
    }
}
