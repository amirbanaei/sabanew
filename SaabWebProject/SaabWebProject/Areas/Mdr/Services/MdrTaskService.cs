using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 4 | سرویس مدیریت تسک (پنل ۷)
    // وضعیت‌ها: Todo → InProgress → Done ؛ Blocked ؛ Cancelled
    // قواعد: Complete نتیجه می‌خواهد و تا زیرتسک باز هست رد می‌شود؛
    //        Block/Cancel دلیل می‌خواهند؛ Delegate مسئول را عوض و اعلان می‌دهد.
    // دسترسی: مسئول (Start/Complete/Block/Unblock)، سازنده یا مسئول (Delegate)،
    //        سازنده (Edit/Cancel)؛ manager (CanEdit صفحه) همه را دارد.
    // هر تغییر: History (CorrelationID) + Audit؛ Concurrent update با RowVer.
    // =====================================================
    public static class MdrTaskService
    {
        private const string Module = "Tasks";
        private static readonly string[] Priorities = { "Low", "Normal", "High", "Urgent" };

        public class TaskInput
        {
            public long Id { get; set; }
            public long? ParentId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Priority { get; set; }
            public int? AssigneeUserId { get; set; }
            public DateTime? DueAt { get; set; }
            public string SourceType { get; set; }
            public string SourceRef { get; set; }
            public string SourceUrl { get; set; }
        }

        public static bool IsOpen(string status) { return status == "Todo" || status == "InProgress" || status == "Blocked"; }

        // ---------------- ایجاد / ویرایش ----------------
        public static MdrResult Save(TaskInput t, int actorId, bool isManager, byte[] rowVer)
        {
            var title = (t.Title ?? "").Trim();
            if (title.Length == 0 || title.Length > 300) return MdrResult.Fail("tsk.err.title");
            var priority = string.IsNullOrEmpty(t.Priority) ? "Normal" : t.Priority;
            if (!Priorities.Contains(priority)) return MdrResult.Fail("tsk.err.priority");
            if (t.SourceUrl != null && !NotificationService.IsLocalUrl(t.SourceUrl)) t.SourceUrl = null;

            try
            {
                using (var db = new MdrContext())
                {
                    if (t.AssigneeUserId.HasValue && !UserExists(db, t.AssigneeUserId.Value)) return MdrResult.Fail("wf.err.baduser");

                    if (t.Id == 0)
                    {
                        if (t.ParentId.HasValue)
                        {
                            var parent = db.Tasks.FirstOrDefault(p => p.TaskID == t.ParentId.Value);
                            if (parent == null) return MdrResult.Fail("wf.err.notfound");
                            if (!IsOpen(parent.Status)) return MdrResult.Fail("tsk.err.closed");
                            if (!isManager && parent.CreatedBy != actorId && parent.AssigneeUserID != actorId) return MdrResult.Fail("tsk.err.notallowed");
                        }

                        var task = new MdrTask
                        {
                            ParentTaskID = t.ParentId, Title = title, Description = Trim(t.Description, 4000), Status = "Todo", Priority = priority,
                            AssigneeUserID = t.AssigneeUserId, CreatedBy = actorId, DueAt = t.DueAt,
                            SourceType = Trim(t.SourceType, 30), SourceRef = Trim(t.SourceRef, 50), SourceUrl = Trim(t.SourceUrl, 300),
                            CreatedAt = DateTime.Now
                        };
                        db.Tasks.Add(task);
                        db.SaveChanges();
                        Log(db, task.TaskID, "Create", actorId, task.AssigneeUserID.HasValue ? "assignee=" + task.AssigneeUserID : null);
                        db.SaveChanges();

                        NotifyAssignee(task, actorId, "تسک جدید");
                        AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد تسک",
                            entityName: "MDR_Tasks", entityId: task.TaskID, entityTitle: task.Title);
                        return MdrResult.Success(task.TaskID);
                    }

                    var e = db.Tasks.FirstOrDefault(x => x.TaskID == t.Id);
                    if (e == null) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !e.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (!IsOpen(e.Status)) return MdrResult.Fail("tsk.err.closed");
                    if (!isManager && e.CreatedBy != actorId) return MdrResult.Fail("tsk.err.notallowed");

                    var before = new { e.Title, e.Priority, e.DueAt, e.AssigneeUserID };
                    var assigneeChanged = e.AssigneeUserID != t.AssigneeUserId;
                    e.Title = title; e.Description = Trim(t.Description, 4000); e.Priority = priority; e.DueAt = t.DueAt; e.AssigneeUserID = t.AssigneeUserId;
                    Log(db, e.TaskID, "Edit", actorId, null);
                    db.SaveChanges();

                    if (assigneeChanged) NotifyAssignee(e, actorId, "تسک به شما واگذار شد");
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ویرایش تسک", entityName: "MDR_Tasks", entityId: e.TaskID,
                        entityTitle: e.Title, oldValues: before, newValues: new { e.Title, e.Priority, e.DueAt, e.AssigneeUserID });
                    return MdrResult.Success(e.TaskID);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- تغییر وضعیت و واگذاری ----------------
        // action: Start | Complete | Block | Unblock | Cancel | Delegate
        // text: نتیجه (Complete) / دلیل (Block, Cancel) / شناسه‌ی مسئول جدید (Delegate)
        public static MdrResult Act(long id, string action, int actorId, bool isManager, string text, byte[] rowVer)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var t = db.Tasks.FirstOrDefault(x => x.TaskID == id);
                    if (t == null) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !t.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");

                    var isAssignee = t.AssigneeUserID == actorId;
                    var isCreator = t.CreatedBy == actorId;
                    var oldStatus = t.Status;
                    string detail = null;
                    text = (text ?? "").Trim();

                    switch (action)
                    {
                        case "Start":
                            if (!(isAssignee || isManager)) return MdrResult.Fail("tsk.err.notallowed");
                            if (t.Status != "Todo") return MdrResult.Fail("tsk.err.state");
                            t.Status = "InProgress"; break;

                        case "Complete":
                            if (!(isAssignee || isManager)) return MdrResult.Fail("tsk.err.notallowed");
                            if (t.Status != "InProgress" && t.Status != "Todo") return MdrResult.Fail("tsk.err.state");
                            if (text.Length == 0) return MdrResult.Fail("tsk.err.result");
                            if (db.Tasks.Any(c => c.ParentTaskID == id && (c.Status == "Todo" || c.Status == "InProgress" || c.Status == "Blocked")))
                                return MdrResult.Fail("tsk.err.subtasks");
                            t.Status = "Done"; t.ResultText = Trim(text, 1000); t.CompletedAt = DateTime.Now; detail = t.ResultText; break;

                        case "Block":
                            if (!(isAssignee || isManager)) return MdrResult.Fail("tsk.err.notallowed");
                            if (t.Status != "Todo" && t.Status != "InProgress") return MdrResult.Fail("tsk.err.state");
                            if (text.Length == 0) return MdrResult.Fail("wf.err.reason");
                            t.Status = "Blocked"; t.BlockedReason = Trim(text, 500); detail = t.BlockedReason; break;

                        case "Unblock":
                            if (!(isAssignee || isManager)) return MdrResult.Fail("tsk.err.notallowed");
                            if (t.Status != "Blocked") return MdrResult.Fail("tsk.err.state");
                            t.Status = "InProgress"; t.BlockedReason = null; break;

                        case "Cancel":
                            if (!(isCreator || isManager)) return MdrResult.Fail("tsk.err.notallowed");
                            if (!IsOpen(t.Status)) return MdrResult.Fail("tsk.err.state");
                            if (text.Length == 0) return MdrResult.Fail("wf.err.reason");
                            t.Status = "Cancelled"; t.CompletedAt = DateTime.Now; detail = Trim(text, 500); break;

                        case "Delegate":
                            if (!(isAssignee || isCreator || isManager)) return MdrResult.Fail("tsk.err.notallowed");
                            if (!IsOpen(t.Status)) return MdrResult.Fail("tsk.err.state");
                            int newUser;
                            if (!int.TryParse(text, out newUser) || !UserExists(db, newUser)) return MdrResult.Fail("wf.err.baduser");
                            t.AssigneeUserID = newUser; detail = "assignee=" + newUser;
                            if (t.Status == "Blocked") { t.Status = "Todo"; t.BlockedReason = null; }
                            break;

                        default:
                            return MdrResult.Fail("wf.err.noaction");
                    }

                    Log(db, id, action, actorId, detail);
                    db.SaveChanges();

                    if (action == "Delegate") NotifyAssignee(t, actorId, "تسک به شما واگذار شد");
                    if (action == "Complete" && t.CreatedBy != actorId)
                        NotificationService.Send(t.CreatedBy, "تسک انجام شد", t.Title, severity: NotificationService.Severities.Success,
                            moduleName: Module, entityName: "MDR_Tasks", entityId: t.TaskID, linkUrl: "/Mdr/Tasks#id=" + t.TaskID);

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "اقدام روی تسک: " + action, entityName: "MDR_Tasks", entityId: t.TaskID,
                        entityTitle: t.Title, oldValues: new { Status = oldStatus }, newValues: new { t.Status, detail });
                    return MdrResult.Success(t.TaskID);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Comment(long id, string body, int actorId)
        {
            body = (body ?? "").Trim();
            if (body.Length == 0 || body.Length > 1000) return MdrResult.Fail("tsk.err.comment");

            try
            {
                using (var db = new MdrContext())
                {
                    var t = db.Tasks.FirstOrDefault(x => x.TaskID == id);
                    if (t == null) return MdrResult.Fail("wf.err.notfound");

                    db.TaskComments.Add(new MdrTaskComment { TaskID = id, UserID = actorId, Body = body, CreatedAt = DateTime.Now });
                    Log(db, id, "Comment", actorId, null);
                    db.SaveChanges();

                    var other = t.AssigneeUserID == actorId ? (int?)t.CreatedBy : t.AssigneeUserID;
                    if (other.HasValue && other.Value != actorId)
                        NotificationService.Send(other.Value, "نظر جدید روی تسک", t.Title, moduleName: Module, entityName: "MDR_Tasks",
                            entityId: t.TaskID, linkUrl: "/Mdr/Tasks#id=" + t.TaskID);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- helpers ----------------
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

        private static void Log(MdrContext db, long taskId, string action, int? actorId, string detail)
        {
            db.TaskHistory.Add(new MdrTaskHistory
            {
                TaskID = taskId, ActionKey = action, ActorUserID = actorId, Detail = Trim(detail, 500),
                CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now
            });
        }

        private static void NotifyAssignee(MdrTask t, int actorId, string title)
        {
            if (!t.AssigneeUserID.HasValue || t.AssigneeUserID.Value == actorId) return;
            NotificationService.Send(t.AssigneeUserID.Value, title + ": " + t.Title, t.Description,
                severity: t.Priority == "Urgent" ? NotificationService.Severities.Warning : NotificationService.Severities.Info,
                moduleName: Module, entityName: "MDR_Tasks", entityId: t.TaskID, linkUrl: "/Mdr/Tasks#id=" + t.TaskID);
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای مدیریت تسک", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
