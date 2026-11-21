using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 2 | سرویس موتور گردش کار
    // - Publish: اعتبارسنجی ساختار (Start یکتا، End، دسترس‌پذیری، مسیر خروج)
    // - StartInstance / Advance / Pause / Resume / Terminate
    // - هر تغییر: ردیف History (با CorrelationID) + AuditLogger
    //   + اعلان به مسئول گام (NotificationService)
    // - Concurrent update با RowVer؛ خطاها به‌صورت کد (MdrText: wf.err.*)
    // =====================================================

    public class MdrResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string Warning { get; set; }
        public string Message { get; set; }      // پیام آماده (مثلاً خطای FileManager)؛ اگر پر باشد به‌جای متن کدِ Error نمایش داده می‌شود
        public long? Id { get; set; }

        public static MdrResult Success(long? id = null) { return new MdrResult { Ok = true, Id = id }; }
        public static MdrResult Fail(string code) { return new MdrResult { Ok = false, Error = code }; }
        public static MdrResult FailWith(string code, string message) { return new MdrResult { Ok = false, Error = code, Message = message }; }
    }

    public static class MdrWorkflowService
    {
        public static class Statuses
        {
            public const string Draft = "Draft";
            public const string Published = "Published";
            public const string Retired = "Retired";
            public const string Running = "Running";
            public const string Paused = "Paused";
            public const string Completed = "Completed";
            public const string Terminated = "Terminated";
        }

        private const string Module = "Workflow";

        // رویداد بسته‌شدن Instance (Outcome: Approved | Rejected | Terminated) برای ماژول‌های مصرف‌کننده
        // (مثلاً درخواست‌ها). بعد از ذخیره‌ی موفق صدا زده می‌شود و خطای مشترکین عملیات اصلی را خراب نمی‌کند.
        public class ClosedEventArgs
        {
            public long InstanceID { get; set; }
            public string EntityName { get; set; }
            public string EntityID { get; set; }
            public string Subject { get; set; }
            public string Outcome { get; set; }
            public int ActorId { get; set; }
            public string Reason { get; set; }
        }

        public static event Action<ClosedEventArgs> InstanceClosed;

        private static void RaiseClosed(MdrWorkflowInstance inst, string outcome, int actorId, string reason)
        {
            var handler = InstanceClosed;
            if (handler == null) return;

            var args = new ClosedEventArgs
            {
                InstanceID = inst.InstanceID, EntityName = inst.EntityName, EntityID = inst.EntityID,
                Subject = inst.Subject, Outcome = outcome, ActorId = actorId, Reason = reason
            };

            foreach (Action<ClosedEventArgs> h in handler.GetInvocationList())
            {
                try { h(args); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("MDR workflow event: " + ex.Message); }
            }
        }

        // ---------------------------------------------
        // Publish
        // ---------------------------------------------
        public static MdrResult Publish(int workflowId, int actorId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var def = db.WorkflowDefinitions.FirstOrDefault(d => d.WorkflowID == workflowId);
                    if (def == null) return MdrResult.Fail("wf.err.notfound");
                    if (def.Status != Statuses.Draft) return MdrResult.Fail("wf.err.notdraft");

                    var steps = db.WorkflowSteps.Where(s => s.WorkflowID == workflowId).ToList();
                    var trans = db.WorkflowTransitions.Where(t => t.WorkflowID == workflowId).ToList();

                    var error = Validate(steps, trans);
                    if (error != null) return MdrResult.Fail(error);

                    foreach (var old in db.WorkflowDefinitions.Where(d => d.Code == def.Code && d.Status == Statuses.Published).ToList())
                        old.Status = Statuses.Retired;

                    def.Status = Statuses.Published;
                    def.PublishedAt = DateTime.Now;
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "انتشار گردش کار",
                        entityName: "MDR_WorkflowDefinitions", entityId: def.WorkflowID, entityTitle: def.Name,
                        newValues: new { def.Code, def.Version, actorId });
                    return MdrResult.Success(def.WorkflowID);
                }
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        // قواعد سند: مسیر بدون End، node یتیم، گام بدون مسیر خروج
        public static string Validate(IList<MdrWorkflowStep> steps, IList<MdrWorkflowTransition> trans)
        {
            var starts = steps.Where(s => s.StepType == "Start").ToList();
            var ends = steps.Where(s => s.StepType == "End").ToList();
            if (starts.Count != 1) return "wf.err.onestart";
            if (ends.Count < 1) return "wf.err.noend";

            var ids = new HashSet<int>(steps.Select(s => s.StepID));
            if (trans.Any(t => !ids.Contains(t.FromStepID) || !ids.Contains(t.ToStepID))) return "wf.err.badtransition";

            foreach (var s in steps)
            {
                var outCount = trans.Count(t => t.FromStepID == s.StepID);
                if (s.StepType == "End" && outCount > 0) return "wf.err.endout";
                if (s.StepType != "End" && outCount == 0) return "wf.err.noexit";
            }

            var seen = new HashSet<int> { starts[0].StepID };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var t in trans.Where(x => x.FromStepID == cur))
                    if (seen.Add(t.ToStepID)) queue.Enqueue(t.ToStepID);
            }
            if (seen.Count != steps.Count) return "wf.err.orphan";
            if (!ends.Any(e => seen.Contains(e.StepID))) return "wf.err.noend";

            return null;
        }

        // ---------------------------------------------
        // StartInstance
        // ---------------------------------------------
        public static MdrResult StartInstance(int workflowId, string subject, int actorId, string entityName = null, string entityId = null)
        {
            if (string.IsNullOrWhiteSpace(subject)) return MdrResult.Fail("wf.err.subject");

            try
            {
                using (var db = new MdrContext())
                {
                    var def = db.WorkflowDefinitions.FirstOrDefault(d => d.WorkflowID == workflowId);
                    if (def == null) return MdrResult.Fail("wf.err.notfound");
                    if (def.Status != Statuses.Published) return MdrResult.Fail("wf.err.notpublished");

                    var start = db.WorkflowSteps.First(s => s.WorkflowID == workflowId && s.StepType == "Start");
                    var first = db.WorkflowTransitions
                        .Where(t => t.FromStepID == start.StepID).OrderBy(t => t.TransitionID).FirstOrDefault();
                    if (first == null) return MdrResult.Fail("wf.err.noexit");

                    var inst = new MdrWorkflowInstance
                    {
                        WorkflowID = workflowId,
                        Subject = subject.Trim(),
                        EntityName = entityName,
                        EntityID = entityId,
                        Status = Statuses.Running,
                        StartedBy = actorId,
                        StartedAt = DateTime.Now
                    };
                    db.WorkflowInstances.Add(inst);
                    db.SaveChanges();

                    Log(db, inst.InstanceID, start.StepID, "Start", actorId, null);
                    EnterStep(db, inst, db.WorkflowSteps.First(s => s.StepID == first.ToStepID), actorId);
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "شروع فرایند",
                        entityName: "MDR_WorkflowInstances", entityId: inst.InstanceID, entityTitle: inst.Subject);
                    return MdrResult.Success(inst.InstanceID);
                }
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        // ---------------------------------------------
        // Advance (انجام گام جاری با actionKey: Next/Approve/Reject)
        // مجاز: مسئول گام جاری، یا مدیر با isAdmin=true
        // ---------------------------------------------
        public static MdrResult Advance(long instanceId, string actionKey, int actorId, bool isAdmin, string reason, byte[] rowVer)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var inst = db.WorkflowInstances.FirstOrDefault(i => i.InstanceID == instanceId);
                    if (inst == null) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !inst.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (inst.Status != Statuses.Running || !inst.CurrentStepID.HasValue) return MdrResult.Fail("wf.err.notrunning");

                    var step = db.WorkflowSteps.First(s => s.StepID == inst.CurrentStepID.Value);
                    if (!isAdmin && !(step.AssigneeType == "User" && step.AssigneeRef == actorId))
                        return MdrResult.Fail("wf.err.notassignee");

                    if (actionKey == "Reject" && string.IsNullOrWhiteSpace(reason)) return MdrResult.Fail("wf.err.reason");

                    var tr = db.WorkflowTransitions.FirstOrDefault(t => t.FromStepID == step.StepID && t.ActionKey == actionKey);
                    if (tr == null) return MdrResult.Fail("wf.err.noaction");

                    Log(db, inst.InstanceID, step.StepID, actionKey, actorId, reason);
                    EnterStep(db, inst, db.WorkflowSteps.First(s => s.StepID == tr.ToStepID), actorId);
                    db.SaveChanges();
                    if (inst.Status == Statuses.Completed)
                        RaiseClosed(inst, actionKey == "Reject" ? "Rejected" : "Approved", actorId, reason);

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "اقدام روی گام: " + actionKey,
                        entityName: "MDR_WorkflowInstances", entityId: inst.InstanceID, entityTitle: inst.Subject,
                        newValues: new { actionKey, step = step.StepKey, reason });
                    return MdrResult.Success(inst.InstanceID);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return MdrResult.Fail("wf.err.concurrent");
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        // ---------------------------------------------
        // مداخله‌ی مدیر: Pause / Resume / Terminate (Reason اجباری)
        // ---------------------------------------------
        public static MdrResult Intervene(long instanceId, string action, int actorId, string reason, byte[] rowVer)
        {
            if (string.IsNullOrWhiteSpace(reason)) return MdrResult.Fail("wf.err.reason");

            try
            {
                using (var db = new MdrContext())
                {
                    var inst = db.WorkflowInstances.FirstOrDefault(i => i.InstanceID == instanceId);
                    if (inst == null) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !inst.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");

                    string newStatus;
                    switch (action)
                    {
                        case "Pause":
                            if (inst.Status != Statuses.Running) return MdrResult.Fail("wf.err.notrunning");
                            newStatus = Statuses.Paused; break;
                        case "Resume":
                            if (inst.Status != Statuses.Paused) return MdrResult.Fail("wf.err.notpaused");
                            newStatus = Statuses.Running; break;
                        case "Terminate":
                            if (inst.Status != Statuses.Running && inst.Status != Statuses.Paused) return MdrResult.Fail("wf.err.notrunning");
                            newStatus = Statuses.Terminated; break;
                        default:
                            return MdrResult.Fail("wf.err.noaction");
                    }

                    var old = inst.Status;
                    inst.Status = newStatus;
                    if (newStatus == Statuses.Terminated) inst.CompletedAt = DateTime.Now;

                    Log(db, inst.InstanceID, inst.CurrentStepID, action, actorId, reason);
                    db.SaveChanges();
                    if (newStatus == Statuses.Terminated)
                        RaiseClosed(inst, "Terminated", actorId, reason);

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "مداخله‌ی مدیر: " + action,
                        entityName: "MDR_WorkflowInstances", entityId: inst.InstanceID, entityTitle: inst.Subject,
                        oldValues: new { Status = old }, newValues: new { Status = newStatus, reason });
                    return MdrResult.Success(inst.InstanceID);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return MdrResult.Fail("wf.err.concurrent");
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        // ---------------------------------------------
        // helpers
        // ---------------------------------------------
        private static void EnterStep(MdrContext db, MdrWorkflowInstance inst, MdrWorkflowStep step, int actorId)
        {
            inst.CurrentStepID = step.StepID;
            inst.DueAt = step.SlaHours.HasValue ? DateTime.Now.AddHours(step.SlaHours.Value) : (DateTime?)null;

            if (step.StepType == "End")
            {
                inst.Status = Statuses.Completed;
                inst.CompletedAt = DateTime.Now;
                inst.DueAt = null;
                Log(db, inst.InstanceID, step.StepID, "Complete", actorId, null);
                return;
            }

            Log(db, inst.InstanceID, step.StepID, "Enter", actorId, null);

            if (step.AssigneeType == "User" && step.AssigneeRef.HasValue)
            {
                NotificationService.Send(
                    step.AssigneeRef.Value,
                    "کار جدید: " + step.Name,
                    inst.Subject,
                    severity: NotificationService.Severities.Info,
                    moduleName: Module,
                    entityName: "MDR_WorkflowInstances",
                    entityId: inst.InstanceID,
                    linkUrl: "/Mdr/Workflow/Monitor?id=" + inst.InstanceID);
            }
        }

        private static void Log(MdrContext db, long instanceId, int? stepId, string actionKey, int? actorId, string reason)
        {
            db.WorkflowHistory.Add(new MdrWorkflowHistory
            {
                InstanceID = instanceId,
                StepID = stepId,
                ActionKey = actionKey,
                ActorUserID = actorId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : (reason.Length > 500 ? reason.Substring(0, 500) : reason),
                CorrelationID = Guid.NewGuid(),
                CreatedAt = DateTime.Now
            });
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای موتور گردش کار",
                newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
