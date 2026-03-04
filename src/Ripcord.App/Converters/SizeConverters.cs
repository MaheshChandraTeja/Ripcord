#nullable enable
using System;
using Microsoft.UI.Xaml.Data;

namespace Ripcord.App.Converters
{
    public sealed class BytesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long bytes = value switch
            {
                null => 0,
                long l => l,
                int i => i,
                double d => (long)d,
                _ => 0
            };
            return FormatBytes(bytes);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return $"{v:0.##} {units[u]}";
        }
    }

    public sealed class PercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is null) return "—";
            double p = value is double d ? d : System.Convert.ToDouble(value);
            if (double.IsNaN(p) || double.IsInfinity(p)) return "—";
            return $"{p:0}%";
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    public sealed class DurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is null) return "—";
            TimeSpan ts = value is TimeSpan t ? t
                : TimeSpan.FromSeconds(System.Convert.ToDouble(value));
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }
}
