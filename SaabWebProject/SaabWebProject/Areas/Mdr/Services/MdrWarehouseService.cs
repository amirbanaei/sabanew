using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 7 | انبارداری (پنل ۱۴)
    // - موجودی = مجموع Qty علامت‌دار دفتر گردش (MDR_WhMovements)؛ دفتر فقط افزودنی است و هیچ ردیفی ویرایش/حذف نمی‌شود.
    //   اصلاح فقط با Adjust (مدیر + دلیل اجباری + Audit).
    // - موجودی هرگز منفی نمی‌شود: Issue/TransferOut/Adjust منفی زیر قفل (UPDLOCK) همان کالا و در یک تراکنش بررسی می‌شوند
    //   تا دو حواله‌ی هم‌زمان از یک موجودی هم‌زمان خارج نشوند.
    // - عملیات انبار (کالا/انبار/رسید/حواله/انتقال/اصلاح/صدور درخواست) فقط انباردار (CanEdit صفحه‌ی MDR.Warehouse)؛
    //   درخواست کالا برای هر کاربر دارای دسترسی به صفحه.
    // - کالای تکراری (همان کد یا همان نام نرمال‌شده) هشدار می‌دهد؛ کد تکراری هرگز مجاز نیست.
    // =====================================================
    public static class MdrWarehouseService
    {
        private const string Module = "Warehouse";
        public const string Page = "MDR.Warehouse";
        public const decimal MaxQty = 1000000000m;

        public static bool IsManager(int uid) { return PermissionHelper.HasAccess(uid, Page) && PermissionHelper.CanEdit(uid, Page); }

        private static string Fmt(decimal d) { return d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
        private static string NormCode(string s) { return (s ?? "").Trim().ToUpperInvariant(); }

        // ---------------- کالا و انبار ----------------
        public static MdrResult CreateStore(int actor, string name, int? managerUserId)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 150) return MdrResult.Fail("wh.err.name");
            try
            {
                using (var db = new MdrContext())
                {
                    var n = MdrCaseService.Norm(name);
                    if (db.WhStores.ToList().Any(s => MdrCaseService.Norm(s.Name) == n)) return MdrResult.Fail("wh.err.dupstore");
                    if (managerUserId.HasValue && !UserOk(db, managerUserId.Value)) return MdrResult.Fail("wf.err.baduser");
                    var s2 = new MdrWhStore { Name = name, ManagerUserID = managerUserId, IsActive = true, CreatedAt = DateTime.Now };
                    db.WhStores.Add(s2); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد انبار", entityName: "MDR_WhStores", entityId: s2.StoreID, entityTitle: name);
                    return MdrResult.Success(s2.StoreID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult CreateItem(int actor, string code, string name, string unit, decimal? reorderPoint, bool confirmDuplicateName)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            code = NormCode(code); name = (name ?? "").Trim(); unit = (unit ?? "").Trim();
            if (code.Length == 0 || code.Length > 40) return MdrResult.Fail("wh.err.code");
            if (name.Length == 0 || name.Length > 200) return MdrResult.Fail("wh.err.name");
            if (unit.Length == 0 || unit.Length > 20) return MdrResult.Fail("wh.err.unit");
            if (reorderPoint.HasValue && (reorderPoint.Value < 0 || reorderPoint.Value > MaxQty)) return MdrResult.Fail("wh.err.qty");
            try
            {
                using (var db = new MdrContext())
                {
                    if (db.WhItems.Any(i => i.Code == code)) return MdrResult.Fail("wh.err.dupcode");
                    var n = MdrCaseService.Norm(name);
                    if (!confirmDuplicateName && db.WhItems.ToList().Any(i => MdrCaseService.Norm(i.Name) == n)) return MdrResult.Fail("wh.err.dupname");
                    var it = new MdrWhItem { Code = code, Name = name, Unit = unit, ReorderPoint = reorderPoint, IsActive = true, CreatedBy = actor, CreatedAt = DateTime.Now };
                    db.WhItems.Add(it); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد کالا", entityName: "MDR_WhItems", entityId: it.ItemID, entityTitle: code + " — " + name);
                    return MdrResult.Success(it.ItemID);
                }
            }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("wh.err.dupcode") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult UpdateItem(int actor, int itemId, string name, decimal? reorderPoint, bool isActive)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 200) return MdrResult.Fail("wh.err.name");
            if (reorderPoint.HasValue && (reorderPoint.Value < 0 || reorderPoint.Value > MaxQty)) return MdrResult.Fail("wh.err.qty");
            try
            {
                using (var db = new MdrContext())
                {
                    var it = db.WhItems.FirstOrDefault(i => i.ItemID == itemId);
                    if (it == null) return MdrResult.Fail("wf.err.notfound");
                    var before = new { it.Name, it.ReorderPoint, it.IsActive };
                    it.Name = name; it.ReorderPoint = reorderPoint; it.IsActive = isActive;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ویرایش کالا", entityName: "MDR_WhItems", entityId: itemId, entityTitle: it.Code, oldValues: before, newValues: new { it.Name, it.ReorderPoint, it.IsActive });
                    return MdrResult.Success(itemId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- موجودی ----------------
        public static decimal Stock(MdrContext db, int itemId, int? storeId)
        {
            var q = db.WhMovements.Where(m => m.ItemID == itemId);
            if (storeId.HasValue) q = q.Where(m => m.StoreID == storeId.Value);
            return q.Select(m => (decimal?)m.Qty).Sum() ?? 0m;
        }

        private static void Lock(MdrContext db, int itemId)
        {
            db.Database.SqlQuery<int>("SELECT ItemID FROM dbo.MDR_WhItems WITH (UPDLOCK, ROWLOCK) WHERE ItemID = @p0", itemId).FirstOrDefault();
        }

        // ---------------- گردش ----------------
        private class MoveSpec { public string Kind; public int StoreId; public decimal Qty; public Guid? Pair; public long? RequestId; }

        private static MdrResult Check(MdrContext db, int itemId, int storeId, decimal qty, bool needsStock, bool allowInactiveItem = false)
        {
            if (qty <= 0 || qty > MaxQty) return MdrResult.Fail("wh.err.qty");
            var item = db.WhItems.FirstOrDefault(i => i.ItemID == itemId);
            if (item == null || (!item.IsActive && !allowInactiveItem)) return MdrResult.Fail("wh.err.item");
            var st = db.WhStores.FirstOrDefault(s => s.StoreID == storeId);
            if (st == null || !st.IsActive) return MdrResult.Fail("wh.err.store");
            return null;
        }

        private static MdrResult Insufficient(MdrContext db, int itemId, int storeId, decimal need)
        {
            var have = Stock(db, itemId, storeId);
            if (have >= need) return null;
            var code = db.WhItems.Where(i => i.ItemID == itemId).Select(i => i.Code).First();
            return MdrResult.FailWith("wh.err.stock", Infrastructure.MdrText.T("wh.err.stock") + " (" + code + ": " + Fmt(have) + " < " + Fmt(need) + ")");
        }

        public static MdrResult Receipt(int actor, int storeId, int itemId, decimal qty, string reference, string note)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    var c = Check(db, itemId, storeId, qty, false); if (c != null) return c;
                    var m = Add(db, "Receipt", itemId, storeId, qty, null, null, reference, note, actor);
                    db.SaveChanges(); SetDocNo(db, m); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "رسید انبار", entityName: "MDR_WhMovements", entityId: m.MovementID, entityTitle: m.DocNo, newValues: new { itemId, storeId, qty });
                    return MdrResult.Success(m.MovementID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Issue(int actor, int storeId, int itemId, decimal qty, string reference, string note)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var c = Check(db, itemId, storeId, qty, true); if (c != null) return c;
                    Lock(db, itemId);
                    var s = Insufficient(db, itemId, storeId, qty); if (s != null) return s;
                    var m = Add(db, "Issue", itemId, storeId, -qty, null, null, reference, note, actor);
                    db.SaveChanges(); SetDocNo(db, m); db.SaveChanges(); tx.Commit();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "حواله‌ی انبار", entityName: "MDR_WhMovements", entityId: m.MovementID, entityTitle: m.DocNo, newValues: new { itemId, storeId, qty });
                    return MdrResult.Success(m.MovementID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Transfer(int actor, int fromStore, int toStore, int itemId, decimal qty, string note)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            if (fromStore == toStore) return MdrResult.Fail("wh.err.samestore");
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var c = Check(db, itemId, fromStore, qty, true); if (c != null) return c;
                    c = Check(db, itemId, toStore, qty, false); if (c != null) return c;
                    Lock(db, itemId);
                    var s = Insufficient(db, itemId, fromStore, qty); if (s != null) return s;
                    var pair = Guid.NewGuid();
                    var o = Add(db, "TransferOut", itemId, fromStore, -qty, pair, null, null, note, actor);
                    var i = Add(db, "TransferIn", itemId, toStore, qty, pair, null, null, note, actor);
                    db.SaveChanges(); SetDocNo(db, o); i.DocNo = o.DocNo; db.SaveChanges(); tx.Commit();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "انتقال بین انبارها", entityName: "MDR_WhMovements", entityId: o.MovementID, entityTitle: o.DocNo, newValues: new { itemId, fromStore, toStore, qty });
                    return MdrResult.Success(o.MovementID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // اصلاح موجودی (± delta)؛ دلیل اجباری
        public static MdrResult Adjust(int actor, int storeId, int itemId, decimal delta, string reason)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            if (delta == 0) return MdrResult.Fail("wh.err.qty");
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var c = Check(db, itemId, storeId, Math.Abs(delta), false, true); if (c != null) return c;
                    Lock(db, itemId);
                    if (delta < 0) { var s = Insufficient(db, itemId, storeId, -delta); if (s != null) return s; }
                    var m = Add(db, "Adjust", itemId, storeId, delta, null, null, null, reason, actor);
                    db.SaveChanges(); SetDocNo(db, m); db.SaveChanges(); tx.Commit();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "اصلاح موجودی", entityName: "MDR_WhMovements", entityId: m.MovementID, entityTitle: m.DocNo, newValues: new { itemId, storeId, delta, reason });
                    return MdrResult.Success(m.MovementID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        private static MdrWhMovement Add(MdrContext db, string kind, int itemId, int storeId, decimal qty, Guid? pair, long? requestId, string reference, string note, int actor)
        {
            var m = new MdrWhMovement { Kind = kind, ItemID = itemId, StoreID = storeId, Qty = qty, PairKey = pair, RequestID = requestId, Reference = Trim(reference, 100), Note = Trim(note, 500), CreatedBy = actor, CreatedAt = DateTime.Now };
            db.WhMovements.Add(m);
            return m;
        }

        private static void SetDocNo(MdrContext db, MdrWhMovement m)
        {
            m.DocNo = "WH-" + new System.Globalization.PersianCalendar().GetYear(DateTime.Now) + "-" + m.MovementID.ToString("D6");
        }

        // ---------------- درخواست کالا ----------------
        public class LineInput { public int ItemId { get; set; } public decimal Qty { get; set; } }

        public static MdrResult SaveRequest(int actor, long id, int storeId, string note, IEnumerable<LineInput> lines)
        {
            var merged = (lines ?? new LineInput[0]).GroupBy(l => l.ItemId).Select(g => new LineInput { ItemId = g.Key, Qty = g.Sum(x => x.Qty) }).ToList();
            if (merged.Count == 0 || merged.Count > 50) return MdrResult.Fail("wh.err.lines");
            if (merged.Any(l => l.Qty <= 0 || l.Qty > MaxQty)) return MdrResult.Fail("wh.err.qty");
            try
            {
                using (var db = new MdrContext())
                {
                    var st = db.WhStores.FirstOrDefault(s => s.StoreID == storeId);
                    if (st == null || !st.IsActive) return MdrResult.Fail("wh.err.store");
                    var ids = merged.Select(l => l.ItemId).ToList();
                    if (db.WhItems.Count(i => ids.Contains(i.ItemID) && i.IsActive) != ids.Count) return MdrResult.Fail("wh.err.item");

                    MdrWhRequest r;
                    if (id == 0)
                    {
                        r = new MdrWhRequest { RequesterUserID = actor, StoreID = storeId, Status = "Draft", Note = Trim(note, 500), CreatedAt = DateTime.Now };
                        db.WhRequests.Add(r); db.SaveChanges();
                        r.Number = "WHR-" + new System.Globalization.PersianCalendar().GetYear(DateTime.Now) + "-" + r.RequestID.ToString("D6");
                    }
                    else
                    {
                        r = db.WhRequests.FirstOrDefault(x => x.RequestID == id);
                        if (r == null || r.RequesterUserID != actor) return MdrResult.Fail("wf.err.notfound");
                        if (r.Status != "Draft") return MdrResult.Fail("wh.err.state");
                        r.StoreID = storeId; r.Note = Trim(note, 500);
                        db.WhRequestLines.RemoveRange(db.WhRequestLines.Where(l => l.RequestID == id).ToList());
                    }
                    foreach (var l in merged) db.WhRequestLines.Add(new MdrWhRequestLine { RequestID = r.RequestID, ItemID = l.ItemId, Qty = l.Qty });
                    db.SaveChanges();
                    return MdrResult.Success(r.RequestID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult SubmitRequest(int actor, long id)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var r = db.WhRequests.FirstOrDefault(x => x.RequestID == id);
                    if (r == null || r.RequesterUserID != actor) return MdrResult.Fail("wf.err.notfound");
                    if (r.Status != "Draft") return MdrResult.Fail("wh.err.state");
                    if (!db.WhRequestLines.Any(l => l.RequestID == id)) return MdrResult.Fail("wh.err.lines");
                    r.Status = "Submitted"; r.SubmittedAt = DateTime.Now;
                    db.SaveChanges();
                    var mgr = db.WhStores.Where(s => s.StoreID == r.StoreID).Select(s => s.ManagerUserID).FirstOrDefault();
                    if (mgr.HasValue && mgr.Value != actor) Notify(mgr.Value, "درخواست کالای جدید: " + r.Number, r);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ارسال درخواست کالا", entityName: "MDR_WhRequests", entityId: id, entityTitle: r.Number);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult CancelRequest(int actor, long id)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var r = db.WhRequests.FirstOrDefault(x => x.RequestID == id);
                    if (r == null || r.RequesterUserID != actor) return MdrResult.Fail("wf.err.notfound");
                    if (r.Status != "Draft" && r.Status != "Submitted") return MdrResult.Fail("wh.err.state");
                    r.Status = "Cancelled"; r.DecidedAt = DateTime.Now;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "لغو درخواست کالا", entityName: "MDR_WhRequests", entityId: id, entityTitle: r.Number);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // صدور درخواست: همه‌ی اقلام با هم (اتمیک)؛ اگر موجودی یک قلم کم باشد هیچ قلمی خارج نمی‌شود
        public static MdrResult IssueRequest(int actor, long id, byte[] rowVer)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var r = db.WhRequests.FirstOrDefault(x => x.RequestID == id);
                    if (r == null) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !r.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (r.Status != "Submitted") return MdrResult.Fail("wh.err.state");
                    var lines = db.WhRequestLines.Where(l => l.RequestID == id).OrderBy(l => l.ItemID).ToList();   // ترتیب ثابت = بدون بن‌بست
                    foreach (var l in lines) Lock(db, l.ItemID);
                    foreach (var l in lines) { var s = Insufficient(db, l.ItemID, r.StoreID, l.Qty); if (s != null) return s; }

                    string docNo = null;
                    foreach (var l in lines)
                    {
                        var m = Add(db, "Issue", l.ItemID, r.StoreID, -l.Qty, null, id, r.Number, null, actor);
                        db.SaveChanges();
                        if (docNo == null) { SetDocNo(db, m); docNo = m.DocNo; } else m.DocNo = docNo;
                    }
                    r.Status = "Issued"; r.DecidedBy = actor; r.DecidedAt = DateTime.Now;
                    db.SaveChanges(); tx.Commit();
                    Notify(r.RequesterUserID, "درخواست کالا صادر شد: " + r.Number, r);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "صدور درخواست کالا", entityName: "MDR_WhRequests", entityId: id, entityTitle: r.Number, newValues: new { lines = lines.Count, docNo });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult RejectRequest(int actor, long id, string reason)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    var r = db.WhRequests.FirstOrDefault(x => x.RequestID == id);
                    if (r == null) return MdrResult.Fail("wf.err.notfound");
                    if (r.Status != "Submitted") return MdrResult.Fail("wh.err.state");
                    r.Status = "Rejected"; r.DecidedBy = actor; r.DecidedAt = DateTime.Now; r.DecisionNote = Trim(reason, 500);
                    db.SaveChanges();
                    Notify(r.RequesterUserID, "درخواست کالا رد شد: " + r.Number, r);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "رد درخواست کالا", entityName: "MDR_WhRequests", entityId: id, entityTitle: r.Number, newValues: new { reason });
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- helpers ----------------
        private static bool UserOk(MdrContext db, int userId) { return db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", userId).First() == 1; }

        private static string Trim(string s, int max) { if (string.IsNullOrWhiteSpace(s)) return null; s = s.Trim(); return s.Length > max ? s.Substring(0, max) : s; }

        private static void Notify(int userId, string title, MdrWhRequest r)
        {
            NotificationService.Send(userId, title, null, moduleName: Module, entityName: "MDR_WhRequests", entityId: r.RequestID, linkUrl: "/Mdr/Warehouse#r=" + r.RequestID);
        }

        private static bool IsDup(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException) { var s = e as System.Data.SqlClient.SqlException; if (s != null && (s.Number == 2601 || s.Number == 2627)) return true; }
            return false;
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای انبار", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
