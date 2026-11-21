using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Web;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Infrastructure;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 5 | سرویس اسناد و مستندسازی (پنل ۸)
    // کاتالوگ روی FileManager/MDR_Files (ذخیره‌ی فایل و نسخه‌ها همان‌جاست).
    // دسترسی: مالک؛ اشتراک View/Edit؛ مدیر (CanEdit صفحه) برای اسناد غیرسری.
    //   «سری» فقط مالک و اشتراک‌گیرنده‌ها (حتی مدیر نه).
    // - بارگذاری/نسخه‌ی جدید از دو نماینده‌ی قابل جایگزینی (UploadFile/AddFileVersion) می‌گذرد
    //   تا منطق بدون HttpContext تست شود.
    // =====================================================
    public static class MdrDocumentService
    {
        private const string Module = "Documents";
        private static readonly string[] Classes = { "Normal", "Confidential", "Secret" };
        private static readonly string[] LinkableEntities = { "MDR_Requests", "MDR_Tasks", "MDR_Letters" };

        // نمایندگان پیش‌فرض روی FileManager؛ تست‌ها جایگزین می‌کنند
        public static Func<HttpPostedFileBase, long, string, FileManager.FileOperationResult> UploadFile =
            (file, docId, title) => FileManager.Upload(file, "Documents", "MDR_Documents", docId, title, null, null);

        public static Func<Guid, HttpPostedFileBase, string, FileManager.FileOperationResult> AddFileVersion =
            (guid, file, note) => FileManager.AddVersion(guid, file, note);

        public class DocInput
        {
            public int? FolderId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Classification { get; set; }
            public List<string> Tags { get; set; }
        }

        public class AccessInfo
        {
            public bool IsOwner { get; set; }
            public bool CanView { get; set; }
            public bool CanEdit { get; set; }       // ویرایش متادیتا/نسخه‌ی جدید/برچسب/پیوند (فقط سند فعال)
            public bool CanShare { get; set; }
            public bool CanArchive { get; set; }
            public string SharePermission { get; set; }
        }

        // ---------------- دسترسی ----------------
        public static AccessInfo GetAccess(MdrContext db, MdrDocument d, int uid, bool isManager)
        {
            var owner = d.OwnerUserID == uid;
            var share = db.DocumentShares.Where(s => s.DocumentID == d.DocumentID && s.UserID == uid).Select(s => s.Permission).FirstOrDefault();
            var mgr = isManager && d.Classification != "Secret";
            var active = d.Status == "Active";

            return new AccessInfo
            {
                IsOwner = owner, SharePermission = share,
                CanView = owner || share != null || mgr,
                CanEdit = active && (owner || share == "Edit"),
                CanShare = owner || mgr,
                CanArchive = owner || mgr
            };
        }

        // ---------------- پوشه ----------------
        public static MdrResult CreateFolder(int? parentId, string name, int actorId)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 150) return MdrResult.Fail("doc.err.foldername");

            try
            {
                using (var db = new MdrContext())
                {
                    if (parentId.HasValue && !db.DocFolders.Any(f => f.FolderID == parentId.Value)) return MdrResult.Fail("wf.err.notfound");
                    if (db.DocFolders.Any(f => f.ParentFolderID == parentId && f.Name == name)) return MdrResult.Fail("doc.err.folderexists");

                    var f2 = new MdrDocFolder { ParentFolderID = parentId, Name = name, OwnerUserID = actorId, CreatedAt = DateTime.Now };
                    db.DocFolders.Add(f2);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد پوشه", entityName: "MDR_DocFolders", entityId: f2.FolderID, entityTitle: name);
                    return MdrResult.Success(f2.FolderID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // فقط پوشه‌ی خالی؛ مالک یا مدیر
        public static MdrResult DeleteFolder(int id, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var f = db.DocFolders.FirstOrDefault(x => x.FolderID == id);
                    if (f == null) return MdrResult.Fail("wf.err.notfound");
                    if (f.OwnerUserID != actorId && !isManager) return MdrResult.Fail("tsk.err.notallowed");
                    if (db.DocFolders.Any(x => x.ParentFolderID == id) || db.Documents.Any(d => d.FolderID == id)) return MdrResult.Fail("doc.err.foldernotempty");

                    db.DocFolders.Remove(f);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Delete, Module, "حذف پوشه", entityName: "MDR_DocFolders", entityId: id, entityTitle: f.Name);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- سند ----------------
        public static MdrResult CreateDocument(DocInput i, HttpPostedFileBase file, int actorId)
        {
            var title = (i.Title ?? "").Trim();
            if (title.Length == 0 || title.Length > 300) return MdrResult.Fail("tsk.err.title");
            var cls = string.IsNullOrEmpty(i.Classification) ? "Normal" : i.Classification;
            if (!Classes.Contains(cls)) return MdrResult.Fail("ltr.err.enum");
            if (file == null || file.ContentLength <= 0) return MdrResult.Fail("att.err.nofile");
            string tagErr;
            var tags = CleanTags(i.Tags, out tagErr);
            if (tagErr != null) return MdrResult.Fail(tagErr);

            try
            {
                using (var db = new MdrContext())
                {
                    if (i.FolderId.HasValue && !db.DocFolders.Any(f => f.FolderID == i.FolderId.Value)) return MdrResult.Fail("wf.err.notfound");

                    var d = new MdrDocument
                    {
                        FolderID = i.FolderId, Title = title, Description = Cut(i.Description, 1000), Classification = cls,
                        OwnerUserID = actorId, Status = "Active", CreatedAt = DateTime.Now
                    };
                    db.Documents.Add(d);
                    db.SaveChanges();

                    var up = UploadFile(file, d.DocumentID, title);
                    if (up == null || !up.Success || !up.FileGuid.HasValue)
                    {
                        db.Documents.Remove(d);                      // بدون فایل، سندی ساخته نمی‌شود
                        db.SaveChanges();
                        return MdrResult.FailWith("doc.err.upload", up == null ? null : up.Message);
                    }

                    d.FileGuid = up.FileGuid;
                    foreach (var t in tags) db.DocumentTags.Add(new MdrDocumentTag { DocumentID = d.DocumentID, Tag = t });
                    Log(db, d.DocumentID, "Create", actorId, null);
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد سند", entityName: "MDR_Documents", entityId: d.DocumentID,
                        entityTitle: d.Title, newValues: new { cls, tags = tags.Count });
                    return MdrResult.Success(d.DocumentID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult NewVersion(long id, HttpPostedFileBase file, string note, int actorId, bool isManager)
        {
            if (file == null || file.ContentLength <= 0) return MdrResult.Fail("att.err.nofile");

            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail(d.Status != "Active" ? "doc.err.archived" : "tsk.err.notallowed");
                    if (!d.FileGuid.HasValue) return MdrResult.Fail("wf.err.notfound");

                    var r = AddFileVersion(d.FileGuid.Value, file, Cut(note, 500));
                    if (r == null || !r.Success) return MdrResult.FailWith("doc.err.upload", r == null ? null : r.Message);

                    Log(db, id, "NewVersion", actorId, r.VersionNo.HasValue ? "v" + r.VersionNo : null);
                    db.SaveChanges();
                    NotifyOthers(db, d, actorId, "نسخه‌ی جدید سند");
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "نسخه‌ی جدید سند", entityName: "MDR_Documents", entityId: id,
                        entityTitle: d.Title, newValues: new { r.VersionNo });
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult UpdateMeta(long id, string title, string description, string classification, int? folderId, int actorId, bool isManager, byte[] rowVer)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 300) return MdrResult.Fail("tsk.err.title");
            if (!Classes.Contains(classification ?? "")) return MdrResult.Fail("ltr.err.enum");

            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail(d.Status != "Active" ? "doc.err.archived" : "tsk.err.notallowed");
                    if (rowVer != null && !d.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (classification != d.Classification && !a.IsOwner) return MdrResult.Fail("doc.err.classowner");
                    if (folderId.HasValue && !db.DocFolders.Any(f => f.FolderID == folderId.Value)) return MdrResult.Fail("wf.err.notfound");

                    var before = new { d.Title, d.Classification, d.FolderID };
                    d.Title = title; d.Description = Cut(description, 1000); d.Classification = classification; d.FolderID = folderId;
                    Log(db, id, "Edit", actorId, null);
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ویرایش مشخصات سند", entityName: "MDR_Documents", entityId: id, entityTitle: d.Title,
                        oldValues: before, newValues: new { d.Title, d.Classification, d.FolderID });
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult SetTags(long id, List<string> tagsIn, int actorId, bool isManager)
        {
            string tagErr;
            var tags = CleanTags(tagsIn, out tagErr);
            if (tagErr != null) return MdrResult.Fail(tagErr);

            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail(d.Status != "Active" ? "doc.err.archived" : "tsk.err.notallowed");

                    db.DocumentTags.RemoveRange(db.DocumentTags.Where(t => t.DocumentID == id));
                    db.SaveChanges();
                    foreach (var t in tags) db.DocumentTags.Add(new MdrDocumentTag { DocumentID = id, Tag = t });
                    Log(db, id, "Tags", actorId, string.Join("، ", tags));
                    db.SaveChanges();
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- اشتراک ----------------
        public static MdrResult Share(long id, int userId, string permission, int actorId, bool isManager)
        {
            if (permission != "View" && permission != "Edit") return MdrResult.Fail("doc.err.perm");

            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanShare) return MdrResult.Fail("tsk.err.notallowed");
                    if (userId == d.OwnerUserID) return MdrResult.Fail("doc.err.shareowner");
                    if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0", userId).First() == 0) return MdrResult.Fail("wf.err.baduser");

                    var s = db.DocumentShares.FirstOrDefault(x => x.DocumentID == id && x.UserID == userId);
                    var isNew = s == null;
                    if (isNew) { s = new MdrDocumentShare { DocumentID = id, UserID = userId, SharedBy = actorId, CreatedAt = DateTime.Now }; db.DocumentShares.Add(s); }
                    s.Permission = permission;
                    Log(db, id, "Share", actorId, "user=" + userId + " " + permission);
                    db.SaveChanges();

                    if (isNew && userId != actorId)
                        NotificationService.Send(userId, "سندی با شما به اشتراک گذاشته شد", d.Classification == "Secret" ? null : d.Title, moduleName: Module,
                            entityName: "MDR_Documents", entityId: id, linkUrl: "/Mdr/Documents#id=" + id);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "اشتراک سند", entityName: "MDR_Documents", entityId: id, entityTitle: d.Title,
                        newValues: new { userId, permission });
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // مالک/مدیر هر اشتراکی را برمی‌دارد؛ هر کاربر اشتراک خودش را
        public static MdrResult Unshare(long id, int userId, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanShare && userId != actorId) return MdrResult.Fail("tsk.err.notallowed");

                    var s = db.DocumentShares.FirstOrDefault(x => x.DocumentID == id && x.UserID == userId);
                    if (s == null) return MdrResult.Fail("wf.err.notfound");
                    db.DocumentShares.Remove(s);
                    Log(db, id, "Unshare", actorId, "user=" + userId);
                    db.SaveChanges();
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- پیوند به موجودیت ----------------
        public static MdrResult Link(long id, string entityName, string entityId, int actorId, bool isManager)
        {
            long eid;
            if (!LinkableEntities.Contains(entityName ?? "") || !long.TryParse(entityId, out eid)) return MdrResult.Fail("doc.err.link");

            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail(d.Status != "Active" ? "doc.err.archived" : "tsk.err.notallowed");

                    // کاربر باید خودش مقصد را بتواند ببیند (وگرنه وجود/عدم وجود آن لو می‌رود)
                    var target = MdrAttachmentAccess.Resolve(entityName, eid.ToString(), actorId);
                    if (!target.Known || !target.CanView) return MdrResult.Fail("doc.err.link");
                    if (db.DocumentLinks.Any(l => l.DocumentID == id && l.EntityName == entityName && l.EntityID == entityId)) return MdrResult.Fail("doc.err.linkexists");

                    db.DocumentLinks.Add(new MdrDocumentLink { DocumentID = id, EntityName = entityName, EntityID = eid.ToString(), CreatedBy = actorId, CreatedAt = DateTime.Now });
                    Log(db, id, "Link", actorId, entityName + ":" + eid);
                    db.SaveChanges();
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Unlink(long id, long linkId, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanEdit) return MdrResult.Fail(d.Status != "Active" ? "doc.err.archived" : "tsk.err.notallowed");

                    var l = db.DocumentLinks.FirstOrDefault(x => x.LinkID == linkId && x.DocumentID == id);
                    if (l == null) return MdrResult.Fail("wf.err.notfound");
                    db.DocumentLinks.Remove(l);
                    Log(db, id, "Unlink", actorId, l.EntityName + ":" + l.EntityID);
                    db.SaveChanges();
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- بایگانی ----------------
        public static MdrResult SetArchived(long id, bool archive, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (d == null) return MdrResult.Fail("wf.err.notfound");
                    var a = GetAccess(db, d, actorId, isManager);
                    if (!a.CanView) return MdrResult.Fail("wf.err.notfound");
                    if (!a.CanArchive) return MdrResult.Fail("tsk.err.notallowed");
                    if (archive && d.Status != "Active") return MdrResult.Fail("tsk.err.state");
                    if (!archive && d.Status != "Archived") return MdrResult.Fail("tsk.err.state");

                    d.Status = archive ? "Archived" : "Active";
                    d.ArchivedAt = archive ? DateTime.Now : (DateTime?)null;
                    Log(db, id, archive ? "Archive" : "Restore", actorId, null);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, archive ? "بایگانی سند" : "بازیابی سند", entityName: "MDR_Documents", entityId: id, entityTitle: d.Title);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- helpers ----------------
        // حداکثر ۱۰ برچسب؛ هر کدام ≤ ۵۰ حرف؛ یکتا (بدون حساسیت به حروف)
        public static List<string> CleanTags(IEnumerable<string> tags, out string error)
        {
            error = null;
            var list = new List<string>();
            foreach (var raw in tags ?? new List<string>())
            {
                var t = (raw ?? "").Trim();
                if (t.Length == 0) continue;
                if (t.Length > 50) { error = "doc.err.tag"; return null; }
                if (!list.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) list.Add(t);
            }
            if (list.Count > 10) { error = "doc.err.tag"; return null; }
            return list;
        }

        private static string Cut(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        private static void Log(MdrContext db, long id, string action, int? actor, string detail)
        {
            db.DocumentHistory.Add(new MdrDocumentHistory { DocumentID = id, ActionKey = action, ActorUserID = actor, Detail = Cut(detail, 500), CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now });
        }

        private static void NotifyOthers(MdrContext db, MdrDocument d, int actorId, string title)
        {
            var users = db.DocumentShares.Where(s => s.DocumentID == d.DocumentID).Select(s => s.UserID).ToList();
            users.Add(d.OwnerUserID);
            foreach (var u in users.Distinct().Where(u => u != actorId))
                NotificationService.Send(u, title, d.Classification == "Secret" ? null : d.Title, moduleName: Module, entityName: "MDR_Documents",
                    entityId: d.DocumentID, linkUrl: "/Mdr/Documents#id=" + d.DocumentID);
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای اسناد", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
