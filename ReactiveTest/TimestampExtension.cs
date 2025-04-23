using System;
using System.Globalization;

namespace ReactiveTest
{
    public static class TimestampExtension
    {
        public static string Pretty(this DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
    }
}