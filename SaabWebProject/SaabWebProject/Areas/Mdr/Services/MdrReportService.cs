using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Infrastructure;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 6 | گزارش و تحلیل (پنل ۱۱)
    // - فقط‌خواندنی؛ هیچ داده‌ای کپی نمی‌شود و گزارش «همیشه زنده» است.
    // - هر ماژول با همان قاعده‌ی دسترسی جستجو (MdrSearchService.ScopeFor) خوانده می‌شود:
    //   گزارش هرگز بیشتر از آنچه کاربر در خود ماژول می‌بیند نشان نمی‌دهد؛ ماژولی که کاربر به صفحه‌اش دسترسی ندارد «Skipped» است.
    //   personal = گزارش شخصی (حتی مدیر فقط موارد خودش)؛ دید مدیر ماژول فقط با personal = false.
    // - بازه‌ی گزارش حداکثر ۳۶۶ روز؛ drill-down حداکثر ۱۰۰ ردیف؛ Export حداکثر ۵۰۰۰ ردیف و فقط با دسترسی مستقل MDR.Reports.Export.
    // - تحلیل فرایند: زمان هر مرحله از رویدادهای Enter/Complete/Terminate تاریخچه‌ی گردش کار محاسبه می‌شود.
    // =====================================================

    public class NameCount { public string Name { get; set; } public int Count { get; set; } }
    public class TrendPoint { public string Label { get; set; } public int Created { get; set; } public int Closed { get; set; } }

    public class ReportOverview
    {
        public string From { get; set; }
        public string To { get; set; }
        public bool Personal { get; set; }
        public long Ms { get; set; }
        public List<string> Skipped { get; set; }
        public List<string> Warnings { get; set; }
        public Dictionary<string, Dictionary<string, decimal?>> Kpi { get; set; }
        public Dictionary<string, List<NameCount>> Breakdown { get; set; }
        public Dictionary<string, List<TrendPoint>> Trend { get; set; }
    }

    public class DrillRow
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public DateTime? Dt { get; set; }
        public string Date { get; set; }
        public string Url { get; set; }
    }

    public class DrillResult { public string Error { get; set; } public int Total { get; set; } public List<DrillRow> Items { get; set; } }
    public class ExportFile { public string Error { get; set; } public byte[] Bytes { get; set; } public string FileName { get; set; } }

    public class StepStat
    {
        public int StepID { get; set; }
        public string StepKey { get; set; }
        public string Name { get; set; }
        public string StepType { get; set; }
        public int? SlaHours { get; set; }
        public int Reached { get; set; }
        public int Visits { get; set; }          // ملاقات‌های بسته‌شده (دارای مدت)
        public int Ongoing { get; set; }         // هنوز در همین مرحله
        public double? AvgH { get; set; }
        public double? MedianH { get; set; }
        public double? P90H { get; set; }
        public double? MaxH { get; set; }
        public int Breaches { get; set; }
        public int Outliers { get; set; }
        public bool SmallSample { get; set; }
        public bool Bottleneck { get; set; }
    }

    public class ProcessAnalysis
    {
        public string Error { get; set; }
        public int WorkflowID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Running { get; set; }
        public int Terminated { get; set; }
        public double? AvgTotalH { get; set; }
        public bool Capped { get; set; }
        public int MissingHistory { get; set; }
        public List<string> Warnings { get; set; }
        public List<StepStat> Steps { get; set; }
    }

    public class StepVisitRow
    {
        public long InstanceID { get; set; }
        public string Subject { get; set; }     // فقط برای کاربری که به مانیتور فرایند دسترسی دارد
        public double DurationH { get; set; }
        public bool Ongoing { get; set; }
        public bool Breach { get; set; }
        public bool Outlier { get; set; }
        public string Url { get; set; }
    }

    public class StepDetail { public string Error { get; set; } public string Step { get; set; } public int? SlaHours { get; set; } public List<StepVisitRow> Items { get; set; } }

    public static class MdrReportService
    {
        public const int MaxRangeDays = 366;
        public const int DrillMax = 100;
        public const int ExportMax = 5000;
        public const int ProcessInstanceCap = 5000;
        public const string ExportPage = "MDR.Reports.Export";
        private const string Module = "Reports";

        // ---------------- بازه ----------------
        // خالی = ۳۰ روز تا امروز. خطا: rep.err.dates | rep.err.range
        public static string NormalizeRange(DateTime? from, DateTime? to, out DateTime f, out DateTime t)
        {
            t = (to ?? DateTime.Today).Date;
            f = (from ?? t.AddDays(-29)).Date;
            if (f > t) return "rep.err.dates";
            if ((t - f).TotalDays + 1 > MaxRangeDays) return "rep.err.range";
            return null;
        }

        private class Cnt { public int N { get; set; } }
        private class Num { public double? V { get; set; } }
        private class Bucket { public int B { get; set; } public int N { get; set; } }
        private class IdCnt { public int Id { get; set; } public int N { get; set; } }
        private class Pair { public int? A { get; set; } public int? B { get; set; } }

        private static object[] P(int uid, MdrSearchService.SourceScope sc, DateTime from, DateTime to, int step = 1)
        {
            return new object[]
            {
                new SqlParameter("@uid", uid), new SqlParameter("@mgr", sc.Manager ? 1 : 0),
                new SqlParameter("@from", from.Date), new SqlParameter("@to", to.Date.AddDays(1)),
                new SqlParameter("@now", DateTime.Now), new SqlParameter("@step", step)
            };
        }

        private static int Count(MdrContext db, int uid, MdrSearchService.SourceScope sc, DateTime f, DateTime t, string where, string extraFrom = "")
        {
            var sql = "SELECT COUNT(*) AS N FROM " + sc.From + extraFrom + " WHERE " + sc.Acl + (string.IsNullOrEmpty(where) ? "" : " AND " + where);
            return db.Database.SqlQuery<Cnt>(sql, P(uid, sc, f, t)).First().N;
        }

        private static decimal? Hours(MdrContext db, int uid, MdrSearchService.SourceScope sc, DateTime f, DateTime t, string minutesExpr, string where)
        {
            var sql = "SELECT AVG(CAST(" + minutesExpr + " AS FLOAT)) / 60.0 AS V FROM " + sc.From + " WHERE " + sc.Acl + " AND " + where;
            var v = db.Database.SqlQuery<Num>(sql, P(uid, sc, f, t)).First().V;
            return v.HasValue ? (decimal?)Math.Round((decimal)v.Value, 1) : null;
        }

        private static List<NameCount> Group(MdrContext db, int uid, MdrSearchService.SourceScope sc, DateTime f, DateTime t, string nameExpr, string where, int top = 0, string extraFrom = "")
        {
            var sql = "SELECT " + (top > 0 ? "TOP " + top + " " : "") + "CAST(" + nameExpr + " AS NVARCHAR(200)) AS Name, COUNT(*) AS [Count] FROM " + sc.From + extraFrom +
                      " WHERE " + sc.Acl + " AND " + where + " GROUP BY " + nameExpr + " ORDER BY COUNT(*) DESC";
            return db.Database.SqlQuery<NameCount>(sql, P(uid, sc, f, t)).ToList();
        }

        private static List<TrendPoint> Trend(MdrContext db, int uid, MdrSearchService.SourceScope sc, DateTime f, DateTime t, string createdCol, string createdWhere, string closedCol, string closedWhere)
        {
            var days = (int)(t - f).TotalDays + 1;
            var step = days <= 31 ? 1 : 7;
            var n = (days + step - 1) / step;
            var pts = Enumerable.Range(0, n).Select(i => new TrendPoint { Label = f.AddDays(i * step).ToString("yyyy-MM-dd") }).ToArray();

            Action<string, string, bool> fill = (col, extra, created) =>
            {
                if (col == null) return;
                var sql = "SELECT DATEDIFF(DAY, @from, " + col + ") / @step AS B, COUNT(*) AS N FROM " + sc.From + " WHERE " + sc.Acl +
                          " AND " + col + " >= @from AND " + col + " < @to" + (string.IsNullOrEmpty(extra) ? "" : " AND " + extra) +
                          " GROUP BY DATEDIFF(DAY, @from, " + col + ") / @step";
                foreach (var b in db.Database.SqlQuery<Bucket>(sql, P(uid, sc, f, t, step)).ToList())
                    if (b.B >= 0 && b.B < n) { if (created) pts[b.B].Created = b.N; else pts[b.B].Closed = b.N; }
            };
            fill(createdCol, createdWhere, true);
            fill(closedCol, closedWhere, false);
            return pts.ToList();
        }

        private static Dictionary<int, string> UserNames(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            // فقط عدد صحیح در SQL می‌آید
            return db.Database.SqlQuery<UserName>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" + string.Join(",", list) + ")")
                .ToDictionary(r => r.Id, r => r.Name);
        }
        private class UserName { public int Id { get; set; } public string Name { get; set; } }

        // ---------------- گزارش عملیاتی ----------------
        public static ReportOverview Overview(int uid, DateTime from, DateTime to, bool personal)
        {
            var sw = Stopwatch.StartNew();
            var o = new ReportOverview
            {
                From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd"), Personal = personal,
                Skipped = new List<string>(), Warnings = new List<string>(),
                Kpi = new Dictionary<string, Dictionary<string, decimal?>>(), Breakdown = new Dictionary<string, List<NameCount>>(),
                Trend = new Dictionary<string, List<TrendPoint>>()
            };
            Func<string, string> R = col => col + " >= @from AND " + col + " < @to";

            using (var db = new MdrContext())
            {
                var sc = MdrSearchService.ScopeFor(uid, "req", personal);
                if (sc == null) o.Skipped.Add("req");
                else
                {
                    var k = o.Kpi["req"] = new Dictionary<string, decimal?>();
                    k["created"] = Count(db, uid, sc, from, to, R("s.CreatedAt"));
                    k["submitted"] = Count(db, uid, sc, from, to, R("s.SubmittedAt"));
                    k["open"] = Count(db, uid, sc, from, to, "s.Status = N'Submitted'");
                    k["approved"] = Count(db, uid, sc, from, to, "s.Status = N'Approved' AND " + R("s.ClosedAt"));
                    k["rejected"] = Count(db, uid, sc, from, to, "s.Status = N'Rejected' AND " + R("s.ClosedAt"));
                    k["avgCycleH"] = Hours(db, uid, sc, from, to, "DATEDIFF(MINUTE, s.SubmittedAt, s.ClosedAt)", "s.Status IN (N'Approved', N'Rejected') AND s.SubmittedAt IS NOT NULL AND " + R("s.ClosedAt"));
                    o.Breakdown["req.status"] = Group(db, uid, sc, from, to, "s.Status", R("s.CreatedAt"));
                    o.Breakdown["req.type"] = Group(db, uid, sc, from, to, "ty.Name", R("s.CreatedAt"), 5);
                    o.Trend["req"] = Trend(db, uid, sc, from, to, "s.CreatedAt", null, "s.ClosedAt", "s.Status IN (N'Approved', N'Rejected')");
                }

                sc = MdrSearchService.ScopeFor(uid, "tsk", personal);
                if (sc == null) o.Skipped.Add("tsk");
                else
                {
                    const string open = "s.Status IN (N'Todo', N'InProgress', N'Blocked')";
                    var k = o.Kpi["tsk"] = new Dictionary<string, decimal?>();
                    k["created"] = Count(db, uid, sc, from, to, R("s.CreatedAt"));
                    k["done"] = Count(db, uid, sc, from, to, "s.Status = N'Done' AND " + R("s.CompletedAt"));
                    k["open"] = Count(db, uid, sc, from, to, open);
                    k["overdue"] = Count(db, uid, sc, from, to, open + " AND s.DueAt IS NOT NULL AND s.DueAt < @now");
                    var withDue = Count(db, uid, sc, from, to, "s.Status = N'Done' AND s.DueAt IS NOT NULL AND " + R("s.CompletedAt"));
                    var onTime = Count(db, uid, sc, from, to, "s.Status = N'Done' AND s.DueAt IS NOT NULL AND s.CompletedAt <= s.DueAt AND " + R("s.CompletedAt"));
                    k["onTimePct"] = withDue == 0 ? (decimal?)null : Math.Round(100m * onTime / withDue, 0);
                    o.Breakdown["tsk.status"] = Group(db, uid, sc, from, to, "s.Status", R("s.CreatedAt"));

                    var work = db.Database.SqlQuery<IdCnt>("SELECT TOP 10 s.AssigneeUserID AS Id, COUNT(*) AS N FROM " + sc.From + " WHERE " + sc.Acl + " AND " + open +
                        " AND s.AssigneeUserID IS NOT NULL GROUP BY s.AssigneeUserID ORDER BY COUNT(*) DESC", P(uid, sc, from, to)).ToList();
                    var names = UserNames(db, work.Select(w => w.Id));
                    o.Breakdown["tsk.workload"] = work.Select(w => new NameCount { Name = names.ContainsKey(w.Id) ? names[w.Id] : "#" + w.Id, Count = w.N }).ToList();
                    o.Trend["tsk"] = Trend(db, uid, sc, from, to, "s.CreatedAt", null, "s.CompletedAt", "s.Status = N'Done'");
                }

                sc = MdrSearchService.ScopeFor(uid, "ltr", personal);
                if (sc == null) o.Skipped.Add("ltr");
                else
                {
                    const string reg = "s.Status IN (N'Registered', N'Archived')";
                    var k = o.Kpi["ltr"] = new Dictionary<string, decimal?>();
                    k["registered"] = Count(db, uid, sc, from, to, reg + " AND " + R("s.RegisteredAt"));
                    k["noReply"] = Count(db, uid, sc, from, to, "s.Status = N'Registered' AND s.DueAt IS NOT NULL AND s.DueAt < @now AND NOT EXISTS (SELECT 1 FROM dbo.MDR_LetterRecipients r WHERE r.LetterID = s.LetterID AND r.RepliedAt IS NOT NULL)");
                    k["unread"] = Count(db, uid, sc, from, to, "s.Status = N'Registered' AND " + R("s.RegisteredAt") + " AND r.ReadAt IS NULL", " JOIN dbo.MDR_LetterRecipients r ON r.LetterID = s.LetterID");
                    var avg = db.Database.SqlQuery<Num>("SELECT AVG(CAST(DATEDIFF(MINUTE, r.CreatedAt, r.RepliedAt) AS FLOAT)) / 60.0 AS V FROM " + sc.From +
                        " JOIN dbo.MDR_LetterRecipients r ON r.LetterID = s.LetterID WHERE " + sc.Acl + " AND r.RepliedAt >= @from AND r.RepliedAt < @to", P(uid, sc, from, to)).First().V;
                    k["avgReplyH"] = avg.HasValue ? (decimal?)Math.Round((decimal)avg.Value, 1) : null;
                    o.Breakdown["ltr.direction"] = Group(db, uid, sc, from, to, "s.Direction", reg + " AND " + R("s.RegisteredAt"));
                    o.Trend["ltr"] = Trend(db, uid, sc, from, to, "s.RegisteredAt", reg, null, null);
                }

                sc = MdrSearchService.ScopeFor(uid, "doc", personal);
                if (sc == null) o.Skipped.Add("doc");
                else
                {
                    var k = o.Kpi["doc"] = new Dictionary<string, decimal?>();
                    k["created"] = Count(db, uid, sc, from, to, R("s.CreatedAt"));
                    k["active"] = Count(db, uid, sc, from, to, "s.Status = N'Active'");
                    k["archived"] = Count(db, uid, sc, from, to, R("s.ArchivedAt"));
                    o.Breakdown["doc.class"] = Group(db, uid, sc, from, to, "s.Classification", R("s.CreatedAt"));
                }

                sc = MdrSearchService.ScopeFor(uid, "wf", personal);
                if (sc == null) o.Skipped.Add("wf");
                else
                {
                    var k = o.Kpi["wf"] = new Dictionary<string, decimal?>();
                    k["started"] = Count(db, uid, sc, from, to, R("s.StartedAt"));
                    k["running"] = Count(db, uid, sc, from, to, "s.Status = N'Running'");
                    k["overdue"] = Count(db, uid, sc, from, to, "s.Status = N'Running' AND s.DueAt IS NOT NULL AND s.DueAt < @now");
                    k["completed"] = Count(db, uid, sc, from, to, "s.Status = N'Completed' AND " + R("s.CompletedAt"));
                    k["avgDurationH"] = Hours(db, uid, sc, from, to, "DATEDIFF(MINUTE, s.StartedAt, s.CompletedAt)", "s.Status = N'Completed' AND " + R("s.CompletedAt"));
                    o.Breakdown["wf.status"] = Group(db, uid, sc, from, to, "s.Status", R("s.StartedAt"));
                    o.Trend["wf"] = Trend(db, uid, sc, from, to, "s.StartedAt", null, "s.CompletedAt", "s.Status = N'Completed'");

                    // داده‌ی ناقص: فرایند تکمیل‌شده‌ای که هیچ رویداد ورود به گام ندارد (تحلیل مرحله برای آن ممکن نیست)
                    var miss = Count(db, uid, sc, from, to, "s.Status = N'Completed' AND " + R("s.CompletedAt") +
                        " AND NOT EXISTS (SELECT 1 FROM dbo.MDR_WorkflowHistory h WHERE h.InstanceID = s.InstanceID AND h.ActionKey = N'Enter')");
                    if (miss > 0) o.Warnings.Add("incomplete");
                }
            }

            sw.Stop(); o.Ms = sw.ElapsedMilliseconds;
            if (o.Ms > 3000) o.Warnings.Add("slow");
            if (o.Skipped.Count > 0) o.Warnings.Add("skipped");
            return o;
        }

        // ---------------- drill-down ----------------
        // metric → { منبع، شرط (alias = s)، ستون تاریخ نمایشی }
        private static readonly Dictionary<string, string[]> Metrics = new Dictionary<string, string[]>
        {
            { "req.created",   new[] { "req", "s.CreatedAt >= @from AND s.CreatedAt < @to", "s.CreatedAt" } },
            { "req.open",      new[] { "req", "s.Status = N'Submitted'", "s.SubmittedAt" } },
            { "req.approved",  new[] { "req", "s.Status = N'Approved' AND s.ClosedAt >= @from AND s.ClosedAt < @to", "s.ClosedAt" } },
            { "req.rejected",  new[] { "req", "s.Status = N'Rejected' AND s.ClosedAt >= @from AND s.ClosedAt < @to", "s.ClosedAt" } },
            { "tsk.created",   new[] { "tsk", "s.CreatedAt >= @from AND s.CreatedAt < @to", "s.CreatedAt" } },
            { "tsk.open",      new[] { "tsk", "s.Status IN (N'Todo', N'InProgress', N'Blocked')", "s.DueAt" } },
            { "tsk.overdue",   new[] { "tsk", "s.Status IN (N'Todo', N'InProgress', N'Blocked') AND s.DueAt IS NOT NULL AND s.DueAt < @now", "s.DueAt" } },
            { "tsk.done",      new[] { "tsk", "s.Status = N'Done' AND s.CompletedAt >= @from AND s.CompletedAt < @to", "s.CompletedAt" } },
            { "ltr.registered", new[] { "ltr", "s.Status IN (N'Registered', N'Archived') AND s.RegisteredAt >= @from AND s.RegisteredAt < @to", "s.RegisteredAt" } },
            { "ltr.noReply",   new[] { "ltr", "s.Status = N'Registered' AND s.DueAt IS NOT NULL AND s.DueAt < @now AND NOT EXISTS (SELECT 1 FROM dbo.MDR_LetterRecipients r WHERE r.LetterID = s.LetterID AND r.RepliedAt IS NOT NULL)", "s.DueAt" } },
            { "doc.created",   new[] { "doc", "s.CreatedAt >= @from AND s.CreatedAt < @to", "s.CreatedAt" } },
            { "doc.archived",  new[] { "doc", "s.ArchivedAt >= @from AND s.ArchivedAt < @to", "s.ArchivedAt" } },
            { "wf.started",    new[] { "wf", "s.StartedAt >= @from AND s.StartedAt < @to", "s.StartedAt" } },
            { "wf.overdue",    new[] { "wf", "s.Status = N'Running' AND s.DueAt IS NOT NULL AND s.DueAt < @now", "s.DueAt" } },
            { "wf.completed",  new[] { "wf", "s.Status = N'Completed' AND s.CompletedAt >= @from AND s.CompletedAt < @to", "s.CompletedAt" } }
        };

        public static string[] MetricKeys { get { return Metrics.Keys.ToArray(); } }

        public static DrillResult Drill(int uid, string metric, DateTime from, DateTime to, bool personal, int take = DrillMax)
        {
            string[] m;
            if (string.IsNullOrEmpty(metric) || !Metrics.TryGetValue(metric, out m)) return new DrillResult { Error = "rep.err.metric" };
            var sc = MdrSearchService.ScopeFor(uid, m[0], personal);
            if (sc == null) return new DrillResult { Error = "denied" };

            take = Math.Max(1, Math.Min(ExportMax, take));
            using (var db = new MdrContext())
            {
                var total = db.Database.SqlQuery<Cnt>("SELECT COUNT(*) AS N FROM " + sc.From + " WHERE " + sc.Acl + " AND " + m[1], P(uid, sc, from, to)).First().N;
                var rows = db.Database.SqlQuery<DrillRow>(
                    "SELECT TOP (" + take + ") CAST(" + sc.IdColumn + " AS BIGINT) AS Id, " + sc.TitleExpr + " AS Title, " + sc.StatusExpr + " AS Status, " + m[2] + " AS Dt FROM " + sc.From +
                    " WHERE " + sc.Acl + " AND " + m[1] + " ORDER BY " + m[2] + " DESC", P(uid, sc, from, to)).ToList();
                foreach (var r in rows)
                {
                    r.Url = string.Format(sc.UrlFormat, r.Id);
                    r.Date = r.Dt.HasValue ? r.Dt.Value.ToString("yyyy-MM-dd HH:mm") : null;
                }
                return new DrillResult { Total = total, Items = rows };
            }
        }

        // ---------------- Export (CSV) ----------------
        // حق مستقل: MDR.Reports.Export. محتوا همان چیزی است که کاربر در صفحه می‌بیند (scope/ACL ثابت).
        public static ExportFile Export(int uid, string metric, DateTime from, DateTime to, bool personal)
        {
            if (!PermissionHelper.HasAccess(uid, ExportPage)) return new ExportFile { Error = "rep.err.noexport" };

            var sb = new StringBuilder();
            int rows;
            string name;
            if (metric == "summary")
            {
                var o = Overview(uid, from, to, personal);
                sb.AppendLine(Csv(MdrText.T("rep.csv.module"), MdrText.T("rep.csv.metric"), MdrText.T("rep.csv.value")));
                rows = 0;
                foreach (var mod in o.Kpi)
                    foreach (var kv in mod.Value)
                    {
                        sb.AppendLine(Csv(MdrText.T("rep.mod." + mod.Key), MdrText.T("rep.k." + mod.Key + "." + kv.Key), kv.Value.HasValue ? kv.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""));
                        rows++;
                    }
                name = "mdr-summary";
            }
            else
            {
                var d = Drill(uid, metric, from, to, personal, ExportMax);
                if (d.Error != null) return new ExportFile { Error = d.Error };
                sb.AppendLine(Csv("Id", MdrText.T("rep.csv.title"), MdrText.T("rep.csv.status"), MdrText.T("rep.csv.date"), MdrText.T("rep.csv.url")));
                foreach (var r in d.Items) sb.AppendLine(Csv(r.Id.ToString(), r.Title, r.Status, r.Date, r.Url));
                rows = d.Items.Count;
                name = "mdr-" + metric.Replace('.', '-');
            }

            AuditLogger.Log(AuditLogger.Actions.Export, Module, "خروجی گزارش: " + metric, entityName: "MDR_Reports", entityTitle: metric,
                newValues: new { metric, from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), personal, rows });

            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            return new ExportFile { Bytes = preamble.Concat(body).ToArray(), FileName = name + "-" + from.ToString("yyyyMMdd") + "-" + to.ToString("yyyyMMdd") + ".csv" };
        }

        // CSV امن: نقل‌قول و جلوگیری از Formula injection در Excel (سلول‌های شروع‌شده با = + - @)
        public static string Csv(params string[] cells)
        {
            return string.Join(",", cells.Select(c =>
            {
                c = c ?? "";
                if (c.Length > 0 && (c[0] == '=' || c[0] == '+' || c[0] == '-' || c[0] == '@' || c[0] == '\t' || c[0] == '\r')) c = "'" + c;
                return "\"" + c.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
            }));
        }

        // ---------------- ریاضی آمار (قابل تست بدون DB) ----------------
        // درون‌یابی خطی؛ p بین ۰ و ۱؛ ورودی باید مرتب باشد
        public static double Percentile(IList<double> sorted, double p)
        {
            if (sorted == null || sorted.Count == 0) return 0;
            if (sorted.Count == 1) return sorted[0];
            var pos = Math.Max(0, Math.Min(1, p)) * (sorted.Count - 1);
            var lo = (int)Math.Floor(pos); var hi = (int)Math.Ceiling(pos);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        // آستانه‌ی outlier = Q3 + 1.5×IQR؛ با کمتر از ۵ نمونه null (تشخیص معتبر نیست)
        public static double? OutlierThreshold(IList<double> sorted)
        {
            if (sorted == null || sorted.Count < 5) return null;
            var q1 = Percentile(sorted, 0.25); var q3 = Percentile(sorted, 0.75);
            return q3 + 1.5 * (q3 - q1);
        }

        // ---------------- تحلیل فرایند ----------------
        public class DefRow
        {
            public int WorkflowID { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public int Version { get; set; }
            public string Status { get; set; }
            public int Instances { get; set; }
        }

        public static List<DefRow> Definitions()
        {
            using (var db = new MdrContext())
            {
                return db.WorkflowDefinitions.Where(d => d.Status != "Draft").OrderBy(d => d.Code).ThenByDescending(d => d.Version)
                    .Select(d => new DefRow
                    {
                        WorkflowID = d.WorkflowID, Code = d.Code, Name = d.Name, Version = d.Version, Status = d.Status,
                        Instances = db.WorkflowInstances.Count(i => i.WorkflowID == d.WorkflowID)
                    }).Take(2000).ToList();
            }
        }

        private class Visit { public long InstanceID; public int StepID; public double Hours; public bool Ongoing; }
        private class Ev { public long InstanceID { get; set; } public int? StepID { get; set; } public string ActionKey { get; set; } public DateTime CreatedAt { get; set; } }

        private static List<Visit> BuildVisits(MdrContext db, int workflowId, DateTime from, DateTime to, out List<MdrWorkflowInstance> instances, out bool capped, out HashSet<string> reached, out int missing)
        {
            var f = from.Date; var t = to.Date.AddDays(1);
            instances = db.WorkflowInstances.Where(i => i.WorkflowID == workflowId && i.StartedAt >= f && i.StartedAt < t)
                .OrderBy(i => i.InstanceID).Take(ProcessInstanceCap + 1).ToList();
            capped = instances.Count > ProcessInstanceCap;
            if (capped) instances = instances.Take(ProcessInstanceCap).ToList();

            var visits = new List<Visit>();
            reached = new HashSet<string>();     // "instance:step"
            missing = 0;
            if (instances.Count == 0) return visits;

            var maxId = instances[instances.Count - 1].InstanceID;
            var byId = instances.ToDictionary(i => i.InstanceID);
            var events = (from h in db.WorkflowHistory
                          join i in db.WorkflowInstances on h.InstanceID equals i.InstanceID
                          where i.WorkflowID == workflowId && i.StartedAt >= f && i.StartedAt < t && h.InstanceID <= maxId &&
                                (h.ActionKey == "Start" || h.ActionKey == "Enter" || h.ActionKey == "Complete" || h.ActionKey == "Terminate")
                          orderby h.InstanceID, h.HistoryID
                          select new Ev { InstanceID = h.InstanceID, StepID = h.StepID, ActionKey = h.ActionKey, CreatedAt = h.CreatedAt }).ToList();

            var now = DateTime.Now;
            var withEvents = new HashSet<long>(events.Select(e => e.InstanceID));
            foreach (var g in events.GroupBy(e => e.InstanceID))
            {
                int? openStep = null; DateTime openAt = DateTime.MinValue; var enters = 0;
                foreach (var e in g)
                {
                    if (e.StepID.HasValue) reached.Add(g.Key + ":" + e.StepID.Value);
                    if (e.ActionKey == "Enter")
                    {
                        enters++;
                        if (openStep.HasValue) visits.Add(new Visit { InstanceID = g.Key, StepID = openStep.Value, Hours = Math.Max(0, (e.CreatedAt - openAt).TotalHours) });
                        openStep = e.StepID; openAt = e.CreatedAt;
                    }
                    else if (e.ActionKey == "Complete" || e.ActionKey == "Terminate")
                    {
                        if (openStep.HasValue) visits.Add(new Visit { InstanceID = g.Key, StepID = openStep.Value, Hours = Math.Max(0, (e.CreatedAt - openAt).TotalHours) });
                        openStep = null;
                    }
                }
                if (openStep.HasValue) visits.Add(new Visit { InstanceID = g.Key, StepID = openStep.Value, Hours = Math.Max(0, (now - openAt).TotalHours), Ongoing = true });
                if (enters == 0 && byId.ContainsKey(g.Key) && byId[g.Key].Status != "Running") missing++;
            }
            // فرایندی که هیچ رویدادی ندارد هم ناقص است
            missing += instances.Count(i => !withEvents.Contains(i.InstanceID) && i.Status != "Running");
            return visits;
        }

        public static ProcessAnalysis Process(int workflowId, DateTime from, DateTime to)
        {
            using (var db = new MdrContext())
            {
                var def = db.WorkflowDefinitions.FirstOrDefault(d => d.WorkflowID == workflowId);
                if (def == null) return new ProcessAnalysis { Error = "wf.err.notfound" };

                List<MdrWorkflowInstance> insts; bool capped; HashSet<string> reached; int missing;
                var visits = BuildVisits(db, workflowId, from, to, out insts, out capped, out reached, out missing);
                var steps = db.WorkflowSteps.Where(s => s.WorkflowID == workflowId).OrderBy(s => s.SortOrder).ThenBy(s => s.StepID).ToList();

                var res = new ProcessAnalysis
                {
                    WorkflowID = workflowId, Code = def.Code, Name = def.Name, Version = def.Version, Total = insts.Count,
                    Completed = insts.Count(i => i.Status == "Completed"), Running = insts.Count(i => i.Status == "Running" || i.Status == "Paused"),
                    Terminated = insts.Count(i => i.Status == "Terminated"), Capped = capped, MissingHistory = missing,
                    Warnings = new List<string>(), Steps = new List<StepStat>()
                };
                var done = insts.Where(i => i.Status == "Completed" && i.CompletedAt.HasValue).Select(i => (i.CompletedAt.Value - i.StartedAt).TotalHours).ToList();
                res.AvgTotalH = done.Count == 0 ? (double?)null : Math.Round(done.Average(), 1);

                foreach (var s in steps)
                {
                    var closed = visits.Where(v => v.StepID == s.StepID && !v.Ongoing).Select(v => v.Hours).OrderBy(x => x).ToList();
                    var ongoing = visits.Where(v => v.StepID == s.StepID && v.Ongoing).ToList();
                    var thr = OutlierThreshold(closed);
                    var st = new StepStat
                    {
                        StepID = s.StepID, StepKey = s.StepKey, Name = s.Name, StepType = s.StepType, SlaHours = s.SlaHours,
                        Reached = insts.Count(i => reached.Contains(i.InstanceID + ":" + s.StepID)),
                        Visits = closed.Count, Ongoing = ongoing.Count, SmallSample = closed.Count < 10,
                        Outliers = thr.HasValue ? closed.Count(x => x > thr.Value) : 0
                    };
                    if (closed.Count > 0)
                    {
                        st.AvgH = Math.Round(closed.Average(), 2); st.MedianH = Math.Round(Percentile(closed, 0.5), 2);
                        st.P90H = Math.Round(Percentile(closed, 0.9), 2); st.MaxH = Math.Round(closed.Last(), 2);
                    }
                    if (s.SlaHours.HasValue && s.SlaHours.Value > 0)
                        st.Breaches = closed.Count(x => x > s.SlaHours.Value) + ongoing.Count(v => v.Hours > s.SlaHours.Value);
                    res.Steps.Add(st);
                }

                // گلوگاه: کندترین گام کاری (غیر شروع/پایان) با حداقل ۳ ملاقات
                var cand = res.Steps.Where(x => x.StepType != "Start" && x.StepType != "End" && x.Visits >= 3 && x.AvgH.HasValue && x.AvgH.Value > 0)
                    .OrderByDescending(x => x.AvgH.Value).FirstOrDefault();
                if (cand != null) cand.Bottleneck = true;

                if (res.Total < 10) res.Warnings.Add("small");
                if (capped) res.Warnings.Add("capped");
                if (missing > 0) res.Warnings.Add("incomplete");
                if (res.Steps.Any(x => x.Outliers > 0)) res.Warnings.Add("outliers");
                return res;
            }
        }

        // جزئیات یک مرحله؛ عنوان فرایندها فقط برای کاربری که به مانیتور فرایند دسترسی دارد (همان قاعده‌ی جستجو)
        public static StepDetail StepVisits(int uid, int workflowId, int stepId, DateTime from, DateTime to)
        {
            using (var db = new MdrContext())
            {
                var step = db.WorkflowSteps.FirstOrDefault(s => s.StepID == stepId && s.WorkflowID == workflowId);
                if (step == null) return new StepDetail { Error = "wf.err.notfound" };

                List<MdrWorkflowInstance> insts; bool capped; HashSet<string> reached; int missing;
                var visits = BuildVisits(db, workflowId, from, to, out insts, out capped, out reached, out missing).Where(v => v.StepID == stepId).ToList();
                var thr = OutlierThreshold(visits.Where(v => !v.Ongoing).Select(v => v.Hours).OrderBy(x => x).ToList());
                var canSee = PermissionHelper.HasAccess(uid, "MDR.Workflow.Monitor");
                var subj = insts.ToDictionary(i => i.InstanceID, i => i.Subject);
                var sla = step.SlaHours;

                var items = visits.OrderByDescending(v => v.Hours).Take(50).Select(v => new StepVisitRow
                {
                    InstanceID = v.InstanceID, Subject = canSee ? subj[v.InstanceID] : null, DurationH = Math.Round(v.Hours, 2), Ongoing = v.Ongoing,
                    Breach = sla.HasValue && sla.Value > 0 && v.Hours > sla.Value, Outlier = !v.Ongoing && thr.HasValue && v.Hours > thr.Value,
                    Url = canSee ? "/Mdr/Workflow/Monitor?id=" + v.InstanceID : null
                }).ToList();
                return new StepDetail { Step = step.Name, SlaHours = sla, Items = items };
            }
        }
    }
}
