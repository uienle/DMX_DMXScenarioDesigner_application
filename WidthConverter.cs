using System;
using System.Globalization;
using System.Windows.Data;

namespace DMXScenarioDesigner
{
    // Lớp này giúp nhân con số Mili giây với một tỷ lệ (0.2) để ra chiều dài Pixel
    public class WidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double ms)
            {
                // Lấy tỷ lệ từ tham số truyền vào (ví dụ: 0.2)
                double factor = double.Parse(parameter.ToString());
                return ms * factor;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}