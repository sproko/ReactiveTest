using System;
using System.Globalization;

namespace ReactiveTest
{
    public static class Ts
    {
        public static string Timestamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }
}