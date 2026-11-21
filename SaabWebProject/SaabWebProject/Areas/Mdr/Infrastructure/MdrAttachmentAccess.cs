using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // =====================================================
    // MDR | پیوست ماژول‌های جدید روی FileManager مشترک
    // تصمیم دسترسی به پیوست = دسترسی به موجودیت مالک آن:
    //   MDR_Requests : دیدن = درخواست‌کننده یا مدیر؛ افزودن/حذف = درخواست‌کننده در پیش‌نویس
    //   MDR_Tasks    : دیدن = سازنده/مسئول/مدیر؛ افزودن = همان‌ها تا باز است؛ حذف = مدیر یا بارگذارنده
//   MDR_ChatMessages : دیدن = عضو فعال گفتگو؛ بارگذاری فقط توسط فرستنده از ChatController
    // علاوه بر آن کاربر باید به صفحه‌ی ماژول (tbUserPageAccess) دسترسی داشته باشد.
    // =====================================================
    public static class MdrAttachmentAccess
    {
        public class Decision
        {
            public bool Known { get; set; }
            public bool CanView { get; set; }
            public bool CanWrite { get; set; }
            public bool IsManager { get; set; }
            public string ModuleName { get; set; }
        }

        public static Decision Resolve(string entityName, string entityId, int userId)
        {
            long id;
            if (!long.TryParse(entityId, out id)) return new Decision();

            using (var db = new MdrContext())
            {
                if (entityName == "MDR_Requests")
                {
                    const string page = "MDR.Requests";
                    if (!PermissionHelper.HasAccess(userId, page)) return new Decision { Known = true, ModuleName = "Requests" };

                    var r = db.Requests.Where(x => x.RequestID == id).Select(x => new { x.RequesterUserID, x.Status }).FirstOrDefault();
                    if (r == null) return new Decision();
                    var manager = PermissionHelper.CanEdit(userId, page);
                    var mine = r.RequesterUserID == userId;
                    return new Decision { Known = true, ModuleName = "Requests", IsManager = manager, CanView = mine || manager, CanWrite = mine && r.Status == "Draft" };
                }

                if (entityName == "MDR_Documents")
                {
                    const string page = "MDR.Documents";
                    if (!PermissionHelper.HasAccess(userId, page)) return new Decision { Known = true, ModuleName = "Documents" };

                    var doc = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                    if (doc == null) return new Decision();
                    var manager = PermissionHelper.CanEdit(userId, page);
                    var a = Services.MdrDocumentService.GetAccess(db, doc, userId, manager);
                    // دانلود فایل‌ها و نسخه‌ها از همین مسیر؛ بارگذاری/حذف فقط از DocumentsController (CanWrite عمداً false)
                    return new Decision { Known = true, ModuleName = "Documents", IsManager = manager, CanView = a.CanView, CanWrite = false };
                }

                if (entityName == "MDR_Letters")
                {
                    const string page = "MDR.Letters";
                    if (!PermissionHelper.HasAccess(userId, page)) return new Decision { Known = true, ModuleName = "Letters" };

                    var l = db.Letters.FirstOrDefault(x => x.LetterID == id);
                    if (l == null) return new Decision();
                    var manager = PermissionHelper.CanEdit(userId, page);
                    var a = Services.MdrLetterService.GetAccess(db, l, userId, manager);
                    // افزودن/حذف پیوست فقط توسط سازنده تا وقتی پیش‌نویس است
                    return new Decision { Known = true, ModuleName = "Letters", IsManager = manager, CanView = a.CanView, CanWrite = a.IsCreator && l.Status == "Draft" };
                }

                if (entityName == "MDR_FinDocuments")
                {
                    // دیدن = همان قاعده‌ی دیدن سند مالی؛ افزودن/حذف فقط درخواست‌کننده تا وقتی پیش‌نویس است
                    if (!PermissionHelper.HasAccess(userId, "MDR.Finance.Docs")) return new Decision { Known = true, ModuleName = "Finance" };
                    var fd = db.FinDocuments.FirstOrDefault(x => x.DocumentID == id);
                    if (fd == null) return new Decision();
                    var fa = Services.MdrFinanceService.AccessFor(db, fd, userId);
                    return new Decision { Known = true, ModuleName = "Finance", IsManager = fa.IsManager, CanView = fa.CanView, CanWrite = fa.IsRequester && fd.Status == "Draft" };
                }

                if (entityName == "MDR_ChatMessages")
                {
                    // دیدن = عضو فعال گفتگو (و برای گفتگوی موجودیت: دسترسی به خود موجودیت)؛ بارگذاری فقط از ChatController (CanWrite عمداً false)
                    if (!PermissionHelper.HasAccess(userId, "MDR.Chat")) return new Decision { Known = true, ModuleName = "Chat" };
                    var tid = db.ChatMessages.Where(x => x.MessageID == id).Select(x => (long?)x.ThreadID).FirstOrDefault();
                    if (!tid.HasValue) return new Decision();
                    return new Decision { Known = true, ModuleName = "Chat", CanView = Services.MdrChatService.HasThreadAccess(userId, tid.Value), CanWrite = false };
                }

                if (entityName == "MDR_Tasks")
                {
                    const string page = "MDR.Tasks";
                    if (!PermissionHelper.HasAccess(userId, page)) return new Decision { Known = true, ModuleName = "Tasks" };

                    var t = db.Tasks.Where(x => x.TaskID == id).Select(x => new { x.CreatedBy, x.AssigneeUserID, x.Status }).FirstOrDefault();
                    if (t == null) return new Decision();
                    var manager = PermissionHelper.CanEdit(userId, page);
                    var involved = t.CreatedBy == userId || t.AssigneeUserID == userId || manager;
                    var open = t.Status == "Todo" || t.Status == "InProgress" || t.Status == "Blocked";
                    return new Decision { Known = true, ModuleName = "Tasks", IsManager = manager, CanView = involved, CanWrite = involved && open };
                }
            }

            return new Decision();
        }
    }
}
