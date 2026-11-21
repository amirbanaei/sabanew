using System.Web.Mvc;
using SaabWebProject.Areas.Mdr.Infrastructure;
using SaabWebProject.Areas.Mdr.Services;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using SaabWebProject.Areas.Mdr.Data;
using System.Web.Script.Serialization;
using System.Text;
using System.IO;
using System.Web;
using System.Data.Entity;
using Newtonsoft.Json;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Controllers
{
    // =====================================================
    // MDR | تنها کنترلر Area. همه‌ی ماژول‌ها (Workflow، Letters، Tasks، … و صفحه‌های زیرساخت AG) اینجا هستند.
    // اکشن‌ها به‌صورت «بخش_عملیات» نام‌گذاری شده‌اند (مثلاً Tasks_GetList) و MdrRoute آدرس‌های همیشگی /Mdr/Tasks/GetList را به آن‌ها می‌رساند.
    // =====================================================
    [MdrSystemLayout]
    public class MdrController : Controller
    {
        // ============================================================================================
        // بخش Home  →  آدرس‌ها: /Mdr/Home/{Action}
        // ============================================================================================

        // صفحه‌ی ورودی ماژول‌های میز مدیریت
        [MdrPageAuthorize("MDR.Home")]
        public ActionResult Home_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            return View("~/Areas/Mdr/Views/Home/Index.cshtml", MdrModuleCatalog.All);
        }


        // بدون نیاز به مجوز صفحه (مقصد رد دسترسی)
        public ActionResult Home_AccessDenied()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            return View("~/Areas/Mdr/Views/Home/AccessDenied.cshtml");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Home_SetTheme(string theme)
        {
            var userId = CurrentUserHelper.UserID;

            if (!userId.HasValue)
                return Json(new { success = false });

            return Json(new { success = MdrPreferenceService.SaveTheme(userId.Value, theme) });
        }

        // ============================================================================================
        // بخش Workbench  →  آدرس‌ها: /Mdr/Workbench/{Action}
        // ============================================================================================

        private const string Workbench_PageCode = "MDR.Workbench";


        [MdrPageAuthorize(Workbench_PageCode)]
        public ActionResult Workbench_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            return View("~/Areas/Mdr/Views/Workbench/Index.cshtml");
        }


        // KPI + اقلام کارتابل (فیلتر منبع سمت کلاینت؛ سقف ۵۰۰ قلم)
        [MdrPageAuthorize(Workbench_PageCode)]
        public JsonResult Workbench_GetData()
        {
            var userId = CurrentUserHelper.UserID.Value;
            var snap = MdrCartable.Load(userId);
            var soon = DateTime.Now.AddHours(24);

            int unread = 0;
            try { unread = NotificationService.GetUnreadCount(userId); } catch { }

            return Json(new
            {
                success = true,
                kpi = new
                {
                    open = snap.Items.Count,
                    overdue = snap.Items.Count(i => i.Overdue),
                    dueSoon = snap.Items.Count(i => !i.Overdue && i.DueAt.HasValue && i.DueAt.Value <= soon),
                    unread
                },
                failed = snap.FailedSources,
                canCreateTask = MdrPermission.CanInsert("MDR.Tasks"),
                items = snap.Items.Select(i => new
                {
                    i.Source, i.Ref, i.Title, i.Subject, i.Priority, i.Overdue, i.Url, i.Actions, i.RowVer,
                    DueAt = i.DueAt.HasValue ? i.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : null
                })
            }, JsonRequestBehavior.AllowGet);
        }


        // اجرای اقدام روی یک قلم (Batch = چند فراخوانی؛ نتیجه‌ی هر قلم جداگانه)
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workbench_PageCode)]
        public JsonResult Workbench_Act(string source, string id, string action, string reason, string rowVer)
        {
            var uid = CurrentUserHelper.UserID.Value;

            // «ایجاد تسک» از هر قلم کارتابل: تسکِ منسوب به خود کاربر با لینک به منبع
            if (action == "CreateTask")
            {
                if (source == "tsk" || !MdrPermission.CanInsert("MDR.Tasks"))
                {
                    Response.StatusCode = 403;
                    return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
                }

                var item = MdrCartable.Load(uid).Items.FirstOrDefault(i => i.Source == source && i.Ref == id);
                if (item == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") });

                var made = MdrTaskService.Save(new MdrTaskService.TaskInput
                {
                    Title = item.Subject ?? item.Title, Description = item.Title, Priority = item.Priority == "High" ? "High" : "Normal",
                    AssigneeUserId = uid, DueAt = item.DueAt, SourceType = item.Source, SourceRef = item.Ref, SourceUrl = item.Url
                }, uid, false, null);
                return Json(new { success = made.Ok, id = made.Id, error = made.Error, message = made.Ok ? null : MdrText.T(made.Error) });
            }

            var r = MdrCartable.Execute(uid, source, id, action, reason, rowVer);
            return Json(new { success = r.Ok, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }

        // ============================================================================================
        // بخش Letters  →  آدرس‌ها: /Mdr/Letters/{Action}
        // ============================================================================================

        private const string Letters_PageCode = "MDR.Letters";


        private class Letters_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Letters_PageCode)]
        public ActionResult Letters_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanInsert = MdrPermission.CanInsert(Letters_PageCode);
            ViewBag.IsManager = MdrPermission.CanEdit(Letters_PageCode);
            return View("~/Areas/Mdr/Views/Letters/Index.cshtml");
        }


        // box: inbox | sent | drafts | all (فقط مدیر)
        [MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_GetList(string box, string direction, string q, bool unread = false, bool overdue = false)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Letters_PageCode);
            var now = DateTime.Now;

            using (var db = new MdrContext())
            {
                var mine = db.LetterRecipients.Where(r => r.UserID == uid);
                IQueryable<MdrLetter> qry;

                if (box == "drafts") qry = db.Letters.Where(l => l.CreatedBy == uid && l.Status == "Draft");
                else if (box == "sent") qry = db.Letters.Where(l => l.CreatedBy == uid && (l.Status == "Registered" || l.Status == "Archived"));
                else if (box == "all" && manager)
                    qry = db.Letters.Where(l => (l.Status == "Registered" || l.Status == "Archived") &&
                                                (l.Confidentiality != "Secret" || l.CreatedBy == uid || mine.Any(r => r.LetterID == l.LetterID)));
                else qry = db.Letters.Where(l => (l.Status == "Registered" || l.Status == "Archived") && mine.Any(r => r.LetterID == l.LetterID));

                if (!string.IsNullOrEmpty(direction)) qry = qry.Where(l => l.Direction == direction);
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(l => l.Subject.Contains(s) || l.Number.Contains(s) || l.ExternalParty.Contains(s)); }

                var rows = qry.OrderByDescending(l => l.LetterID).Take(300).ToList();
                var ids = rows.Select(r => r.LetterID).ToList();
                var myRows = db.LetterRecipients.Where(r => r.UserID == uid && ids.Contains(r.LetterID)).ToList();
                var names = Letters_Names(db, rows.Select(r => r.CreatedBy));

                var items = rows.Select(l =>
                {
                    var my = myRows.Where(r => r.LetterID == l.LetterID).ToList();
                    var isUnread = my.Any(r => r.ReadAt == null);
                    var needsReply = my.Any(r => (r.Kind == "To" || r.Kind == "Referral") && r.RepliedAt == null) && l.DueAt.HasValue && l.Status == "Registered";
                    return new
                    {
                        l.LetterID, l.Number, l.Subject, l.Direction, l.Status, l.Urgency, l.Confidentiality,
                        Sender = l.Direction == "Incoming" ? l.ExternalParty : (names.ContainsKey(l.CreatedBy) ? names[l.CreatedBy] : null),
                        DueAt = l.DueAt.HasValue ? l.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        Overdue = needsReply && l.DueAt.Value < now,
                        Unread = isUnread,
                        RegisteredAt = (l.RegisteredAt ?? l.CreatedAt).ToString("yyyy-MM-dd HH:mm")
                    };
                }).ToList();

                if (unread) items = items.Where(i => i.Unread).ToList();
                if (overdue) items = items.Where(i => i.Overdue).ToList();
                return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_GetLetter(long id)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Letters_PageCode);

            using (var db = new MdrContext())
            {
                var l = db.Letters.FirstOrDefault(x => x.LetterID == id);
                if (l == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);

                var a = MdrLetterService.GetAccess(db, l, uid, manager);
                if (!a.CanView)
                {
                    // تلاش ناموفق برای نامه‌ی محرمانه/سری ثبت می‌شود
                    if (l.Confidentiality != "Normal")
                        AuditLogger.Log(AuditLogger.Actions.View, "Letters", "تلاش ناموفق برای دیدن نامه‌ی " + l.Confidentiality, entityName: "MDR_Letters",
                            entityId: id, isSuccess: false);
                    Response.StatusCode = 403;
                    return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet);
                }

                if (a.Involved && l.Status != "Draft") MdrLetterService.MarkRead(id, uid);

                var recips = db.LetterRecipients.Where(r => r.LetterID == id).OrderBy(r => r.RecipientID).ToList();
                var hist = db.LetterHistory.Where(h => h.LetterID == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.HistoryID).ToList();
                var replies = db.Letters.Where(r => r.ParentLetterID == id && r.Status != "Draft" && r.Status != "Cancelled")
                    .Select(r => new { r.LetterID, r.Number, r.Subject, r.CreatedBy, r.Confidentiality }).ToList();
                var parent = l.ParentLetterID.HasValue ? db.Letters.Where(p => p.LetterID == l.ParentLetterID.Value).Select(p => new { p.LetterID, p.Number, p.Subject }).FirstOrDefault() : null;

                var uids = recips.Select(r => r.UserID).Concat(recips.Where(r => r.FromUserID.HasValue).Select(r => r.FromUserID.Value))
                    .Concat(hist.Where(h => h.ActorUserID.HasValue).Select(h => h.ActorUserID.Value)).Concat(new[] { l.CreatedBy }).ToList();
                var names = Letters_Names(db, uids);
                Func<int?, string> nm = x => x.HasValue && names.ContainsKey(x.Value) ? names[x.Value] : null;

                var registered = l.Status == "Registered";
                var canReply = registered && a.IsRecipient;
                var onlyMine = recips.Where(r => r.UserID == uid).ToList();
                var replyTo = new List<object>();
                if (l.Direction != "Incoming" && l.CreatedBy != uid) replyTo.Add(new { Id = l.CreatedBy, Name = nm(l.CreatedBy) });

                return Json(new
                {
                    success = true,
                    letter = new
                    {
                        l.LetterID, l.Number, l.Subject, l.Body, l.Direction, l.Status, l.Urgency, l.Confidentiality, l.ExternalParty,
                        DueAt = l.DueAt.HasValue ? l.DueAt.Value.ToString("yyyy-MM-ddTHH:mm") : null,
                        Creator = nm(l.CreatedBy), l.CreatedBy,
                        CreatedAt = l.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        RegisteredAt = l.RegisteredAt.HasValue ? l.RegisteredAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        RowVer = Convert.ToBase64String(l.RowVer),
                        Parent = parent,
                        CanEdit = l.Status == "Draft" && a.IsCreator,
                        CanRegister = l.Status == "Draft" && a.IsCreator,
                        CanCancel = l.Status == "Draft" && a.IsCreator,
                        CanRefer = registered && (a.IsRecipient || (manager && l.Confidentiality != "Secret")),
                        CanReply = canReply,
                        CanArchive = registered && (a.IsCreator || manager),
                        Replied = onlyMine.Any(r => r.RepliedAt != null),
                        ReplyDefaults = canReply ? new
                        {
                            Direction = l.Direction == "Incoming" ? "Outgoing" : "Internal",
                            Subject = "پاسخ: " + l.Subject,
                            ExternalParty = l.Direction == "Incoming" ? l.ExternalParty : null,
                            To = replyTo,
                            l.Confidentiality
                        } : null
                    },
                    recipients = recips.Select(r => new
                    {
                        r.UserID, Name = nm(r.UserID), r.Kind, From = nm(r.FromUserID), r.Note,
                        ReadAt = r.ReadAt.HasValue ? r.ReadAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        RepliedAt = r.RepliedAt.HasValue ? r.RepliedAt.Value.ToString("yyyy-MM-dd HH:mm") : null
                    }),
                    replies = replies.Where(r => r.Confidentiality != "Secret" || r.CreatedBy == uid).Select(r => new { r.LetterID, r.Number, r.Subject }),
                    history = hist.Select(h => new
                    {
                        h.ActionKey, Actor = nm(h.ActorUserID), h.Detail, CreatedAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm"), CorrelationID = h.CorrelationID.ToString()
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);

            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Letters_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers " +
                    "WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        // to / cc: شناسه‌ها با کاما
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_Save(long id, string direction, string subject, string body, string urgency, string confidentiality,
            string externalParty, string dueAt, long? parentId, string to, string cc, string rowVer)
        {
            if (id == 0 && !MdrPermission.CanInsert(Letters_PageCode)) return Letters_Denied();

            DateTime? due = null;
            if (!string.IsNullOrWhiteSpace(dueAt))
            {
                DateTime d;
                if (!DateTime.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return Letters_Result(MdrResult.Fail("tsk.err.due"));
                due = d;
            }

            return Letters_Result(MdrLetterService.Save(new MdrLetterService.LetterInput
            {
                Id = id, Direction = direction, Subject = subject, Body = body, Urgency = urgency, Confidentiality = confidentiality,
                ExternalParty = externalParty, DueAt = due, ParentId = parentId, To = Letters_Ids(to), Cc = Letters_Ids(cc)
            }, CurrentUserHelper.UserID.Value, Letters_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_Register(long id, string rowVer)
        {
            if (!MdrPermission.CanInsert(Letters_PageCode)) return Letters_Denied();
            return Letters_Result(MdrLetterService.Register(id, CurrentUserHelper.UserID.Value, Letters_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_Refer(long id, int toUserId, string note)
        {
            return Letters_Result(MdrLetterService.Refer(id, toUserId, note, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Letters_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_Archive(long id)
        {
            return Letters_Result(MdrLetterService.Archive(id, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Letters_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Letters_PageCode)]
        public JsonResult Letters_CancelDraft(long id)
        {
            return Letters_Result(MdrLetterService.CancelDraft(id, CurrentUserHelper.UserID.Value));
        }


        // ---------------- helpers ----------------
        private static List<int> Letters_Ids(string csv)
        {
            return (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => { int v; return int.TryParse(s.Trim(), out v) ? v : 0; }).Where(v => v > 0).Distinct().ToList();
        }


        private static Dictionary<int, string> Letters_Names(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = db.Database.SqlQuery<Letters_UserRow>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" +
                string.Join(",", list) + ")").ToList();
            return rows.ToDictionary(r => r.Id, r => r.Name);
        }


        private static byte[] Letters_Ver(string b64)
        {
            try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; }
        }


        private JsonResult Letters_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }


        private JsonResult Letters_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Requests  →  آدرس‌ها: /Mdr/Requests/{Action}
        // ============================================================================================

        private const string Requests_PageCode = "MDR.Requests";


        private class Requests_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Requests_PageCode)]
        public ActionResult Requests_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanInsert = MdrPermission.CanInsert(Requests_PageCode);
            ViewBag.IsManager = MdrPermission.CanEdit(Requests_PageCode);
            return View("~/Areas/Mdr/Views/Requests/Index.cshtml");
        }


        // کاتالوگ: کاربر فقط انواع فعال؛ مدیر همه (با جزئیات تعریف)
        [MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_GetTypes()
        {
            var manager = MdrPermission.CanEdit(Requests_PageCode);
            using (var db = new MdrContext())
            {
                var rows = db.RequestTypes.Where(t => manager || t.IsActive).OrderBy(t => t.Name).ToList();
                var published = new HashSet<string>(db.WorkflowDefinitions.Where(d => d.Status == "Published").Select(d => d.Code));

                return Json(new
                {
                    success = true,
                    items = rows.Select(t => new
                    {
                        t.TypeID, t.Code, t.Name, t.Description, t.IsActive,
                        Ready = t.IsActive && t.WorkflowCode != null && published.Contains(t.WorkflowCode),
                        Fields = Requests_ParseFields(t.FieldsJson),
                        WorkflowCode = manager ? t.WorkflowCode : null,
                        FieldsJson = manager ? t.FieldsJson : null
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_SaveType(int id, string code, string name, string description, string workflowCode, string fieldsJson, bool isActive)
        {
            if (!MdrPermission.CanEdit(Requests_PageCode)) return Requests_Denied();
            return Requests_Result(MdrRequestService.SaveType(id, code, name, description, workflowCode, fieldsJson, isActive, CurrentUserHelper.UserID.Value));
        }


        [MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_GetList(string status, string q)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Requests_PageCode);

            using (var db = new MdrContext())
            {
                var qry = from r in db.Requests join t in db.RequestTypes on r.TypeID equals t.TypeID select new { r, t };
                if (!manager) qry = qry.Where(x => x.r.RequesterUserID == uid);
                if (!string.IsNullOrEmpty(status)) qry = qry.Where(x => x.r.Status == status);
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(x => x.r.Title.Contains(s) || x.r.Number.Contains(s)); }

                var rows = qry.OrderByDescending(x => x.r.RequestID).Take(200).ToList();
                var names = Requests_Names(db, rows.Select(x => x.r.RequesterUserID));

                return Json(new
                {
                    success = true,
                    mine = !manager,
                    items = rows.Select(x => new
                    {
                        x.r.RequestID, x.r.Number, x.r.Title, x.r.Status, Type = x.t.Name,
                        Requester = names.ContainsKey(x.r.RequesterUserID) ? names[x.r.RequesterUserID] : null,
                        CreatedAt = x.r.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_GetRequest(long id)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Requests_PageCode);

            using (var db = new MdrContext())
            {
                var r = db.Requests.FirstOrDefault(x => x.RequestID == id);
                if (r == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
                if (!manager && r.RequesterUserID != uid)
                {
                    Response.StatusCode = 403;
                    return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet);
                }

                var type = db.RequestTypes.First(t => t.TypeID == r.TypeID);
                var hist = db.RequestHistory.Where(h => h.RequestID == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.HistoryID).ToList();

                object flow = null;
                if (r.WorkflowInstanceID.HasValue)
                {
                    var inst = db.WorkflowInstances.FirstOrDefault(i => i.InstanceID == r.WorkflowInstanceID.Value);
                    if (inst != null)
                    {
                        var stepName = db.WorkflowSteps.Where(s => s.StepID == inst.CurrentStepID).Select(s => s.Name).FirstOrDefault();
                        var steps = (from h in db.WorkflowHistory where h.InstanceID == inst.InstanceID
                                     join s in db.WorkflowSteps on h.StepID equals (int?)s.StepID into sj from s in sj.DefaultIfEmpty()
                                     orderby h.CreatedAt, h.HistoryID
                                     select new { h.ActionKey, Step = s.Name, h.Reason, h.CreatedAt }).ToList();
                        flow = new
                        {
                            inst.InstanceID, inst.Status, CurrentStep = stepName,
                            DueAt = inst.DueAt.HasValue ? inst.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                            Overdue = inst.DueAt.HasValue && inst.DueAt.Value < DateTime.Now && inst.Status == "Running",
                            Timeline = steps.Select(s => new { s.ActionKey, s.Step, s.Reason, CreatedAt = s.CreatedAt.ToString("yyyy-MM-dd HH:mm") })
                        };
                    }
                }

                var ids = hist.Where(h => h.ActorUserID.HasValue).Select(h => h.ActorUserID.Value).ToList(); ids.Add(r.RequesterUserID);
                var names = Requests_Names(db, ids);

                return Json(new
                {
                    success = true,
                    request = new
                    {
                        r.RequestID, r.Number, r.Title, r.Status, r.TypeID, TypeName = type.Name,
                        Requester = names.ContainsKey(r.RequesterUserID) ? names[r.RequesterUserID] : null,
                        Data = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(r.DataJson),
                        Fields = Requests_ParseFields(type.FieldsJson),
                        RowVer = Convert.ToBase64String(r.RowVer),
                        CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        CanEdit = r.RequesterUserID == uid && r.Status == "Draft",
                        CanSubmit = r.RequesterUserID == uid && r.Status == "Draft",
                        CanCancel = (r.RequesterUserID == uid || manager) && (r.Status == "Draft" || r.Status == "Submitted")
                    },
                    flow,
                    history = hist.Select(h => new
                    {
                        h.ActionKey, Actor = h.ActorUserID.HasValue && names.ContainsKey(h.ActorUserID.Value) ? names[h.ActorUserID.Value] : null,
                        h.Detail, CreatedAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm"), CorrelationID = h.CorrelationID.ToString()
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        // data: JSON شیء {key: value}
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_Save(long id, int typeId, string title, string data, string rowVer)
        {
            if (id == 0 && !MdrPermission.CanInsert(Requests_PageCode)) return Requests_Denied();

            Dictionary<string, string> dict;
            try { dict = new JavaScriptSerializer { MaxJsonLength = 512 * 1024 }.Deserialize<Dictionary<string, string>>(string.IsNullOrWhiteSpace(data) ? "{}" : data); }
            catch { return Requests_Result(MdrResult.Fail("wf.err.badinput")); }

            return Requests_Result(MdrRequestService.Save(id, typeId, title, dict, CurrentUserHelper.UserID.Value, Requests_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_Submit(long id, string rowVer)
        {
            if (!MdrPermission.CanInsert(Requests_PageCode)) return Requests_Denied();
            return Requests_Result(MdrRequestService.Submit(id, CurrentUserHelper.UserID.Value, Requests_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Requests_PageCode)]
        public JsonResult Requests_Cancel(long id, string reason, string rowVer)
        {
            return Requests_Result(MdrRequestService.Cancel(id, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Requests_PageCode), reason, Requests_Ver(rowVer)));
        }


        // ---------------- helpers ----------------
        private static List<MdrFieldDef> Requests_ParseFields(string json) { return MdrRequestService.ParseFields(json) ?? new List<MdrFieldDef>(); }


        private static Dictionary<int, string> Requests_Names(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = db.Database.SqlQuery<Requests_UserRow>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" +
                string.Join(",", list) + ")").ToList();
            return rows.ToDictionary(r => r.Id, r => r.Name);
        }


        private static byte[] Requests_Ver(string b64)
        {
            try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; }
        }


        private JsonResult Requests_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }


        private JsonResult Requests_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Tasks  →  آدرس‌ها: /Mdr/Tasks/{Action}
        // ============================================================================================

        private const string Tasks_PageCode = "MDR.Tasks";


        private class Tasks_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Tasks_PageCode)]
        public ActionResult Tasks_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanInsert = MdrPermission.CanInsert(Tasks_PageCode);
            ViewBag.IsManager = MdrPermission.CanEdit(Tasks_PageCode);
            ViewBag.UserId = CurrentUserHelper.UserID.Value;
            return View("~/Areas/Mdr/Views/Tasks/Index.cshtml");
        }


        // scope: mine (مسئول یا سازنده) | all (فقط manager)
        [MdrPageAuthorize(Tasks_PageCode)]
        public JsonResult Tasks_GetList(string scope, string status, string q, bool overdue = false)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Tasks_PageCode);
            var all = scope == "all" && manager;
            var now = DateTime.Now;

            using (var db = new MdrContext())
            {
                var qry = db.Tasks.AsQueryable();
                if (!all) qry = qry.Where(t => t.AssigneeUserID == uid || t.CreatedBy == uid);
                if (status == "open") qry = qry.Where(t => t.Status == "Todo" || t.Status == "InProgress" || t.Status == "Blocked");
                else if (!string.IsNullOrEmpty(status)) qry = qry.Where(t => t.Status == status);
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(t => t.Title.Contains(s)); }
                if (overdue) qry = qry.Where(t => t.DueAt != null && t.DueAt < now && (t.Status == "Todo" || t.Status == "InProgress" || t.Status == "Blocked"));

                var rows = qry.OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.TaskID).Take(300).ToList();
                var names = Tasks_Names(db, rows.Select(r => r.AssigneeUserID).Where(x => x.HasValue).Select(x => x.Value));

                var items = rows.Select(t => new
                {
                    t.TaskID, t.ParentTaskID, t.Title, t.Status, t.Priority, t.AssigneeUserID,
                    Assignee = t.AssigneeUserID.HasValue && names.ContainsKey(t.AssigneeUserID.Value) ? names[t.AssigneeUserID.Value] : null,
                    DueAt = t.DueAt.HasValue ? t.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                    Overdue = t.DueAt.HasValue && t.DueAt.Value < now && MdrTaskService.IsOpen(t.Status),
                    t.SourceType
                });
                return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Tasks_PageCode)]
        public JsonResult Tasks_GetTask(long id)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Tasks_PageCode);

            using (var db = new MdrContext())
            {
                var t = db.Tasks.FirstOrDefault(x => x.TaskID == id);
                if (t == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
                if (!manager && t.AssigneeUserID != uid && t.CreatedBy != uid)
                {
                    Response.StatusCode = 403;
                    return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet);
                }

                var subs = db.Tasks.Where(s => s.ParentTaskID == id).OrderBy(s => s.TaskID).ToList();
                var comments = db.TaskComments.Where(c => c.TaskID == id).OrderBy(c => c.CreatedAt).ToList();
                var hist = db.TaskHistory.Where(h => h.TaskID == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.HistoryID).ToList();

                var ids = new List<int> { t.CreatedBy };
                if (t.AssigneeUserID.HasValue) ids.Add(t.AssigneeUserID.Value);
                ids.AddRange(subs.Where(s => s.AssigneeUserID.HasValue).Select(s => s.AssigneeUserID.Value));
                ids.AddRange(comments.Select(c => c.UserID));
                ids.AddRange(hist.Where(h => h.ActorUserID.HasValue).Select(h => h.ActorUserID.Value));
                var names = Tasks_Names(db, ids);
                Func<int?, string> nm = x => x.HasValue && names.ContainsKey(x.Value) ? names[x.Value] : null;

                var isAssignee = t.AssigneeUserID == uid;
                return Json(new
                {
                    success = true,
                    task = new
                    {
                        t.TaskID, t.ParentTaskID, t.Title, t.Description, t.Status, t.Priority, t.AssigneeUserID, Assignee = nm(t.AssigneeUserID),
                        t.CreatedBy, Creator = nm(t.CreatedBy), t.SourceType, t.SourceUrl, t.ResultText, t.BlockedReason,
                        DueAt = t.DueAt.HasValue ? t.DueAt.Value.ToString("yyyy-MM-ddTHH:mm") : null,
                        CreatedAt = t.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        RowVer = Convert.ToBase64String(t.RowVer),
                        CanEdit = (manager || t.CreatedBy == uid) && MdrTaskService.IsOpen(t.Status),
                        CanWork = (manager || isAssignee) && MdrTaskService.IsOpen(t.Status),
                        CanDelegate = (manager || isAssignee || t.CreatedBy == uid) && MdrTaskService.IsOpen(t.Status),
                        CanCancel = (manager || t.CreatedBy == uid) && MdrTaskService.IsOpen(t.Status)
                    },
                    subtasks = subs.Select(s => new { s.TaskID, s.Title, s.Status, Assignee = nm(s.AssigneeUserID) }),
                    comments = comments.Select(c => new { User = nm(c.UserID), c.Body, CreatedAt = c.CreatedAt.ToString("yyyy-MM-dd HH:mm") }),
                    history = hist.Select(h => new
                    {
                        h.ActionKey, Actor = nm(h.ActorUserID), h.Detail, CreatedAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        CorrelationID = h.CorrelationID.ToString()
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        // انتخاب مسئول: جستجوی نام کاربر (حداکثر ۲۰ مورد)
        [MdrPageAuthorize(Tasks_PageCode)]
        public JsonResult Tasks_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);

            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Tasks_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers " +
                    "WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Tasks_PageCode)]
        public JsonResult Tasks_Save(long id, long? parentId, string title, string description, string priority, int? assigneeUserId, string dueAt, string rowVer)
        {
            if (id == 0 && !MdrPermission.CanInsert(Tasks_PageCode)) return Tasks_Denied();

            DateTime? due = null;
            if (!string.IsNullOrWhiteSpace(dueAt))
            {
                DateTime d;
                if (!DateTime.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return Tasks_Result(MdrResult.Fail("tsk.err.due"));
                due = d;
            }

            return Tasks_Result(MdrTaskService.Save(new MdrTaskService.TaskInput
            {
                Id = id, ParentId = parentId, Title = title, Description = description, Priority = priority,
                AssigneeUserId = assigneeUserId, DueAt = due
            }, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Tasks_PageCode), Tasks_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Tasks_PageCode)]
        public JsonResult Tasks_Act(long id, string action, string text, string rowVer)
        {
            return Tasks_Result(MdrTaskService.Act(id, action, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Tasks_PageCode), text, Tasks_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Tasks_PageCode)]
        public JsonResult Tasks_Comment(long id, string body)
        {
            var uid = CurrentUserHelper.UserID.Value;
            using (var db = new MdrContext())
            {
                var t = db.Tasks.Where(x => x.TaskID == id).Select(x => new { x.CreatedBy, x.AssigneeUserID }).FirstOrDefault();
                if (t != null && !MdrPermission.CanEdit(Tasks_PageCode) && t.CreatedBy != uid && t.AssigneeUserID != uid) return Tasks_Denied();
            }
            return Tasks_Result(MdrTaskService.Comment(id, body, uid));
        }


        // ---------------- helpers ----------------
        private static Dictionary<int, string> Tasks_Names(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();

            // فقط عدد صحیح در SQL می‌آید (بدون ورودی کاربر)
            var rows = db.Database.SqlQuery<Tasks_UserRow>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" +
                string.Join(",", list) + ")").ToList();
            return rows.ToDictionary(r => r.Id, r => r.Name);
        }


        private static byte[] Tasks_Ver(string b64)
        {
            try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; }
        }


        private JsonResult Tasks_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }


        private JsonResult Tasks_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Documents  →  آدرس‌ها: /Mdr/Documents/{Action}
        // ============================================================================================

        private const string Documents_PageCode = "MDR.Documents";


        private class Documents_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Documents_PageCode)]
        public ActionResult Documents_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanInsert = MdrPermission.CanInsert(Documents_PageCode);
            ViewBag.IsManager = MdrPermission.CanEdit(Documents_PageCode);
            return View("~/Areas/Mdr/Views/Documents/Index.cshtml");
        }


        [MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_GetFolders()
        {
            using (var db = new MdrContext())
                return Json(new { success = true, items = db.DocFolders.OrderBy(f => f.Name).Select(f => new { f.FolderID, f.ParentFolderID, f.Name, f.OwnerUserID }).ToList() },
                    JsonRequestBehavior.AllowGet);
        }


        // scope: mine | shared | all (فقط مدیر؛ غیرسری)
        [MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_GetList(int? folderId, string q, string tag, string scope, bool archived = false)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Documents_PageCode);

            using (var db = new MdrContext())
            {
                var shared = db.DocumentShares.Where(s => s.UserID == uid).Select(s => s.DocumentID);
                IQueryable<MdrDocument> qry;
                if (scope == "all" && manager) qry = db.Documents.Where(d => d.OwnerUserID == uid || shared.Contains(d.DocumentID) || d.Classification != "Secret");
                else if (scope == "shared") qry = db.Documents.Where(d => shared.Contains(d.DocumentID));
                else qry = db.Documents.Where(d => d.OwnerUserID == uid);

                qry = qry.Where(d => archived ? d.Status == "Archived" : d.Status == "Active");
                if (folderId.HasValue && folderId.Value > 0) qry = qry.Where(d => d.FolderID == folderId.Value);
                if (folderId.HasValue && folderId.Value == -1) qry = qry.Where(d => d.FolderID == null);
                if (!string.IsNullOrWhiteSpace(tag)) { var tg = tag.Trim(); qry = qry.Where(d => db.DocumentTags.Any(t => t.DocumentID == d.DocumentID && t.Tag == tg)); }
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    qry = qry.Where(d => d.Title.Contains(s) || d.Description.Contains(s) || db.DocumentTags.Any(t => t.DocumentID == d.DocumentID && t.Tag.Contains(s)));
                }

                var rows = qry.OrderByDescending(d => d.DocumentID).Take(300).ToList();
                var ids = rows.Select(r => r.DocumentID).ToList();
                var tags = db.DocumentTags.Where(t => ids.Contains(t.DocumentID)).ToList().GroupBy(t => t.DocumentID).ToDictionary(g => g.Key, g => g.Select(t => t.Tag).ToList());
                var names = Documents_Names(db, rows.Select(r => r.OwnerUserID));

                return Json(new
                {
                    success = true,
                    items = rows.Select(d => new
                    {
                        d.DocumentID, d.Title, d.Classification, d.Status, d.FolderID, d.OwnerUserID,
                        Owner = names.ContainsKey(d.OwnerUserID) ? names[d.OwnerUserID] : null,
                        Tags = tags.ContainsKey(d.DocumentID) ? tags[d.DocumentID] : new List<string>(),
                        CreatedAt = d.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_GetDocument(long id)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Documents_PageCode);

            using (var db = new MdrContext())
            {
                var d = db.Documents.FirstOrDefault(x => x.DocumentID == id);
                if (d == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);

                var a = MdrDocumentService.GetAccess(db, d, uid, manager);
                if (!a.CanView)
                {
                    if (d.Classification != "Normal")
                        AuditLogger.Log(AuditLogger.Actions.View, "Documents", "تلاش ناموفق برای دیدن سند " + d.Classification, entityName: "MDR_Documents", entityId: id, isSuccess: false);
                    Response.StatusCode = 403;
                    return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet);
                }

                var tags = db.DocumentTags.Where(t => t.DocumentID == id).OrderBy(t => t.Tag).Select(t => t.Tag).ToList();
                var shares = db.DocumentShares.Where(s => s.DocumentID == id).OrderBy(s => s.ShareID).ToList();
                var links = db.DocumentLinks.Where(l => l.DocumentID == id).OrderBy(l => l.LinkID).ToList();
                var hist = db.DocumentHistory.Where(h => h.DocumentID == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.HistoryID).ToList();

                var ids = new List<int> { d.OwnerUserID };
                ids.AddRange(shares.Select(s => s.UserID)); ids.AddRange(hist.Where(h => h.ActorUserID.HasValue).Select(h => h.ActorUserID.Value));
                var names = Documents_Names(db, ids);
                Func<int?, string> nm = x => x.HasValue && names.ContainsKey(x.Value) ? names[x.Value] : null;

                var versions = d.FileGuid.HasValue ? FileManager.GetVersions(d.FileGuid.Value) : new List<FileManager.FileVersionItem>();

                return Json(new
                {
                    success = true,
                    document = new
                    {
                        d.DocumentID, d.Title, d.Description, d.Classification, d.Status, d.FolderID, d.OwnerUserID, Owner = nm(d.OwnerUserID),
                        FileGuid = d.FileGuid,
                        CreatedAt = d.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        RowVer = Convert.ToBase64String(d.RowVer),
                        a.IsOwner, a.CanEdit, a.CanShare, a.CanArchive
                    },
                    tags,
                    shares = shares.Select(s => new { s.UserID, Name = nm(s.UserID), s.Permission, CanRemove = a.CanShare || s.UserID == uid }),
                    links = links.Select(l => new { l.LinkID, l.EntityName, l.EntityID, Url = Documents_LinkUrl(l.EntityName, l.EntityID) }),
                    versions = versions.Select(v => new { v.VersionNo, v.OriginalFileName, v.FileSize, v.ChangeNote, v.PersianDate, v.UploadedByName, v.IsCurrent }),
                    history = hist.Select(h => new { h.ActionKey, Actor = nm(h.ActorUserID), h.Detail, CreatedAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm"), CorrelationID = h.CorrelationID.ToString() })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);
            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Documents_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers " +
                    "WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        // ---------------- نوشتن ----------------
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_CreateFolder(int? parentId, string name)
        {
            if (!MdrPermission.CanInsert(Documents_PageCode)) return Documents_Denied();
            return Documents_Result(MdrDocumentService.CreateFolder(parentId, name, CurrentUserHelper.UserID.Value));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_DeleteFolder(int id)
        {
            return Documents_Result(MdrDocumentService.DeleteFolder(id, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        // multipart: file + فیلدها (tags با کاما)
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_Create(int? folderId, string title, string description, string classification, string tags)
        {
            if (!MdrPermission.CanInsert(Documents_PageCode)) return Documents_Denied();
            var file = Request.Files.Count > 0 ? Request.Files[0] : null;

            return Documents_Result(MdrDocumentService.CreateDocument(new MdrDocumentService.DocInput
            {
                FolderId = folderId > 0 ? folderId : null, Title = title, Description = description, Classification = classification, Tags = Documents_SplitTags(tags)
            }, file, CurrentUserHelper.UserID.Value));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_NewVersion(long id, string note)
        {
            var file = Request.Files.Count > 0 ? Request.Files[0] : null;
            return Documents_Result(MdrDocumentService.NewVersion(id, file, note, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_UpdateMeta(long id, string title, string description, string classification, int? folderId, string rowVer)
        {
            return Documents_Result(MdrDocumentService.UpdateMeta(id, title, description, classification, folderId > 0 ? folderId : null,
                CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode), Documents_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_SetTags(long id, string tags)
        {
            return Documents_Result(MdrDocumentService.SetTags(id, Documents_SplitTags(tags), CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_Share(long id, int userId, string permission)
        {
            return Documents_Result(MdrDocumentService.Share(id, userId, permission, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_Unshare(long id, int userId)
        {
            return Documents_Result(MdrDocumentService.Unshare(id, userId, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_Link(long id, string entityName, string entityId)
        {
            return Documents_Result(MdrDocumentService.Link(id, entityName, entityId, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_Unlink(long id, long linkId)
        {
            return Documents_Result(MdrDocumentService.Unlink(id, linkId, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Documents_PageCode)]
        public JsonResult Documents_Archive(long id, bool archive)
        {
            return Documents_Result(MdrDocumentService.SetArchived(id, archive, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Documents_PageCode)));
        }


        // ---------------- helpers ----------------
        private static string Documents_LinkUrl(string entity, string id)
        {
            switch (entity)
            {
                case "MDR_Requests": return "/Mdr/Requests#id=" + id;
                case "MDR_Tasks": return "/Mdr/Tasks#id=" + id;
                case "MDR_Letters": return "/Mdr/Letters#id=" + id;
                default: return null;
            }
        }


        private static List<string> Documents_SplitTags(string csv)
        {
            return (csv ?? "").Split(new[] { ',', '،' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        }


        private static Dictionary<int, string> Documents_Names(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = db.Database.SqlQuery<Documents_UserRow>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" +
                string.Join(",", list) + ")").ToList();
            return rows.ToDictionary(r => r.Id, r => r.Name);
        }


        private static byte[] Documents_Ver(string b64)
        {
            try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; }
        }


        private JsonResult Documents_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : (r.Message ?? MdrText.T(r.Error)) });
        }


        private JsonResult Documents_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Cases  →  آدرس‌ها: /Mdr/Cases/{Action}
        // ============================================================================================

        private const string Cases_PageCode = "MDR.Cases";


        private class Cases_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Cases_PageCode)]
        public ActionResult Cases_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanInsert = MdrPermission.CanInsert(Cases_PageCode);
            ViewBag.IsManager = MdrPermission.CanEdit(Cases_PageCode);
            ViewBag.CanTask = PermissionHelper.HasAccess(CurrentUserHelper.UserID.Value, "MDR.Tasks");
            ViewBag.CanChat = PermissionHelper.HasAccess(CurrentUserHelper.UserID.Value, "MDR.Chat");
            return View("~/Areas/Mdr/Views/Cases/Index.cshtml");
        }


        // scope: mine (مالک/سازنده/عضو) | all (فقط مدیر؛ غیر از پرونده‌ی سری). status: خالی = همه.  excludeId برای انتخاب مقصد ادغام.
        [MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_GetList(string scope, string status, string q, long? excludeId)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var all = scope == "all" && MdrPermission.CanEdit(Cases_PageCode);

            using (var db = new MdrContext())
            {
                var qry = db.Cases.Where(c => c.OwnerUserID == uid || c.CreatedBy == uid ||
                    db.CaseMembers.Any(m => m.CaseID == c.CaseID && m.UserID == uid) || (all && c.Confidentiality != "Secret"));
                if (!string.IsNullOrEmpty(status)) qry = qry.Where(c => c.Status == status);
                if (excludeId.HasValue) qry = qry.Where(c => c.CaseID != excludeId.Value);
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(c => c.Title.Contains(s) || c.Number.Contains(s)); }

                var rows = qry.OrderByDescending(c => c.CaseID).Take(300)
                    .Select(c => new { c.CaseID, c.Number, c.Title, c.Status, c.Confidentiality, c.OwnerUserID, c.CreatedAt, Links = db.CaseLinks.Count(l => l.CaseID == c.CaseID) }).ToList();
                var names = Cases_Names(db, rows.Select(r => r.OwnerUserID));

                var items = rows.Select(r => new
                {
                    r.CaseID, r.Number, r.Title, r.Status, r.Confidentiality, Owner = names.ContainsKey(r.OwnerUserID) ? names[r.OwnerUserID] : null,
                    r.Links, CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd")
                });
                return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_GetCase(long id)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var manager = MdrPermission.CanEdit(Cases_PageCode);

            using (var db = new MdrContext())
            {
                var c = db.Cases.FirstOrDefault(x => x.CaseID == id);
                // «وجود ندارد» و «دسترسی ندارید» عمداً یک پاسخ‌اند تا وجود پرونده لو نرود
                if (c == null) return Cases_NotFound();
                var a = MdrCaseService.AccessFor(db, c, uid, manager);
                if (!a.CanView) return Cases_NotFound();

                var links = db.CaseLinks.Where(l => l.CaseID == id).OrderBy(l => l.LinkID).ToList();
                var members = db.CaseMembers.Where(m => m.CaseID == id).OrderBy(m => m.AddedAt).ToList();
                var notes = db.CaseNotes.Where(n => n.CaseID == id).OrderBy(n => n.CreatedAt).ThenBy(n => n.NoteID).ToList();
                var hist = db.CaseHistory.Where(h => h.CaseID == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.HistoryID).ToList();

                // هر موجودیتی که خود کاربر نمی‌تواند ببیند فقط شمرده می‌شود؛ عنوان و وضعیت آن هرگز نمی‌آید
                var info = new Dictionary<string, MdrSearchService.EntityInfo>();
                foreach (var g in links.GroupBy(l => l.EntityType))
                    foreach (var e in MdrSearchService.Lookup(uid, g.Key, g.Select(x => x.EntityID))) info[g.Key + ":" + e.Id] = e;

                var ids = new List<int> { c.OwnerUserID, c.CreatedBy };
                ids.AddRange(members.Select(m => m.UserID)); ids.AddRange(links.Select(l => l.LinkedBy));
                ids.AddRange(notes.Select(n => n.UserID)); ids.AddRange(hist.Where(h => h.ActorUserID.HasValue).Select(h => h.ActorUserID.Value));
                var names = Cases_Names(db, ids);
                Func<int?, string> nm = x => x.HasValue && names.ContainsKey(x.Value) ? names[x.Value] : null;

                string mergedInto = null;
                if (c.MergedIntoCaseID.HasValue)
                    mergedInto = db.Cases.Where(x => x.CaseID == c.MergedIntoCaseID.Value).Select(x => x.Number).FirstOrDefault();

                var open = c.Status == "Open";
                return Json(new
                {
                    success = true,
                    @case = new
                    {
                        c.CaseID, c.Number, c.Title, c.Description, c.Status, c.Confidentiality, c.OwnerUserID, Owner = nm(c.OwnerUserID), Creator = nm(c.CreatedBy),
                        CreatedAt = c.CreatedAt.ToString("yyyy-MM-dd HH:mm"), ClosedAt = c.ClosedAt.HasValue ? c.ClosedAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        c.CloseReason, MergedInto = mergedInto, RowVer = Convert.ToBase64String(c.RowVer), a.Role,
                        CanEdit = open && a.CanEdit, CanManage = open && a.CanManage, CanReopen = c.Status == "Closed" && a.CanManage,
                        CanMerge = open && a.CanManage
                    },
                    links = links.Select(l =>
                    {
                        MdrSearchService.EntityInfo e;
                        var seen = info.TryGetValue(l.EntityType + ":" + l.EntityID, out e);
                        return new
                        {
                            l.LinkID, Type = l.EntityType, l.EntityID, Visible = seen, Title = seen ? e.Title : null, Status = seen ? e.Status : null,
                            Url = seen ? e.Url : null, l.Note, LinkedBy = nm(l.LinkedBy), LinkedAt = l.LinkedAt.ToString("yyyy-MM-dd HH:mm")
                        };
                    }),
                    hidden = links.Count(l => !info.ContainsKey(l.EntityType + ":" + l.EntityID)),
                    members = members.Select(m => new { m.UserID, Name = nm(m.UserID), m.Role }),
                    notes = notes.Select(n => new { n.NoteID, User = nm(n.UserID), n.Body, CreatedAt = n.CreatedAt.ToString("yyyy-MM-dd HH:mm") }),
                    history = hist.Select(h => new
                    {
                        h.ActionKey, Actor = nm(h.ActorUserID), h.Detail, CreatedAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm"), CorrelationID = h.CorrelationID.ToString()
                    })
                }, JsonRequestBehavior.AllowGet);
            }
        }


        // نتیجه‌ی جستجوی مشترک (همان ACL جستجوی جامع) برای انتخاب موجودیتِ قابل‌لینک
        [MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_SearchEntities(string q, string key)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var r = MdrSearchService.Search(uid, q, key, 0, 10, null, null);
            if (r.Error != null) return Json(new { success = false, error = r.Error, message = MdrText.T(r.Error) }, JsonRequestBehavior.AllowGet);
            var src = r.Sources.FirstOrDefault(s => s.Key == key);
            return Json(new
            {
                success = true,
                items = src == null ? new object[0] : src.Items.Select(h => (object)new { h.Id, h.Title, h.Status, h.Url }).ToArray()
            }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);

            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Cases_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers " +
                    "WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Save(long id, string title, string description, string confidentiality, int? ownerUserId, string rowVer, bool confirmSimilar = false)
        {
            var uid = CurrentUserHelper.UserID.Value;
            var input = new MdrCaseService.CaseInput { Title = title, Description = description, Confidentiality = confidentiality, OwnerUserId = ownerUserId };

            if (id == 0)
            {
                if (!MdrPermission.CanInsert(Cases_PageCode)) return Cases_Denied();
                var r = MdrCaseService.Create(input, uid, MdrPermission.CanEdit(Cases_PageCode), confirmSimilar);
                if (r.Error == "case.err.similar")
                    return Json(new
                    {
                        success = false, error = r.Error, message = MdrText.T(r.Error),
                        similar = MdrCaseService.FindSimilar(uid, MdrPermission.CanEdit(Cases_PageCode), title)
                    });
                return Cases_Result(r);
            }
            return Cases_Result(MdrCaseService.Update(id, input, Cases_Ver(rowVer), uid, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Link(long id, string key, long entityId, string note)
        {
            return Cases_Result(MdrCaseService.Link(id, key, entityId, note, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Unlink(long linkId)
        {
            return Cases_Result(MdrCaseService.Unlink(linkId, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Note(long id, string body)
        {
            return Cases_Result(MdrCaseService.AddNote(id, body, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_SetMember(long id, int userId, string role)
        {
            return Cases_Result(MdrCaseService.SetMember(id, userId, role, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_RemoveMember(long id, int userId)
        {
            return Cases_Result(MdrCaseService.RemoveMember(id, userId, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_CreateTask(long id, string title, string description, string priority, int? assigneeUserId, string dueAt)
        {
            DateTime? due = null;
            if (!string.IsNullOrWhiteSpace(dueAt))
            {
                DateTime d;
                if (!DateTime.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return Cases_Result(MdrResult.Fail("tsk.err.due"));
                due = d;
            }
            if (!PermissionHelper.HasAccess(CurrentUserHelper.UserID.Value, "MDR.Tasks") || !MdrPermission.CanInsert("MDR.Tasks")) return Cases_Denied();

            return Cases_Result(MdrCaseService.CreateTask(id, new MdrTaskService.TaskInput
            {
                Title = title, Description = description, Priority = priority, AssigneeUserId = assigneeUserId, DueAt = due
            }, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Close(long id, string reason, string rowVer, bool force = false)
        {
            var r = MdrCaseService.Close(id, reason, force, Cases_Ver(rowVer), CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode));
            if (r.Error == "case.err.openitems")
                return Json(new { success = false, error = r.Error, message = MdrText.T(r.Error), openCount = MdrCaseService.OpenItemCount(id) });
            return Cases_Result(r);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Reopen(long id, string reason, string rowVer)
        {
            return Cases_Result(MdrCaseService.Reopen(id, reason, Cases_Ver(rowVer), CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Cases_PageCode)]
        public JsonResult Cases_Merge(long id, long targetId, string reason)
        {
            return Cases_Result(MdrCaseService.Merge(id, targetId, reason, CurrentUserHelper.UserID.Value, MdrPermission.CanEdit(Cases_PageCode)));
        }


        // ---------------- helpers ----------------
        private static Dictionary<int, string> Cases_Names(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();

            // فقط عدد صحیح در SQL می‌آید (بدون ورودی کاربر)
            var rows = db.Database.SqlQuery<Cases_UserRow>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" +
                string.Join(",", list) + ")").ToList();
            return rows.ToDictionary(r => r.Id, r => r.Name);
        }


        private static byte[] Cases_Ver(string b64)
        {
            try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; }
        }


        private JsonResult Cases_NotFound()
        {
            return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
        }


        private JsonResult Cases_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }


        private JsonResult Cases_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Workflow  →  آدرس‌ها: /Mdr/Workflow/{Action}
        // ============================================================================================

        private const string Workflow_ListCode = "MDR.Workflow.List";

        private const string Workflow_MonitorCode = "MDR.Workflow.Monitor";


        // ------------------ فهرست فرایندها ------------------

        [MdrPageAuthorize(Workflow_ListCode)]
        public ActionResult Workflow_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanEdit = MdrPermission.CanEdit(Workflow_ListCode);
            ViewBag.CanInsert = MdrPermission.CanInsert(Workflow_ListCode);
            return View("~/Areas/Mdr/Views/Workflow/Index.cshtml");
        }


        [MdrPageAuthorize(Workflow_ListCode)]
        public JsonResult Workflow_GetDefinitions()
        {
            using (var db = new MdrContext())
            {
                var active = db.WorkflowInstances
                    .Where(i => i.Status == "Running" || i.Status == "Paused")
                    .GroupBy(i => i.WorkflowID)
                    .Select(g => new { WorkflowID = g.Key, Count = g.Count() })
                    .ToDictionary(x => x.WorkflowID, x => x.Count);

                var rows = db.WorkflowDefinitions
                    .OrderBy(d => d.Code).ThenByDescending(d => d.Version)
                    .ToList()
                    .Select(d => new
                    {
                        d.WorkflowID, d.Code, d.Name, d.Version, d.Status,
                        ActiveInstances = active.ContainsKey(d.WorkflowID) ? active[d.WorkflowID] : 0
                    });

                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_ListCode)]
        public JsonResult Workflow_Publish(int id)
        {
            if (!MdrPermission.CanEdit(Workflow_ListCode)) return Workflow_Denied();
            return Workflow_Result(MdrWorkflowService.Publish(id, CurrentUserHelper.UserID.Value));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_ListCode)]
        public JsonResult Workflow_Start(int id, string subject)
        {
            if (!MdrPermission.CanInsert(Workflow_ListCode)) return Workflow_Denied();
            return Workflow_Result(MdrWorkflowService.StartInstance(id, subject, CurrentUserHelper.UserID.Value));
        }


        // ------------------ مانیتور فرایند ------------------

        [MdrPageAuthorize(Workflow_MonitorCode)]
        public ActionResult Workflow_Monitor()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanEdit = MdrPermission.CanEdit(Workflow_MonitorCode);
            return View("~/Areas/Mdr/Views/Workflow/Monitor.cshtml");
        }


        [MdrPageAuthorize(Workflow_MonitorCode)]
        public JsonResult Workflow_GetInstances(string status, int page = 1)
        {
            const int pageSize = 20;
            if (page < 1) page = 1;

            using (var db = new MdrContext())
            {
                var q = db.WorkflowInstances.AsQueryable();
                if (!string.IsNullOrEmpty(status)) q = q.Where(i => i.Status == status);

                var total = q.Count();
                var now = DateTime.Now;

                var items = (from i in q
                             join d in db.WorkflowDefinitions on i.WorkflowID equals d.WorkflowID
                             join s in db.WorkflowSteps on i.CurrentStepID equals (int?)s.StepID into sj
                             from s in sj.DefaultIfEmpty()
                             orderby i.StartedAt descending
                             select new { i.InstanceID, i.Subject, i.Status, i.StartedAt, i.DueAt, i.RowVer,
                                          Workflow = d.Name, Step = s.Name })
                            .Skip((page - 1) * pageSize).Take(pageSize).ToList()
                            .Select(x => new
                            {
                                x.InstanceID, x.Subject, x.Status, x.Workflow, x.Step,
                                StartedAt = x.StartedAt.ToString("yyyy-MM-dd HH:mm"),
                                DueAt = x.DueAt.HasValue ? x.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                                Overdue = x.DueAt.HasValue && x.DueAt.Value < now && x.Status == "Running",
                                RowVer = Convert.ToBase64String(x.RowVer)
                            });

                return Json(new { success = true, total, pageSize, items }, JsonRequestBehavior.AllowGet);
            }
        }


        [MdrPageAuthorize(Workflow_MonitorCode)]
        public JsonResult Workflow_GetTimeline(long id)
        {
            using (var db = new MdrContext())
            {
                var rows = (from h in db.WorkflowHistory
                            where h.InstanceID == id
                            join s in db.WorkflowSteps on h.StepID equals (int?)s.StepID into sj
                            from s in sj.DefaultIfEmpty()
                            orderby h.CreatedAt, h.HistoryID
                            select new { h.ActionKey, Step = s.Name, h.ActorUserID, h.Reason, h.CreatedAt, h.CorrelationID })
                           .ToList()
                           .Select(h => new
                           {
                               h.ActionKey, h.Step, h.ActorUserID, h.Reason,
                               CreatedAt = h.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                               CorrelationID = h.CorrelationID.ToString()
                           });

                var actions = db.WorkflowInstances.Where(i => i.InstanceID == id && i.CurrentStepID != null)
                    .Join(db.WorkflowTransitions, i => i.CurrentStepID, t => (int?)t.FromStepID, (i, t) => t.ActionKey)
                    .ToList();

                return Json(new { success = true, items = rows, actions }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_MonitorCode)]
        public JsonResult Workflow_Act(long id, string actionKey, string reason, string rowVer)
        {
            return Workflow_Result(MdrWorkflowService.Advance(id, actionKey, CurrentUserHelper.UserID.Value,
                MdrPermission.CanEdit(Workflow_MonitorCode), reason, Workflow_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_MonitorCode)]
        public JsonResult Workflow_Intervene(long id, string action, string reason, string rowVer)
        {
            if (!MdrPermission.CanEdit(Workflow_MonitorCode)) return Workflow_Denied();
            return Workflow_Result(MdrWorkflowService.Intervene(id, action, CurrentUserHelper.UserID.Value, reason, Workflow_Ver(rowVer)));
        }


        // بررسی دستی SLA (مدیر)
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_MonitorCode)]
        public JsonResult Workflow_RunSla()
        {
            if (!MdrPermission.CanEdit(Workflow_MonitorCode)) return Workflow_Denied();

            try
            {
                return Json(new { success = true, notified = MdrWorkflowSlaService.CheckOverdue() });
            }
            catch
            {
                return Workflow_Result(MdrResult.Fail("wf.err.unexpected"));
            }
        }


        // ------------------ طراح گردش کار ------------------

        private const string Workflow_DesignerCode = "MDR.Workflow.Designer";


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_ListCode)]
        public JsonResult Workflow_Create(string code, string name)
        {
            if (!MdrPermission.CanInsert(Workflow_ListCode)) return Workflow_Denied();
            return Workflow_Result(MdrWorkflowDesignService.Create(code, name, CurrentUserHelper.UserID.Value));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_ListCode)]
        public JsonResult Workflow_Clone(int id)
        {
            if (!MdrPermission.CanInsert(Workflow_ListCode)) return Workflow_Denied();
            return Workflow_Result(MdrWorkflowDesignService.Clone(id, CurrentUserHelper.UserID.Value));
        }


        [MdrPageAuthorize(Workflow_DesignerCode)]
        public ActionResult Workflow_Designer(int id)
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanEdit = MdrPermission.CanEdit(Workflow_DesignerCode);
            ViewBag.WorkflowId = id;
            return View("~/Areas/Mdr/Views/Workflow/Designer.cshtml");
        }


        [MdrPageAuthorize(Workflow_DesignerCode)]
        public JsonResult Workflow_GetDefinition(int id)
        {
            var dto = MdrWorkflowDesignService.Get(id);
            if (dto == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, item = dto }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Workflow_DesignerCode)]
        public JsonResult Workflow_Save(int id, string json)
        {
            if (!MdrPermission.CanEdit(Workflow_DesignerCode)) return Workflow_Denied();

            MdrDefinitionDto dto;
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                dto = ser.Deserialize<MdrDefinitionDto>(json ?? "");
            }
            catch
            {
                return Workflow_Result(MdrResult.Fail("wf.err.badinput"));
            }

            var r = MdrWorkflowDesignService.Save(id, dto, CurrentUserHelper.UserID.Value);
            return Json(new
            {
                success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error),
                warning = r.Warning, warningText = r.Warning == null ? null : MdrText.T(r.Warning)
            });
        }


        // ------------------ helpers ------------------

        private static byte[] Workflow_Ver(string b64)
        {
            try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); }
            catch { return null; }
        }


        private JsonResult Workflow_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }


        private JsonResult Workflow_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Search  →  آدرس‌ها: /Mdr/Search/{Action}
        // ============================================================================================

        private const string Search_PageCode = "MDR.Search";


        [MdrPageAuthorize(Search_PageCode)]
        public ActionResult Search_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            return View("~/Areas/Mdr/Views/Search/Index.cshtml");
        }


        [MdrPageAuthorize(Search_PageCode)]
        public JsonResult Search_Run(string q, string source, int skip = 0, int take = 10, string from = null, string to = null)
        {
            DateTime? f = Search_ParseDate(from), t = Search_ParseDate(to);
            if ((!string.IsNullOrWhiteSpace(from) && !f.HasValue) || (!string.IsNullOrWhiteSpace(to) && !t.HasValue))
                return Json(new { success = false, error = "tsk.err.due", message = MdrText.T("tsk.err.due") }, JsonRequestBehavior.AllowGet);

            var r = MdrSearchService.Search(CurrentUserHelper.UserID.Value, q, source, skip, take, f, t);
            if (r.Error != null) return Json(new { success = false, error = r.Error, message = MdrText.T(r.Error) }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                success = true,
                capped = Search_CapMessage(r),
                skipped = r.SkippedForAccess,
                sources = r.Sources.Select(s => new
                {
                    s.Key, s.Total, s.Capped,
                    Items = s.Items.Select(i => new
                    {
                        i.Source, i.Id, i.Title, i.Snippet, i.Status, i.Url,
                        Date = i.Date.HasValue ? i.Date.Value.ToString("yyyy-MM-dd HH:mm") : null
                    })
                })
            }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Search_PageCode)]
        public JsonResult Search_SavedQueries()
        {
            var uid = CurrentUserHelper.UserID.Value;
            using (var db = new MdrContext())
                return Json(new
                {
                    success = true,
                    items = db.SavedSearches.Where(s => s.UserID == uid).OrderBy(s => s.Name).ToList().Select(s => new
                    {
                        s.SearchID, s.Name, s.QueryText, s.Source,
                        DateFrom = s.DateFrom.HasValue ? s.DateFrom.Value.ToString("yyyy-MM-dd") : null,
                        DateTo = s.DateTo.HasValue ? s.DateTo.Value.ToString("yyyy-MM-dd") : null
                    })
                }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Search_PageCode)]
        public JsonResult Search_SaveQuery(string name, string q, string source, string from, string to)
        {
            return Search_Result(MdrSearchService.SaveQuery(CurrentUserHelper.UserID.Value, name, q, source, Search_ParseDate(from), Search_ParseDate(to)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Search_PageCode)]
        public JsonResult Search_DeleteQuery(long id)
        {
            return Search_Result(MdrSearchService.DeleteQuery(CurrentUserHelper.UserID.Value, id));
        }


        private static bool Search_CapMessage(SearchOutcome r) { return r.Sources.Any(s => s.Capped); }


        private static DateTime? Search_ParseDate(string s)
        {
            DateTime d;
            return !string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }


        private JsonResult Search_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : (r.Message ?? MdrText.T(r.Error)) });
        }

        // ============================================================================================
        // بخش Reports  →  آدرس‌ها: /Mdr/Reports/{Action}
        // ============================================================================================

        private const string Reports_PageCode = "MDR.Reports";

        private const string Reports_ProcessCode = "MDR.Reports.Process";

        private static readonly string[] Reports_ModulePages = { "MDR.Requests", "MDR.Tasks", "MDR.Letters", "MDR.Documents", "MDR.Workflow.Monitor" };


        [MdrPageAuthorize(Reports_PageCode)]
        public ActionResult Reports_Index()
        {
            var uid = CurrentUserHelper.UserID.Value;
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanExport = PermissionHelper.HasAccess(uid, MdrReportService.ExportPage);
            ViewBag.CanProcess = PermissionHelper.HasAccess(uid, Reports_ProcessCode);
            ViewBag.IsManager = Reports_ModulePages.Any(p => PermissionHelper.HasAccess(uid, p) && PermissionHelper.CanEdit(uid, p));
            return View("~/Areas/Mdr/Views/Reports/Index.cshtml");
        }


        [MdrPageAuthorize(Reports_ProcessCode)]
        public ActionResult Reports_Process()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            return View("~/Areas/Mdr/Views/Reports/Process.cshtml");
        }


        // ---------------- گزارش عملیاتی ----------------
        [MdrPageAuthorize(Reports_PageCode)]
        public JsonResult Reports_GetOverview(string from, string to, string scope)
        {
            DateTime f, t;
            var err = Reports_Range(from, to, out f, out t);
            if (err != null) return Reports_Fail(err);
            return Json(new { success = true, data = MdrReportService.Overview(CurrentUserHelper.UserID.Value, f, t, Reports_Personal(scope)) }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Reports_PageCode)]
        public JsonResult Reports_GetDrill(string metric, string from, string to, string scope)
        {
            DateTime f, t;
            var err = Reports_Range(from, to, out f, out t);
            if (err != null) return Reports_Fail(err);

            var d = MdrReportService.Drill(CurrentUserHelper.UserID.Value, metric, f, t, Reports_Personal(scope));
            if (d.Error == "denied") { Response.StatusCode = 403; return Reports_Fail("denied", "denied.title"); }
            if (d.Error != null) return Reports_Fail(d.Error);
            return Json(new { success = true, total = d.Total, items = d.Items }, JsonRequestBehavior.AllowGet);
        }


        // Export فقط با دسترسی مستقل (در سرویس هم بررسی می‌شود)
        [MdrPageAuthorize(Reports_PageCode)]
        public ActionResult Reports_Export(string metric, string from, string to, string scope)
        {
            DateTime f, t;
            var err = Reports_Range(from, to, out f, out t);
            if (err != null) return new HttpStatusCodeResult(400, MdrText.T(err));

            var x = MdrReportService.Export(CurrentUserHelper.UserID.Value, metric, f, t, Reports_Personal(scope));
            if (x.Error != null) return new HttpStatusCodeResult(x.Error == "rep.err.noexport" || x.Error == "denied" ? 403 : 400, MdrText.T(x.Error == "denied" ? "denied.title" : x.Error));
            return File(x.Bytes, "text/csv; charset=utf-8", x.FileName);
        }


        // ---------------- تحلیل فرایند ----------------
        [MdrPageAuthorize(Reports_ProcessCode)]
        public JsonResult Reports_GetDefs()
        {
            return Json(new { success = true, items = MdrReportService.Definitions() }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Reports_ProcessCode)]
        public JsonResult Reports_GetProcess(int workflowId, string from, string to)
        {
            DateTime f, t;
            var err = Reports_Range(from, to, out f, out t);
            if (err != null) return Reports_Fail(err);
            var p = MdrReportService.Process(workflowId, f, t);
            if (p.Error != null) return Reports_Fail(p.Error);
            return Json(new { success = true, data = p }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Reports_ProcessCode)]
        public JsonResult Reports_GetStep(int workflowId, int stepId, string from, string to)
        {
            DateTime f, t;
            var err = Reports_Range(from, to, out f, out t);
            if (err != null) return Reports_Fail(err);
            var d = MdrReportService.StepVisits(CurrentUserHelper.UserID.Value, workflowId, stepId, f, t);
            if (d.Error != null) return Reports_Fail(d.Error);
            return Json(new { success = true, step = d.Step, sla = d.SlaHours, items = d.Items }, JsonRequestBehavior.AllowGet);
        }


        // ---------------- زمان‌بندی ----------------
        [MdrPageAuthorize(Reports_PageCode)]
        public JsonResult Reports_GetSchedules()
        {
            var items = MdrReportScheduleService.List(CurrentUserHelper.UserID.Value).Select(s => new
            {
                s.ScheduleID, s.Frequency, s.RangeDays, s.Personal, s.IsActive,
                NextRunAt = s.NextRunAt.ToString("yyyy-MM-dd HH:mm"), LastRunAt = s.LastRunAt.HasValue ? s.LastRunAt.Value.ToString("yyyy-MM-dd HH:mm") : null
            });
            return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Reports_PageCode)]
        public JsonResult Reports_SaveSchedule(string frequency, int rangeDays, string scope)
        {
            return Reports_Result(MdrReportScheduleService.Create(CurrentUserHelper.UserID.Value, Reports_Personal(scope), frequency, rangeDays));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Reports_PageCode)]
        public JsonResult Reports_DeleteSchedule(long id)
        {
            return Reports_Result(MdrReportScheduleService.Delete(CurrentUserHelper.UserID.Value, id));
        }


        // ---------------- helpers ----------------
        // scope: mine (پیش‌فرض، شخصی) | all (دید مدیر ماژول؛ برای غیرمدیر خودبه‌خود همان موارد خودش)
        private static bool Reports_Personal(string scope) { return scope != "all"; }


        private static string Reports_Range(string from, string to, out DateTime f, out DateTime t)
        {
            DateTime? df = null, dt = null; DateTime x;
            if (!string.IsNullOrWhiteSpace(from)) { if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out x)) { f = t = DateTime.Today; return "rep.err.dates"; } df = x; }
            if (!string.IsNullOrWhiteSpace(to)) { if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out x)) { f = t = DateTime.Today; return "rep.err.dates"; } dt = x; }
            return MdrReportService.NormalizeRange(df, dt, out f, out t);
        }


        private JsonResult Reports_Fail(string code, string textKey = null)
        {
            return Json(new { success = false, error = code, message = MdrText.T(textKey ?? code) }, JsonRequestBehavior.AllowGet);
        }


        private JsonResult Reports_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }

        // ============================================================================================
        // بخش Chat  →  آدرس‌ها: /Mdr/Chat/{Action}
        // ============================================================================================

        private const string Chat_PageCode = "MDR.Chat";


        private class Chat_UserRow { public int Id { get; set; } public string Name { get; set; } }


        private int Chat_Uid { get { return CurrentUserHelper.UserID.Value; } }


        [MdrPageAuthorize(Chat_PageCode)]
        public ActionResult Chat_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.UserId = Chat_Uid;
            ViewBag.CanTask = PermissionHelper.HasAccess(Chat_Uid, "MDR.Tasks") && MdrPermission.CanInsert("MDR.Tasks");
            return View("~/Areas/Mdr/Views/Chat/Index.cshtml");
        }


        // ---------------- خواندن ----------------
        [MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_GetThreads()
        {
            return Json(new { success = true, items = MdrChatReadService.Threads(Chat_Uid) }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_GetThread(long id)
        {
            var t = MdrChatReadService.Thread(Chat_Uid, id);
            if (t == null) return Chat_NotFound();
            return Json(new { success = true, thread = t }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_GetMessages(long id, long after = 0, long before = 0)
        {
            var r = MdrChatReadService.Messages(Chat_Uid, id, after, before);
            if (r == null) return Chat_NotFound();
            return Json(new { success = true, hasMore = r.HasMore, lastId = r.LastId, items = r.Items }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);
            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Chat_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers " +
                    "WHERE usr_IsActive = 1 AND usr_ID <> @p1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%", Chat_Uid).ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        // ---------------- ایجاد گفتگو ----------------
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_OpenDirect(int userId) { return Chat_Result(MdrChatService.OpenDirect(Chat_Uid, userId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_CreateGroup(string title, string members) { return Chat_Result(MdrChatService.CreateGroup(Chat_Uid, title, Chat_Ids(members))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_OpenEntity(string type, long entityId) { return Chat_Result(MdrChatService.OpenEntity(Chat_Uid, type, entityId)); }


        // ---------------- اعضا / گروه ----------------
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_AddMember(long id, int userId) { return Chat_Result(MdrChatService.AddMember(Chat_Uid, id, userId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_RemoveMember(long id, int userId) { return Chat_Result(MdrChatService.RemoveMember(Chat_Uid, id, userId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Rename(long id, string title) { return Chat_Result(MdrChatService.Rename(Chat_Uid, id, title)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Archive(long id, string reason) { return Chat_Result(MdrChatService.Archive(Chat_Uid, id, reason)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Mute(long id, bool muted) { return Chat_Result(MdrChatService.SetMuted(Chat_Uid, id, muted)); }


        // ---------------- پیام ----------------
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Send(long id, string body, long? replyTo, string mentions)
        {
            return Chat_Result(MdrChatService.Send(Chat_Uid, id, body, replyTo, Chat_Ids(mentions)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Edit(long id, string body) { return Chat_Result(MdrChatService.Edit(Chat_Uid, id, body)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Delete(long id, string reason) { return Chat_Result(MdrChatService.Delete(Chat_Uid, id, reason)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Read(long id, long upTo) { return Chat_Result(MdrChatService.MarkRead(Chat_Uid, id, upTo)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Forward(long id, long targetThreadId) { return Chat_Result(MdrChatService.Forward(Chat_Uid, id, targetThreadId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_CreateTask(long id, string title, int? assigneeUserId, string dueAt)
        {
            DateTime? due = null;
            if (!string.IsNullOrWhiteSpace(dueAt))
            {
                DateTime d;
                if (!DateTime.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return Chat_Result(MdrResult.Fail("tsk.err.due"));
                due = d;
            }
            if (!MdrPermission.CanInsert("MDR.Tasks")) { Response.StatusCode = 403; return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }); }
            return Chat_Result(MdrChatService.CreateTask(Chat_Uid, id, title, assigneeUserId, due));
        }


        // پیوست پیام: فقط فرستنده، تا ۱۵ دقیقه، یک فایل برای هر پیام؛ نوع/اندازه را FileManager کنترل می‌کند («فایل ممنوع» همان‌جا رد می‌شود)
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Chat_PageCode)]
        public JsonResult Chat_Attach(long id)
        {
            var check = MdrChatService.MarkAttachment(Chat_Uid, id);
            if (!check.Ok) return Chat_Result(check);
            if (Request.Files.Count == 0) return Json(new { success = false, error = "att.err.nofile", message = MdrText.T("att.err.nofile") });

            var f = Request.Files[0];
            var r = FileManager.Upload(f, "Chat", "MDR_ChatMessages", id.ToString(), System.IO.Path.GetFileName(f.FileName), null, null);
            if (!r.Success) return Json(new { success = false, error = "upload", message = r.Message });
            MdrChatService.SetHasAttachment(id);
            return Json(new { success = true, id });
        }


        // ---------------- helpers ----------------
        private static List<int> Chat_Ids(string csv)
        {
            var res = new List<int>();
            foreach (var p in (csv ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int x; if (int.TryParse(p, out x)) res.Add(x);
            }
            return res.Distinct().Take(200).ToList();
        }


        private JsonResult Chat_NotFound()
        {
            return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
        }


        private JsonResult Chat_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }

        // ============================================================================================
        // بخش Finance  →  آدرس‌ها: /Mdr/Finance/{Action}
        // ============================================================================================

        private const string Finance_DashPage = "MDR.Finance";

        private const string Finance_DocsPage = MdrFinanceService.DocsPage;


        private int Finance_Uid { get { return CurrentUserHelper.UserID.Value; } }


        [MdrPageAuthorize(Finance_DashPage)]
        public ActionResult Finance_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.IsManager = MdrFinanceService.IsManager(Finance_Uid);
            ViewBag.CanDocs = PermissionHelper.HasAccess(Finance_Uid, Finance_DocsPage);
            return View("~/Areas/Mdr/Views/Finance/Index.cshtml");
        }


        [MdrPageAuthorize(Finance_DocsPage)]
        public ActionResult Finance_Documents()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.IsManager = MdrFinanceService.IsManager(Finance_Uid);
            ViewBag.CanExport = PermissionHelper.HasAccess(Finance_Uid, MdrReportService.ExportPage);
            ViewBag.CanDash = PermissionHelper.HasAccess(Finance_Uid, Finance_DashPage);
            ViewBag.UserId = Finance_Uid;
            return View("~/Areas/Mdr/Views/Finance/Documents.cshtml");
        }


        // ---------------- داشبورد ----------------
        [MdrPageAuthorize(Finance_DashPage)]
        public JsonResult Finance_GetDashboard(string from, string to, string scope)
        {
            DateTime f, t; var err = Finance_Range(from, to, out f, out t);
            if (err != null) return Finance_Fail(err);
            return Json(new { success = true, data = MdrFinanceReadService.Dashboard(Finance_Uid, f, t, scope != "all") }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Finance_DashPage)]
        public JsonResult Finance_GetDrill(string metric, string from, string to, string scope)
        {
            DateTime f, t; var err = Finance_Range(from, to, out f, out t);
            if (err != null) return Finance_Fail(err);
            string e;
            var rows = MdrFinanceReadService.Drill(Finance_Uid, metric, f, t, scope != "all", out e);
            if (e != null) return Finance_Fail(e);
            return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Finance_DashPage)]
        public JsonResult Finance_GetLegacyDocs(string title, string from, string to)
        {
            DateTime f, t; var err = Finance_Range(from, to, out f, out t);
            if (err != null) return Finance_Fail(err);
            var rows = MdrFinanceReadService.LegacyDocs(Finance_Uid, title, f, t);
            if (rows == null) { Response.StatusCode = 403; return Finance_Fail("denied", "denied.title"); }
            return Json(new { success = true, items = rows.Select(r => new { r.ID, r.Title, Date = r.DataDocument.HasValue ? r.DataDocument.Value.ToString("yyyy-MM-dd") : null, r.Debtore, r.Creditor }) }, JsonRequestBehavior.AllowGet);
        }


        // ---------------- اسناد ----------------
        [MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_GetList(string scope, string status, string direction, string q)
        {
            return Json(new { success = true, items = MdrFinanceReadService.List(Finance_Uid, scope, status, direction, q) }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_GetDocument(long id)
        {
            var d = MdrFinanceReadService.Get(Finance_Uid, id);
            if (d == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, doc = d }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_GetCostCenters()
        {
            return Json(new { success = true, items = MdrFinanceReadService.CostCenters() }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_GetConfig()
        {
            var c = MdrFinanceReadService.Config(Finance_Uid);
            if (c == null) { Response.StatusCode = 403; return Finance_Fail("denied", "denied.title"); }
            return Json(new { success = true, config = c }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Save(long id, string direction, string title, long amount, string docDate, int? costCenterId, int? peymanId, string payee, string referenceNo, string rowVer)
        {
            if (id == 0 && !MdrPermission.CanInsert(Finance_DocsPage)) return Finance_Denied();
            DateTime d;
            if (!DateTime.TryParseExact(docDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return Finance_Result(MdrResult.Fail("fin.err.date"));
            return Finance_Result(MdrFinanceService.Save(id, new MdrFinanceService.FinInput
            {
                Direction = direction, Title = title, Amount = amount, DocDate = d, CostCenterId = costCenterId, PeymanId = peymanId, Payee = payee, ReferenceNo = referenceNo
            }, Finance_Uid, Finance_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Submit(long id, string rowVer, bool confirmDuplicate = false)
        {
            var r = MdrFinanceService.Submit(id, Finance_Uid, confirmDuplicate, Finance_Ver(rowVer));
            return Finance_Result(r);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Approve(long id, string comment, string rowVer) { return Finance_Result(MdrFinanceService.Approve(id, comment, Finance_Uid, Finance_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Return(long id, string reason, string rowVer) { return Finance_Result(MdrFinanceService.Return(id, reason, Finance_Uid, Finance_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Reject(long id, string reason, string rowVer) { return Finance_Result(MdrFinanceService.Reject(id, reason, Finance_Uid, Finance_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Cancel(long id, string reason) { return Finance_Result(MdrFinanceService.Cancel(id, reason, Finance_Uid)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_Pay(long id, string paymentRef, string rowVer) { return Finance_Result(MdrFinanceService.Pay(id, paymentRef, Finance_Uid, Finance_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_AddApprover(int? costCenterId, int level, int userId, long? minAmount) { return Finance_Result(MdrFinanceService.AddApprover(Finance_Uid, costCenterId, level, userId, minAmount)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_RemoveApprover(int id) { return Finance_Result(MdrFinanceService.RemoveApprover(Finance_Uid, id)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_SetBudget(int costCenterId, int year, long amount) { return Finance_Result(MdrFinanceService.SetBudget(Finance_Uid, costCenterId, year, amount)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_ClosePeriod(int year, int month, string reason) { return Finance_Result(MdrFinanceService.ClosePeriod(Finance_Uid, year, month, reason)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_ReopenPeriod(int year, int month, string reason) { return Finance_Result(MdrFinanceService.ReopenPeriod(Finance_Uid, year, month, reason)); }


        [MdrPageAuthorize(Finance_DocsPage)]
        public JsonResult Finance_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);
            using (var db = new SaabWebProject.Areas.Mdr.Data.MdrContext())
            {
                var rows = db.Database.SqlQuery<Finance_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }

        private class Finance_UserRow { public int Id { get; set; } public string Name { get; set; } }


        // Export فقط با دسترسی مستقل MDR.Reports.Export؛ همان scope/فیلتر فهرست؛ Audit می‌شود
        [MdrPageAuthorize(Finance_DocsPage)]
        public ActionResult Finance_ExportDocs(string scope, string status, string direction, string q)
        {
            if (!PermissionHelper.HasAccess(Finance_Uid, MdrReportService.ExportPage)) return new HttpStatusCodeResult(403, MdrText.T("rep.err.noexport"));
            var rows = MdrFinanceReadService.List(Finance_Uid, scope, status, direction, q);
            var sb = new StringBuilder();
            sb.AppendLine(MdrReportService.Csv("Number", "Direction", "Title", "Amount", "Date", "Status", "CostCenter", "Requester"));
            foreach (var r in rows) sb.AppendLine(MdrReportService.Csv(r.Number, r.Direction, r.Title, r.Amount.ToString(CultureInfo.InvariantCulture), r.DocDate, r.Status, r.CostCenter, r.Requester));
            AuditLogger.Log(AuditLogger.Actions.Export, "Finance", "خروجی فهرست اسناد مالی", entityName: "MDR_FinDocuments", entityTitle: scope, newValues: new { scope, status, direction, rows = rows.Count });
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", "mdr-finance-docs-" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        }


        // ---------------- helpers ----------------
        private static string Finance_Range(string from, string to, out DateTime f, out DateTime t)
        {
            DateTime? df = null, dt = null; DateTime x;
            if (!string.IsNullOrWhiteSpace(from)) { if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out x)) { f = t = DateTime.Today; return "rep.err.dates"; } df = x; }
            if (!string.IsNullOrWhiteSpace(to)) { if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out x)) { f = t = DateTime.Today; return "rep.err.dates"; } dt = x; }
            return MdrReportService.NormalizeRange(df, dt, out f, out t);
        }


        private static byte[] Finance_Ver(string b64) { try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; } }


        private JsonResult Finance_Fail(string code, string textKey = null)
        {
            return Json(new { success = false, error = code, message = MdrText.T(textKey ?? code) }, JsonRequestBehavior.AllowGet);
        }


        private JsonResult Finance_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : MdrText.T(r.Error) });
        }


        private JsonResult Finance_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") });
        }

        // ============================================================================================
        // بخش Warehouse  →  آدرس‌ها: /Mdr/Warehouse/{Action}
        // ============================================================================================

        private const string Warehouse_Page = MdrWarehouseService.Page;

        private int Warehouse_Uid { get { return CurrentUserHelper.UserID.Value; } }


        [MdrPageAuthorize(Warehouse_Page)]
        public ActionResult Warehouse_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.IsManager = MdrWarehouseService.IsManager(Warehouse_Uid);
            ViewBag.CanInsert = MdrPermission.CanInsert(Warehouse_Page);
            return View("~/Areas/Mdr/Views/Warehouse/Index.cshtml");
        }


        [MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_GetLookups() { return Json(new { success = true, data = MdrWarehouseReadService.Lookups() }, JsonRequestBehavior.AllowGet); }


        [MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_GetStock(string q, bool low = false)
        {
            return Json(new { success = true, items = MdrWarehouseReadService.Stock(q, low) }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_GetLedger(int itemId, int? storeId)
        {
            var rows = MdrWarehouseReadService.Ledger(Warehouse_Uid, itemId, storeId);
            if (rows == null) return Warehouse_Denied();
            return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_GetRequests(string scope) { return Json(new { success = true, items = MdrWarehouseReadService.Requests(Warehouse_Uid, scope) }, JsonRequestBehavior.AllowGet); }


        [MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_GetRequest(long id)
        {
            var r = MdrWarehouseReadService.Request(Warehouse_Uid, id);
            if (r == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, request = r }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_CreateStore(string name, int? managerUserId) { return Warehouse_Result(MdrWarehouseService.CreateStore(Warehouse_Uid, name, managerUserId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_CreateItem(string code, string name, string unit, string reorderPoint, bool confirmDuplicate = false)
        { return Warehouse_Result(MdrWarehouseService.CreateItem(Warehouse_Uid, code, name, unit, Warehouse_Dec(reorderPoint), confirmDuplicate)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_UpdateItem(int id, string name, string reorderPoint, bool isActive) { return Warehouse_Result(MdrWarehouseService.UpdateItem(Warehouse_Uid, id, name, Warehouse_Dec(reorderPoint), isActive)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_Receipt(int storeId, int itemId, string qty, string reference, string note) { return Warehouse_Result(MdrWarehouseService.Receipt(Warehouse_Uid, storeId, itemId, Warehouse_Dec(qty) ?? 0, reference, note)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_Issue(int storeId, int itemId, string qty, string reference, string note) { return Warehouse_Result(MdrWarehouseService.Issue(Warehouse_Uid, storeId, itemId, Warehouse_Dec(qty) ?? 0, reference, note)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_Transfer(int fromStore, int toStore, int itemId, string qty, string note) { return Warehouse_Result(MdrWarehouseService.Transfer(Warehouse_Uid, fromStore, toStore, itemId, Warehouse_Dec(qty) ?? 0, note)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_Adjust(int storeId, int itemId, string delta, string reason) { return Warehouse_Result(MdrWarehouseService.Adjust(Warehouse_Uid, storeId, itemId, Warehouse_Dec(delta) ?? 0, reason)); }


        // lines: JSON [{ItemId, Qty}]
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_SaveRequest(long id, int storeId, string note, string lines)
        {
            if (id == 0 && !MdrPermission.CanInsert(Warehouse_Page)) return Warehouse_Denied();
            List<MdrWarehouseService.LineInput> list;
            try { list = new JavaScriptSerializer().Deserialize<List<MdrWarehouseService.LineInput>>(lines ?? "[]"); }
            catch (Exception) { return Warehouse_Result(MdrResult.Fail("wh.err.lines")); }
            return Warehouse_Result(MdrWarehouseService.SaveRequest(Warehouse_Uid, id, storeId, note, list));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_SubmitRequest(long id) { return Warehouse_Result(MdrWarehouseService.SubmitRequest(Warehouse_Uid, id)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_CancelRequest(long id) { return Warehouse_Result(MdrWarehouseService.CancelRequest(Warehouse_Uid, id)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_IssueRequest(long id, string rowVer) { return Warehouse_Result(MdrWarehouseService.IssueRequest(Warehouse_Uid, id, Warehouse_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Warehouse_Page)]
        public JsonResult Warehouse_RejectRequest(long id, string reason) { return Warehouse_Result(MdrWarehouseService.RejectRequest(Warehouse_Uid, id, reason)); }


        // ---------------- helpers ----------------
        private static decimal? Warehouse_Dec(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var t = MdrSearchService.Normalize(s.Trim()).Replace("٫", ".").Replace("٬", "").Replace(",", "");
            decimal d; return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out d) ? (decimal?)d : null;
        }


        private static byte[] Warehouse_Ver(string b64) { try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; } }


        private JsonResult Warehouse_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : (r.Message ?? MdrText.T(r.Error)) });
        }


        private JsonResult Warehouse_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet);
        }

        // ============================================================================================
        // بخش Assets  →  آدرس‌ها: /Mdr/Assets/{Action}
        // ============================================================================================

        private const string Assets_Page = MdrAssetService.Page;

        private int Assets_Uid { get { return CurrentUserHelper.UserID.Value; } }

        private class Assets_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Assets_Page)]
        public ActionResult Assets_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.IsManager = MdrAssetService.IsManager(Assets_Uid);
            return View("~/Areas/Mdr/Views/Assets/Index.cshtml");
        }


        [MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_GetList(string scope, string status, string category, string q)
        {
            return Json(new { success = true, items = MdrAssetReadService.List(Assets_Uid, scope, status, category, q), categories = MdrAssetReadService.Categories() }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_GetAsset(long id)
        {
            var a = MdrAssetReadService.Get(Assets_Uid, id);
            if (a == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, asset = a }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_GetSummary()
        {
            var s = MdrAssetReadService.Summary(Assets_Uid);
            if (s == null) { Response.StatusCode = 403; return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet); }
            return Json(new { success = true, summary = s }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_SearchUsers(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);
            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Assets_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_Register(string tag, string name, string category, string location, string acquiredAt, string cost, string note)
        {
            DateTime? d = null;
            if (!string.IsNullOrWhiteSpace(acquiredAt)) { DateTime x; if (!DateTime.TryParseExact(acquiredAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out x)) return Assets_Result(MdrResult.Fail("fin.err.date")); d = x; }
            long? c = null;
            if (!string.IsNullOrWhiteSpace(cost)) { long y; if (!long.TryParse(MdrSearchService.Normalize(cost.Trim()).Replace(",", "").Replace("٬", ""), out y)) return Assets_Result(MdrResult.Fail("asset.err.cost")); c = y; }
            return Assets_Result(MdrAssetService.Register(Assets_Uid, new AssetInput { Tag = tag, Name = name, Category = category, Location = location, AcquiredAt = d, Cost = c, Note = note }));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_Assign(long id, int toUser, string note, string rowVer) { return Assets_Result(MdrAssetService.Assign(Assets_Uid, id, toUser, note, Assets_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_Return(long id, string location, string rowVer) { return Assets_Result(MdrAssetService.Return(Assets_Uid, id, location, Assets_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_Transfer(long id, int toUser, string reason, string rowVer) { return Assets_Result(MdrAssetService.Transfer(Assets_Uid, id, toUser, reason, Assets_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_Move(long id, string location, string reason, string rowVer) { return Assets_Result(MdrAssetService.Move(Assets_Uid, id, location, reason, Assets_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_RepairStart(long id, string reason, string rowVer) { return Assets_Result(MdrAssetService.RepairStart(Assets_Uid, id, reason, Assets_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_RepairEnd(long id, string note, string rowVer) { return Assets_Result(MdrAssetService.RepairEnd(Assets_Uid, id, note, Assets_Ver(rowVer))); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Assets_Page)]
        public JsonResult Assets_Dispose(long id, string reason, string rowVer) { return Assets_Result(MdrAssetService.Dispose(Assets_Uid, id, reason, Assets_Ver(rowVer))); }


        private static byte[] Assets_Ver(string b64) { try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; } }


        private JsonResult Assets_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, error = r.Error, message = r.Ok ? null : (r.Message ?? MdrText.T(r.Error)) });
        }

        // ============================================================================================
        // بخش Training  →  آدرس‌ها: /Mdr/Training/{Action}
        // ============================================================================================

        private const string Training_TrainPage = MdrTrainingService.Page;

        private const string Training_SkillPage = MdrSkillService.Page;

        private int Training_Uid { get { return CurrentUserHelper.UserID.Value; } }

        private class Training_UserRow { public int Id { get; set; } public string Name { get; set; } }


        [MdrPageAuthorize(Training_TrainPage)]
        public ActionResult Training_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.IsManager = MdrTrainingService.IsManager(Training_Uid);
            ViewBag.CanSkills = PermissionHelper.HasAccess(Training_Uid, Training_SkillPage);
            return View("~/Areas/Mdr/Views/Training/Index.cshtml");
        }


        [MdrPageAuthorize(Training_SkillPage)]
        public ActionResult Training_Skills()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.IsManager = MdrSkillService.IsManager(Training_Uid);
            ViewBag.UserId = Training_Uid;
            ViewBag.CanTraining = PermissionHelper.HasAccess(Training_Uid, Training_TrainPage);
            return View("~/Areas/Mdr/Views/Training/Skills.cshtml");
        }


        // ---------------- آموزش ----------------
        [MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_GetCourses(string scope, string q) { return Json(new { success = true, items = MdrTrainingReadService.List(Training_Uid, scope, q) }, JsonRequestBehavior.AllowGet); }


        [MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_GetCourse(int id)
        {
            var d = MdrTrainingReadService.Get(Training_Uid, id);
            if (d == null) return Json(new { success = false, error = "wf.err.notfound", message = MdrText.T("wf.err.notfound") }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, course = d }, JsonRequestBehavior.AllowGet);
        }


        [MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_GetCertificates() { return Json(new { success = true, items = MdrTrainingReadService.MyCertificates(Training_Uid) }, JsonRequestBehavior.AllowGet); }


        [MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_GetLookups()
        {
            using (var db = new MdrContext())
                return Json(new
                {
                    success = true, skills = db.Skills.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new { s.SkillID, s.Name }).ToList(),
                    courses = db.Courses.Where(c => c.Status != "Cancelled").OrderByDescending(c => c.CourseID).Take(300).Select(c => new { c.CourseID, c.Code, c.Title }).ToList()
                }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_SaveCourse(int id, string code, string title, string description, int? skillId, int? targetLevel, string startAt, string endAt, int capacity, int? prereqCourseId, string instructor, string location, string rowVer)
        {
            DateTime s, e;
            if (!Training_Dt(startAt, out s) || !Training_Dt(endAt, out e)) return Training_Result(MdrResult.Fail("train.err.dates"));
            var input = new CourseInput { Code = code, Title = title, Description = description, SkillId = skillId, TargetLevel = targetLevel, StartAt = s, EndAt = e, Capacity = capacity, PrereqCourseId = prereqCourseId, InstructorName = instructor, Location = location };
            return Training_Result(id == 0 ? MdrTrainingService.CreateCourse(Training_Uid, input) : MdrTrainingService.UpdateCourse(Training_Uid, id, input, Training_Ver(rowVer)));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_Publish(int id) { return Training_Result(MdrTrainingService.Publish(Training_Uid, id)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_CloseEnrollment(int id) { return Training_Result(MdrTrainingService.CloseEnrollment(Training_Uid, id)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_CancelCourse(int id, string reason) { return Training_Result(MdrTrainingService.CancelCourse(Training_Uid, id, reason)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_Enroll(int courseId, int? userId) { return Training_Result(MdrTrainingService.Enroll(Training_Uid, courseId, userId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_CancelEnrollment(long id, string reason) { return Training_Result(MdrTrainingService.CancelEnrollment(Training_Uid, id, reason)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_Attendance(int courseId, int userId, string date, bool present)
        {
            DateTime d;
            if (!DateTime.TryParseExact(date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return Training_Result(MdrResult.Fail("train.err.session"));
            return Training_Result(MdrTrainingService.MarkAttendance(Training_Uid, courseId, userId, d, present));
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_TrainPage)]
        public JsonResult Training_Complete(int courseId, int userId, bool passed, string score)
        {
            decimal? sc = null; decimal x;
            if (!string.IsNullOrWhiteSpace(score)) { if (!decimal.TryParse(MdrSearchService.Normalize(score.Trim()), NumberStyles.Number, CultureInfo.InvariantCulture, out x)) return Training_Result(MdrResult.Fail("train.err.score")); sc = x; }
            return Training_Result(MdrTrainingService.Complete(Training_Uid, courseId, userId, passed, sc));
        }


        // ---------------- ماتریس مهارت ----------------
        [MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_GetMatrix(int? profileId) { return Json(new { success = true, data = MdrSkillService.Matrix(Training_Uid, profileId) }, JsonRequestBehavior.AllowGet); }


        [MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_GetSkillLookups() { return Json(new { success = true, data = MdrSkillService.Lookups() }, JsonRequestBehavior.AllowGet); }


        [MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_GetSuggestions(int userId)
        {
            var s = MdrSkillService.Suggestions(Training_Uid, userId);
            if (s == null) { Response.StatusCode = 403; return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet); }
            return Json(new { success = true, items = s }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_CreateSkill(string name, string category) { return Training_Result(MdrSkillService.CreateSkill(Training_Uid, name, category)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_CreateProfile(string name) { return Training_Result(MdrSkillService.CreateProfile(Training_Uid, name)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_SetProfileSkill(int profileId, int skillId, int level) { return Training_Result(MdrSkillService.SetProfileSkill(Training_Uid, profileId, skillId, level)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_AssignProfile(int userId, int profileId) { return Training_Result(MdrSkillService.AssignProfile(Training_Uid, userId, profileId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_UnassignProfile(int userId, int profileId) { return Training_Result(MdrSkillService.UnassignProfile(Training_Uid, userId, profileId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(Training_SkillPage)]
        public JsonResult Training_Assess(int userId, int skillId, int level, string note) { return Training_Result(MdrSkillService.Assess(Training_Uid, userId, skillId, level, note)); }


        // انتخاب کاربر (ثبت‌نام دیگران، نسبت‌دادن پروفایل، ارزیابی): فقط مدیر
        public JsonResult Training_SearchUsers(string q)
        {
            if (!MdrTrainingService.IsManager(Training_Uid) && !MdrSkillService.IsManager(Training_Uid)) { Response.StatusCode = 403; return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet); }
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);
            using (var db = new MdrContext())
            {
                var rows = db.Database.SqlQuery<Training_UserRow>(
                    "SELECT TOP 20 usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_IsActive = 1 AND (ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,'')) LIKE @p0 ORDER BY usr_Family, usr_Name",
                    "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").ToList();
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
        }


        // ---------------- helpers ----------------
        private static bool Training_Dt(string s, out DateTime d)
        {
            return DateTime.TryParseExact(s ?? "", new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }


        private static byte[] Training_Ver(string b64) { try { return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64); } catch { return null; } }


        private JsonResult Training_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, warning = r.Warning, error = r.Error, message = r.Ok ? null : (r.Message ?? MdrText.T(r.Error)) });
        }

        // ============================================================================================
        // بخش Ai  →  آدرس‌ها: /Mdr/Ai/{Action}
        // ============================================================================================

        private int Ai_Uid { get { return CurrentUserHelper.UserID.Value; } }


        [MdrPageAuthorize(MdrAiService.Page)]
        public ActionResult Ai_Index()
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanTasks = PermissionHelper.HasAccess(Ai_Uid, "MDR.Tasks");
            ViewBag.CanContext = PermissionHelper.HasAccess(Ai_Uid, MdrAiService.ContextPage);
            return View("~/Areas/Mdr/Views/Ai/Index.cshtml");
        }


        [MdrPageAuthorize(MdrAiService.ContextPage)]
        public ActionResult Ai_Context(string key, long? id)
        {
            ViewBag.Theme = MdrPreferenceService.GetTheme(CurrentUserHelper.UserID);
            ViewBag.CanTasks = PermissionHelper.HasAccess(Ai_Uid, "MDR.Tasks");
            ViewBag.Key = key; ViewBag.Id = id;
            return View("~/Areas/Mdr/Views/Ai/Context.cshtml");
        }


        // ---------------- دستیار ----------------
        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_Ask(string question)
        {
            var r = MdrAiService.Ask(Ai_Uid, question);
            if (!r.Ok) return Json(new { success = false, error = r.Error, message = r.Message ?? MdrText.T(r.Error) });
            return Json(new
            {
                success = true, queryId = r.QueryId, outcome = r.Outcome, confidence = r.Confidence, answer = r.Answer, provider = r.Provider, keywords = r.Keywords, sources = r.Sources,
                warnings = r.Warnings.Select(w => new { code = w, text = MdrText.T(w) })
            });
        }


        [MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_GetHistory() { return Json(new { success = true, items = MdrAiService.History(Ai_Uid) }, JsonRequestBehavior.AllowGet); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_Rate(long queryId, int rating, string note) { return Ai_Result(MdrAiService.Rate(Ai_Uid, queryId, rating, note)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_DeleteQuery(long queryId) { return Ai_Result(MdrAiService.DeleteQuery(Ai_Uid, queryId)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_ClearHistory() { return Ai_Result(MdrAiService.ClearHistory(Ai_Uid)); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_Propose(long queryId, string key, long entityId) { return Ai_Result(MdrAiService.Propose(Ai_Uid, queryId, key, entityId)); }


        [MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_GetSuggestions() { return Json(new { success = true, items = MdrAiService.Suggestions(Ai_Uid, true) }, JsonRequestBehavior.AllowGet); }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.Page)]
        public JsonResult Ai_Decide(long suggestionId, bool accept, string title, string due)
        {
            DateTime? d = null;
            if (accept && !string.IsNullOrWhiteSpace(due))
            {
                DateTime x;
                if (!DateTime.TryParseExact(due.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out x)) return Ai_Result(MdrResult.Fail("tsk.err.due"));
                d = x;
            }
            return Ai_Result(MdrAiService.Decide(Ai_Uid, suggestionId, accept, title, d));
        }


        // ---------------- AI در متن کار ----------------
        [MdrPageAuthorize(MdrAiService.ContextPage)]
        public JsonResult Ai_ContextSummary(string key, long id)
        {
            var r = MdrAiService.Context(Ai_Uid, key, id);
            if (!r.Ok) return Json(new { success = false, error = r.Error, message = MdrText.T(r.Error) }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, key = r.Key, id = r.Id, title = r.Title, url = r.Url, level = r.Level, summary = r.Summary, tooShort = r.TooShort, warnings = r.Warnings.Select(w => new { code = w, text = MdrText.T(w) }) }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken, MdrPageAuthorize(MdrAiService.ContextPage)]
        public JsonResult Ai_ContextTasks(string key, long id)
        {
            string err; var items = MdrAiService.GenerateTasks(Ai_Uid, key, id, out err);
            if (items == null) return Json(new { success = false, error = err, message = MdrText.T(err) });
            return Json(new { success = true, items });
        }


        [MdrPageAuthorize(MdrAiService.ContextPage)]
        public JsonResult Ai_ContextSimilar(string key, long id)
        {
            string err; var items = MdrAiService.Similar(Ai_Uid, key, id, out err);
            if (items == null) return Json(new { success = false, error = err, message = MdrText.T(err) }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, items }, JsonRequestBehavior.AllowGet);
        }


        // انتخاب موجودیت برای AI در متن کار: جستجوی ACL-دار روی نامه/تسک/سند
        [MdrPageAuthorize(MdrAiService.ContextPage)]
        public JsonResult Ai_PickEntity(string q, string key)
        {
            if (key != "ltr" && key != "tsk" && key != "doc") return Json(new { success = true, items = new object[0] }, JsonRequestBehavior.AllowGet);
            var o = MdrSearchService.Search(Ai_Uid, q, key, 0, 10, null, null);
            if (o.Error != null) return Json(new { success = false, error = o.Error, message = MdrText.T(o.Error) }, JsonRequestBehavior.AllowGet);
            return Json(new { success = true, items = o.Sources.SelectMany(s => s.Items).Select(h => new { h.Id, h.Title, h.Status }) }, JsonRequestBehavior.AllowGet);
        }


        private JsonResult Ai_Result(MdrResult r)
        {
            return Json(new { success = r.Ok, id = r.Id, warning = r.Warning, error = r.Error, message = r.Ok ? null : (r.Message ?? MdrText.T(r.Error)) });
        }

        // ============================================================================================
        // بخش Files  →  آدرس‌ها: /Mdr/Files/{Action}
        // ============================================================================================

        private int? Files_Uid { get { return CurrentUserHelper.UserID; } }


        public JsonResult Files_List(string entityName, string entityId)
        {
            if (!Files_Uid.HasValue) return Files_Denied();
            var d = MdrAttachmentAccess.Resolve(entityName, entityId, Files_Uid.Value);
            if (!d.Known || !d.CanView) return Files_Denied();

            var uid = Files_Uid.Value;
            var items = FileManager.GetFiles(entityName, entityId);

            // بارگذارنده‌ی هر فایل (برای مجوز حذف)
            Func<Guid, bool> mineFile = g =>
            {
                using (var db = new MdrContext())
                    return db.MDR_Files.Any(f => f.FileGuid == g && f.CreatedByUserID == uid);
            };

            return Json(new
            {
                success = true,
                canUpload = d.CanWrite,
                maxMb = FileManager.FileSettings.MaxFileSizeBytes / (1024 * 1024),
                allowed = FileManager.AllowedExtensionsText,
                items = items.Select(i => new
                {
                    i.FileGuid, i.Title, i.OriginalFileName, i.Extension, i.FileSize, i.CurrentVersionNo, i.PersianDate, i.CreatedByName,
                    CanDelete = d.CanWrite && (d.IsManager || mineFile(i.FileGuid))
                })
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Files_Upload(string entityName, string entityId, string title)
        {
            if (!Files_Uid.HasValue) return Files_Denied();
            var d = MdrAttachmentAccess.Resolve(entityName, entityId, Files_Uid.Value);
            if (!d.Known || !d.CanWrite) return Files_Denied();
            if (Request.Files.Count == 0) return Json(new { success = false, message = MdrText.T("att.err.nofile") });

            var r = FileManager.Upload(Request.Files[0], d.ModuleName, entityName, entityId, title, null, null);
            return Json(new { success = r.Success, message = r.Message, fileGuid = r.FileGuid });
        }


        public ActionResult Files_Download(Guid id, int? version)
        {
            if (!Files_Uid.HasValue) return new HttpStatusCodeResult(403);

            string entityName, entityId;
            if (!Files_Owner(id, out entityName, out entityId)) return HttpNotFound();

            var d = MdrAttachmentAccess.Resolve(entityName, entityId, Files_Uid.Value);
            if (!d.Known || !d.CanView) return new HttpStatusCodeResult(403);

            var dl = FileManager.GetDownload(id, version);
            if (dl == null) return HttpNotFound();
            return File(dl.PhysicalPath, dl.ContentType, dl.DownloadName);
        }


        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult Files_Delete(Guid id)
        {
            if (!Files_Uid.HasValue) return Files_Denied();

            string entityName, entityId;
            if (!Files_Owner(id, out entityName, out entityId)) return Json(new { success = false, message = MdrText.T("wf.err.notfound") });

            var d = MdrAttachmentAccess.Resolve(entityName, entityId, Files_Uid.Value);
            if (!d.Known || !d.CanWrite) return Files_Denied();

            var uid = Files_Uid.Value;
            if (!d.IsManager)
            {
                using (var db = new MdrContext())
                    if (!db.MDR_Files.Any(f => f.FileGuid == id && f.CreatedByUserID == uid)) return Files_Denied();
            }

            var r = FileManager.Delete(id);
            return Json(new { success = r.Success, message = r.Message });
        }


        // مالک پیوست: فقط فایل‌های موجودیت‌های Mdr قابل دسترسی‌اند
        private static bool Files_Owner(Guid fileGuid, out string entityName, out string entityId)
        {
            using (var db = new MdrContext())
            {
                var f = db.MDR_Files.AsNoTracking().Where(x => x.FileGuid == fileGuid && !x.IsDeleted)
                    .Select(x => new { x.EntityName, x.EntityID }).FirstOrDefault();
                entityName = f == null ? null : f.EntityName;
                entityId = f == null ? null : f.EntityID;
                return f != null;
            }
        }


        private JsonResult Files_Denied()
        {
            Response.StatusCode = 403;
            return Json(new { success = false, error = "AccessDenied", message = MdrText.T("denied.title") }, JsonRequestBehavior.AllowGet);
        }

        // ============================================================================================
        // بخش AG  →  آدرس‌ها: /Mdr/AG/{Action}
        // ============================================================================================

        private MdrContext AG_db = new MdrContext();


        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.3  : ساختار سازمانی و واحدها
        // Part 1.3.1: صفحه اصلی ساختار سازمانی
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public ActionResult AG_OrganizationStructure()
        {
            return View("~/Areas/Mdr/Views/AG/OrganizationStructure.cshtml");
        }

        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.3  : ساختار سازمانی و واحدها
        // Part 1.3.3.3 : دریافت اطلاعات فرم افزودن واحد
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetOrganizationFormData()
        {
            try
            {
                // ---------------------------------------------
                // واحدهای سازمانی
                // برای Dropdown واحد بالادستی
                // ---------------------------------------------

                var units = AG_db.MDR_OrganizationUnits
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.UnitName)
                    .Select(x => new
                    {
                        id = x.OrganizationUnitID,
                        text = x.UnitName
                    })
                    .ToList();


                // ---------------------------------------------
                // کاربران فعال
                // برای Dropdown مدیر / مسئول واحد
                // ---------------------------------------------

                var users = AG_db.tbUsers
                    .Where(x => x.usr_IsActive == true)
                    .OrderBy(x => x.usr_Family)
                    .ThenBy(x => x.usr_Name)
                    .Select(x => new
                    {
                        id = x.usr_ID,

                        text =
                            (x.usr_Name ?? "") +
                            " " +
                            (x.usr_Family ?? "")
                    })
                    .ToList();


                return Json(
                    new
                    {
                        success = true,
                        units = units,
                        users = users
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        message = ex.Message
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
        }

        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.3  : ساختار سازمانی و واحدها
        // Part 1.3.3.4 : ثبت واحد سازمانی
        // =====================================================
        // ==========================================================
        // MDR - 1.3.2.1
        // دریافت اطلاعات واقعی چارت سازمانی
        // ==========================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetOrganizationChartData()
        {
            try
            {
                var units = AG_db.MDR_OrganizationUnits
                    .OrderBy(x => x.ParentID)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.UnitName)
                    .ToList();

                var result = units.Select(x =>
                {
                    var manager = x.ManagerUserID.HasValue
                        ? AG_db.tbUsers.FirstOrDefault(
                            u => u.usr_ID == x.ManagerUserID.Value
                          )
                        : null;

                    string managerName = "";
                    string managerPicture = "";
                    string managerJobTitle = "";

                    if (manager != null)
                    {
                        managerName =
                            ((manager.usr_Name ?? "") + " " +
                             (manager.usr_Family ?? "")).Trim();

                        managerPicture =
                            manager.usr_Picture ?? "";

                        if (manager.usr_job.HasValue)
                        {
                            int jobId = manager.usr_job.Value;

                            var job = AG_db.tbdetailjob
                                .FirstOrDefault(j => j.ID == jobId);

                            if (job != null)
                            {
                                managerJobTitle =
                                    job.detail_title ?? "";
                            }
                        }
                    }

                    return new
                    {
                        id = x.OrganizationUnitID,

                        unitName = x.UnitName,

                        unitType = x.UnitType,

                        parentId = x.ParentID,

                        managerUserId = x.ManagerUserID,

                        managerName = managerName,

                        managerJobTitle = managerJobTitle,

                        managerPicture = managerPicture,

                        sortOrder = x.SortOrder
                    };
                })
                .ToList();

                return Json(
                    new
                    {
                        success = true,
                        units = result
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        message = ex.Message
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
        }

        [MdrManagerAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_SaveOrganizationUnit(
            string unitName,
            string unitType,
            int? parentId,
            int? managerUserId,
            int? sortOrder)
        {
            try
            {
                // ---------------------------------------------
                // Validation - نام واحد
                // ---------------------------------------------

                unitName = (unitName ?? "").Trim();
                unitType = (unitType ?? "").Trim();

                if (string.IsNullOrWhiteSpace(unitName))
                {
                    return Json(new
                    {
                        success = false,
                        message = "نام واحد سازمانی را وارد کنید."
                    });
                }


                // ---------------------------------------------
                // Validation - نوع واحد
                // ---------------------------------------------

                if (string.IsNullOrWhiteSpace(unitType))
                {
                    return Json(new
                    {
                        success = false,
                        message = "نوع واحد سازمانی را انتخاب کنید."
                    });
                }


                // ---------------------------------------------
                // Validation - واحد بالادستی
                // ---------------------------------------------

                if (parentId.HasValue)
                {
                    bool parentExists =
                        AG_db.MDR_OrganizationUnits.Any(
                            x => x.OrganizationUnitID == parentId.Value
                        );

                    if (!parentExists)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "واحد بالادستی انتخاب‌شده معتبر نیست."
                        });
                    }
                }


                // ---------------------------------------------
                // Validation - مدیر / مسئول
                // ---------------------------------------------

                if (managerUserId.HasValue)
                {
                    bool managerExists =
                        AG_db.tbUsers.Any(
                            x =>
                                x.usr_ID == managerUserId.Value &&
                                x.usr_IsActive == true
                        );

                    if (!managerExists)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "مدیر / مسئول انتخاب‌شده معتبر نیست."
                        });
                    }
                }


                // ---------------------------------------------
                // SortOrder
                // اگر کاربر وارد نکرده باشد:
                // آخرین ترتیب همان سطح + 1
                // ---------------------------------------------

                int finalSortOrder;

                if (sortOrder.HasValue)
                {
                    if (sortOrder.Value < 1)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "ترتیب نمایش باید بزرگ‌تر از صفر باشد."
                        });
                    }

                    finalSortOrder = sortOrder.Value;
                }
                else
                {
                    int? maxSortOrder;

                    if (parentId.HasValue)
                    {
                        maxSortOrder =
                            AG_db.MDR_OrganizationUnits
                                .Where(
                                    x => x.ParentID == parentId.Value
                                )
                                .Select(
                                    x => (int?)x.SortOrder
                                )
                                .Max();
                    }
                    else
                    {
                        maxSortOrder =
                            AG_db.MDR_OrganizationUnits
                                .Where(
                                    x => x.ParentID == null
                                )
                                .Select(
                                    x => (int?)x.SortOrder
                                )
                                .Max();
                    }

                    finalSortOrder =
                        (maxSortOrder ?? 0) + 1;
                }


                // ---------------------------------------------
                // ایجاد واحد
                // ---------------------------------------------

                var organizationUnit =
                    new MdrOrganizationUnit
                    {
                        UnitName = unitName,
                        UnitType = unitType,
                        ParentID = parentId,
                        ManagerUserID = managerUserId,
                        SortOrder = finalSortOrder,
                        CreatedAt = DateTime.Now
                    };


                AG_db.MDR_OrganizationUnits.Add(
                    organizationUnit
                );

                AG_db.SaveChanges();


                // ---------------------------------------------
                // MDR 1.4.3 — Audit Log
                // ---------------------------------------------

                AuditLogger.Log(
                    AuditLogger.Actions.Create,
                    AuditLogger.Modules.OrganizationStructure,
                    "ثبت واحد سازمانی «" + organizationUnit.UnitName + "»",
                    entityName: "MDR_OrganizationUnits",
                    entityId: organizationUnit.OrganizationUnitID,
                    entityTitle: organizationUnit.UnitName,
                    newValues: AG_GetOrganizationUnitAuditValues(organizationUnit)
                );


                // ---------------------------------------------
                // نتیجه
                // ---------------------------------------------

                return Json(new
                {
                    success = true,

                    message =
                        "واحد سازمانی با موفقیت ثبت شد.",

                    id =
                        organizationUnit.OrganizationUnitID,

                    sortOrder =
                        organizationUnit.SortOrder
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "خطا در ثبت واحد سازمانی: " +
                        ex.Message
                });
            }
        }

        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.3  : ساختار سازمانی و واحدها
        // Part 1.3.4: ویرایش واحد سازمانی
        // =====================================================

        [MdrManagerAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_UpdateOrganizationUnit(
            int organizationUnitId,
            string unitName,
            string unitType,
            int? parentId,
            int? managerUserId,
            int? sortOrder)
        {
            try
            {
                unitName = (unitName ?? "").Trim();
                unitType = (unitType ?? "").Trim();

                // ---------------------------------------------
                // پیدا کردن واحد
                // ---------------------------------------------

                var organizationUnit =
                    AG_db.MDR_OrganizationUnits
                        .FirstOrDefault(
                            x => x.OrganizationUnitID ==
                                 organizationUnitId
                        );

                if (organizationUnit == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "واحد سازمانی موردنظر پیدا نشد."
                    });
                }


                // ---------------------------------------------
                // Validation
                // ---------------------------------------------

                if (string.IsNullOrWhiteSpace(unitName))
                {
                    return Json(new
                    {
                        success = false,
                        message = "نام واحد سازمانی را وارد کنید."
                    });
                }


                if (string.IsNullOrWhiteSpace(unitType))
                {
                    return Json(new
                    {
                        success = false,
                        message = "نوع واحد سازمانی را انتخاب کنید."
                    });
                }


                // ---------------------------------------------
                // جلوگیری از Parent شدن واحد برای خودش
                // ---------------------------------------------

                if (
                    parentId.HasValue &&
                    parentId.Value == organizationUnitId
                )
                {
                    return Json(new
                    {
                        success = false,
                        message =
                            "یک واحد نمی‌تواند واحد بالادستی خودش باشد."
                    });
                }


                // ---------------------------------------------
                // بررسی Parent
                // ---------------------------------------------

                if (parentId.HasValue)
                {
                    bool parentExists =
                        AG_db.MDR_OrganizationUnits.Any(
                            x => x.OrganizationUnitID ==
                                 parentId.Value
                        );

                    if (!parentExists)
                    {
                        return Json(new
                        {
                            success = false,
                            message =
                                "واحد بالادستی انتخاب‌شده معتبر نیست."
                        });
                    }


                    // -----------------------------------------
                    // جلوگیری از Cycle
                    // A → B → C
                    // C نمی‌تواند Parent مربوط به A شود
                    // -----------------------------------------

                    int? currentParentId =
                        parentId;

                    while (currentParentId.HasValue)
                    {
                        if (
                            currentParentId.Value ==
                            organizationUnitId
                        )
                        {
                            return Json(new
                            {
                                success = false,
                                message =
                                    "این جابه‌جایی باعث ایجاد حلقه در ساختار سازمانی می‌شود."
                            });
                        }


                        currentParentId =
                            AG_db.MDR_OrganizationUnits
                                .Where(
                                    x => x.OrganizationUnitID ==
                                         currentParentId.Value
                                )
                                .Select(
                                    x => x.ParentID
                                )
                                .FirstOrDefault();
                    }
                }


                // ---------------------------------------------
                // بررسی مدیر
                // ---------------------------------------------

                if (managerUserId.HasValue)
                {
                    bool managerExists =
                        AG_db.tbUsers.Any(
                            x =>
                                x.usr_ID ==
                                managerUserId.Value &&
                                x.usr_IsActive == true
                        );

                    if (!managerExists)
                    {
                        return Json(new
                        {
                            success = false,
                            message =
                                "مدیر / مسئول انتخاب‌شده معتبر نیست."
                        });
                    }
                }


                // ---------------------------------------------
                // SortOrder
                // ---------------------------------------------

                int finalSortOrder;

                if (sortOrder.HasValue)
                {
                    if (sortOrder.Value < 1)
                    {
                        return Json(new
                        {
                            success = false,
                            message =
                                "ترتیب نمایش باید بزرگ‌تر از صفر باشد."
                        });
                    }

                    finalSortOrder =
                        sortOrder.Value;
                }
                else
                {
                    int? maxSortOrder;

                    if (parentId.HasValue)
                    {
                        maxSortOrder =
                            AG_db.MDR_OrganizationUnits
                                .Where(
                                    x =>
                                        x.ParentID ==
                                        parentId.Value &&
                                        x.OrganizationUnitID !=
                                        organizationUnitId
                                )
                                .Select(
                                    x => (int?)x.SortOrder
                                )
                                .Max();
                    }
                    else
                    {
                        maxSortOrder =
                            AG_db.MDR_OrganizationUnits
                                .Where(
                                    x =>
                                        x.ParentID == null &&
                                        x.OrganizationUnitID !=
                                        organizationUnitId
                                )
                                .Select(
                                    x => (int?)x.SortOrder
                                )
                                .Max();
                    }

                    finalSortOrder =
                        (maxSortOrder ?? 0) + 1;
                }


                // ---------------------------------------------
                // MDR 1.4.3 — مقادیر قبل از ویرایش (برای Audit Log)
                // ---------------------------------------------

                var oldAuditValues =
                    AG_GetOrganizationUnitAuditValues(organizationUnit);


                // ---------------------------------------------
                // Update
                // ---------------------------------------------

                organizationUnit.UnitName =
                    unitName;

                organizationUnit.UnitType =
                    unitType;

                organizationUnit.ParentID =
                    parentId;

                organizationUnit.ManagerUserID =
                    managerUserId;

                organizationUnit.SortOrder =
                    finalSortOrder;


                AG_db.SaveChanges();


                // ---------------------------------------------
                // MDR 1.4.3 — Audit Log
                // ---------------------------------------------

                AuditLogger.Log(
                    AuditLogger.Actions.Update,
                    AuditLogger.Modules.OrganizationStructure,
                    "ویرایش واحد سازمانی «" + organizationUnit.UnitName + "»",
                    entityName: "MDR_OrganizationUnits",
                    entityId: organizationUnit.OrganizationUnitID,
                    entityTitle: organizationUnit.UnitName,
                    oldValues: oldAuditValues,
                    newValues: AG_GetOrganizationUnitAuditValues(organizationUnit)
                );


                return Json(new
                {
                    success = true,

                    message =
                        "اطلاعات واحد سازمانی با موفقیت ویرایش شد.",

                    id =
                        organizationUnit.OrganizationUnitID,

                    sortOrder =
                        organizationUnit.SortOrder
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,

                    message =
                        "خطا در ویرایش واحد سازمانی: " +
                        ex.Message
                });
            }
        }


        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.3  : ساختار سازمانی و واحدها
        // Part 1.3.4.1 : حذف واحد سازمانی
        // قانون: واحدی که زیرمجموعه دارد حذف نمی‌شود
        // =====================================================

        [MdrManagerAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_DeleteOrganizationUnit(
            int organizationUnitId)
        {
            try
            {
                // ---------------------------------------------
                // پیدا کردن واحد
                // ---------------------------------------------

                var organizationUnit =
                    AG_db.MDR_OrganizationUnits
                        .FirstOrDefault(
                            x => x.OrganizationUnitID ==
                                 organizationUnitId
                        );

                if (organizationUnit == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "واحد سازمانی موردنظر پیدا نشد."
                    });
                }


                // ---------------------------------------------
                // بررسی زیرمجموعه‌ها
                // ---------------------------------------------

                int childCount =
                    AG_db.MDR_OrganizationUnits
                        .Count(
                            x => x.ParentID ==
                                 organizationUnitId
                        );

                if (childCount > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message =
                            "این واحد " + childCount +
                            " زیرمجموعه دارد. ابتدا زیرمجموعه‌ها را حذف یا به واحد دیگری منتقل کنید."
                    });
                }


                // ---------------------------------------------
                // Delete
                // ---------------------------------------------

                string unitName =
                    organizationUnit.UnitName;

                var oldAuditValues =
                    AG_GetOrganizationUnitAuditValues(organizationUnit);

                AG_db.MDR_OrganizationUnits.Remove(
                    organizationUnit
                );

                AG_db.SaveChanges();


                // ---------------------------------------------
                // MDR 1.4.3 — Audit Log
                // ---------------------------------------------

                AuditLogger.Log(
                    AuditLogger.Actions.Delete,
                    AuditLogger.Modules.OrganizationStructure,
                    "حذف واحد سازمانی «" + unitName + "»",
                    entityName: "MDR_OrganizationUnits",
                    entityId: organizationUnitId,
                    entityTitle: unitName,
                    oldValues: oldAuditValues
                );


                return Json(new
                {
                    success = true,

                    message =
                        "واحد «" + unitName + "» با موفقیت حذف شد.",

                    id =
                        organizationUnitId
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,

                    message =
                        "خطا در حذف واحد سازمانی: " +
                        ex.Message
                });
            }
        }


        // =====================================================
        // MDR
        // Step 1.4  : Audit Log
        // Part 1.4.3: مقادیر یک واحد برای ثبت در لاگ (قبل / بعد)
        // =====================================================

        private object AG_GetOrganizationUnitAuditValues(
            MdrOrganizationUnit unit)
        {
            if (unit == null)
            {
                return null;
            }

            string parentName =
                unit.ParentID.HasValue
                    ? AG_db.MDR_OrganizationUnits
                        .Where(x => x.OrganizationUnitID == unit.ParentID.Value)
                        .Select(x => x.UnitName)
                        .FirstOrDefault()
                    : null;

            string managerName = null;

            if (unit.ManagerUserID.HasValue)
            {
                var manager =
                    AG_db.tbUsers
                        .Where(x => x.usr_ID == unit.ManagerUserID.Value)
                        .Select(x => new { x.usr_Name, x.usr_Family })
                        .FirstOrDefault();

                if (manager != null)
                {
                    managerName =
                        ((manager.usr_Name ?? "") + " " +
                         (manager.usr_Family ?? "")).Trim();
                }
            }

            return new
            {
                UnitName = unit.UnitName,
                UnitType = unit.UnitType,
                ParentID = unit.ParentID,
                ParentName = parentName,
                ManagerUserID = unit.ManagerUserID,
                ManagerName = managerName,
                SortOrder = unit.SortOrder
            };
        }


        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.4  : Audit Log
        // Part 1.4.4: صفحه مشاهده تاریخچه رویدادها
        // آدرس: /AG/AuditLogs
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public ActionResult AG_AuditLogs()
        {
            return View("~/Areas/Mdr/Views/AG/AuditLogs.cshtml");
        }



        // =====================================================
        // MDR 1.4.4.1
        // داده‌های فیلترها (کاربران و جدول‌ها)
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetAuditLogFilters()
        {
            try
            {
                // همه کاربران فعال از tbUsers (نه فقط کسانی که رکورد دارند)
                var users = AG_GetMdrActiveUsersForFilter();

                var entities =
                    AG_db.MDR_AuditLogs
                        .Where(x => x.EntityName != null)
                        .Select(x => x.EntityName)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                return Json(new
                {
                    success = true,
                    users = users,
                    entities = entities
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت فیلترها: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.4.4.2
        // لیست لاگ‌ها + فیلتر + صفحه‌بندی + آمار امروز
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetAuditLogs(
            int page = 1,
            int pageSize = 20,
            string fromDate = null,
            string toDate = null,
            int? userId = null,
            string actionType = null,
            string entityName = null,
            string source = null,
            string search = null)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 5) pageSize = 5;
                if (pageSize > 100) pageSize = 100;

                var query =
                    AG_db.MDR_AuditLogs.AsQueryable();


                // ---------------------------------------------
                // فیلتر تاریخ (شمسی؛ مثل 1405/07/01)
                // ---------------------------------------------

                string from = AG_NormalizeAuditPersianDate(fromDate);
                string to = AG_NormalizeAuditPersianDate(toDate);

                if (from != null)
                {
                    query = query.Where(x => x.PersianDate.CompareTo(from) >= 0);
                }

                if (to != null)
                {
                    query = query.Where(x => x.PersianDate.CompareTo(to) <= 0);
                }


                // ---------------------------------------------
                // سایر فیلترها
                // ---------------------------------------------

                if (userId.HasValue)
                {
                    query = query.Where(x => x.UserID == userId.Value);
                }

                if (!string.IsNullOrWhiteSpace(actionType))
                {
                    query = query.Where(x => x.ActionType == actionType);
                }

                if (!string.IsNullOrWhiteSpace(entityName))
                {
                    query = query.Where(x => x.EntityName == entityName);
                }

                if (source == "auto")
                {
                    query = query.Where(x => x.ModuleName.StartsWith("Auto"));
                }
                else if (source == "manual")
                {
                    query = query.Where(x => !x.ModuleName.StartsWith("Auto"));
                }


                // ---------------------------------------------
                // جستجو (با هر دو شکل «ی/ي» و «ک/ك»)
                // ---------------------------------------------

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s1 = search.Trim()
                        .Replace('ي', 'ی')
                        .Replace('ك', 'ک');

                    string s2 = s1
                        .Replace('ی', 'ي')
                        .Replace('ک', 'ك');

                    query = query.Where(x =>
                        x.Description.Contains(s1) || x.Description.Contains(s2) ||
                        x.UserFullName.Contains(s1) || x.UserFullName.Contains(s2) ||
                        x.EntityTitle.Contains(s1) || x.EntityTitle.Contains(s2) ||
                        x.EntityID == s1 ||
                        x.ModuleName.Contains(s1));
                }


                // ---------------------------------------------
                // صفحه‌بندی
                // ---------------------------------------------

                int total = query.Count();

                var items =
                    query
                        .OrderByDescending(x => x.AuditLogID)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new
                        {
                            x.AuditLogID,
                            x.CreatedAt,
                            x.PersianDate,
                            x.UserFullName,
                            x.ActionType,
                            x.ModuleName,
                            x.Description,
                            x.EntityName,
                            x.EntityID,
                            x.EntityTitle,
                            x.IsSuccess
                        })
                        .ToList()
                        .Select(x => new
                        {
                            id = x.AuditLogID,
                            date = x.PersianDate,
                            time = x.CreatedAt.ToString("HH:mm"),
                            user = x.UserFullName,
                            action = x.ActionType,
                            module = x.ModuleName,
                            description = x.Description,
                            entity = x.EntityName,
                            entityId = x.EntityID,
                            entityTitle = x.EntityTitle,
                            success = x.IsSuccess
                        })
                        .ToList();


                // ---------------------------------------------
                // آمار امروز (بدون فیلتر)
                // ---------------------------------------------

                string today = AG_GetTodayPersianDate();

                var todayLogs =
                    AG_db.MDR_AuditLogs.Where(x => x.PersianDate == today);

                var stats = new
                {
                    today = today,
                    total = todayLogs.Count(),
                    creates = todayLogs.Count(x => x.ActionType == "Create"),
                    updates = todayLogs.Count(x => x.ActionType == "Update"),
                    deletes = todayLogs.Count(x => x.ActionType == "Delete"),
                    users = todayLogs
                        .Where(x => x.UserID != null)
                        .Select(x => x.UserID)
                        .Distinct()
                        .Count()
                };

                return Json(new
                {
                    success = true,
                    page = page,
                    pageSize = pageSize,
                    total = total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize),
                    items = items,
                    stats = stats
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت لاگ‌ها: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.4.4.3
        // جزئیات یک لاگ (برای مودال)
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetAuditLogDetail(long id)
        {
            try
            {
                var log =
                    AG_db.MDR_AuditLogs
                        .FirstOrDefault(x => x.AuditLogID == id);

                if (log == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "لاگ موردنظر پیدا نشد."
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new
                {
                    success = true,
                    log = new
                    {
                        id = log.AuditLogID,
                        date = log.PersianDate,
                        time = log.CreatedAt.ToString("HH:mm:ss"),
                        user = log.UserFullName,
                        userId = log.UserID,
                        action = log.ActionType,
                        module = log.ModuleName,
                        description = log.Description,
                        entity = log.EntityName,
                        entityId = log.EntityID,
                        entityTitle = log.EntityTitle,
                        oldValues = log.OldValues,
                        newValues = log.NewValues,
                        ip = log.IPAddress,
                        userAgent = log.UserAgent,
                        pageUrl = log.PageUrl,
                        success = log.IsSuccess
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت جزئیات: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // -----------------------------------------------------
        // Helpers — Audit Log Page
        // -----------------------------------------------------

        private static string AG_GetTodayPersianDate()
        {
            var pc = new PersianCalendar();
            var now = DateTime.Now;

            return string.Format(
                "{0:0000}/{1:00}/{2:00}",
                pc.GetYear(now),
                pc.GetMonth(now),
                pc.GetDayOfMonth(now)
            );
        }



        // «1405/7/1» یا «۱۴۰۵/۰۷/۰۱» → «1405/07/01»
        private static string AG_NormalizeAuditPersianDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var digits = new StringBuilder();

            foreach (char c in value.Trim())
            {
                if (c >= '۰' && c <= '۹')
                {
                    digits.Append((char)('0' + (c - '۰')));
                }
                else if (c >= '٠' && c <= '٩')
                {
                    digits.Append((char)('0' + (c - '٠')));
                }
                else
                {
                    digits.Append(c == '-' ? '/' : c);
                }
            }

            var parts = digits.ToString().Split('/');

            int year, month, day;

            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out year) ||
                !int.TryParse(parts[1], out month) ||
                !int.TryParse(parts[2], out day))
            {
                return null;
            }

            return string.Format("{0:0000}/{1:00}/{2:00}", year, month, day);
        }



        // =====================================================
        // MDR 1.5.3.6
        // حذف پیوست (حذف نرم)
        // آدرس: POST /AG/DeleteFile
        // =====================================================

        [MdrManagerAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_DeleteFile(Guid fileGuid)
        {
            var result = FileManager.Delete(fileGuid);

            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }



        // MDR 1.5.3 — نمایش خوانای حجم فایل
        private static string AG_FormatMdrFileSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " بایت";
            }

            if (bytes < 1024 * 1024)
            {
                return Math.Round(bytes / 1024.0, 1) + " کیلوبایت";
            }

            return Math.Round(bytes / 1024.0 / 1024.0, 1) + " مگابایت";
        }


        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.5  : مدیریت فایل و پیوست
        // Part 1.5.5: صفحه مدیریت فایل‌ها (برای مدیر سیستم)
        // آدرس: /AG/FileManagement
        // -----------------------------------------------------
        // لیست همه پیوست‌های سامانه + فیلتر، آمار و حجم،
        // فایل‌های حذف‌شده و بازگردانی، فایل‌های تکراری
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public ActionResult AG_FileManagement()
        {
            return View("~/Areas/Mdr/Views/AG/FileManagement.cshtml");
        }



        // =====================================================
        // MDR 1.5.5.2
        // داده‌های فیلترها (بخش، جدول، کاربر، پسوند)
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetFileManagementFilters()
        {
            try
            {
                var modules =
                    AG_db.MDR_Files
                        .Select(x => x.ModuleName)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                var entities =
                    AG_db.MDR_Files
                        .Select(x => x.EntityName)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                // همه کاربران فعال از tbUsers (نه فقط کسانی که رکورد دارند)
                var users = AG_GetMdrActiveUsersForFilter();

                var extensions =
                    AG_db.MDR_FileVersions
                        .Select(x => x.Extension)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                return Json(new
                {
                    success = true,
                    modules = modules,
                    entities = entities,
                    users = users,
                    extensions = extensions
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت فیلترها: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.5.5.3
        // لیست فایل‌ها + فیلتر + صفحه‌بندی + آمار
        // status    : "" همه | "active" فعال | "deleted" حذف‌شده
        // duplicate : true یعنی فقط فایل‌هایی که محتوای تکراری دارند
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetFileManagementList(
            int page = 1,
            int pageSize = 20,
            string fromDate = null,
            string toDate = null,
            string moduleName = null,
            string entityName = null,
            int? userId = null,
            string extension = null,
            string status = null,
            bool duplicate = false,
            string search = null)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 5) pageSize = 5;
                if (pageSize > 100) pageSize = 100;


                // ---------------------------------------------
                // Hashهایی که در بیش از یک پیوست تکرار شده‌اند
                // ---------------------------------------------

                var duplicateHashes =
                    AG_db.MDR_FileVersions
                        .GroupBy(v => v.FileHash)
                        .Where(g => g.Select(v => v.FileID).Distinct().Count() > 1)
                        .Select(g => g.Key);


                // ---------------------------------------------
                // پیوست + نسخه فعلی
                // ---------------------------------------------

                var query =
                    from f in AG_db.MDR_Files
                    join v in AG_db.MDR_FileVersions
                        on new { A = f.FileID, B = f.CurrentVersionNo }
                        equals new { A = v.FileID, B = v.VersionNo }
                    select new { f, v };


                // ---------------------------------------------
                // فیلترها
                // ---------------------------------------------

                string fromValue = AG_NormalizeAuditPersianDate(fromDate);
                string toValue = AG_NormalizeAuditPersianDate(toDate);

                if (fromValue != null)
                {
                    query = query.Where(x => x.f.PersianDate.CompareTo(fromValue) >= 0);
                }

                if (toValue != null)
                {
                    query = query.Where(x => x.f.PersianDate.CompareTo(toValue) <= 0);
                }

                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    query = query.Where(x => x.f.ModuleName == moduleName);
                }

                if (!string.IsNullOrWhiteSpace(entityName))
                {
                    query = query.Where(x => x.f.EntityName == entityName);
                }

                if (userId.HasValue)
                {
                    query = query.Where(x => x.f.CreatedByUserID == userId.Value);
                }

                if (!string.IsNullOrWhiteSpace(extension))
                {
                    query = query.Where(x => x.v.Extension == extension);
                }

                if (status == "active")
                {
                    query = query.Where(x => !x.f.IsDeleted);
                }
                else if (status == "deleted")
                {
                    query = query.Where(x => x.f.IsDeleted);
                }

                if (duplicate)
                {
                    query = query.Where(x => duplicateHashes.Contains(x.v.FileHash));
                }


                // جستجو (با هر دو شکل «ی/ي» و «ک/ك»)
                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s1 = search.Trim()
                        .Replace('ي', 'ی')
                        .Replace('ك', 'ک');

                    string s2 = s1
                        .Replace('ی', 'ي')
                        .Replace('ک', 'ك');

                    query = query.Where(x =>
                        x.f.Title.Contains(s1) || x.f.Title.Contains(s2) ||
                        x.v.OriginalFileName.Contains(s1) || x.v.OriginalFileName.Contains(s2) ||
                        x.f.CreatedByName.Contains(s1) || x.f.CreatedByName.Contains(s2) ||
                        x.f.EntityID == s1);
                }


                // ---------------------------------------------
                // صفحه‌بندی
                // ---------------------------------------------

                int total = query.Count();

                var rows =
                    query
                        .OrderByDescending(x => x.f.FileID)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(x => new
                        {
                            x.f.FileGuid,
                            x.f.Title,
                            x.f.ModuleName,
                            x.f.EntityName,
                            x.f.EntityID,
                            x.f.CurrentVersionNo,
                            x.f.PersianDate,
                            x.f.CreatedByName,
                            x.f.IsDeleted,
                            x.v.OriginalFileName,
                            x.v.Extension,
                            x.v.FileSize,
                            VersionsCount = AG_db.MDR_FileVersions.Count(z => z.FileID == x.f.FileID),
                            IsDuplicate = duplicateHashes.Contains(x.v.FileHash)
                        })
                        .ToList();

                var items =
                    rows.Select(x => new
                    {
                        fileGuid = x.FileGuid,
                        title = x.Title,
                        module = x.ModuleName,
                        entity = x.EntityName,
                        entityId = x.EntityID,
                        versionNo = x.CurrentVersionNo,
                        versionsCount = x.VersionsCount,
                        date = x.PersianDate,
                        createdBy = x.CreatedByName,
                        isDeleted = x.IsDeleted,
                        isDuplicate = x.IsDuplicate,
                        fileName = x.OriginalFileName,
                        extension = x.Extension,
                        sizeText = AG_FormatMdrFileSize(x.FileSize)
                    })
                    .ToList();


                // ---------------------------------------------
                // آمار کل (بدون فیلتر)
                // ---------------------------------------------

                long totalBytes =
                    AG_db.MDR_FileVersions.Sum(v => (long?)v.FileSize) ?? 0;

                var stats = new
                {
                    active = AG_db.MDR_Files.Count(x => !x.IsDeleted),
                    deleted = AG_db.MDR_Files.Count(x => x.IsDeleted),
                    versions = AG_db.MDR_FileVersions.Count(),
                    duplicates = duplicateHashes.Count(),
                    totalSizeText = AG_FormatMdrFileSize(totalBytes)
                };


                // حجم هر بخش (همه نسخه‌ها)
                var moduleSizes =
                    (from v in AG_db.MDR_FileVersions
                     join f in AG_db.MDR_Files on v.FileID equals f.FileID
                     group v by f.ModuleName into g
                     select new
                     {
                         Module = g.Key,
                         Bytes = g.Sum(z => z.FileSize),
                         Files = g.Select(z => z.FileID).Distinct().Count()
                     })
                    .OrderByDescending(x => x.Bytes)
                    .ToList()
                    .Select(x => new
                    {
                        module = x.Module,
                        files = x.Files,
                        sizeText = AG_FormatMdrFileSize(x.Bytes),
                        percent = totalBytes > 0
                            ? Math.Round(x.Bytes * 100.0 / totalBytes, 1)
                            : 0
                    })
                    .ToList();


                return Json(new
                {
                    success = true,
                    page = page,
                    pageSize = pageSize,
                    total = total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize),
                    items = items,
                    stats = stats,
                    moduleSizes = moduleSizes
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت فایل‌ها: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.5.5.4
        // جزئیات یک پیوست: اطلاعات + نسخه‌ها + فایل‌های هم‌محتوا
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public JsonResult AG_GetFileManagementDetail(Guid fileGuid)
        {
            try
            {
                var file =
                    AG_db.MDR_Files
                        .AsNoTracking()
                        .FirstOrDefault(x => x.FileGuid == fileGuid);

                if (file == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "پیوست موردنظر پیدا نشد."
                    }, JsonRequestBehavior.AllowGet);
                }

                var versions = FileManager
                    .GetVersions(fileGuid, includeDeleted: true)
                    .Select(x => new
                    {
                        versionNo = x.VersionNo,
                        fileName = x.OriginalFileName,
                        sizeText = AG_FormatMdrFileSize(x.FileSize),
                        changeNote = x.ChangeNote,
                        date = x.PersianDate,
                        time = x.Time,
                        uploadedBy = x.UploadedByName,
                        isCurrent = x.IsCurrent
                    })
                    .ToList();


                // پیوست‌های دیگری که دقیقاً همین محتوا را دارند
                var hashes =
                    AG_db.MDR_FileVersions
                        .Where(v => v.FileID == file.FileID)
                        .Select(v => v.FileHash)
                        .Distinct();

                var sameContent =
                    (from v in AG_db.MDR_FileVersions
                     join f in AG_db.MDR_Files on v.FileID equals f.FileID
                     where hashes.Contains(v.FileHash) && f.FileID != file.FileID
                     select new
                     {
                         f.FileGuid,
                         f.Title,
                         f.ModuleName,
                         f.EntityName,
                         f.EntityID,
                         f.IsDeleted
                     })
                    .Distinct()
                    .ToList()
                    .Select(x => new
                    {
                        fileGuid = x.FileGuid,
                        title = x.Title,
                        module = x.ModuleName,
                        entity = x.EntityName,
                        entityId = x.EntityID,
                        isDeleted = x.IsDeleted
                    })
                    .ToList();


                return Json(new
                {
                    success = true,
                    file = new
                    {
                        fileGuid = file.FileGuid,
                        title = file.Title,
                        description = file.Description,
                        category = file.Category,
                        module = file.ModuleName,
                        entity = file.EntityName,
                        entityId = file.EntityID,
                        versionNo = file.CurrentVersionNo,
                        date = file.PersianDate,
                        createdBy = file.CreatedByName,
                        updatedBy = file.UpdatedByName,
                        isDeleted = file.IsDeleted,
                        deletedAt = file.DeletedAt.HasValue
                            ? file.DeletedAt.Value.ToString("HH:mm")
                            : null,
                        deletedBy = file.DeletedByName
                    },
                    versions = versions,
                    sameContent = sameContent
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت جزئیات: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.5.5.5
        // دانلود از صفحه مدیریت (فایل‌های حذف‌شده هم قابل دانلودند)
        // آدرس: GET /AG/DownloadFileAdmin?id=...&version=...
        // =====================================================

        [MdrManagerAuthorize]
        [HttpGet]
        public ActionResult AG_DownloadFileAdmin(Guid id, int? version)
        {
            var download = FileManager.GetDownload(id, version, includeDeleted: true);

            if (download == null)
            {
                return HttpNotFound("فایل موردنظر پیدا نشد.");
            }

            return File(
                download.PhysicalPath,
                download.ContentType,
                download.DownloadName);
        }



        // =====================================================
        // MDR 1.5.5.6
        // بازگردانی پیوست حذف‌شده
        // آدرس: POST /AG/RestoreFile
        // =====================================================

        [MdrManagerAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_RestoreFile(Guid fileGuid)
        {
            var result = FileManager.Restore(fileGuid);

            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }



        // =====================================================
        // MDR — لیست کاربران فعال برای فیلتر «کاربر»
        // (مشترک صفحه تاریخچه رویدادها و مدیریت فایل‌ها)
        // =====================================================

        private List<object> AG_GetMdrActiveUsersForFilter()
        {
            return AG_db.tbUsers
                .Where(x => x.usr_IsActive == true)
                .OrderBy(x => x.usr_Family)
                .ThenBy(x => x.usr_Name)
                .Select(x => new
                {
                    id = x.usr_ID,
                    name = (x.usr_Name ?? "") + " " + (x.usr_Family ?? "")
                })
                .ToList()
                .Select(x => (object)new
                {
                    x.id,
                    name = x.name.Trim()
                })
                .ToList();
        }


        // =====================================================
        // MDR
        // Phase 1   : زیرساخت مشترک
        // Step 1.6  : اعلان‌ها و Notification
        // Part 1.6.3: Actionهای مرکز اعلان
        // -----------------------------------------------------
        // طبق سند جامع: خواندن، تأیید، Snooze، رفتن به منبع
        // هر کاربر فقط اعلان‌های خودش را می‌بیند و تغییر می‌دهد
        // (شناسه کاربر همیشه سمت سرور از کوکی خوانده می‌شود)
        // =====================================================

        // پاسخ استاندارد وقتی کاربر شناسایی نشد
        private JsonResult AG_MdrNotLoggedIn()
        {
            return Json(new
            {
                success = false,
                message = "کاربر شناسایی نشد. دوباره وارد سامانه شوید."
            }, JsonRequestBehavior.AllowGet);
        }



        // =====================================================
        // MDR 1.6.3.1
        // خلاصه برای زنگوله: تعداد خوانده‌نشده + آخرین‌ها + اعلان مهم
        // آدرس: GET /AG/GetNotificationSummary
        // =====================================================

        [MdrLoginAuthorize]
        [HttpGet]
        public JsonResult AG_GetNotificationSummary()
        {
            try
            {
                int? userId = NotificationService.GetCurrentUserId();

                if (!userId.HasValue)
                {
                    return AG_MdrNotLoggedIn();
                }

                var latest = NotificationService
                    .GetLatest(userId.Value, 8)
                    .Select(AG_MdrNotificationToJson)
                    .ToList();

                var important = NotificationService.GetPendingImportant(userId.Value);

                return Json(new
                {
                    success = true,
                    unreadCount = NotificationService.GetUnreadCount(userId.Value),
                    items = latest,
                    pendingImportant = important != null
                        ? AG_MdrNotificationToJson(important)
                        : null
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت اعلان‌ها: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.6.3.2
        // صفحه مرکز اعلان
        // آدرس: /AG/Notifications
        // =====================================================

        [MdrLoginAuthorize]
        [HttpGet]
        public ActionResult AG_Notifications()
        {
            return View("~/Areas/Mdr/Views/AG/Notifications.cshtml");
        }



        // =====================================================
        // MDR 1.6.3.3
        // لیست اعلان‌های من + آمار + گروه‌بندی منبع
        // status: "" | unread | read | snoozed | pending
        // =====================================================

        [MdrLoginAuthorize]
        [HttpGet]
        public JsonResult AG_GetMyNotifications(
            int page = 1,
            int pageSize = 20,
            string status = null,
            string severity = null,
            string moduleName = null,
            string search = null)
        {
            try
            {
                int? userId = NotificationService.GetCurrentUserId();

                if (!userId.HasValue)
                {
                    return AG_MdrNotLoggedIn();
                }

                if (page < 1) page = 1;
                if (pageSize < 5) pageSize = 5;
                if (pageSize > 100) pageSize = 100;

                int total;

                var items = NotificationService
                    .GetPage(userId.Value, page, pageSize, status, severity, moduleName, search, out total)
                    .Select(AG_MdrNotificationToJson)
                    .ToList();

                var sources = NotificationService
                    .GetSourceCounts(userId.Value)
                    .Select(x => new
                    {
                        module = x.ModuleName,
                        total = x.Total,
                        unread = x.Unread
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    page = page,
                    pageSize = pageSize,
                    total = total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize),
                    items = items,
                    stats = NotificationService.GetStats(userId.Value),
                    sources = sources
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "خطا در دریافت اعلان‌ها: " + ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }



        // =====================================================
        // MDR 1.6.3.4
        // رفتن به منبع: خوانده‌شده + انتقال به لینک اعلان
        // آدرس: GET /AG/OpenNotification?id=...
        // =====================================================

        [MdrLoginAuthorize]
        [HttpGet]
        public ActionResult AG_OpenNotification(long id)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Notifications");
            }

            string link = NotificationService.MarkRead(id, userId.Value);

            // فقط آدرس داخلی سامانه
            if (NotificationService.IsLocalUrl(link) && Url.IsLocalUrl(link))
            {
                return Redirect(link);
            }

            return RedirectToAction("Notifications");
        }



        // =====================================================
        // MDR 1.6.3.5
        // تغییر وضعیت: خواندن، همه خوانده، تأیید، Snooze، حذف از لیست
        // =====================================================

        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_MarkNotificationRead(long id)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            bool done = NotificationService.TryMarkRead(id, userId.Value);

            return Json(new
            {
                success = done,
                message = done ? null : AG_MdrNotificationChangedMessage
            });
        }



        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_MarkAllNotificationsRead()
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            int count = NotificationService.MarkAllRead(userId.Value);

            return Json(new
            {
                success = true,
                message = count > 0
                    ? count + " اعلان خوانده‌شده علامت خورد."
                    : "اعلان خوانده‌نشده‌ای وجود نداشت."
            });
        }



        // «مطالعه کردم» برای اعلان مهم
        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_AcknowledgeNotification(long id)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            bool done = NotificationService.Acknowledge(id, userId.Value);

            return Json(new
            {
                success = done,
                message = done ? "تأیید شد." : AG_MdrNotificationChangedMessage
            });
        }



        // Snooze: minutes = چند دقیقه بعد دوباره نمایش داده شود
        // (۱۵ دقیقه تا ۳۰ روز)
        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_SnoozeNotification(long id, int minutes)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            if (minutes < 15 || minutes > 30 * 24 * 60)
            {
                return Json(new
                {
                    success = false,
                    message = "زمان تعویق معتبر نیست."
                });
            }

            bool done = NotificationService.Snooze(
                id,
                userId.Value,
                DateTime.Now.AddMinutes(minutes));

            return Json(new
            {
                success = done,
                message = done
                    ? "اعلان به تعویق افتاد."
                    : "این اعلان قابل تعویق نیست (اعلان مهم ابتدا باید تأیید شود)."
            });
        }



        // =====================================================
        // MDR 1.6.3.6
        // Snooze با زمان دلخواه (مودال «تنظیم یادآوری» طبق سند)
        // date: تاریخ شمسی (1405/07/20) — time: ساعت (HH:mm)
        // =====================================================

        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_SnoozeNotificationAt(long id, string date, string time)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            DateTime until;

            if (!AG_TryParseMdrPersianDateTime(date, time, out until))
            {
                return Json(new
                {
                    success = false,
                    message = "تاریخ یا ساعت یادآوری معتبر نیست."
                });
            }

            if (until <= DateTime.Now.AddMinutes(5))
            {
                return Json(new
                {
                    success = false,
                    message = "زمان یادآوری باید بعد از الان باشد."
                });
            }

            if (until > DateTime.Now.AddDays(30))
            {
                return Json(new
                {
                    success = false,
                    message = "حداکثر زمان تعویق ۳۰ روز است."
                });
            }

            bool done = NotificationService.Snooze(id, userId.Value, until);

            return Json(new
            {
                success = done,
                message = done
                    ? "یادآوری تنظیم شد."
                    : "این اعلان قابل تعویق نیست (اعلان مهم ابتدا باید تأیید شود یا اعلان دیگر در لیست نیست)."
            });
        }



        // پیام استاندارد تغییر همزمان (مثلاً در تب یا دستگاه دیگر)
        private const string AG_MdrNotificationChangedMessage =
            "این اعلان در جای دیگری تغییر کرده یا دیگر در لیست شما نیست. لیست به‌روز شد.";



        // تاریخ شمسی + ساعت → DateTime
        private static bool AG_TryParseMdrPersianDateTime(
            string date,
            string time,
            out DateTime result)
        {
            result = DateTime.MinValue;

            string normalized = AG_NormalizeAuditPersianDate(date);

            if (normalized == null)
            {
                return false;
            }

            var parts = normalized.Split('/');

            int hour = 8, minute = 0;

            if (!string.IsNullOrWhiteSpace(time))
            {
                var t = time
                    .Trim()
                    .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                    .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                    .Split(':');

                if (t.Length < 2 ||
                    !int.TryParse(t[0], out hour) ||
                    !int.TryParse(t[1], out minute) ||
                    hour < 0 || hour > 23 || minute < 0 || minute > 59)
                {
                    return false;
                }
            }

            try
            {
                result = new System.Globalization.PersianCalendar().ToDateTime(
                    int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]),
                    hour, minute, 0, 0);

                return true;
            }
            catch
            {
                return false;
            }
        }



        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_CancelSnoozeNotification(long id)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            bool done = NotificationService.CancelSnooze(id, userId.Value);

            return Json(new
            {
                success = done,
                message = done
                    ? "تعویق لغو شد."
                    : "زمان تعویق قبلاً تمام شده یا در جای دیگری لغو شده است. لیست به‌روز شد."
            });
        }



        [MdrLoginAuthorize]
        [MdrSameOrigin]
        [HttpPost]
        public JsonResult AG_ArchiveNotification(long id)
        {
            int? userId = NotificationService.GetCurrentUserId();

            if (!userId.HasValue)
            {
                return AG_MdrNotLoggedIn();
            }

            bool done = NotificationService.Archive(id, userId.Value);

            return Json(new
            {
                success = done,
                message = done
                    ? "اعلان از لیست حذف شد."
                    : "این اعلان قابل حذف نیست؛ یا قبلاً حذف شده یا اعلان مهمی است که ابتدا باید تأیید شود."
            });
        }



        // MDR 1.6.3 — تبدیل اعلان به JSON برای صفحه
        private static object AG_MdrNotificationToJson(
            NotificationService.NotificationItem x)
        {
            return new
            {
                id = x.NotificationID,
                title = x.Title,
                message = x.Message,
                severity = x.Severity,
                important = x.IsImportant,
                module = x.ModuleName,
                hasLink = x.HasLink,
                sender = x.SenderName,
                date = x.PersianDate,
                time = x.Time,
                isRead = x.IsRead,
                isAcknowledged = x.IsAcknowledged,
                isSnoozed = x.IsSnoozed,
                snoozedUntil = x.SnoozedUntil.HasValue
                    ? x.SnoozedUntil.Value.ToString("HH:mm")
                    : null
            };
        }
    }
}
