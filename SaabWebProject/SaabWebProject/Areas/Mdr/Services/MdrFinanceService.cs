using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Globalization;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 7 | مالی و حسابداری (پنل ۱۳) — اسناد و پرداخت
    // وضعیت: Draft → Submitted → Approved → Paid ؛ Rejected ؛ Cancelled.  «برگشت» سند را به Draft برمی‌گرداند (با دلیل).
    // قواعد کلیدی:
    //  - هیچ سقف/آستانه‌ی مالی در کد نیست؛ سطح‌های تأیید و MinAmount از MDR_FinApprovers (داده‌ی پیکربندی) می‌آیند.
    //    اگر هیچ تأییدکننده‌ای تعریف نشده باشد، سند ارسال نمی‌شود.
    //  - تفکیک وظایف: درخواست‌کننده سند خودش را تأیید/پرداخت نمی‌کند؛ یک نفر دو سطح یک سند را تأیید نمی‌کند؛
    //    پرداخت‌کننده نباید تأییدکننده‌ی همان سند باشد و حق مستقل MDR.Finance.Pay لازم دارد.
    //  - دوره‌ی بسته (سال/ماه شمسیِ تاریخ سند): ایجاد/ویرایش/ارسال/تأیید/پرداخت ممنوع.
    //  - بودجه: اگر برای مرکز هزینه و سال شمسی بودجه تعریف شده باشد، مجموع اسناد پرداختِ (در انتظار + تأییدشده + پرداخت‌شده) از آن نمی‌گذرد.
    //  - سند تکراری (جهت، مبلغ، تاریخ + شماره‌ی مرجع/طرف حساب/عنوان یکسان) فقط با تأیید صریح ارسال می‌شود.
    //  - هر تغییر: History (CorrelationID) + Audit (با مبلغ)؛ Concurrent update با RowVer.
    //  - «پرداخت» فقط ثبت واقعیت است؛ هیچ انتقال وجهی از این ماژول انجام نمی‌شود.
    // =====================================================
    public static class MdrFinanceService
    {
        private const string Module = "Finance";
        public const long MaxAmount = 10000000000000L;        // سقف فنی برای جلوگیری از سرریز، نه سقف مالی
        public const string DocsPage = "MDR.Finance.Docs";
        public const string PayPage = "MDR.Finance.Pay";
        public static readonly string[] Directions = { "Payment", "Receipt" };
        private static readonly PersianCalendar Pc = new PersianCalendar();

        public class FinInput
        {
            public string Direction { get; set; }
            public string Title { get; set; }
            public long Amount { get; set; }
            public DateTime DocDate { get; set; }
            public int? CostCenterId { get; set; }
            public int? PeymanId { get; set; }
            public string Payee { get; set; }
            public string ReferenceNo { get; set; }
        }

        public class FinAccess
        {
            public bool CanView, IsRequester, IsManager, IsApprover;
        }

        // ---------------- تاریخ شمسی ----------------
        public static int FiscalYear(DateTime d) { return Pc.GetYear(d); }
        public static int FiscalMonth(DateTime d) { return Pc.GetMonth(d); }
        public static DateTime YearStart(int year) { return Pc.ToDateTime(year, 1, 1, 0, 0, 0, 0); }
        public static DateTime YearEndExclusive(int year) { return Pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0); }

        public static bool IsManager(int uid) { return PermissionHelper.HasAccess(uid, DocsPage) && PermissionHelper.CanEdit(uid, DocsPage); }

        // ---------------- دسترسی ----------------
        public static FinAccess AccessFor(MdrContext db, MdrFinDocument d, int uid)
        {
            var a = new FinAccess { IsRequester = d.RequesterUserID == uid, IsManager = IsManager(uid) };
            a.IsApprover = db.FinApprovals.Any(x => x.DocumentID == d.DocumentID && x.DecidedBy == uid) ||
                           (d.Status == "Submitted" && d.CurrentLevel.HasValue && Eligible(db, d, d.CurrentLevel.Value, uid));
            var payer = (d.Status == "Approved" || d.Status == "Paid") && PermissionHelper.HasAccess(uid, PayPage);
            a.CanView = a.IsRequester || a.IsManager || a.IsApprover || payer;
            return a;
        }

        private static List<MdrFinApprover> LevelApprovers(MdrContext db, MdrFinDocument d, int level)
        {
            var rows = db.FinApprovers.Where(x => x.IsActive && x.Level == level && x.CostCenterID != null && x.CostCenterID == d.CostCenterID).ToList();
            if (rows.Count == 0) rows = db.FinApprovers.Where(x => x.IsActive && x.Level == level && x.CostCenterID == null).ToList();
            return rows.Where(x => !x.MinAmount.HasValue || d.Amount >= x.MinAmount.Value).ToList();
        }

        public static bool Eligible(MdrContext db, MdrFinDocument d, int level, int uid)
        {
            return LevelApprovers(db, d, level).Any(x => x.UserID == uid);
        }

        public static List<int> ComputeLevels(MdrContext db, MdrFinDocument d)
        {
            return Enumerable.Range(1, 5).Where(l => LevelApprovers(db, d, l).Count > 0).ToList();
        }

        public static bool IsClosed(MdrContext db, DateTime date)
        {
            var y = FiscalYear(date); var m = FiscalMonth(date);
            return db.FinPeriods.Any(p => p.FiscalYear == y && p.Month == m);
        }

        // مصرف بودجه‌ی یک مرکز هزینه در سال شمسی (پرداخت‌های در انتظار/تأییدشده/پرداخت‌شده)
        public static long BudgetUsed(MdrContext db, int costCenterId, int year, long? excludeDocId)
        {
            var s = YearStart(year); var e = YearEndExclusive(year);
            return db.FinDocuments.Where(x => x.Direction == "Payment" && x.CostCenterID == costCenterId && x.DocDate >= s && x.DocDate < e &&
                                              (x.Status == "Submitted" || x.Status == "Approved" || x.Status == "Paid") && (!excludeDocId.HasValue || x.DocumentID != excludeDocId.Value))
                .Select(x => (long?)x.Amount).Sum() ?? 0;
        }

        private static string NormText(string s) { return MdrCaseService.Norm(s); }

        public static List<MdrFinDocument> FindDuplicates(MdrContext db, MdrFinDocument d)
        {
            var cands = db.FinDocuments.Where(x => x.DocumentID != d.DocumentID && x.Direction == d.Direction && x.Amount == d.Amount && x.DocDate == d.DocDate &&
                                                   x.Status != "Cancelled" && x.Status != "Rejected" && x.Status != "Draft").Take(200).ToList();
            var t = NormText(d.Title); var p = NormText(d.Payee); var r = NormText(d.ReferenceNo);
            return cands.Where(x => (r.Length > 0 && NormText(x.ReferenceNo) == r) || (p.Length > 0 && NormText(x.Payee) == p) || NormText(x.Title) == t).ToList();
        }

        // ---------------- ثبت / ویرایش ----------------
        public static MdrResult Save(long id, FinInput i, int actorId, byte[] rowVer)
        {
            var err = Validate(i);
            if (err != null) return MdrResult.Fail(err);

            try
            {
                using (var db = new MdrContext())
                {
                    string chk = CheckRefs(db, i);
                    if (chk != null) return MdrResult.Fail(chk);
                    if (IsClosed(db, i.DocDate)) return MdrResult.Fail("fin.err.period");

                    MdrFinDocument d;
                    if (id == 0)
                    {
                        d = new MdrFinDocument { Status = "Draft", RequesterUserID = actorId, CreatedAt = DateTime.Now };
                        Apply(d, i);
                        db.FinDocuments.Add(d);
                        db.SaveChanges();
                        d.Number = "FIN-" + FiscalYear(DateTime.Now) + "-" + d.DocumentID.ToString("D6");
                        Log(db, d.DocumentID, "Create", actorId, null);
                        db.SaveChanges();
                        AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد سند مالی", entityName: "MDR_FinDocuments", entityId: d.DocumentID, entityTitle: d.Number,
                            newValues: new { d.Direction, d.Amount, d.CostCenterID });
                        return MdrResult.Success(d.DocumentID);
                    }

                    d = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null || d.RequesterUserID != actorId) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !d.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (d.Status != "Draft") return MdrResult.Fail("fin.err.state");
                    if (IsClosed(db, d.DocDate)) return MdrResult.Fail("fin.err.period");
                    var before = new { d.Amount, d.Title, d.CostCenterID };
                    Apply(d, i);
                    Log(db, id, "Edit", actorId, null);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ویرایش سند مالی", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number,
                        oldValues: before, newValues: new { d.Amount, d.Title, d.CostCenterID });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        private static void Apply(MdrFinDocument d, FinInput i)
        {
            d.Direction = i.Direction; d.Title = i.Title.Trim(); d.Amount = i.Amount; d.DocDate = i.DocDate.Date; d.CostCenterID = i.CostCenterId; d.PeymanID = i.PeymanId;
            d.Payee = Trim(i.Payee, 200); d.ReferenceNo = Trim(i.ReferenceNo, 50);
        }

        private static string Validate(FinInput i)
        {
            if (!Directions.Contains(i.Direction ?? "")) return "fin.err.direction";
            var title = (i.Title ?? "").Trim();
            if (title.Length == 0 || title.Length > 300) return "fin.err.title";
            if (i.Amount <= 0 || i.Amount > MaxAmount) return "fin.err.amount";
            if (i.DocDate.Date < DateTime.Today.AddYears(-5) || i.DocDate.Date > DateTime.Today.AddYears(1)) return "fin.err.date";
            if (i.Direction == "Payment" && !i.CostCenterId.HasValue) return "fin.err.costcenter";
            return null;
        }

        private static string CheckRefs(MdrContext db, FinInput i)
        {
            if (i.CostCenterId.HasValue && !db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.MDR_OrganizationUnits WHERE OrganizationUnitID = @p0", i.CostCenterId.Value).First().Equals(1)) return "fin.err.costcenter";
            if (i.PeymanId.HasValue && db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Contract.tbPeymanContracts WHERE pec_ID = @p0", i.PeymanId.Value).First() == 0) return "fin.err.peyman";
            return null;
        }

        // ---------------- گردش ----------------
        public static MdrResult Submit(long id, int actorId, bool confirmDuplicate, byte[] rowVer)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null || d.RequesterUserID != actorId) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !d.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (d.Status != "Draft") return MdrResult.Fail("fin.err.state");
                    if (IsClosed(db, d.DocDate)) return MdrResult.Fail("fin.err.period");

                    var dups = FindDuplicates(db, d);
                    if (dups.Count > 0 && !confirmDuplicate) return MdrResult.Fail("fin.err.duplicate");

                    if (d.Direction == "Payment" && d.CostCenterID.HasValue)
                    {
                        var year = FiscalYear(d.DocDate); var cc = d.CostCenterID.Value;
                        var b = db.FinBudgets.FirstOrDefault(x => x.CostCenterID == cc && x.FiscalYear == year);
                        if (b != null && BudgetUsed(db, cc, year, d.DocumentID) + d.Amount > b.Amount) return MdrResult.Fail("fin.err.budget");
                    }

                    var levels = ComputeLevels(db, d);
                    if (levels.Count == 0) return MdrResult.Fail("fin.err.noapprover");

                    foreach (var l in levels) db.FinApprovals.Add(new MdrFinApproval { DocumentID = id, Level = l, Status = "Pending" });
                    d.Status = "Submitted"; d.CurrentLevel = levels[0]; d.SubmittedAt = DateTime.Now; d.ReturnReason = null;
                    Log(db, id, "Submit", actorId, dups.Count > 0 ? "duplicate-confirmed=" + dups.Count : null);
                    db.SaveChanges();

                    NotifyApprovers(db, d, d.CurrentLevel.Value);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ارسال سند مالی", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number,
                        newValues: new { d.Amount, levels = levels.Count, duplicates = dups.Count });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        private class Ctx { public MdrFinDocument Doc; public MdrFinApproval Step; }

        // سند Submitted و مرحله‌ی جاری که actor مجاز به تصمیم روی آن است
        private static MdrResult Gate(MdrContext db, long id, int actorId, byte[] rowVer, out Ctx c)
        {
            c = null;
            var d = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
            if (d == null) return MdrResult.Fail("wf.err.notfound");
            var a = AccessFor(db, d, actorId);
            if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
            if (rowVer != null && !d.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
            if (d.Status != "Submitted" || !d.CurrentLevel.HasValue) return MdrResult.Fail("fin.err.state");
            if (d.RequesterUserID == actorId) return MdrResult.Fail("fin.err.self");
            if (!Eligible(db, d, d.CurrentLevel.Value, actorId)) return MdrResult.Fail("tsk.err.notallowed");
            if (db.FinApprovals.Any(x => x.DocumentID == id && x.DecidedBy == actorId && x.Status == "Approved")) return MdrResult.Fail("fin.err.sameapprover");
            if (IsClosed(db, d.DocDate)) return MdrResult.Fail("fin.err.period");
            var lvl = d.CurrentLevel.Value;
            c = new Ctx { Doc = d, Step = db.FinApprovals.First(x => x.DocumentID == id && x.Level == lvl && x.Status == "Pending") };
            return null;
        }

        public static MdrResult Approve(long id, string comment, int actorId, byte[] rowVer)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    Ctx c; var g = Gate(db, id, actorId, rowVer, out c);
                    if (g != null) return g;
                    var d = c.Doc;
                    c.Step.Status = "Approved"; c.Step.DecidedBy = actorId; c.Step.DecidedAt = DateTime.Now; c.Step.Comment = Trim(comment, 500);

                    var next = db.FinApprovals.Where(x => x.DocumentID == id && x.Status == "Pending" && x.Level != c.Step.Level).OrderBy(x => x.Level).FirstOrDefault();
                    if (next != null) d.CurrentLevel = next.Level;
                    else { d.Status = "Approved"; d.CurrentLevel = null; d.DecidedAt = DateTime.Now; }
                    d.ReturnReason = null;
                    Log(db, id, "Approve", actorId, "L" + c.Step.Level);
                    db.SaveChanges();

                    if (next != null) NotifyApprovers(db, d, next.Level);
                    else Notify(d.RequesterUserID, "سند مالی تأیید نهایی شد: " + d.Number, d);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "تأیید سند مالی (سطح " + c.Step.Level + ")", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number,
                        newValues: new { d.Amount, d.Status, level = c.Step.Level });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // برگشت به درخواست‌کننده برای اصلاح (دلیل اجباری)؛ مراحل تأیید پاک می‌شوند و با ارسال دوباره از اول ساخته می‌شوند
        public static MdrResult Return(long id, string reason, int actorId, byte[] rowVer)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    Ctx c; var g = Gate(db, id, actorId, rowVer, out c);
                    if (g != null) return g;
                    var d = c.Doc;
                    db.FinApprovals.RemoveRange(db.FinApprovals.Where(x => x.DocumentID == id).ToList());
                    d.Status = "Draft"; d.CurrentLevel = null; d.SubmittedAt = null; d.ReturnReason = Trim(reason, 500);
                    Log(db, id, "Return", actorId, "L" + c.Step.Level + " | " + d.ReturnReason);
                    db.SaveChanges();
                    Notify(d.RequesterUserID, "سند مالی برای اصلاح برگشت خورد: " + d.Number, d);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "برگشت سند مالی", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number, newValues: new { reason = d.ReturnReason, level = c.Step.Level });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Reject(long id, string reason, int actorId, byte[] rowVer)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    Ctx c; var g = Gate(db, id, actorId, rowVer, out c);
                    if (g != null) return g;
                    var d = c.Doc;
                    c.Step.Status = "Rejected"; c.Step.DecidedBy = actorId; c.Step.DecidedAt = DateTime.Now; c.Step.Comment = Trim(reason, 500);
                    d.Status = "Rejected"; d.CurrentLevel = null; d.DecidedAt = DateTime.Now; d.ReturnReason = Trim(reason, 500);
                    Log(db, id, "Reject", actorId, "L" + c.Step.Level + " | " + d.ReturnReason);
                    db.SaveChanges();
                    Notify(d.RequesterUserID, "سند مالی رد شد: " + d.Number, d);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "رد سند مالی", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number, newValues: new { reason = d.ReturnReason, level = c.Step.Level });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Cancel(long id, string reason, int actorId)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null || d.RequesterUserID != actorId) return MdrResult.Fail("wf.err.notfound");
                    if (d.Status != "Draft") return MdrResult.Fail("fin.err.state");
                    d.Status = "Cancelled"; d.DecidedAt = DateTime.Now;
                    Log(db, id, "Cancel", actorId, Trim(reason, 500));
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "لغو سند مالی", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number, newValues: new { reason });
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ثبت پرداخت/تسویه — فقط ثبت واقعیت (بدون انتقال وجه)؛ حق مستقل لازم دارد
        public static MdrResult Pay(long id, string paymentRef, int actorId, byte[] rowVer)
        {
            paymentRef = (paymentRef ?? "").Trim();
            if (paymentRef.Length == 0 || paymentRef.Length > 50) return MdrResult.Fail("fin.err.payref");
            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    if (!PermissionHelper.HasAccess(actorId, PayPage)) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !d.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (d.Status != "Approved") return MdrResult.Fail("fin.err.state");
                    if (d.RequesterUserID == actorId) return MdrResult.Fail("fin.err.self");
                    if (db.FinApprovals.Any(x => x.DocumentID == id && x.DecidedBy == actorId)) return MdrResult.Fail("fin.err.payerapprover");
                    if (IsClosed(db, d.DocDate)) return MdrResult.Fail("fin.err.period");

                    d.Status = "Paid"; d.PaidAt = DateTime.Now; d.PaidBy = actorId; d.PaymentRef = paymentRef;
                    Log(db, id, "Pay", actorId, paymentRef);
                    db.SaveChanges();
                    Notify(d.RequesterUserID, (d.Direction == "Payment" ? "پرداخت ثبت شد: " : "دریافت ثبت شد: ") + d.Number, d);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, d.Direction == "Payment" ? "ثبت پرداخت" : "ثبت دریافت", entityName: "MDR_FinDocuments", entityId: id, entityTitle: d.Number,
                        newValues: new { d.Amount, paymentRef });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- پیکربندی (فقط مدیر مالی) ----------------
        public static MdrResult AddApprover(int actorId, int? costCenterId, int level, int userId, long? minAmount)
        {
            if (!IsManager(actorId)) return MdrResult.Fail("tsk.err.notallowed");
            if (level < 1 || level > 5) return MdrResult.Fail("fin.err.level");
            if (minAmount.HasValue && minAmount.Value < 0) return MdrResult.Fail("fin.err.amount");
            try
            {
                using (var db = new MdrContext())
                {
                    if (!db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", userId).First().Equals(1)) return MdrResult.Fail("wf.err.baduser");
                    if (costCenterId.HasValue && !db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.MDR_OrganizationUnits WHERE OrganizationUnitID = @p0", costCenterId.Value).First().Equals(1)) return MdrResult.Fail("fin.err.costcenter");

                    var e = db.FinApprovers.FirstOrDefault(x => x.CostCenterID == costCenterId && x.Level == level && x.UserID == userId);
                    if (e != null && e.IsActive) return MdrResult.Fail("fin.err.dupapprover");
                    if (e == null) { e = new MdrFinApprover { CostCenterID = costCenterId, Level = level, UserID = userId, CreatedBy = actorId, CreatedAt = DateTime.Now }; db.FinApprovers.Add(e); }
                    e.IsActive = true; e.MinAmount = minAmount;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "تعریف تأییدکننده‌ی مالی", entityName: "MDR_FinApprovers", entityId: e.ApproverID, newValues: new { costCenterId, level, userId, minAmount });
                    return MdrResult.Success(e.ApproverID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult RemoveApprover(int actorId, int approverId)
        {
            if (!IsManager(actorId)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    var e = db.FinApprovers.FirstOrDefault(x => x.ApproverID == approverId && x.IsActive);
                    if (e == null) return MdrResult.Fail("wf.err.notfound");
                    e.IsActive = false;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "حذف تأییدکننده‌ی مالی", entityName: "MDR_FinApprovers", entityId: approverId, oldValues: new { e.CostCenterID, e.Level, e.UserID });
                    return MdrResult.Success(approverId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult SetBudget(int actorId, int costCenterId, int year, long amount)
        {
            if (!IsManager(actorId)) return MdrResult.Fail("tsk.err.notallowed");
            if (amount < 0 || amount > MaxAmount) return MdrResult.Fail("fin.err.amount");
            if (year < 1300 || year > 1500) return MdrResult.Fail("fin.err.year");
            try
            {
                using (var db = new MdrContext())
                {
                    if (!db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.MDR_OrganizationUnits WHERE OrganizationUnitID = @p0", costCenterId).First().Equals(1)) return MdrResult.Fail("fin.err.costcenter");
                    var b = db.FinBudgets.FirstOrDefault(x => x.CostCenterID == costCenterId && x.FiscalYear == year);
                    var old = b == null ? (long?)null : b.Amount;
                    if (b == null) { b = new MdrFinBudget { CostCenterID = costCenterId, FiscalYear = year }; db.FinBudgets.Add(b); }
                    b.Amount = amount; b.UpdatedBy = actorId; b.UpdatedAt = DateTime.Now;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "تعیین بودجه", entityName: "MDR_FinBudgets", entityId: b.BudgetID, oldValues: new { Amount = old }, newValues: new { costCenterId, year, amount });
                    return MdrResult.Success(b.BudgetID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // بستن/بازکردن دوره (ماه شمسی)؛ بازکردن دلیل اجباری دارد
        public static MdrResult ClosePeriod(int actorId, int year, int month, string reason)
        {
            if (!IsManager(actorId)) return MdrResult.Fail("tsk.err.notallowed");
            if (month < 1 || month > 12 || year < 1300 || year > 1500) return MdrResult.Fail("fin.err.year");
            try
            {
                using (var db = new MdrContext())
                {
                    if (db.FinPeriods.Any(p => p.FiscalYear == year && p.Month == month)) return MdrResult.Fail("fin.err.periodclosed");
                    var period = new MdrFinPeriod { FiscalYear = year, Month = month, ClosedBy = actorId, ClosedAt = DateTime.Now, Reason = Trim(reason, 300) };
                    db.FinPeriods.Add(period);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "بستن دوره‌ی مالی", entityName: "MDR_FinPeriods", entityId: period.PeriodID, newValues: new { year, month });
                    return MdrResult.Success(period.PeriodID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult ReopenPeriod(int actorId, int year, int month, string reason)
        {
            if (!IsManager(actorId)) return MdrResult.Fail("tsk.err.notallowed");
            if (string.IsNullOrWhiteSpace(reason)) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    var p = db.FinPeriods.FirstOrDefault(x => x.FiscalYear == year && x.Month == month);
                    if (p == null) return MdrResult.Fail("wf.err.notfound");
                    db.FinPeriods.Remove(p);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "بازگشایی دوره‌ی مالی", entityName: "MDR_FinPeriods", entityId: p.PeriodID, newValues: new { year, month, reason = reason.Trim() });
                    return MdrResult.Success(p.PeriodID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- helpers ----------------
        private static string Trim(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        private static void Log(MdrContext db, long id, string action, int? actor, string detail)
        {
            db.FinHistory.Add(new MdrFinHistory { DocumentID = id, ActionKey = action, ActorUserID = actor, Detail = Trim(detail, 500), CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now });
        }

        // اعلان هرگز مبلغ ندارد (فقط شماره‌ی سند)
        private static void Notify(int userId, string title, MdrFinDocument d)
        {
            NotificationService.Send(userId, title, null, moduleName: Module, entityName: "MDR_FinDocuments", entityId: d.DocumentID, linkUrl: "/Mdr/Finance/Documents#id=" + d.DocumentID);
        }

        private static void NotifyApprovers(MdrContext db, MdrFinDocument d, int level)
        {
            foreach (var u in LevelApprovers(db, d, level).Select(x => x.UserID).Distinct().Where(x => x != d.RequesterUserID))
                Notify(u, "سند مالی در انتظار تأیید شماست: " + d.Number, d);
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای سند مالی", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
