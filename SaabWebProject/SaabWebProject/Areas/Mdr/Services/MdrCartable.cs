using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 3 | کارتابل یکپارچه (پنل ۴)
    // هر منبع یک Provider است؛ ماژول‌های بعدی (درخواست‌ها، تسک،
    // مکاتبات، …) فقط یک Provider ثبت می‌کنند و به کارتابل اضافه می‌شوند.
    // - Provider ها خطای هم را ایزوله می‌کنند (خرابی یک منبع، بقیه را نمی‌اندازد).
    // - اجرای اقدام همیشه سمت سرور و در همان Provider بررسی مجوز می‌شود.
    // =====================================================

    public class MdrCartableItem
    {
        public string Source { get; set; }        // wf | ntf | mtg
        public string Ref { get; set; }           // شناسه‌ی یکتا: "wf:12"
        public string Title { get; set; }
        public string Subject { get; set; }
        public string Priority { get; set; }      // High | Normal
        public DateTime? DueAt { get; set; }
        public bool Overdue { get; set; }
        public string Url { get; set; }
        public string[] Actions { get; set; }
        public string RowVer { get; set; }
    }

    public interface IMdrCartableProvider
    {
        string Source { get; }
        IList<MdrCartableItem> GetItems(int userId);
        MdrResult Execute(int userId, string itemId, string action, string reason, string rowVer);
    }

    public static class MdrCartable
    {
        public static readonly List<IMdrCartableProvider> Providers = new List<IMdrCartableProvider>
        {
            new WorkflowCartableProvider(),
            new NotificationCartableProvider(),
            new MeetingDecisionCartableProvider(),
            new TaskCartableProvider(),
            new LetterCartableProvider()
        };

        public class Snapshot
        {
            public List<MdrCartableItem> Items = new List<MdrCartableItem>();
            public List<string> FailedSources = new List<string>();
        }

        public static Snapshot Load(int userId)
        {
            var snap = new Snapshot();

            foreach (var p in Providers)
            {
                try { snap.Items.AddRange(p.GetItems(userId)); }
                catch (Exception ex)
                {
                    snap.FailedSources.Add(p.Source);
                    System.Diagnostics.Debug.WriteLine("MDR cartable provider " + p.Source + ": " + ex.Message);
                }
            }

            snap.Items = snap.Items
                .OrderByDescending(i => i.Overdue)
                .ThenBy(i => i.DueAt ?? DateTime.MaxValue)
                .ThenBy(i => i.Title)
                .Take(500).ToList();
            return snap;
        }

        public static MdrResult Execute(int userId, string source, string itemId, string action, string reason, string rowVer)
        {
            var p = Providers.FirstOrDefault(x => x.Source == source);
            if (p == null) return MdrResult.Fail("wf.err.noaction");

            try { return p.Execute(userId, itemId, action, reason, rowVer); }
            catch (Exception)
            {
                AuditLogger.Log(AuditLogger.Actions.Error, "Cartable", "خطای اجرای اقدام کارتابل",
                    newValues: new { source, itemId, action }, isSuccess: false);
                return MdrResult.Fail("wf.err.unexpected");
            }
        }

        internal static long ParseId(string itemId)
        {
            var s = itemId ?? "";
            var i = s.IndexOf(':');
            long v;
            return long.TryParse(i >= 0 ? s.Substring(i + 1) : s, out v) ? v : -1;
        }
    }

    // ---------- وظایف گردش کار (گام جاری به نام کاربر) ----------
    public class WorkflowCartableProvider : IMdrCartableProvider
    {
        public string Source { get { return "wf"; } }

        public IList<MdrCartableItem> GetItems(int userId)
        {
            using (var db = new MdrContext())
            {
                var rows = (from i in db.WorkflowInstances
                            join s in db.WorkflowSteps on i.CurrentStepID equals (int?)s.StepID
                            join d in db.WorkflowDefinitions on i.WorkflowID equals d.WorkflowID
                            where i.Status == "Running" && s.AssigneeType == "User" && s.AssigneeRef == userId
                            select new { i.InstanceID, i.Subject, i.DueAt, i.RowVer, Step = s.Name, StepID = s.StepID, Workflow = d.Name }).ToList();

                var stepIds = rows.Select(r => r.StepID).Distinct().ToList();
                var actions = db.WorkflowTransitions.Where(t => stepIds.Contains(t.FromStepID))
                    .Select(t => new { t.FromStepID, t.ActionKey }).ToList()
                    .GroupBy(t => t.FromStepID).ToDictionary(g => g.Key, g => g.Select(x => x.ActionKey).ToArray());

                var now = DateTime.Now;
                return rows.Select(r => new MdrCartableItem
                {
                    Source = Source,
                    Ref = "wf:" + r.InstanceID,
                    Title = r.Workflow + " — " + r.Step,
                    Subject = r.Subject,
                    DueAt = r.DueAt,
                    Overdue = r.DueAt.HasValue && r.DueAt.Value < now,
                    Priority = r.DueAt.HasValue && r.DueAt.Value < now ? "High" : "Normal",
                    Url = "/Mdr/Workflow/Monitor?id=" + r.InstanceID,
                    Actions = actions.ContainsKey(r.StepID) ? actions[r.StepID] : new string[0],
                    RowVer = Convert.ToBase64String(r.RowVer)
                }).ToList();
        }
        }

        public MdrResult Execute(int userId, string itemId, string action, string reason, string rowVer)
        {
            var id = MdrCartable.ParseId(itemId);
            byte[] ver = null;
            try { if (!string.IsNullOrEmpty(rowVer)) ver = Convert.FromBase64String(rowVer); } catch { }

            // isAdmin=false: فقط مسئول گام می‌تواند از کارتابل اقدام کند
            return MdrWorkflowService.Advance(id, action, userId, false, reason, ver);
        }
    }

    // ---------- اعلان‌های حیاتی نیازمند تأیید (RequiresAcknowledgement) ----------
    public class NotificationCartableProvider : IMdrCartableProvider
    {
        public string Source { get { return "ntf"; } }

        public IList<MdrCartableItem> GetItems(int userId)
        {
            using (var db = new MdrContext())
            {
                var now = DateTime.Now;
                var rows = db.MDR_Notifications
                    .Where(n => n.RecipientUserID == userId && !n.IsArchived && n.IsImportant && n.AcknowledgedAt == null
                                && (n.SnoozedUntil == null || n.SnoozedUntil < now))
                    .OrderByDescending(n => n.CreatedAt).Take(100).ToList();

                return rows.Select(n => new MdrCartableItem
                {
                    Source = Source,
                    Ref = "ntf:" + n.NotificationID,
                    Title = n.Title,
                    Subject = n.Message,
                    Priority = "High",
                    DueAt = null,
                    Overdue = false,
                    Url = NotificationService.IsLocalUrl(n.LinkUrl) ? n.LinkUrl : null,
                    Actions = new[] { "Acknowledge" }
                }).ToList();
            }
        }

        public MdrResult Execute(int userId, string itemId, string action, string reason, string rowVer)
        {
            if (action != "Acknowledge") return MdrResult.Fail("wf.err.noaction");
            var id = MdrCartable.ParseId(itemId);
            return NotificationService.Acknowledge(id, userId) ? MdrResult.Success(id) : MdrResult.Fail("wf.err.notfound");
        }
    }

    // ---------- مصوبات جلسات (فقط‌خواندنی؛ اقدام در پنل جلسات) ----------
    public class MeetingDecisionCartableProvider : IMdrCartableProvider
    {
        public string Source { get { return "mtg"; } }

        private class DecisionRow { public int DecisionID { get; set; } public string DecisionNumber { get; set; } public string DecisionTitle { get; set; } public DateTime? DueDate { get; set; } public int ProgressPercent { get; set; } }

        // جدول مصوبات جلسات (Meeting.tbDecisions) متعلق به پنل جلسات است؛ با SQL خوانده می‌شود تا Area به مدل آن وابسته نباشد.
        // اگر روی این دیتابیس نبود، فهرست خالی برمی‌گردد.
        public IList<MdrCartableItem> GetItems(int userId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var today = DateTime.Today;
                    var rows = db.Database.SqlQuery<DecisionRow>(
                        "SELECT TOP (200) DecisionID, DecisionNumber, DecisionTitle, DueDate, ProgressPercent FROM Meeting.tbDecisions " +
                        "WHERE IsActive = 1 AND ResponsibleUserID = @p0 AND FinalStatusID IS NULL AND ProgressPercent < 100 " +
                        "ORDER BY CASE WHEN DueDate IS NULL THEN 1 ELSE 0 END, DueDate, DecisionID DESC", userId).ToList();   // ترتیب قطعی: سررسید نزدیک‌تر اول، بعد تازه‌ترین

                    return rows.Select(d => new MdrCartableItem
                    {
                        Source = Source,
                        Ref = "mtg:" + d.DecisionID,
                        Title = "مصوبه " + d.DecisionNumber,
                        Subject = d.DecisionTitle + " (" + d.ProgressPercent + "٪)",
                        DueAt = d.DueDate,
                        Overdue = d.DueDate.HasValue && d.DueDate.Value < today,
                        Priority = d.DueDate.HasValue && d.DueDate.Value < today ? "High" : "Normal",
                        Url = "/Setting/Meeting/listjalasat",
                        Actions = new string[0]
                    }).ToList();
                }
            }
            catch
            {
                return new List<MdrCartableItem>();
            }
        }

        public MdrResult Execute(int userId, string itemId, string action, string reason, string rowVer)
        {
            return MdrResult.Fail("wf.err.noaction");
        }
    }
}

