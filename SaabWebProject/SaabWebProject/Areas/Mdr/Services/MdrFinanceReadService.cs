using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 7 | مالی — خواندن (فهرست، جزئیات، پیکربندی، داشبورد)
    // - فهرست: «مال من» (درخواست‌کننده)، «در انتظار من» (تأییدکننده‌ی مرحله‌ی جاری) و «همه» (فقط مدیر مالی).
    // - جزئیات: فقط برای درخواست‌کننده، مدیر مالی، تأییدکننده‌ی همان سند و پرداخت‌کننده‌ی سند تأییدشده؛ وضعیت بودجه فقط برای مدیر/تأییدکننده.
    // - داشبورد: اسناد MDR بر اساس scope (شخصی/مدیر). بخش «سیستم قدیم» (جدول FinancialDocuments) فقط‌خواندنی و فقط برای مدیر مالی؛
    //   مبلغ‌ها همان‌طور که در جدول قدیمی هست خوانده می‌شوند و هیچ تفسیر یا سقفی از خط اعتبار قدیمی حدس زده نمی‌شود.
    // =====================================================
    public class FinRow
    {
        public long DocumentID { get; set; }
        public string Number { get; set; }
        public string Direction { get; set; }
        public string Title { get; set; }
        public long Amount { get; set; }
        public string DocDate { get; set; }
        public string Status { get; set; }
        public int? CurrentLevel { get; set; }
        public string Requester { get; set; }
        public string CostCenter { get; set; }
    }

    public class FinStepRow { public int Level { get; set; } public string Status { get; set; } public string DecidedBy { get; set; } public string DecidedAt { get; set; } public string Comment { get; set; } public List<string> Approvers { get; set; } }
    public class FinHistRow { public string ActionKey { get; set; } public string Actor { get; set; } public string Detail { get; set; } public string At { get; set; } public string CorrelationID { get; set; } }
    public class FinBudgetInfo { public long Budget { get; set; } public long Used { get; set; } public long Remaining { get; set; } public int Year { get; set; } }

    public class FinDetail
    {
        public long DocumentID { get; set; }
        public string Number { get; set; }
        public string Direction { get; set; }
        public string Title { get; set; }
        public long Amount { get; set; }
        public string DocDate { get; set; }
        public int? CostCenterID { get; set; }
        public string CostCenter { get; set; }
        public int? PeymanID { get; set; }
        public string PeymanTitle { get; set; }
        public string Payee { get; set; }
        public string ReferenceNo { get; set; }
        public string Status { get; set; }
        public int? CurrentLevel { get; set; }
        public string Requester { get; set; }
        public string CreatedAt { get; set; }
        public string PaidAt { get; set; }
        public string PaidBy { get; set; }
        public string PaymentRef { get; set; }
        public string ReturnReason { get; set; }
        public string RowVer { get; set; }
        public bool PeriodClosed { get; set; }
        public int DuplicateCount { get; set; }
        public List<string> DuplicateNumbers { get; set; }
        public FinBudgetInfo Budget { get; set; }
        public List<FinStepRow> Steps { get; set; }
        public List<FinHistRow> History { get; set; }
        public bool CanEdit { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPay { get; set; }
        public bool CanCancel { get; set; }
    }

    public class FinNamedAmount { public string Name { get; set; } public long Amount { get; set; } public int Count { get; set; } }
    public class FinTrend { public string Label { get; set; } public long Payment { get; set; } public long Receipt { get; set; } }
    public class FinBudgetRow { public int CostCenterID { get; set; } public string CostCenter { get; set; } public long Budget { get; set; } public long Used { get; set; } public int Pct { get; set; } }
    public class LegacyTrend { public string Month { get; set; } public double Debtor { get; set; } public double Creditor { get; set; } }
    public class LegacyTop { public string Title { get; set; } public double Debtor { get; set; } public double Creditor { get; set; } public int Count { get; set; } }
    public class LegacyBlock { public int Count { get; set; } public double Debtor { get; set; } public double Creditor { get; set; } public double Net { get; set; } public List<LegacyTrend> Trend { get; set; } public List<LegacyTop> Top { get; set; } }

    public class FinDashboard
    {
        public string From { get; set; }
        public string To { get; set; }
        public bool Personal { get; set; }
        public bool IsManager { get; set; }
        public Dictionary<string, long?> Kpi { get; set; }
        public List<FinTrend> Trend { get; set; }
        public List<FinBudgetRow> Budgets { get; set; }
        public Dictionary<string, int> Alerts { get; set; }
        public LegacyBlock Legacy { get; set; }
    }

    public class LegacyDocRow { public int ID { get; set; } public string Title { get; set; } public DateTime? DataDocument { get; set; } public double? Debtore { get; set; } public double? Creditor { get; set; } }

    public static class MdrFinanceReadService
    {
        public const int PendingSlaHours = 72;     // «تأخیر در تأیید» فقط برای هشدار داشبورد (نه قاعده‌ی مالی)
        private class UserName { public int Id { get; set; } public string Name { get; set; } }
        private class Cnt { public int N { get; set; } }

        private static string D(DateTime d) { return d.ToString("yyyy-MM-dd"); }
        private static string DT(DateTime? d) { return d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm") : null; }

        private static Dictionary<int, string> Units(MdrContext db)
        {
            return db.Database.SqlQuery<UserName>("SELECT OrganizationUnitID AS Id, UnitName AS Name FROM dbo.MDR_OrganizationUnits").ToDictionary(x => x.Id, x => x.Name);
        }

        public static List<UserName2> CostCenters()
        {
            using (var db = new MdrContext())
                return db.Database.SqlQuery<UserName2>("SELECT OrganizationUnitID AS Id, UnitName AS Name FROM dbo.MDR_OrganizationUnits ORDER BY SortOrder, UnitName").ToList();
        }
        public class UserName2 { public int Id { get; set; } public string Name { get; set; } }

        // ---------------- فهرست ----------------
        public static List<FinRow> List(int uid, string scope, string status, string direction, string q)
        {
            using (var db = new MdrContext())
            {
                var manager = MdrFinanceService.IsManager(uid);
                List<MdrFinDocument> rows;
                if (scope == "todo")
                {
                    rows = db.FinDocuments.Where(d => d.Status == "Submitted" && d.RequesterUserID != uid).OrderBy(d => d.SubmittedAt).Take(500).ToList()
                        .Where(d => d.CurrentLevel.HasValue && MdrFinanceService.Eligible(db, d, d.CurrentLevel.Value, uid) &&
                                    !db.FinApprovals.Any(a => a.DocumentID == d.DocumentID && a.DecidedBy == uid && a.Status == "Approved")).ToList();
                }
                else
                {
                    var qry = db.FinDocuments.AsQueryable();
                    if (scope == "all" && manager) { }
                    else qry = qry.Where(d => d.RequesterUserID == uid);
                    if (!string.IsNullOrEmpty(status)) qry = qry.Where(d => d.Status == status);
                    if (!string.IsNullOrEmpty(direction)) qry = qry.Where(d => d.Direction == direction);
                    if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(d => d.Title.Contains(s) || d.Number.Contains(s) || d.Payee.Contains(s) || d.ReferenceNo.Contains(s)); }
                    rows = qry.OrderByDescending(d => d.DocumentID).Take(300).ToList();
                }
                var names = MdrChatService.Names(db, rows.Select(r => r.RequesterUserID));
                var units = Units(db);
                return rows.Select(d => new FinRow
                {
                    DocumentID = d.DocumentID, Number = d.Number, Direction = d.Direction, Title = d.Title, Amount = d.Amount, DocDate = D(d.DocDate), Status = d.Status, CurrentLevel = d.CurrentLevel,
                    Requester = names.ContainsKey(d.RequesterUserID) ? names[d.RequesterUserID] : null,
                    CostCenter = d.CostCenterID.HasValue && units.ContainsKey(d.CostCenterID.Value) ? units[d.CostCenterID.Value] : null
                }).ToList();
            }
        }

        // ---------------- جزئیات ----------------
        public static FinDetail Get(int uid, long id)
        {
            using (var db = new MdrContext())
            {
                var d = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
                if (d == null) return null;
                var a = MdrFinanceService.AccessFor(db, d, uid);
                if (!a.CanView) return null;

                var approvals = db.FinApprovals.Where(x => x.DocumentID == id).OrderBy(x => x.Level).ToList();
                var hist = db.FinHistory.Where(x => x.DocumentID == id).OrderBy(x => x.CreatedAt).ThenBy(x => x.HistoryID).ToList();
                var uids = new List<int> { d.RequesterUserID };
                if (d.PaidBy.HasValue) uids.Add(d.PaidBy.Value);
                uids.AddRange(approvals.Where(x => x.DecidedBy.HasValue).Select(x => x.DecidedBy.Value));
                uids.AddRange(hist.Where(x => x.ActorUserID.HasValue).Select(x => x.ActorUserID.Value));
                var levelApprovers = approvals.ToDictionary(x => x.Level, x => db.FinApprovers.Where(z => z.IsActive && z.Level == x.Level).ToList());
                foreach (var ap in approvals) uids.AddRange(LevelUsers(db, d, ap.Level));
                var names = MdrChatService.Names(db, uids);
                Func<int?, string> nm = x => x.HasValue && names.ContainsKey(x.Value) ? names[x.Value] : null;
                var units = Units(db);

                var detail = new FinDetail
                {
                    DocumentID = d.DocumentID, Number = d.Number, Direction = d.Direction, Title = d.Title, Amount = d.Amount, DocDate = D(d.DocDate),
                    CostCenterID = d.CostCenterID, CostCenter = d.CostCenterID.HasValue && units.ContainsKey(d.CostCenterID.Value) ? units[d.CostCenterID.Value] : null,
                    PeymanID = d.PeymanID, Payee = d.Payee, ReferenceNo = d.ReferenceNo, Status = d.Status, CurrentLevel = d.CurrentLevel, Requester = nm(d.RequesterUserID),
                    CreatedAt = DT(d.CreatedAt), PaidAt = DT(d.PaidAt), PaidBy = nm(d.PaidBy), PaymentRef = d.PaymentRef, ReturnReason = d.ReturnReason,
                    RowVer = Convert.ToBase64String(d.RowVer), PeriodClosed = MdrFinanceService.IsClosed(db, d.DocDate),
                    Steps = approvals.Select(x => new FinStepRow
                    {
                        Level = x.Level, Status = x.Status, DecidedBy = nm(x.DecidedBy), DecidedAt = DT(x.DecidedAt), Comment = x.Comment,
                        Approvers = LevelUsers(db, d, x.Level).Select(u => names.ContainsKey(u) ? names[u] : "#" + u).ToList()
                    }).ToList(),
                    History = hist.Select(h => new FinHistRow { ActionKey = h.ActionKey, Actor = nm(h.ActorUserID), Detail = h.Detail, At = DT(h.CreatedAt), CorrelationID = h.CorrelationID.ToString() }).ToList()
                };
                if (d.PeymanID.HasValue)
                    detail.PeymanTitle = db.Database.SqlQuery<string>("SELECT TOP 1 pec_Title FROM Contract.tbPeymanContracts WHERE pec_ID = @p0", d.PeymanID.Value).FirstOrDefault();

                // تکراری‌ها: شماره‌ها فقط برای مدیر مالی (وجود سندِ غیرقابل‌دیدن لو نرود)
                var dups = d.Status == "Draft" || d.Status == "Submitted" ? MdrFinanceService.FindDuplicates(db, d) : new List<MdrFinDocument>();
                detail.DuplicateCount = dups.Count;
                detail.DuplicateNumbers = a.IsManager ? dups.Select(x => x.Number).ToList() : new List<string>();

                if ((a.IsManager || a.IsApprover) && d.Direction == "Payment" && d.CostCenterID.HasValue)
                {
                    var year = MdrFinanceService.FiscalYear(d.DocDate);
                    var b = db.FinBudgets.FirstOrDefault(x => x.CostCenterID == d.CostCenterID.Value && x.FiscalYear == year);
                    if (b != null)
                    {
                        var used = MdrFinanceService.BudgetUsed(db, d.CostCenterID.Value, year, null);
                        detail.Budget = new FinBudgetInfo { Budget = b.Amount, Used = used, Remaining = b.Amount - used, Year = year };
                    }
                }

                var canDecide = d.Status == "Submitted" && d.CurrentLevel.HasValue && d.RequesterUserID != uid && MdrFinanceService.Eligible(db, d, d.CurrentLevel.Value, uid) &&
                                !approvals.Any(x => x.DecidedBy == uid && x.Status == "Approved") && !detail.PeriodClosed;
                detail.CanApprove = canDecide;
                detail.CanEdit = a.IsRequester && d.Status == "Draft" && !detail.PeriodClosed;
                detail.CanCancel = a.IsRequester && d.Status == "Draft";
                detail.CanPay = d.Status == "Approved" && PermissionHelper.HasAccess(uid, MdrFinanceService.PayPage) && d.RequesterUserID != uid &&
                                !approvals.Any(x => x.DecidedBy == uid) && !detail.PeriodClosed;
                return detail;
            }
        }

        private static List<int> LevelUsers(MdrContext db, MdrFinDocument d, int level)
        {
            var rows = db.FinApprovers.Where(x => x.IsActive && x.Level == level && x.CostCenterID != null && x.CostCenterID == d.CostCenterID).ToList();
            if (rows.Count == 0) rows = db.FinApprovers.Where(x => x.IsActive && x.Level == level && x.CostCenterID == null).ToList();
            return rows.Where(x => !x.MinAmount.HasValue || d.Amount >= x.MinAmount.Value).Select(x => x.UserID).Distinct().ToList();
        }

        // ---------------- پیکربندی (مدیر مالی) ----------------
        public class ConfigApprover { public int ApproverID { get; set; } public int? CostCenterID { get; set; } public string CostCenter { get; set; } public int Level { get; set; } public int UserID { get; set; } public string User { get; set; } public long? MinAmount { get; set; } }
        public class ConfigBudget { public int BudgetID { get; set; } public int CostCenterID { get; set; } public string CostCenter { get; set; } public int FiscalYear { get; set; } public long Amount { get; set; } public long Used { get; set; } }
        public class ConfigPeriod { public int FiscalYear { get; set; } public int Month { get; set; } public string ClosedAt { get; set; } public string Reason { get; set; } }
        public class FinConfig { public List<ConfigApprover> Approvers { get; set; } public List<ConfigBudget> Budgets { get; set; } public List<ConfigPeriod> Periods { get; set; } }

        public static FinConfig Config(int uid)
        {
            if (!MdrFinanceService.IsManager(uid)) return null;
            using (var db = new MdrContext())
            {
                var units = Units(db);
                var ap = db.FinApprovers.Where(x => x.IsActive).OrderBy(x => x.CostCenterID).ThenBy(x => x.Level).ToList();
                var names = MdrChatService.Names(db, ap.Select(x => x.UserID));
                var year = MdrFinanceService.FiscalYear(DateTime.Now);
                var budgets = db.FinBudgets.Where(b => b.FiscalYear >= year - 1).OrderByDescending(b => b.FiscalYear).ThenBy(b => b.CostCenterID).ToList();
                return new FinConfig
                {
                    Approvers = ap.Select(x => new ConfigApprover
                    {
                        ApproverID = x.ApproverID, CostCenterID = x.CostCenterID, CostCenter = x.CostCenterID.HasValue && units.ContainsKey(x.CostCenterID.Value) ? units[x.CostCenterID.Value] : null,
                        Level = x.Level, UserID = x.UserID, User = names.ContainsKey(x.UserID) ? names[x.UserID] : "#" + x.UserID, MinAmount = x.MinAmount
                    }).ToList(),
                    Budgets = budgets.Select(b => new ConfigBudget
                    {
                        BudgetID = b.BudgetID, CostCenterID = b.CostCenterID, CostCenter = units.ContainsKey(b.CostCenterID) ? units[b.CostCenterID] : null, FiscalYear = b.FiscalYear, Amount = b.Amount,
                        Used = MdrFinanceService.BudgetUsed(db, b.CostCenterID, b.FiscalYear, null)
                    }).ToList(),
                    Periods = db.FinPeriods.OrderByDescending(p => p.FiscalYear).ThenByDescending(p => p.Month).Take(36).ToList()
                        .Select(p => new ConfigPeriod { FiscalYear = p.FiscalYear, Month = p.Month, ClosedAt = DT(p.ClosedAt), Reason = p.Reason }).ToList()
                };
            }
        }

        // ---------------- داشبورد ----------------
        public static FinDashboard Dashboard(int uid, DateTime from, DateTime to, bool personal)
        {
            using (var db = new MdrContext())
            {
                var manager = MdrFinanceService.IsManager(uid);
                var all = manager && !personal;
                var f = from.Date; var t = to.Date.AddDays(1);
                var docs = db.FinDocuments.Where(d => d.DocDate >= f && d.DocDate < t && (all || d.RequesterUserID == uid)).ToList();

                Func<string, int> cnt = s => docs.Count(d => d.Status == s);
                var paid = docs.Where(d => d.Status == "Paid").ToList();
                var done = docs.Where(d => (d.Status == "Approved" || d.Status == "Paid") && d.SubmittedAt.HasValue && d.DecidedAt.HasValue).Select(d => (d.DecidedAt.Value - d.SubmittedAt.Value).TotalHours).ToList();
                var kpi = new Dictionary<string, long?>
                {
                    { "draft", cnt("Draft") }, { "submitted", cnt("Submitted") }, { "approved", cnt("Approved") }, { "rejected", cnt("Rejected") }, { "paid", cnt("Paid") },
                    { "paymentPaid", paid.Where(d => d.Direction == "Payment").Sum(d => d.Amount) },
                    { "receiptPaid", paid.Where(d => d.Direction == "Receipt").Sum(d => d.Amount) },
                    { "pendingAmount", docs.Where(d => d.Status == "Submitted" && d.Direction == "Payment").Sum(d => d.Amount) },
                    { "approvedUnpaidAmount", docs.Where(d => d.Status == "Approved" && d.Direction == "Payment").Sum(d => d.Amount) },
                    { "avgApprovalH", done.Count == 0 ? (long?)null : (long)Math.Round(done.Average()) }
                };

                var days = (int)(t - f).TotalDays; var step = days <= 31 ? 1 : 7; var n = (days + step - 1) / step;
                var trend = Enumerable.Range(0, n).Select(i => new FinTrend { Label = f.AddDays(i * step).ToString("yyyy-MM-dd") }).ToList();
                foreach (var d in paid)
                {
                    var b = (int)(d.DocDate - f).TotalDays / step;
                    if (b < 0 || b >= n) continue;
                    if (d.Direction == "Payment") trend[b].Payment += d.Amount; else trend[b].Receipt += d.Amount;
                }

                var res = new FinDashboard { From = D(from), To = D(to), Personal = personal, IsManager = manager, Kpi = kpi, Trend = trend, Budgets = new List<FinBudgetRow>(), Alerts = new Dictionary<string, int>() };

                if (all)
                {
                    var units = Units(db); var year = MdrFinanceService.FiscalYear(to);
                    foreach (var b in db.FinBudgets.Where(x => x.FiscalYear == year).ToList())
                    {
                        var used = MdrFinanceService.BudgetUsed(db, b.CostCenterID, year, null);
                        res.Budgets.Add(new FinBudgetRow { CostCenterID = b.CostCenterID, CostCenter = units.ContainsKey(b.CostCenterID) ? units[b.CostCenterID] : null, Budget = b.Amount, Used = used, Pct = b.Amount == 0 ? (used > 0 ? 999 : 0) : (int)Math.Min(999, 100 * used / b.Amount) });
                    }
                    res.Alerts["budgetNear"] = res.Budgets.Count(x => x.Pct >= 90 && x.Pct < 100);
                    res.Alerts["budgetOver"] = res.Budgets.Count(x => x.Pct >= 100);
                }
                var limit = DateTime.Now.AddHours(-PendingSlaHours);
                res.Alerts["longPending"] = db.FinDocuments.Count(d => d.Status == "Submitted" && d.SubmittedAt < limit && (all || d.RequesterUserID == uid));

                var pend = db.FinDocuments.Where(d => (d.Status == "Submitted" || d.Status == "Approved") && (all || d.RequesterUserID == uid)).Select(d => new { d.DocDate }).ToList();
                var closed = db.FinPeriods.ToList();
                res.Alerts["closedPeriodPending"] = pend.Count(p => closed.Any(c => c.FiscalYear == MdrFinanceService.FiscalYear(p.DocDate) && c.Month == MdrFinanceService.FiscalMonth(p.DocDate)));

                var dupGroups = docs.Where(d => d.Status == "Submitted" || d.Status == "Approved").GroupBy(d => new { d.Direction, d.Amount, d.DocDate }).Count(g => g.Count() > 1);
                res.Alerts["duplicates"] = dupGroups;

                if (all) res.Legacy = LegacyBlockFor(db, f, t);
                return res;
            }
        }

        private static LegacyBlock LegacyBlockFor(MdrContext db, DateTime f, DateTime t)
        {
            var p = new object[] { f, t };
            var sums = db.Database.SqlQuery<LegacySum>("SELECT COUNT(*) AS N, ISNULL(SUM(Debtore), 0) AS Debtor, ISNULL(SUM(Creditor), 0) AS Creditor FROM dbo.FinancialDocuments WHERE DataDocument >= @p0 AND DataDocument < @p1", p).First();
            var trend = db.Database.SqlQuery<LegacyTrend>("SELECT CONVERT(CHAR(7), DataDocument, 126) AS Month, ISNULL(SUM(Debtore), 0) AS Debtor, ISNULL(SUM(Creditor), 0) AS Creditor FROM dbo.FinancialDocuments " +
                "WHERE DataDocument >= @p0 AND DataDocument < @p1 GROUP BY CONVERT(CHAR(7), DataDocument, 126) ORDER BY 1", new object[] { f, t }).ToList();
            var top = db.Database.SqlQuery<LegacyTop>("SELECT TOP 10 fd.Title AS Title, ISNULL(SUM(fd.Debtore), 0) AS Debtor, ISNULL(SUM(fd.Creditor), 0) AS Creditor, COUNT(*) AS Count " +
                "FROM dbo.FinancialDocuments fd WHERE fd.DataDocument >= @p0 AND fd.DataDocument < @p1 " +
                "GROUP BY fd.Title ORDER BY SUM(ISNULL(fd.Debtore, 0) + ISNULL(fd.Creditor, 0)) DESC", new object[] { f, t }).ToList();
            return new LegacyBlock { Count = sums.N, Debtor = sums.Debtor, Creditor = sums.Creditor, Net = sums.Creditor - sums.Debtor, Trend = trend, Top = top };
        }
        private class LegacySum { public int N { get; set; } public double Debtor { get; set; } public double Creditor { get; set; } }

        // ---------------- drill-down ----------------
        // metric: status.<Draft|Submitted|Approved|Rejected|Paid> | pendingPayments | approvedUnpaid | longPending
        public static List<FinRow> Drill(int uid, string metric, DateTime from, DateTime to, bool personal, out string error)
        {
            error = null;
            using (var db = new MdrContext())
            {
                var all = MdrFinanceService.IsManager(uid) && !personal;
                var f = from.Date; var t = to.Date.AddDays(1);
                var q = db.FinDocuments.Where(d => all || d.RequesterUserID == uid);
                if (metric != null && metric.StartsWith("status.") && new[] { "Draft", "Submitted", "Approved", "Rejected", "Paid" }.Contains(metric.Substring(7)))
                { var s = metric.Substring(7); q = q.Where(d => d.Status == s && d.DocDate >= f && d.DocDate < t); }
                else if (metric == "pendingPayments") q = q.Where(d => d.Status == "Submitted" && d.Direction == "Payment" && d.DocDate >= f && d.DocDate < t);
                else if (metric == "approvedUnpaid") q = q.Where(d => d.Status == "Approved" && d.Direction == "Payment" && d.DocDate >= f && d.DocDate < t);
                else if (metric == "longPending") { var lim = DateTime.Now.AddHours(-PendingSlaHours); q = q.Where(d => d.Status == "Submitted" && d.SubmittedAt < lim); }
                else { error = "rep.err.metric"; return new List<FinRow>(); }

                var rows = q.OrderByDescending(d => d.DocumentID).Take(100).ToList();
                var names = MdrChatService.Names(db, rows.Select(r => r.RequesterUserID)); var units = Units(db);
                return rows.Select(d => new FinRow
                {
                    DocumentID = d.DocumentID, Number = d.Number, Direction = d.Direction, Title = d.Title, Amount = d.Amount, DocDate = D(d.DocDate), Status = d.Status, CurrentLevel = d.CurrentLevel,
                    Requester = names.ContainsKey(d.RequesterUserID) ? names[d.RequesterUserID] : null,
                    CostCenter = d.CostCenterID.HasValue && units.ContainsKey(d.CostCenterID.Value) ? units[d.CostCenterID.Value] : null
                }).ToList();
            }
        }

        // اسناد قدیمی (فقط‌خواندنی؛ فقط مدیر مالی) — حداکثر ۱۰۰ ردیف؛ ردیف‌های با مبلغ صفر هم نمایش داده می‌شوند؛ title خالی = همه‌ی عنوان‌ها
        public static List<LegacyDocRow> LegacyDocs(int uid, string title, DateTime from, DateTime to)
        {
            if (!MdrFinanceService.IsManager(uid)) return null;
            using (var db = new MdrContext())
            {
                var f = from.Date; var t = to.Date.AddDays(1);
                var hasTitle = !string.IsNullOrEmpty(title);
                var where = hasTitle ? "AND Title = @p2" : "";
                var ps = hasTitle ? new object[] { f, t, title } : new object[] { f, t };
                return db.Database.SqlQuery<LegacyDocRow>("SELECT TOP 100 ID, Title, DataDocument, Debtore, Creditor FROM dbo.FinancialDocuments WHERE DataDocument >= @p0 AND DataDocument < @p1 " + where + " ORDER BY DataDocument DESC, ID DESC", ps).ToList();
            }
        }
    }
}
