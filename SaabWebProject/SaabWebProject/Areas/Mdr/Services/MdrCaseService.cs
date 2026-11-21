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
    // MDR | Phase 5 | پرونده‌ی الکترونیکی (پنل ۹)
    // وضعیت: Open → Closed (→ Open با Reopen) ؛ Merged (نهایی).
    // دسترسی به پرونده: مالک، سازنده، عضو (Editor/Viewer) یا مدیر صفحه (غیر از پرونده‌ی سری).
    //   ویرایش (لینک/یادداشت/تسک): مالک، سازنده، Editor، مدیر؛ مدیریت (ویرایش خود پرونده، اعضا، بستن، ادغام): مالک، سازنده، مدیر.
    // لینک: فقط به موجودیتی که خود کاربر طبق قواعد جستجو می‌بیند؛ سطح محرمانگی موجودیت نباید از پرونده بالاتر باشد.
    // بستن: دلیل اجباری؛ اگر موضوع باز (تسک/درخواست/فرایند/پیش‌نویس نامه) لینک‌شده باشد فقط با تأیید صریح (force).
    // ایجاد: اگر پرونده‌ی باز و مشابهِ قابل‌دیدن هست، فقط با تأیید صریح (confirmSimilar).
    // هر تغییر: History (CorrelationID) + Audit؛ Concurrent update با RowVer.
    // =====================================================
    public static class MdrCaseService
    {
        private const string Module = "Cases";
        public const int MaxLinks = 200;
        private static readonly string[] Levels = { "Normal", "Confidential", "Secret" };
        private static readonly string[] Roles = { "Editor", "Viewer" };

        public class CaseInput
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public string Confidentiality { get; set; }
            public int? OwnerUserId { get; set; }
        }

        public class CaseAccess
        {
            public bool CanView, CanEdit, CanManage, IsOwner;
            public string Role;                 // Owner | Creator | Editor | Viewer | Manager | null
        }

        public class SimilarCase
        {
            public long CaseID { get; set; }
            public string Number { get; set; }
            public string Title { get; set; }
        }

        public static int Level(string confidentiality) { return Math.Max(0, Array.IndexOf(Levels, confidentiality)); }
        public static bool IsOpen(MdrCase c) { return c.Status == "Open"; }

        // ---------------- دسترسی ----------------
        public static CaseAccess AccessFor(MdrContext db, MdrCase c, int uid, bool isManager)
        {
            var a = new CaseAccess { IsOwner = c.OwnerUserID == uid };
            var creator = c.CreatedBy == uid;
            var member = db.CaseMembers.FirstOrDefault(m => m.CaseID == c.CaseID && m.UserID == uid);
            var mgr = isManager && c.Confidentiality != "Secret";

            a.CanView = a.IsOwner || creator || member != null || mgr;
            a.CanManage = a.IsOwner || creator || mgr;
            a.CanEdit = a.CanManage || (member != null && member.Role == "Editor");
            a.Role = a.IsOwner ? "Owner" : creator ? "Creator" : member != null ? member.Role : mgr ? "Manager" : null;
            return a;
        }

        // ---------------- پرونده‌ی مشابه ----------------
        public static string Norm(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = MdrSearchService.Normalize(s).Replace('ي', 'ی').Replace('ك', 'ک').Replace('‌', ' ').ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }

        // فقط پرونده‌های بازِ قابل‌دیدن برای همین کاربر (وجود پرونده‌ی غیرقابل‌دیدن هیچ‌وقت لو نمی‌رود)
        public static List<SimilarCase> FindSimilar(int uid, bool isManager, string title, long? excludeId = null)
        {
            var n = Norm(title);
            if (n.Length < 2) return new List<SimilarCase>();

            using (var db = new MdrContext())
            {
                var rows = db.Cases.Where(c => c.Status == "Open" && (!excludeId.HasValue || c.CaseID != excludeId.Value) &&
                        (c.OwnerUserID == uid || c.CreatedBy == uid || db.CaseMembers.Any(m => m.CaseID == c.CaseID && m.UserID == uid) ||
                         (isManager && c.Confidentiality != "Secret")))
                    .OrderByDescending(c => c.CaseID).Take(500)
                    .Select(c => new SimilarCase { CaseID = c.CaseID, Number = c.Number, Title = c.Title }).ToList();

                return rows.Where(r =>
                {
                    var t = Norm(r.Title);
                    return t == n || (t.Length >= 6 && n.Length >= 6 && (t.Contains(n) || n.Contains(t)));
                }).Take(5).ToList();
            }
        }

        // ---------------- ایجاد / ویرایش ----------------
        public static MdrResult Create(CaseInput i, int actorId, bool isManager, bool confirmSimilar)
        {
            var v = Validate(i);
            if (v != null) return MdrResult.Fail(v);
            var conf = string.IsNullOrEmpty(i.Confidentiality) ? "Normal" : i.Confidentiality;
            var title = i.Title.Trim();

            try
            {
                var owner = i.OwnerUserId ?? actorId;
                using (var db = new MdrContext())
                {
                    if (!UserExists(db, owner)) return MdrResult.Fail("wf.err.baduser");
                }
                if (!confirmSimilar && FindSimilar(actorId, isManager, title).Count > 0) return MdrResult.Fail("case.err.similar");

                using (var db = new MdrContext())
                {
                    var c = new MdrCase
                    {
                        Title = title, Description = Trim(i.Description, 2000), Status = "Open", Confidentiality = conf,
                        OwnerUserID = owner, CreatedBy = actorId, CreatedAt = DateTime.Now
                    };
                    db.Cases.Add(c);
                    db.SaveChanges();
                    c.Number = "CASE-" + new PersianCalendar().GetYear(DateTime.Now) + "-" + c.CaseID.ToString("D6");
                    Log(db, c.CaseID, "Create", actorId, owner != actorId ? "owner=" + owner : null);
                    db.SaveChanges();

                    if (owner != actorId) Notify(owner, "پرونده‌ی جدید به شما سپرده شد: " + c.Number, c, null);
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد پرونده", entityName: "MDR_Cases", entityId: c.CaseID,
                        entityTitle: c.Number, newValues: new { c.Confidentiality, c.OwnerUserID });
                    return MdrResult.Success(c.CaseID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Update(long id, CaseInput i, byte[] rowVer, int actorId, bool isManager)
        {
            var v = Validate(i);
            if (v != null) return MdrResult.Fail(v);

            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == id);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanManage) return MdrResult.Fail("tsk.err.notallowed");
                    if (rowVer != null && !c.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");

                    var conf = string.IsNullOrEmpty(i.Confidentiality) ? c.Confidentiality : i.Confidentiality;
                    if (Level(conf) < MaxLinkLevel(db, id)) return MdrResult.Fail("case.err.level");

                    var owner = i.OwnerUserId ?? c.OwnerUserID;
                    if (owner != c.OwnerUserID && !UserExists(db, owner)) return MdrResult.Fail("wf.err.baduser");

                    var before = new { c.Title, c.Confidentiality, c.OwnerUserID };
                    var ownerChanged = owner != c.OwnerUserID;
                    c.Title = i.Title.Trim(); c.Description = Trim(i.Description, 2000); c.Confidentiality = conf; c.OwnerUserID = owner;
                    Log(db, id, "Edit", actorId, ownerChanged ? "owner=" + owner : null);
                    db.SaveChanges();

                    if (ownerChanged && owner != actorId) Notify(owner, "پرونده به شما سپرده شد: " + c.Number, c, null);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ویرایش پرونده", entityName: "MDR_Cases", entityId: id, entityTitle: c.Number,
                        oldValues: before, newValues: new { c.Title, c.Confidentiality, c.OwnerUserID });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- لینک موجودیت ----------------
        public static MdrResult Link(long caseId, string key, long entityId, string note, int actorId, bool isManager)
        {
            if (!MdrSearchService.SourceKeys.Contains(key ?? "")) return MdrResult.Fail("case.err.type");
            if (entityId <= 0) return MdrResult.Fail("wf.err.notfound");

            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == caseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");
                    if (db.CaseLinks.Count(l => l.CaseID == caseId) >= MaxLinks) return MdrResult.Fail("case.err.limit");
                    if (db.CaseLinks.Any(l => l.CaseID == caseId && l.EntityType == key && l.EntityID == entityId)) return MdrResult.Fail("case.err.dup");

                    // «موجود نیست» و «دسترسی ندارید» عمداً یک پیام‌اند تا وجود موجودیت لو نرود
                    var info = MdrSearchService.Lookup(actorId, key, new[] { entityId }).FirstOrDefault();
                    if (info == null) return MdrResult.Fail("case.err.cantsee");
                    if (info.Level > Level(c.Confidentiality)) return MdrResult.Fail("case.err.level");

                    var link = new MdrCaseLink { CaseID = caseId, EntityType = key, EntityID = entityId, Note = Trim(note, 300), LinkedBy = actorId, LinkedAt = DateTime.Now };
                    db.CaseLinks.Add(link);
                    Log(db, caseId, "Link", actorId, key + ":" + entityId);
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "افزودن رابطه به پرونده", entityName: "MDR_Cases", entityId: caseId,
                        entityTitle: c.Number, newValues: new { key, entityId });
                    return MdrResult.Success(link.LinkID);
                }
            }
            catch (DbUpdateException ex)
            {
                if (IsDuplicate(ex)) return MdrResult.Fail("case.err.dup");
                return Failed(ex);
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Unlink(long linkId, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var l = db.CaseLinks.FirstOrDefault(x => x.LinkID == linkId);
                    if (l == null) return MdrResult.Fail("wf.err.notfound");
                    var c = db.Cases.First(x => x.CaseID == l.CaseID);
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");

                    db.CaseLinks.Remove(l);
                    Log(db, c.CaseID, "Unlink", actorId, l.EntityType + ":" + l.EntityID);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "حذف رابطه از پرونده", entityName: "MDR_Cases", entityId: c.CaseID,
                        entityTitle: c.Number, oldValues: new { l.EntityType, l.EntityID });
                    return MdrResult.Success(c.CaseID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- یادداشت ----------------
        public static MdrResult AddNote(long caseId, string body, int actorId, bool isManager)
        {
            body = (body ?? "").Trim();
            if (body.Length == 0 || body.Length > 2000) return MdrResult.Fail("case.err.note");

            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == caseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");

                    var note = new MdrCaseNote { CaseID = caseId, UserID = actorId, Body = body, CreatedAt = DateTime.Now };
                    db.CaseNotes.Add(note);
                    Log(db, caseId, "Note", actorId, Trim(body, 200));
                    db.SaveChanges();
                    return MdrResult.Success(note.NoteID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- اعضا ----------------
        public static MdrResult SetMember(long caseId, int userId, string role, int actorId, bool isManager)
        {
            if (!Roles.Contains(role ?? "")) return MdrResult.Fail("case.err.role");

            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == caseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanManage) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");
                    if (userId == c.OwnerUserID) return MdrResult.Fail("case.err.owner");
                    if (!UserExists(db, userId)) return MdrResult.Fail("wf.err.baduser");

                    var m = db.CaseMembers.FirstOrDefault(x => x.CaseID == caseId && x.UserID == userId);
                    var isNew = m == null;
                    if (isNew) { m = new MdrCaseMember { CaseID = caseId, UserID = userId, AddedBy = actorId, AddedAt = DateTime.Now }; db.CaseMembers.Add(m); }
                    m.Role = role;
                    Log(db, caseId, isNew ? "MemberAdd" : "MemberRole", actorId, "user=" + userId + ":" + role);
                    db.SaveChanges();

                    if (isNew && userId != actorId) Notify(userId, "شما به پرونده‌ی " + c.Number + " افزوده شدید", c, null);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "عضو پرونده", entityName: "MDR_Cases", entityId: caseId,
                        entityTitle: c.Number, newValues: new { userId, role });
                    return MdrResult.Success(caseId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult RemoveMember(long caseId, int userId, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == caseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanManage) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");

                    var m = db.CaseMembers.FirstOrDefault(x => x.CaseID == caseId && x.UserID == userId);
                    if (m == null) return MdrResult.Fail("wf.err.notfound");
                    db.CaseMembers.Remove(m);
                    Log(db, caseId, "MemberRemove", actorId, "user=" + userId);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "حذف عضو پرونده", entityName: "MDR_Cases", entityId: caseId,
                        entityTitle: c.Number, oldValues: new { userId });
                    return MdrResult.Success(caseId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- تسک از داخل پرونده ----------------
        public static MdrResult CreateTask(long caseId, MdrTaskService.TaskInput t, int actorId, bool isManager)
        {
            try
            {
                MdrCase c;
                using (var db = new MdrContext())
                {
                    c = db.Cases.FirstOrDefault(x => x.CaseID == caseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");
                    if (db.CaseLinks.Count(l => l.CaseID == caseId) >= MaxLinks) return MdrResult.Fail("case.err.limit");
                }
                if (!PermissionHelper.HasAccess(actorId, "MDR.Tasks")) return MdrResult.Fail("tsk.err.notallowed");

                t.Id = 0; t.ParentId = null;
                t.SourceType = "Case"; t.SourceRef = c.Number; t.SourceUrl = "/Mdr/Cases#id=" + caseId;
                var created = MdrTaskService.Save(t, actorId, false, null);
                if (!created.Ok) return created;

                var linked = Link(caseId, "tsk", created.Id.Value, null, actorId, isManager);
                return linked.Ok ? created : linked;
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- بستن / بازگشایی / ادغام ----------------
        // تعداد موضوع‌های بازِ لینک‌شده (همه‌ی لینک‌ها، فقط عدد؛ برای هشدار قبل از بستن)
        public static int OpenItemCount(long caseId)
        {
            using (var db = new MdrContext()) return OpenItemCount(db, caseId);
        }

        private static int OpenItemCount(MdrContext db, long caseId)
        {
            var n = 0;
            foreach (var g in db.CaseLinks.Where(l => l.CaseID == caseId).GroupBy(l => l.EntityType).Select(g => new { g.Key, Ids = g.Select(x => x.EntityID) }).ToList())
                n += MdrSearchService.Lookup(null, g.Key, g.Ids).Count(e => e.IsOpen);
            return n;
        }

        private static int MaxLinkLevel(MdrContext db, long caseId)
        {
            var max = 0;
            foreach (var g in db.CaseLinks.Where(l => l.CaseID == caseId).GroupBy(l => l.EntityType).Select(g => new { g.Key, Ids = g.Select(x => x.EntityID) }).ToList())
                foreach (var e in MdrSearchService.Lookup(null, g.Key, g.Ids)) max = Math.Max(max, e.Level);
            return max;
        }

        public static MdrResult Close(long id, string reason, bool force, byte[] rowVer, int actorId, bool isManager)
        {
            reason = (reason ?? "").Trim();
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == id);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanManage) return MdrResult.Fail("tsk.err.notallowed");
                    if (rowVer != null && !c.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (!IsOpen(c)) return MdrResult.Fail("case.err.state");
                    if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");

                    var open = OpenItemCount(db, id);
                    if (open > 0 && !force) return MdrResult.Fail("case.err.openitems");

                    c.Status = "Closed"; c.ClosedAt = DateTime.Now; c.CloseReason = Trim(reason, 500);
                    Log(db, id, "Close", actorId, c.CloseReason + (open > 0 ? " (open=" + open + ")" : ""));
                    db.SaveChanges();

                    if (c.OwnerUserID != actorId) Notify(c.OwnerUserID, "پرونده‌ی " + c.Number + " بسته شد", c, NotificationService.Severities.Success);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "بستن پرونده", entityName: "MDR_Cases", entityId: id, entityTitle: c.Number,
                        oldValues: new { Status = "Open" }, newValues: new { Status = "Closed", reason = c.CloseReason, openItems = open });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Reopen(long id, string reason, byte[] rowVer, int actorId, bool isManager)
        {
            reason = (reason ?? "").Trim();
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == id);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var a = AccessFor(db, c, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanManage) return MdrResult.Fail("tsk.err.notallowed");
                    if (rowVer != null && !c.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (c.Status != "Closed") return MdrResult.Fail("case.err.state");
                    if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");

                    c.Status = "Open"; c.ClosedAt = null; c.CloseReason = null;
                    Log(db, id, "Reopen", actorId, Trim(reason, 500));
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "بازگشایی پرونده", entityName: "MDR_Cases", entityId: id, entityTitle: c.Number,
                        oldValues: new { Status = "Closed" }, newValues: new { Status = "Open", reason });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ادغام: لینک‌ها (بدون تکراری)، یادداشت‌ها و اعضای «مبدأ» به «مقصد» می‌روند؛ مبدأ Merged می‌شود.
        public static MdrResult Merge(long sourceId, long targetId, string reason, int actorId, bool isManager)
        {
            reason = (reason ?? "").Trim();
            if (sourceId == targetId) return MdrResult.Fail("case.err.same");

            try
            {
                using (var db = new MdrContext())
                {
                    var s = db.Cases.FirstOrDefault(x => x.CaseID == sourceId);
                    var t = db.Cases.FirstOrDefault(x => x.CaseID == targetId);
                    if (s == null || t == null) return MdrResult.Fail("wf.err.notfound");
                    var sa = AccessFor(db, s, actorId, isManager);
                    var ta = AccessFor(db, t, actorId, isManager);
                    if (!sa.CanView || !ta.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!sa.CanManage || !ta.CanManage) return MdrResult.Fail("tsk.err.notallowed");
                    if (!IsOpen(s) || !IsOpen(t)) return MdrResult.Fail("case.err.state");
                    if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
                    // محتوای پرونده‌ی مبدأ نباید به پرونده‌ای با محرمانگی کمتر برود
                    if (Level(t.Confidentiality) < Level(s.Confidentiality) || Level(t.Confidentiality) < MaxLinkLevel(db, sourceId)) return MdrResult.Fail("case.err.level");

                    var have = new HashSet<string>(db.CaseLinks.Where(l => l.CaseID == targetId).ToList().Select(l => l.EntityType + ":" + l.EntityID));
                    var moved = 0;
                    foreach (var l in db.CaseLinks.Where(l => l.CaseID == sourceId).ToList())
                    {
                        if (have.Add(l.EntityType + ":" + l.EntityID)) { l.CaseID = targetId; moved++; }
                        else db.CaseLinks.Remove(l);
                    }
                    if (have.Count > MaxLinks) return MdrResult.Fail("case.err.limit");   // هنوز SaveChanges نشده؛ تغییرات رها می‌شوند

                    foreach (var n in db.CaseNotes.Where(x => x.CaseID == sourceId).ToList()) n.CaseID = targetId;

                    var tm = db.CaseMembers.Where(m => m.CaseID == targetId).ToList();
                    foreach (var m in db.CaseMembers.Where(m => m.CaseID == sourceId).ToList())
                    {
                        if (m.UserID != t.OwnerUserID)
                        {
                            var existing = tm.FirstOrDefault(x => x.UserID == m.UserID);
                            if (existing == null) db.CaseMembers.Add(new MdrCaseMember { CaseID = targetId, UserID = m.UserID, Role = m.Role, AddedBy = actorId, AddedAt = DateTime.Now });
                            else if (m.Role == "Editor") existing.Role = "Editor";
                        }
                        db.CaseMembers.Remove(m);
                    }
                    // مالک مبدأ اگر عضو مقصد نیست، Editor مقصد می‌شود
                    if (s.OwnerUserID != t.OwnerUserID && !tm.Any(x => x.UserID == s.OwnerUserID) &&
                        !db.CaseMembers.Local.Any(x => x.CaseID == targetId && x.UserID == s.OwnerUserID))
                        db.CaseMembers.Add(new MdrCaseMember { CaseID = targetId, UserID = s.OwnerUserID, Role = "Editor", AddedBy = actorId, AddedAt = DateTime.Now });

                    s.Status = "Merged"; s.MergedIntoCaseID = targetId; s.ClosedAt = DateTime.Now; s.CloseReason = Trim(reason, 500);
                    Log(db, sourceId, "Merge", actorId, "→ " + t.Number + " | " + Trim(reason, 300));
                    Log(db, targetId, "MergeIn", actorId, s.Number + " | links=" + moved);
                    db.SaveChanges();

                    if (s.OwnerUserID != actorId) Notify(s.OwnerUserID, "پرونده‌ی " + s.Number + " در " + t.Number + " ادغام شد", t, null);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ادغام پرونده", entityName: "MDR_Cases", entityId: sourceId, entityTitle: s.Number,
                        oldValues: new { Status = "Open" }, newValues: new { Status = "Merged", into = t.Number, reason, movedLinks = moved });
                    return MdrResult.Success(targetId);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- helpers ----------------
        private static string Validate(CaseInput i)
        {
            var title = (i.Title ?? "").Trim();
            if (title.Length == 0 || title.Length > 300) return "case.err.title";
            if (!string.IsNullOrEmpty(i.Confidentiality) && !Levels.Contains(i.Confidentiality)) return "case.err.conf";
            return null;
        }

        private static bool UserExists(MdrContext db, int userId)
        {
            return db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0", userId).First() > 0;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        private static void Log(MdrContext db, long caseId, string action, int? actorId, string detail)
        {
            db.CaseHistory.Add(new MdrCaseHistory
            {
                CaseID = caseId, ActionKey = action, ActorUserID = actorId, Detail = Trim(detail, 500),
                CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now
            });
        }

        // عنوان پرونده‌ی سری در اعلان نمی‌آید
        private static void Notify(int userId, string title, MdrCase c, string severity)
        {
            NotificationService.Send(userId, title, c.Confidentiality == "Secret" ? null : c.Title, severity: severity ?? NotificationService.Severities.Info,
                moduleName: Module, entityName: "MDR_Cases", entityId: c.CaseID, linkUrl: "/Mdr/Cases#id=" + c.CaseID);
        }

        private static bool IsDuplicate(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sql = e as System.Data.SqlClient.SqlException;
                if (sql != null && (sql.Number == 2601 || sql.Number == 2627)) return true;
            }
            return false;
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای پرونده‌ی الکترونیکی", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
