using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace BlendHub.Converters
{
    public class GroupHeaderMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string key)
            {
                if (key.StartsWith("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return new Thickness(0, 8, 0, 0);
                }
            }
            return new Thickness(0, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
