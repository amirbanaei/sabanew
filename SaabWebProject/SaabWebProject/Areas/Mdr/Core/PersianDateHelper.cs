using System;

namespace SaabWebProject.Areas.Mdr.Core
{
    public static class PersianDateHelper
    {
        /// <summary>
        /// تبدیل تاریخ میلادی به تاریخ شمسی
        /// خروجی: yyyy/MM/dd
        /// </summary>
        public static string ToPersianDate(DateTime date)
        {
            var persianCalendar =
                new System.Globalization.PersianCalendar();

            return
                persianCalendar.GetYear(date).ToString("0000") +
                "/" +
                persianCalendar.GetMonth(date).ToString("00") +
                "/" +
                persianCalendar.GetDayOfMonth(date).ToString("00");
        }


        /// <summary>
        /// تبدیل تاریخ nullable میلادی به تاریخ شمسی
        /// اگر تاریخ null باشد، رشته خالی برمی‌گرداند.
        /// </summary>
        public static string ToPersianDate(DateTime? date)
        {
            if (!date.HasValue)
                return "";

            return ToPersianDate(date.Value);
        }
    }
}