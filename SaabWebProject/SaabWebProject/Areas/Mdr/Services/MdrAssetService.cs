using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 7 | اموال (پنل ۱۴)
    // وضعیت: InStock ⇄ Assigned ⇄ InRepair ؛ Disposed (نهایی).
    // قواعد کلیدی:
    //  - ثبت/تحویل/تعمیر/جابه‌جایی مکان: مدیر اموال (CanEdit صفحه‌ی MDR.Assets).
    //  - «دارایی در تعهد»: دارایی‌ای که نزد کسی است (Assigned) یا در تعمیر است اسقاط نمی‌شود؛ ابتدا باید به انبار برگردد.
    //  - اسقاط فقط با حق مستقل MDR.Assets.Dispose + دلیل اجباری؛ بعد از آن هیچ اقدامی ممکن نیست.
    //  - بازگشت/انتقال: خود دارنده یا مدیر؛ انتقال دلیل اجباری دارد و دارنده‌ی جدید باید کاربر فعال و غیر از دارنده‌ی فعلی باشد.
    //  - تگ یکتا (بدون توجه به حروف)؛ هر تغییر: رویداد (CorrelationID) + Audit؛ Concurrent update با RowVer.
    // =====================================================
    public class AssetInput { public string Tag { get; set; } public string Name { get; set; } public string Category { get; set; } public string Location { get; set; } public DateTime? AcquiredAt { get; set; } public long? Cost { get; set; } public string Note { get; set; } }

    public static class MdrAssetService
    {
        private const string Module = "Assets";
        public const string Page = "MDR.Assets";
        public const string DisposePage = "MDR.Assets.Dispose";
        public const long MaxCost = 10000000000000L;

        public static bool IsManager(int uid) { return PermissionHelper.HasAccess(uid, Page) && PermissionHelper.CanEdit(uid, Page); }
        public static bool CanDispose(int uid) { return PermissionHelper.HasAccess(uid, DisposePage); }

        public static MdrResult Register(int actor, AssetInput i)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            var tag = (i.Tag ?? "").Trim().ToUpperInvariant(); var name = (i.Name ?? "").Trim();
            if (tag.Length == 0 || tag.Length > 40) return MdrResult.Fail("asset.err.tag");
            if (name.Length == 0 || name.Length > 200) return MdrResult.Fail("asset.err.name");
            if (i.Cost.HasValue && (i.Cost.Value < 0 || i.Cost.Value > MaxCost)) return MdrResult.Fail("asset.err.cost");
            try
            {
                using (var db = new MdrContext())
                {
                    if (db.Assets.Any(a => a.Tag == tag)) return MdrResult.Fail("asset.err.duptag");
                    var a2 = new MdrAsset
                    {
                        Tag = tag, Name = name, Category = Trim(i.Category, 60), Status = "InStock", Location = Trim(i.Location, 200), AcquiredAt = i.AcquiredAt.HasValue ? (DateTime?)i.AcquiredAt.Value.Date : null,
                        Cost = i.Cost, Note = Trim(i.Note, 500), CreatedBy = actor, CreatedAt = DateTime.Now
                    };
                    db.Assets.Add(a2); db.SaveChanges();
                    Event(db, a2.AssetID, "Register", actor, null, null, null, a2.Location, null);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ثبت دارایی", entityName: "MDR_Assets", entityId: a2.AssetID, entityTitle: tag, newValues: new { a2.Name, a2.Category, a2.Cost });
                    return MdrResult.Success(a2.AssetID);
                }
            }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("asset.err.duptag") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        // تحویل از انبار به یک کاربر
        public static MdrResult Assign(int actor, long id, int toUser, string note, byte[] rowVer)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status != "InStock") return MdrResult.Fail("asset.err.state");
                if (!UserOk(db, toUser)) return MdrResult.Fail("wf.err.baduser");
                a.Status = "Assigned"; a.HolderUserID = toUser;
                Event(db, id, "Assign", actor, null, toUser, a.Location, a.Location, Trim(note, 500));
                return null;
            }, a => { Notify(a.HolderUserID.Value, "دارایی به شما تحویل شد: " + a.Tag, a); return "تحویل دارایی"; }, actor);
        }

        // بازگشت به انبار (دارنده یا مدیر)
        public static MdrResult Return(int actor, long id, string location, byte[] rowVer)
        {
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status != "Assigned") return MdrResult.Fail("asset.err.state");
                if (a.HolderUserID != actor && !IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
                var from = a.HolderUserID;
                var loc = Trim(location, 200) ?? a.Location;
                Event(db, id, "Return", actor, from, null, a.Location, loc, null);
                a.Status = "InStock"; a.HolderUserID = null; a.Location = loc;
                return null;
            }, a => "بازگشت دارایی", actor, true);
        }

        // انتقال به کاربر دیگر (دارنده یا مدیر؛ دلیل اجباری)
        public static MdrResult Transfer(int actor, long id, int toUser, string reason, byte[] rowVer)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status != "Assigned") return MdrResult.Fail("asset.err.state");
                if (a.HolderUserID != actor && !IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
                if (a.HolderUserID == toUser) return MdrResult.Fail("asset.err.sameholder");
                if (!UserOk(db, toUser)) return MdrResult.Fail("wf.err.baduser");
                var from = a.HolderUserID;
                a.HolderUserID = toUser;
                Event(db, id, "Transfer", actor, from, toUser, a.Location, a.Location, Trim(reason, 500));
                return null;
            }, a => { Notify(a.HolderUserID.Value, "دارایی به شما منتقل شد: " + a.Tag, a); return "انتقال دارایی"; }, actor);
        }

        // تغییر مکان (مدیر)
        public static MdrResult Move(int actor, long id, string location, string reason, byte[] rowVer)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            location = (location ?? "").Trim();
            if (location.Length == 0 || location.Length > 200) return MdrResult.Fail("asset.err.location");
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status == "Disposed") return MdrResult.Fail("asset.err.state");
                var from = a.Location; a.Location = location;
                Event(db, id, "Transfer", actor, null, null, from, location, Trim(reason, 500));
                return null;
            }, a => "جابه‌جایی مکان دارایی", actor);
        }

        public static MdrResult RepairStart(int actor, long id, string reason, byte[] rowVer)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status != "InStock" && a.Status != "Assigned") return MdrResult.Fail("asset.err.state");
                a.Status = "InRepair";             // دارنده نگه داشته می‌شود (تعهد باقی است)
                Event(db, id, "RepairStart", actor, a.HolderUserID, null, a.Location, a.Location, Trim(reason, 500));
                return null;
            }, a => "شروع تعمیر دارایی", actor);
        }

        public static MdrResult RepairEnd(int actor, long id, string note, byte[] rowVer)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status != "InRepair") return MdrResult.Fail("asset.err.state");
                a.Status = a.HolderUserID.HasValue ? "Assigned" : "InStock";
                Event(db, id, "RepairEnd", actor, null, a.HolderUserID, a.Location, a.Location, Trim(note, 500));
                return null;
            }, a => "پایان تعمیر دارایی", actor);
        }

        // اسقاط: حق مستقل + دلیل اجباری؛ دارایی در تعهد/تعمیر مجاز نیست
        public static MdrResult Dispose(int actor, long id, string reason, byte[] rowVer)
        {
            if (!CanDispose(actor) || !PermissionHelper.HasAccess(actor, Page)) return MdrResult.Fail("wf.err.notfound");
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            return Change(id, rowVer, (db, a) =>
            {
                if (a.Status == "Disposed") return MdrResult.Fail("asset.err.state");
                if (a.Status != "InStock") return MdrResult.Fail("asset.err.commitment");
                a.Status = "Disposed";
                Event(db, id, "Dispose", actor, null, null, a.Location, null, Trim(reason, 500));
                return null;
            }, a => "اسقاط دارایی", actor);
        }

        // الگوی مشترک: بارگذاری، RowVer، اجرای قاعده، ذخیره، اعلان/Audit
        private static MdrResult Change(long id, byte[] rowVer, Func<MdrContext, MdrAsset, MdrResult> apply, Func<MdrAsset, string> after, int actor, bool visibleToHolder = false)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var a = db.Assets.FirstOrDefault(x => x.AssetID == id);
                    if (a == null) return MdrResult.Fail("wf.err.notfound");
                    // غیرمدیری که دارنده هم نیست دارایی را نمی‌بیند
                    if (!IsManager(actor) && a.HolderUserID != actor && !db.AssetEvents.Any(e => e.AssetID == id && (e.FromUserID == actor || e.ToUserID == actor))) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !a.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    var before = new { a.Status, a.HolderUserID, a.Location };
                    var r = apply(db, a);
                    if (r != null) return r;
                    db.SaveChanges();
                    var title = after(a);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, title, entityName: "MDR_Assets", entityId: id, entityTitle: a.Tag, oldValues: before, newValues: new { a.Status, a.HolderUserID, a.Location });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        private static void Event(MdrContext db, long assetId, string type, int actor, int? fromUser, int? toUser, string fromLoc, string toLoc, string reason)
        {
            db.AssetEvents.Add(new MdrAssetEvent { AssetID = assetId, EventType = type, FromUserID = fromUser, ToUserID = toUser, FromLocation = fromLoc, ToLocation = toLoc, Reason = reason, ActorUserID = actor, CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now });
        }

        private static bool UserOk(MdrContext db, int userId) { return db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", userId).First() == 1; }

        private static string Trim(string s, int max) { if (string.IsNullOrWhiteSpace(s)) return null; s = s.Trim(); return s.Length > max ? s.Substring(0, max) : s; }

        private static void Notify(int userId, string title, MdrAsset a)
        {
            NotificationService.Send(userId, title, null, moduleName: Module, entityName: "MDR_Assets", entityId: a.AssetID, linkUrl: "/Mdr/Assets#id=" + a.AssetID);
        }

        private static bool IsDup(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException) { var s = e as System.Data.SqlClient.SqlException; if (s != null && (s.Number == 2601 || s.Number == 2627)) return true; }
            return false;
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای اموال", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