namespace SaabWebProject.Areas.Mdr.Services
{
    // ---------- تسک‌های باز به نام کاربر (پنل ۷) ----------
    public class TaskCartableProvider : IMdrCartableProvider
    {
        public string Source { get { return "tsk"; } }

        public IList<MdrCartableItem> GetItems(int userId)
        {
            using (var db = new MdrContext())
            {
                var now = DateTime.Now;
                var rows = db.Tasks.Where(t => t.AssigneeUserID == userId && (t.Status == "Todo" || t.Status == "InProgress"))
                    .OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).Take(200).ToList();

                return rows.Select(t => new MdrCartableItem
                {
                    Source = Source,
                    Ref = "tsk:" + t.TaskID,
                    Title = "تسک",
                    Subject = t.Title,
                    DueAt = t.DueAt,
                    Overdue = t.DueAt.HasValue && t.DueAt.Value < now,
                    Priority = (t.DueAt.HasValue && t.DueAt.Value < now) || t.Priority == "High" || t.Priority == "Urgent" ? "High" : "Normal",
                    Url = "/Mdr/Tasks#id=" + t.TaskID,
                    Actions = t.Status == "Todo" ? new[] { "Start", "Complete" } : new[] { "Complete" },
                    RowVer = Convert.ToBase64String(t.RowVer)
                }).ToList();
            }
        }

