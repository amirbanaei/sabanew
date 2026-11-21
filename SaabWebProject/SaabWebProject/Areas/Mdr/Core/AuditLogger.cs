using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Web;
using Newtonsoft.Json;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Core
{
    // =====================================================
    // MDR
    // Phase 1   : زیرساخت مشترک
    // Step 1.4  : Audit Log — ثبت تاریخچه و رویدادهای سیستم
    // Part 1.4.2: کلاس کمکی ثبت لاگ
    // -----------------------------------------------------
    // نمونه استفاده:
    //
    //   AuditLogger.Log(
    //       AuditLogger.Actions.Create,
    //       AuditLogger.Modules.OrganizationStructure,
    //       "ثبت واحد سازمانی جدید",
    //       entityName:  "MDR_OrganizationUnits",
    //       entityId:    unit.OrganizationUnitID,
    //       entityTitle: unit.UnitName,
    //       newValues:   new { unit.UnitName, unit.UnitType }
    //   );
    //
    // نکته مهم: این کلاس هیچ‌وقت Exception پرتاب نمی‌کند؛
    // یعنی اگر ثبت لاگ به هر دلیلی خطا بدهد، عملیات اصلی
    // (ثبت / ویرایش / حذف) خراب نمی‌شود.
    // =====================================================

    public static class AuditLogger
    {
        // -------------------------------------------------
        // انواع عملیات
        // -------------------------------------------------

        public static class Actions
        {
            public const string Create = "Create";
            public const string Update = "Update";
            public const string Delete = "Delete";
            public const string View = "View";
            public const string Login = "Login";
            public const string Logout = "Logout";
            public const string Export = "Export";
            public const string Error = "Error";
        }


        // -------------------------------------------------
        // نام ماژول‌ها
        // با اضافه شدن ماژول‌های جدید، اینجا اضافه کنید
        // -------------------------------------------------

        public static class Modules
        {
            public const string OrganizationStructure = "OrganizationStructure";
            public const string Users = "Users";
            public const string Roles = "Roles";
            public const string Meetings = "Meetings";
            public const string Tasks = "Tasks";
            public const string Requests = "Requests";
        }


        // -------------------------------------------------
        // ثبت لاگ
        // -------------------------------------------------

        public static void Log(
            string actionType,
            string moduleName,
            string description,
            string entityName = null,
            object entityId = null,
            string entityTitle = null,
            object oldValues = null,
            object newValues = null,
            bool isSuccess = true)
        {
            MdrAuditLog log;

            try
            {
                log = new MdrAuditLog
                {
                    ActionType = actionType,
                    ModuleName = moduleName,
                    Description = description,

                    EntityName = entityName,
                    EntityID = entityId?.ToString(),
                    EntityTitle = entityTitle,

                    OldValues = ToJson(oldValues),
                    NewValues = ToJson(newValues),

                    IsSuccess = isSuccess
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "MDR AuditLogger Error: " + ex.Message
                );

                return;
            }

            SaveBatch(new List<MdrAuditLog> { log });
        }


        // -------------------------------------------------
        // MDR 1.4.5
        // ذخیره چند لاگ با هم (برای Auto Audit کل پروژه)
        // اطلاعات مشترک (زمان، کاربر، IP، مرورگر، صفحه) را
        // خودش پر می‌کند؛ فقط فیلدهای عملیات را بدهید.
        // -------------------------------------------------

        // transactionContext: فقط برای سازگاری امضا نگه داشته شده و نادیده گرفته می‌شود (لاگ همیشه با اتصال مستقل ثبت می‌شود).
        public static void SaveBatch(
            IList<MdrAuditLog> logs,
            object transactionContext = null)
        {
            if (logs == null || logs.Count == 0)
            {
                return;
            }

            try
            {
                // Suppress: اگر TransactionScope بیرونی باشد، لاگ وارد آن نشود
                using (new System.Transactions.TransactionScope(
                    System.Transactions.TransactionScopeOption.Suppress))
                using (var db = new MdrContext())
                {
                    FillAndAdd(db, logs);
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                // لاگ نباید عملیات اصلی را خراب کند
                System.Diagnostics.Debug.WriteLine(
                    "MDR AuditLogger Error: " + ex.Message
                );
            }
        }


        // اطلاعات مشترک (زمان، کاربر، IP، مرورگر، صفحه)
        private static void FillAndAdd(
            MdrContext db,
            IList<MdrAuditLog> logs)
        {
            var httpContext = HttpContext.Current;

            var currentUser =
                GetCurrentUser(db, httpContext);

            DateTime now = DateTime.Now;
            string persianDate = ToPersianDate(now);

            string userFullName = currentUser != null
                ? CleanSpaces((currentUser.usr_Name ?? "") + " " +
                              (currentUser.usr_Family ?? ""))
                : null;

            string ipAddress = Cut(GetIpAddress(httpContext), 50);
            string userAgent = Cut(httpContext?.Request?.UserAgent, 500);
            string pageUrl = Cut(GetPageUrl(httpContext), 500);

            foreach (var log in logs)
            {
                log.CreatedAt = now;
                log.PersianDate = persianDate;

                log.UserID = currentUser?.usr_ID;
                log.UserFullName = userFullName;

                log.ActionType = Cut(log.ActionType ?? Actions.Update, 30);
                log.ModuleName = Cut(log.ModuleName ?? "Unknown", 100);
                log.Description = Cut(log.Description, 1000);

                log.EntityName = Cut(log.EntityName, 100);
                log.EntityID = Cut(log.EntityID, 50);
                log.EntityTitle = Cut(log.EntityTitle, 300);

                log.IPAddress = ipAddress;
                log.UserAgent = userAgent;
                log.PageUrl = pageUrl;

                db.MDR_AuditLogs.Add(log);
            }
        }


        // -------------------------------------------------
        // تبدیل به JSON (برای Auto Audit هم استفاده می‌شود)
        // -------------------------------------------------

        public static string ToJsonPublic(object value)
        {
            return ToJson(value);
        }


        // -------------------------------------------------
        // کاربر جاری
        // همان روشی که بقیه پروژه استفاده می‌کند:
        // کوکی CodeMeli → کد ملی → tbUsers
        // -------------------------------------------------

        private static MdrUser GetCurrentUser(
            MdrContext db,
            HttpContext httpContext)
        {
            try
            {
                var cookie =
                    httpContext?.Request?.Cookies[
                        MdrBase64.Base64Encode("CodeMeli")
                    ];

                if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
                {
                    return null;
                }

                string nationalCode =
                    MdrBase64.Base64Decode(cookie.Value);

                return db.tbUsers
                    .AsNoTracking()
                    .FirstOrDefault(
                        u => u.usr_NationalCode.ToString() == nationalCode
                    );
            }
            catch
            {
                return null;
            }
        }


        // -------------------------------------------------
        // Helpers
        // -------------------------------------------------

        private static string ToPersianDate(DateTime date)
        {
            var pc = new PersianCalendar();

            return string.Format(
                "{0:0000}/{1:00}/{2:00}",
                pc.GetYear(date),
                pc.GetMonth(date),
                pc.GetDayOfMonth(date)
            );
        }


        private static string ToJson(object value)
        {
            if (value == null)
            {
                return null;
            }

            var text = value as string;

            if (text != null)
            {
                return text;
            }

            return JsonConvert.SerializeObject(
                value,
                new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    NullValueHandling = NullValueHandling.Include
                }
            );
        }


        private static string GetIpAddress(HttpContext httpContext)
        {
            var request = httpContext?.Request;

            if (request == null)
            {
                return null;
            }

            string forwarded =
                request.ServerVariables["HTTP_X_FORWARDED_FOR"];

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded.Split(',')[0].Trim();
            }

            return request.UserHostAddress;
        }


        private static string GetPageUrl(HttpContext httpContext)
        {
            var request = httpContext?.Request;

            if (request == null)
            {
                return null;
            }

            // صفحه‌ای که کاربر از آن درخواست را فرستاده
            // (برای درخواست‌های Ajax مفیدتر از آدرس خود Action است)
            return request.UrlReferrer != null
                ? request.UrlReferrer.PathAndQuery
                : request.RawUrl;
        }


        private static string CleanSpaces(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return System.Text.RegularExpressions.Regex
                .Replace(value, @"\s+", " ")
                .Trim();
        }


        private static string Cut(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }
    }
}
