using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace DesktopSnap
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Inverse { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool val = value is bool b && b;
            if (Inverse) val = !val;
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class PinnedColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isPinned = value is bool b && b;
            if (isPinned)
            {
                return Application.Current.Resources["SystemAccentColor"];
            }
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255)); // Semi-transparent white
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class PinTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isPinned = value is bool b && b;
            return isPinned ? I18n.Instance.Unpin : I18n.Instance.PinToTop;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