        // reason برای Complete همان «نتیجه‌ی کار» است
        public MdrResult Execute(int userId, string itemId, string action, string reason, string rowVer)
        {
            byte[] ver = null;
            try { if (!string.IsNullOrEmpty(rowVer)) ver = Convert.FromBase64String(rowVer); } catch { }
            return MdrTaskService.Act(MdrCartable.ParseId(itemId), action, userId, false, reason, ver);
        }
    }
}

namespace SaabWebProject.Areas.Mdr.Services
{
    // ---------- نامه‌های ثبت‌شده‌ی خطاب به کاربر: خوانده‌نشده یا مهلت‌دار بی‌پاسخ (پنل ۵) ----------
    public class LetterCartableProvider : IMdrCartableProvider
    {
        public string Source { get { return "ltr"; } }

        public IList<MdrCartableItem> GetItems(int userId)
        {
            using (var db = new MdrContext())
            {
                var now = DateTime.Now;
                var rows = (from r in db.LetterRecipients
                            join l in db.Letters on r.LetterID equals l.LetterID
                            where r.UserID == userId && (r.Kind == "To" || r.Kind == "Referral") && l.Status == "Registered"
                                  && (r.ReadAt == null || (l.DueAt != null && r.RepliedAt == null))
                            orderby l.DueAt == null, l.DueAt, l.LetterID descending   // ترتیب قطعی: مهلت‌دارها (قدیمی‌تر اول)، بعد تازه‌ترین
                            select new { l.LetterID, l.Number, l.Subject, l.Urgency, l.Confidentiality, l.DueAt, Unread = r.ReadAt == null, Replied = r.RepliedAt != null })
                           .Take(200).ToList();

                return rows.Select(x =>
                {
                    var overdue = x.DueAt.HasValue && !x.Replied && x.DueAt.Value < now;
                    return new MdrCartableItem
                    {
                        Source = Source,
                        Ref = "ltr:" + x.LetterID,
                        Title = "نامه " + x.Number + (x.Unread ? " (خوانده‌نشده)" : ""),
                        Subject = x.Confidentiality == "Secret" ? "نامه‌ی سری" : x.Subject,
                        DueAt = x.DueAt,
                        Overdue = overdue,
                        Priority = overdue || x.Urgency != "Normal" ? "High" : "Normal",
                        Url = "/Mdr/Letters#id=" + x.LetterID,
                        Actions = new string[0]
                    };
                }).ToList();
            }
        }

        public MdrResult Execute(int userId, string itemId, string action, string reason, string rowVer)
        {
            return MdrResult.Fail("wf.err.noaction");
        }
    }
}
