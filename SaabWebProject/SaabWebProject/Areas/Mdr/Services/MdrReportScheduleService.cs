using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 6 | زمان‌بندی گزارش دوره‌ای
    // - هر کاربر برای خودش حداکثر ۱۰ زمان‌بندی (روزانه/هفتگی/ماهانه) دارد.
    // - اجرا از همان زمان‌سنج SLA (MdrSlaScheduler) انجام می‌شود؛ نتیجه یک «اعلان با لینک گزارش» است.
    //   هیچ فایلی ساخته یا ایمیل نمی‌شود؛ کاربر با لینک وارد صفحه می‌شود و همان scope/ACL لحظه‌ی خودش را می‌بیند
    //   (پس دسترسی از دست‌رفته‌ی کاربر هرگز داده‌ی کهنه نشت نمی‌دهد).
    // - اگر کاربر دیگر به صفحه‌ی گزارش دسترسی ندارد، زمان‌بندی غیرفعال می‌شود.
    // =====================================================
    public static class MdrReportScheduleService
    {
        public const int MaxPerUser = 10;
        private static readonly string[] Freqs = { "Daily", "Weekly", "Monthly" };
        private static int _running;

        public static DateTime Next(DateTime from, string frequency)
        {
            switch (frequency)
            {
                case "Daily": return from.AddDays(1);
                case "Weekly": return from.AddDays(7);
                default: return from.AddMonths(1);
            }
        }

        public static List<MdrReportSchedule> List(int uid)
        {
            using (var db = new MdrContext())
                return db.ReportSchedules.Where(s => s.UserID == uid).OrderBy(s => s.ScheduleID).ToList();
        }

        public static MdrResult Create(int uid, bool personal, string frequency, int rangeDays)
        {
            if (!Freqs.Contains(frequency ?? "")) return MdrResult.Fail("rep.err.freq");
            if (rangeDays < 1 || rangeDays > MdrReportService.MaxRangeDays) return MdrResult.Fail("rep.err.range");
            if (!PermissionHelper.HasAccess(uid, "MDR.Reports")) return MdrResult.Fail("tsk.err.notallowed");

            try
            {
                using (var db = new MdrContext())
                {
                    if (db.ReportSchedules.Count(s => s.UserID == uid) >= MaxPerUser) return MdrResult.Fail("rep.err.limit");
                    var e = new MdrReportSchedule
                    {
                        UserID = uid, ReportKey = "summary", Personal = personal, Frequency = frequency, RangeDays = rangeDays,
                        NextRunAt = Next(DateTime.Now, frequency), IsActive = true, CreatedAt = DateTime.Now
                    };
                    db.ReportSchedules.Add(e);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, "Reports", "زمان‌بندی گزارش", entityName: "MDR_ReportSchedules", entityId: e.ScheduleID,
                        newValues: new { frequency, rangeDays, personal });
                    return MdrResult.Success(e.ScheduleID);
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditLogger.Actions.Error, "Reports", "خطای زمان‌بندی گزارش", newValues: new { ex.Message }, isSuccess: false);
                return MdrResult.Fail("wf.err.unexpected");
            }
        }

        // فقط مالک می‌تواند حذف کند؛ وجود زمان‌بندی دیگران لو نمی‌رود (همان پیام «پیدا نشد»)
        public static MdrResult Delete(int uid, long id)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var e = db.ReportSchedules.FirstOrDefault(s => s.ScheduleID == id && s.UserID == uid);
                    if (e == null) return MdrResult.Fail("wf.err.notfound");
                    db.ReportSchedules.Remove(e);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Delete, "Reports", "حذف زمان‌بندی گزارش", entityName: "MDR_ReportSchedules", entityId: id);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception) { return MdrResult.Fail("wf.err.unexpected"); }
        }

        // خروجی: تعداد اعلان‌های ارسال‌شده
        public static int RunDue(DateTime now)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return 0;
            try
            {
                var sent = 0;
                using (var db = new MdrContext())
                {
                    var due = db.ReportSchedules.Where(s => s.IsActive && s.NextRunAt <= now).OrderBy(s => s.NextRunAt).Take(200).ToList();
                    foreach (var s in due)
                    {
                        try
                        {
                            if (!PermissionHelper.HasAccess(s.UserID, "MDR.Reports")) { s.IsActive = false; db.SaveChanges(); continue; }

                            var to = now.Date; var from = to.AddDays(-(s.RangeDays - 1));
                            var o = MdrReportService.Overview(s.UserID, from, to, s.Personal);
                            NotificationService.Send(s.UserID, "گزارش دوره‌ای: خلاصه‌ی عملیاتی", Summary(o),
                                moduleName: "Reports", entityName: "MDR_ReportSchedules", entityId: s.ScheduleID,
                                linkUrl: "/Mdr/Reports#from=" + from.ToString("yyyy-MM-dd") + "&to=" + to.ToString("yyyy-MM-dd") + "&scope=" + (s.Personal ? "mine" : "all"));
                            sent++;
                        }
                        catch (Exception ex)
                        {
                            AuditLogger.Log(AuditLogger.Actions.Error, "Reports", "خطای اجرای زمان‌بندی گزارش", entityName: "MDR_ReportSchedules", entityId: s.ScheduleID,
                                newValues: new { ex.Message }, isSuccess: false);
                        }
                        // موفق یا ناموفق، نوبت بعدی جلو می‌رود تا حلقه‌ی بی‌پایان نشود
                        s.LastRunAt = now;
                        s.NextRunAt = Next(now, s.Frequency);
                        db.SaveChanges();
                    }
                }
                return sent;
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }

        private static string Summary(ReportOverview o)
        {
            var parts = new List<string>();
            Func<string, string, string, bool> add = (mod, key, label) =>
            {
                Dictionary<string, decimal?> m; decimal? v;
                if (o.Kpi.TryGetValue(mod, out m) && m.TryGetValue(key, out v) && v.HasValue) { parts.Add(label + " " + v.Value.ToString("0.##")); return true; }
                return false;
            };
            add("req", "open", "درخواست باز");
            add("tsk", "overdue", "تسک معوق");
            add("ltr", "noReply", "نامه‌ی بی‌پاسخ");
            add("wf", "overdue", "فرایند دارای تأخیر");
            return parts.Count == 0 ? "برای دیدن جزئیات وارد گزارش شوید." : string.Join(" · ", parts);
        }
    }
}
