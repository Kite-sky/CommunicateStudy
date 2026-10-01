using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CommunicateDemo.Converters
{
    /// <summary>
    /// bool → Brush：true=自己发送(蓝色), false=对方发来(深灰色)
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isSelf && isSelf ? new SolidColorBrush(Colors.DodgerBlue) : new SolidColorBrush(Colors.DarkSlateGray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
