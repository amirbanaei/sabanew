using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Core
{
    // =====================================================
    // MDR
    // Phase 1   : زیرساخت مشترک
    // Step 1.6  : اعلان‌ها و Notification
    // Part 1.6.2: کلاس مشترک ارسال و مدیریت اعلان
    // -----------------------------------------------------
    // طبق سند جامع: مرکز اعلان «منبع واحد» همه اعلان‌های عملیاتی است
    // (هیچ ماژولی اعلان موازی نسازد). عملیات: خواندن، تأیید، Snooze،
    // رفتن به منبع. اعلان بحرانی (important) = مودال وسط صفحه + تأیید.
    // -----------------------------------------------------
    // همه‌ی بخش‌های سامانه برای اعلان فقط از این کلاس
    // استفاده کنند.
    //
    // نمونه استفاده (ارسال خودکار از هر Controller):
    //
    //   NotificationService.Send(
    //       managerUserId,
    //       "تعیین به‌عنوان مدیر واحد",
    //       "شما به‌عنوان مدیر واحد «فناوری اطلاعات» تعیین شدید.",
    //       severity:   NotificationService.Severities.Info,
    //       moduleName: AuditLogger.Modules.OrganizationStructure,
    //       entityName: "MDR_OrganizationUnits",
    //       entityId:   unit.OrganizationUnitID,
    //       linkUrl:    "/AG/OrganizationStructure");
    //
    //   important: true  → مودال وسط صفحه که تا «مطالعه کردم» بسته نمی‌شود
    //
    // نکته مهم: متدهای ارسال هیچ‌وقت Exception پرتاب نمی‌کنند؛
    // یعنی اگر ثبت اعلان خطا بدهد، عملیات اصلی خراب نمی‌شود.
    // =====================================================

    public static class NotificationService
    {
        // -------------------------------------------------
        // سطح اعلان (رنگ و آیکن)
        // -------------------------------------------------

        public static class Severities
        {
            public const string Info = "Info";
            public const string Success = "Success";
            public const string Warning = "Warning";
            public const string Danger = "Danger";

            internal static string Normalize(string value)
            {
                if (value == Success || value == Warning || value == Danger)
                {
                    return value;
                }

                return Info;
            }
        }


        // -------------------------------------------------
        // خروجی لیست
        // -------------------------------------------------

        public class NotificationItem
        {
            public long NotificationID { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
            public string Severity { get; set; }
            public bool IsImportant { get; set; }
            public string ModuleName { get; set; }
            public bool HasLink { get; set; }
            public string SenderName { get; set; }
            public string PersianDate { get; set; }
            public string Time { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsRead { get; set; }
            public bool IsAcknowledged { get; set; }
            public DateTime? SnoozedUntil { get; set; }
            public bool IsSnoozed { get; set; }
        }


        // تعداد اعلان‌های هر منبع (برای گروه‌بندی و فیلتر منبع)
        public class SourceCount
        {
            public string ModuleName { get; set; }
            public int Total { get; set; }
            public int Unread { get; set; }
        }


        // =================================================
        // MDR 1.6.2.1 — ارسال به یک کاربر
        // خروجی: شناسه اعلان، یا null اگر ثبت نشد
        // =================================================

        public static long? Send(
            int recipientUserId,
            string title,
            string message = null,
            string severity = Severities.Info,
            bool important = false,
            string moduleName = null,
            string entityName = null,
            object entityId = null,
            string linkUrl = null)
        {
            var ids = SendToMany(
                new[] { recipientUserId },
                title, message, severity, important,
                moduleName, entityName, entityId, linkUrl);

            return ids.Count > 0 ? ids[0] : (long?)null;
        }


        // =================================================
        // MDR 1.6.2.2 — ارسال به چند کاربر (هر نفر یک ردیف)
        // =================================================

        public static List<long> SendToMany(
            IEnumerable<int> recipientUserIds,
            string title,
            string message = null,
            string severity = Severities.Info,
            bool important = false,
            string moduleName = null,
            string entityName = null,
            object entityId = null,
            string linkUrl = null)
        {
            var result = new List<long>();

            try
            {
                if (recipientUserIds == null || string.IsNullOrWhiteSpace(title))
                {
                    return result;
                }

                var recipients = recipientUserIds
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                if (recipients.Count == 0)
                {
                    return result;
                }

                // فقط آدرس داخلی سامانه (جلوگیری از لینک به سایت بیرونی)
                string safeLink = IsLocalUrl(linkUrl) ? Cut(linkUrl, 500) : null;

                using (var db = new MdrContext())
                {
                    var sender = GetCurrentUser(db);
                    DateTime now = DateTime.Now;
                    string persianDate = ToPersianDate(now);

                    var rows = recipients.Select(userId => new MdrNotification
                    {
                        RecipientUserID = userId,

                        Title = Cut(title.Trim(), 200),
                        Message = Cut(Clean(message), 1000),
                        Severity = Severities.Normalize(severity),
                        IsImportant = important,

                        ModuleName = Cut(moduleName, 100),
                        EntityName = Cut(entityName, 100),
                        EntityID = Cut(entityId?.ToString(), 50),
                        LinkUrl = safeLink,

                        SenderUserID = sender.UserID,
                        SenderName = sender.FullName,

                        CreatedAt = now,
                        PersianDate = persianDate,

                        IsRead = false,
                        IsArchived = false
                    }).ToList();

                    db.MDR_Notifications.AddRange(rows);
                    db.SaveChanges();

                    result.AddRange(rows.Select(x => x.NotificationID));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "MDR NotificationService Error: " + ex.Message);
            }

            return result;
        }


        // =================================================
        // MDR 1.6.2.3 — ارسال به مدیر یک واحد سازمانی
        // (از MDR_OrganizationUnits.ManagerUserID)
        // =================================================

        public static long? SendToUnitManager(
            int organizationUnitId,
            string title,
            string message = null,
            string severity = Severities.Info,
            bool important = false,
            string moduleName = null,
            string entityName = null,
            object entityId = null,
            string linkUrl = null)
        {
            try
            {
                int? managerId;

                using (var db = new MdrContext())
                {
                    managerId = db.MDR_OrganizationUnits
                        .Where(x => x.OrganizationUnitID == organizationUnitId)
                        .Select(x => x.ManagerUserID)
                        .FirstOrDefault();
                }

                if (!managerId.HasValue)
                {
                    return null;
                }

                return Send(managerId.Value, title, message, severity, important,
                            moduleName, entityName, entityId, linkUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "MDR NotificationService Error: " + ex.Message);

                return null;
            }
        }


        // =================================================
        // MDR 1.6.2.4 — خواندن (همیشه فقط اعلان‌های خود کاربر)
        // =================================================

        // تعداد خوانده‌نشده‌ها (برای عدد روی زنگوله)
        public static int GetUnreadCount(int userId)
        {
            using (var db = new MdrContext())
            {
                DateTime now = DateTime.Now;

                return db.MDR_Notifications.Count(x =>
                    x.RecipientUserID == userId &&
                    !x.IsArchived &&
                    !x.IsRead &&
                    (x.SnoozedUntil == null || x.SnoozedUntil <= now));
            }
        }


        // آخرین اعلان‌ها (برای لیست کشویی زنگوله)
        // اعلان‌های Snoozeشده تا پایان زمان تعویق در زنگوله نمی‌آیند
        public static List<NotificationItem> GetLatest(int userId, int take = 8)
        {
            using (var db = new MdrContext())
            {
                DateTime now = DateTime.Now;

                return ToItems(
                    db.MDR_Notifications
                        .AsNoTracking()
                        .Where(x => x.RecipientUserID == userId &&
                                    !x.IsArchived &&
                                    (x.SnoozedUntil == null || x.SnoozedUntil <= now))
                        .OrderByDescending(x => x.NotificationID)
                        .Take(take)
                        .ToList());
            }
        }


        // قدیمی‌ترین اعلان مهمِ تأییدنشده (برای مودال وسط صفحه)
        public static NotificationItem GetPendingImportant(int userId)
        {
            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications
                    .AsNoTracking()
                    .Where(x => x.RecipientUserID == userId &&
                                x.IsImportant &&
                                x.AcknowledgedAt == null)
                    .OrderBy(x => x.NotificationID)
                    .FirstOrDefault();

                return row == null
                    ? null
                    : ToItems(new List<MdrNotification> { row }).First();
            }
        }


        // صفحه مرکز اعلان‌ها
        // status = "" همه | "unread" | "read" | "snoozed" | "pending" (مهمِ تأییدنشده)
        // moduleName = منبع (بخش فرستنده)
        public static List<NotificationItem> GetPage(
            int userId,
            int page,
            int pageSize,
            string status,
            string severity,
            string moduleName,
            string search,
            out int total)
        {
            using (var db = new MdrContext())
            {
                DateTime now = DateTime.Now;

                var query = db.MDR_Notifications
                    .AsNoTracking()
                    .Where(x => x.RecipientUserID == userId && !x.IsArchived);

                if (status == "unread")
                {
                    // در حال تعویق جزو «خوانده‌نشده» نیست
                    query = query.Where(x => !x.IsRead &&
                                             (x.SnoozedUntil == null || x.SnoozedUntil <= now));
                }
                else if (status == "read")
                {
                    query = query.Where(x => x.IsRead);
                }
                else if (status == "snoozed")
                {
                    query = query.Where(x => x.SnoozedUntil != null && x.SnoozedUntil > now);
                }
                else if (status == "pending")
                {
                    query = query.Where(x => x.IsImportant && x.AcknowledgedAt == null);
                }

                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    query = moduleName == "System"
                        ? query.Where(x => x.ModuleName == null)
                        : query.Where(x => x.ModuleName == moduleName);
                }

                if (!string.IsNullOrWhiteSpace(severity))
                {
                    query = query.Where(x => x.Severity == severity);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s1 = search.Trim().Replace('ي', 'ی').Replace('ك', 'ک');
                    string s2 = s1.Replace('ی', 'ي').Replace('ک', 'ك');

                    query = query.Where(x =>
                        x.Title.Contains(s1) || x.Title.Contains(s2) ||
                        x.Message.Contains(s1) || x.Message.Contains(s2));
                }

                total = query.Count();

                return ToItems(
                    query
                        .OrderByDescending(x => x.NotificationID)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList());
            }
        }


        // تعداد اعلان‌ها به تفکیک منبع (گروه‌بندی مرکز اعلان)
        // منبع خالی = «System»
        public static List<SourceCount> GetSourceCounts(int userId)
        {
            using (var db = new MdrContext())
            {
                DateTime now = DateTime.Now;

                return db.MDR_Notifications
                    .Where(x => x.RecipientUserID == userId && !x.IsArchived)
                    .GroupBy(x => x.ModuleName)
                    .Select(g => new
                    {
                        ModuleName = g.Key,
                        Total = g.Count(),
                        Unread = g.Count(x => !x.IsRead &&
                                              (x.SnoozedUntil == null || x.SnoozedUntil <= now))
                    })
                    .ToList()
                    .Select(x => new SourceCount
                    {
                        ModuleName = x.ModuleName ?? "System",
                        Total = x.Total,
                        Unread = x.Unread
                    })
                    .OrderByDescending(x => x.Unread)
                    .ThenByDescending(x => x.Total)
                    .ToList();
            }
        }


        // آمار کارت‌های مرکز اعلان
        public static object GetStats(int userId)
        {
            using (var db = new MdrContext())
            {
                DateTime now = DateTime.Now;
                DateTime today = DateTime.Today;

                var mine = db.MDR_Notifications
                    .Where(x => x.RecipientUserID == userId && !x.IsArchived);

                return new
                {
                    unread = mine.Count(x => !x.IsRead &&
                                             (x.SnoozedUntil == null || x.SnoozedUntil <= now)),
                    pending = mine.Count(x => x.IsImportant && x.AcknowledgedAt == null),
                    snoozed = mine.Count(x => x.SnoozedUntil != null && x.SnoozedUntil > now),
                    today = mine.Count(x => x.CreatedAt >= today)
                };
            }
        }


        // =================================================
        // MDR 1.6.2.5 — تغییر وضعیت (فقط روی اعلان خود کاربر)
        // =================================================

        // Snooze (تعویق): تا زمان مشخص از زنگوله پنهان می‌شود
        // اعلان مهمِ تأییدنشده قابل تعویق نیست
        public static bool Snooze(long notificationId, int userId, DateTime until)
        {
            if (until <= DateTime.Now || until > DateTime.Now.AddDays(30))
            {
                return false;
            }

            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications.FirstOrDefault(x =>
                    x.NotificationID == notificationId &&
                    x.RecipientUserID == userId &&
                    !x.IsArchived);

                if (row == null || (row.IsImportant && row.AcknowledgedAt == null))
                {
                    return false;
                }

                row.SnoozedUntil = until;

                // بعد از پایان تعویق دوباره «خوانده‌نشده» دیده شود
                row.IsRead = false;
                row.ReadAt = null;

                db.SaveChanges();

                return true;
            }
        }


        public static bool CancelSnooze(long notificationId, int userId)
        {
            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications.FirstOrDefault(x =>
                    x.NotificationID == notificationId &&
                    x.RecipientUserID == userId);

                if (row == null || row.SnoozedUntil == null || row.SnoozedUntil <= DateTime.Now)
                {
                    return false;
                }

                row.SnoozedUntil = null;
                db.SaveChanges();

                return true;
            }
        }


        // خواندن یک اعلان؛ خروجی: آدرس لینک (یا null)
        public static string MarkRead(long notificationId, int userId)
        {
            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications.FirstOrDefault(x =>
                    x.NotificationID == notificationId &&
                    x.RecipientUserID == userId);

                if (row == null)
                {
                    return null;
                }

                if (!row.IsRead)
                {
                    row.IsRead = true;
                    row.ReadAt = DateTime.Now;
                    db.SaveChanges();
                }

                return row.LinkUrl;
            }
        }


        // خواندن با نتیجه (برای تشخیص تغییر همزمان در تب/دستگاه دیگر)
        // false یعنی اعلان پیدا نشد یا از لیست کاربر حذف شده است
        public static bool TryMarkRead(long notificationId, int userId)
        {
            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications.FirstOrDefault(x =>
                    x.NotificationID == notificationId &&
                    x.RecipientUserID == userId &&
                    !x.IsArchived);

                if (row == null)
                {
                    return false;
                }

                if (!row.IsRead)
                {
                    row.IsRead = true;
                    row.ReadAt = DateTime.Now;
                    db.SaveChanges();
                }

                return true;
            }
        }


        // اعلان‌های در حال تعویق (Snooze) دست نمی‌خورند تا بعد از پایان
        // تعویق با علامت «جدید» برگردند و از چشم کاربر جا نمانند
        public static int MarkAllRead(int userId)
        {
            using (var db = new MdrContext())
            {
                DateTime now = DateTime.Now;

                var rows = db.MDR_Notifications
                    .Where(x => x.RecipientUserID == userId &&
                                !x.IsRead &&
                                !x.IsArchived &&
                                (x.SnoozedUntil == null || x.SnoozedUntil <= now))
                    .ToList();

                foreach (var row in rows)
                {
                    row.IsRead = true;
                    row.ReadAt = now;
                }

                db.SaveChanges();

                return rows.Count;
            }
        }


        // «مطالعه کردم» برای اعلان مهم (خوانده‌شده هم حساب می‌شود)
        public static bool Acknowledge(long notificationId, int userId)
        {
            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications.FirstOrDefault(x =>
                    x.NotificationID == notificationId &&
                    x.RecipientUserID == userId);

                if (row == null)
                {
                    return false;
                }

                DateTime now = DateTime.Now;

                if (row.AcknowledgedAt == null)
                {
                    row.AcknowledgedAt = now;
                }

                if (!row.IsRead)
                {
                    row.IsRead = true;
                    row.ReadAt = now;
                }

                db.SaveChanges();

                return true;
            }
        }


        // برداشتن از لیست کاربر (اعلان مهمِ تأییدنشده برداشته نمی‌شود)
        public static bool Archive(long notificationId, int userId)
        {
            using (var db = new MdrContext())
            {
                var row = db.MDR_Notifications.FirstOrDefault(x =>
                    x.NotificationID == notificationId &&
                    x.RecipientUserID == userId &&
                    !x.IsArchived);

                if (row == null || (row.IsImportant && row.AcknowledgedAt == null))
                {
                    return false;
                }

                row.IsArchived = true;

                if (!row.IsRead)
                {
                    row.IsRead = true;
                    row.ReadAt = DateTime.Now;
                }

                db.SaveChanges();

                return true;
            }
        }


        // =================================================
        // MDR 1.6.2.6 — کاربر جاری
        // همان روش AuditLogger: کوکی CodeMeli → tbUsers
        // =================================================

        public static int? GetCurrentUserId()
        {
            try
            {
                using (var db = new MdrContext())
                {
                    return GetCurrentUser(db).UserID;
                }
            }
            catch
            {
                return null;
            }
        }


        private class CurrentUserInfo
        {
            public int? UserID;
            public string FullName;
        }


        private static CurrentUserInfo GetCurrentUser(MdrContext db)
        {
            var info = new CurrentUserInfo();

            try
            {
                var cookie = HttpContext.Current?.Request?.Cookies[
                    MdrBase64.Base64Encode("CodeMeli")];

                if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
                {
                    return info;
                }

                string nationalCode = MdrBase64.Base64Decode(cookie.Value);

                var user = db.tbUsers
                    .AsNoTracking()
                    .Where(u => u.usr_NationalCode.ToString() == nationalCode)
                    .Select(u => new { u.usr_ID, u.usr_Name, u.usr_Family })
                    .FirstOrDefault();

                if (user != null)
                {
                    info.UserID = user.usr_ID;
                    info.FullName = Cut(Clean((user.usr_Name ?? "") + " " + (user.usr_Family ?? "")), 200);
                }
            }
            catch
            {
            }

            return info;
        }


        // =================================================
        // Helpers
        // =================================================

        private static List<NotificationItem> ToItems(List<MdrNotification> rows)
        {
            return rows.Select(x => new NotificationItem
            {
                NotificationID = x.NotificationID,
                Title = x.Title,
                Message = x.Message,
                Severity = x.Severity,
                IsImportant = x.IsImportant,
                ModuleName = x.ModuleName,
                HasLink = !string.IsNullOrEmpty(x.LinkUrl),
                SenderName = x.SenderName,
                PersianDate = x.PersianDate,
                Time = x.CreatedAt.ToString("HH:mm"),
                CreatedAt = x.CreatedAt,
                IsRead = x.IsRead,
                IsAcknowledged = x.AcknowledgedAt != null,
                SnoozedUntil = x.SnoozedUntil,
                IsSnoozed = x.SnoozedUntil != null && x.SnoozedUntil > DateTime.Now
            }).ToList();
        }


        // آدرس داخلی: با «/» شروع شود ولی «//» یا «/\» نباشد
        public static bool IsLocalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return url.StartsWith("/") &&
                   !url.StartsWith("//") &&
                   !url.StartsWith("/\\");
        }


        private static string ToPersianDate(DateTime date)
        {
            var pc = new PersianCalendar();

            return string.Format(
                "{0:0000}/{1:00}/{2:00}",
                pc.GetYear(date),
                pc.GetMonth(date),
                pc.GetDayOfMonth(date));
        }


        private static string Clean(string value)
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
