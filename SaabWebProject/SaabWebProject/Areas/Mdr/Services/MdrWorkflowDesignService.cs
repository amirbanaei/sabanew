using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 2 | طراحی گردش کار (ساخت، Clone، ذخیره‌ی Draft)
    // - فقط نسخه‌ی Draft قابل ویرایش است؛ نسخه‌ی منتشرشده تغییر نمی‌کند
    //   (برای تغییر: Clone → نسخه‌ی جدید Draft).
    // - ذخیره = جایگزینی کامل گام‌ها و مسیرها در یک Transaction.
    // - Validate بلوکه نمی‌کند؛ هشدار برمی‌گرداند و Publish سخت‌گیر است.
    // =====================================================

    public class MdrStepDto
    {
        public string StepKey { get; set; }
        public string Name { get; set; }
        public string StepType { get; set; }
        public int? AssigneeUserId { get; set; }
        public int? SlaHours { get; set; }
    }

    public class MdrTransitionDto
    {
        public string From { get; set; }
        public string To { get; set; }
        public string ActionKey { get; set; }
    }

    public class MdrDefinitionDto
    {
        public int WorkflowID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public List<MdrStepDto> Steps { get; set; }
        public List<MdrTransitionDto> Transitions { get; set; }
    }

    public static class MdrWorkflowDesignService
    {
        private const string Module = "Workflow";
        private static readonly string[] StepTypes = { "Start", "Task", "Approval", "Gateway", "Timer", "End" };
        private static readonly string[] ActionKeys = { "Next", "Approve", "Reject" };
        private static readonly Regex CodeRx = new Regex(@"^[A-Za-z0-9_\-]{2,50}$", RegexOptions.Compiled);
        private static readonly Regex KeyRx = new Regex(@"^[A-Za-z0-9_\-]{1,50}$", RegexOptions.Compiled);

        public static MdrDefinitionDto Get(int workflowId)
        {
            using (var db = new MdrContext())
            {
                var def = db.WorkflowDefinitions.FirstOrDefault(d => d.WorkflowID == workflowId);
                if (def == null) return null;

                var steps = db.WorkflowSteps.Where(s => s.WorkflowID == workflowId).OrderBy(s => s.SortOrder).ToList();
                var keyById = steps.ToDictionary(s => s.StepID, s => s.StepKey);
                var trans = db.WorkflowTransitions.Where(t => t.WorkflowID == workflowId).OrderBy(t => t.TransitionID).ToList();

                return new MdrDefinitionDto
                {
                    WorkflowID = def.WorkflowID, Code = def.Code, Name = def.Name, Version = def.Version, Status = def.Status,
                    Steps = steps.Select(s => new MdrStepDto
                    {
                        StepKey = s.StepKey, Name = s.Name, StepType = s.StepType,
                        AssigneeUserId = s.AssigneeType == "User" ? s.AssigneeRef : null, SlaHours = s.SlaHours
                    }).ToList(),
                    Transitions = trans.Select(t => new MdrTransitionDto
                    {
                        From = keyById[t.FromStepID], To = keyById[t.ToStepID], ActionKey = t.ActionKey
                    }).ToList()
                };
            }
        }

        // تعریف جدید: Draft نسخه‌ی ۱ با گام‌های Start و End
        public static MdrResult Create(string code, string name, int actorId)
        {
            code = (code ?? "").Trim(); name = (name ?? "").Trim();
            if (!CodeRx.IsMatch(code)) return MdrResult.Fail("wf.err.code");
            if (name.Length == 0 || name.Length > 200) return MdrResult.Fail("wf.err.name");

            try
            {
                using (var db = new MdrContext())
                {
                    if (db.WorkflowDefinitions.Any(d => d.Code == code)) return MdrResult.Fail("wf.err.codeexists");

                    var def = new MdrWorkflowDefinition
                    {
                        Code = code, Name = name, Version = 1, Status = MdrWorkflowService.Statuses.Draft,
                        CreatedBy = actorId, CreatedAt = DateTime.Now
                    };
                    db.WorkflowDefinitions.Add(def);
                    db.SaveChanges();

                    db.WorkflowSteps.Add(new MdrWorkflowStep { WorkflowID = def.WorkflowID, StepKey = "start", Name = "شروع", StepType = "Start", SortOrder = 1 });
                    db.WorkflowSteps.Add(new MdrWorkflowStep { WorkflowID = def.WorkflowID, StepKey = "end", Name = "پایان", StepType = "End", SortOrder = 2 });
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد تعریف گردش کار",
                        entityName: "MDR_WorkflowDefinitions", entityId: def.WorkflowID, entityTitle: def.Name, newValues: new { def.Code });
                    return MdrResult.Success(def.WorkflowID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // نسخه‌ی جدید Draft از یک تعریف موجود
        public static MdrResult Clone(int sourceId, int actorId)
        {
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction())
                {
                    var src = db.WorkflowDefinitions.FirstOrDefault(d => d.WorkflowID == sourceId);
                    if (src == null) return MdrResult.Fail("wf.err.notfound");
                    if (db.WorkflowDefinitions.Any(d => d.Code == src.Code && d.Status == MdrWorkflowService.Statuses.Draft))
                        return MdrResult.Fail("wf.err.draftexists");

                    var version = db.WorkflowDefinitions.Where(d => d.Code == src.Code).Max(d => d.Version) + 1;
                    var def = new MdrWorkflowDefinition
                    {
                        Code = src.Code, Name = src.Name, Version = version, Status = MdrWorkflowService.Statuses.Draft,
                        CreatedBy = actorId, CreatedAt = DateTime.Now
                    };
                    db.WorkflowDefinitions.Add(def);
                    db.SaveChanges();

                    var map = new Dictionary<int, MdrWorkflowStep>();
                    foreach (var s in db.WorkflowSteps.Where(s => s.WorkflowID == sourceId).OrderBy(s => s.SortOrder).ToList())
                    {
                        var copy = new MdrWorkflowStep
                        {
                            WorkflowID = def.WorkflowID, StepKey = s.StepKey, Name = s.Name, StepType = s.StepType,
                            AssigneeType = s.AssigneeType, AssigneeRef = s.AssigneeRef, SlaHours = s.SlaHours, SortOrder = s.SortOrder
                        };
                        map[s.StepID] = copy;
                        db.WorkflowSteps.Add(copy);
                    }
                    db.SaveChanges();

                    foreach (var t in db.WorkflowTransitions.Where(t => t.WorkflowID == sourceId).ToList())
                        db.WorkflowTransitions.Add(new MdrWorkflowTransition
                        {
                            WorkflowID = def.WorkflowID, FromStepID = map[t.FromStepID].StepID,
                            ToStepID = map[t.ToStepID].StepID, ActionKey = t.ActionKey
                        });
                    db.SaveChanges();
                    tx.Commit();

                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "نسخه‌ی جدید گردش کار (Clone)",
                        entityName: "MDR_WorkflowDefinitions", entityId: def.WorkflowID, entityTitle: def.Name,
                        newValues: new { def.Code, def.Version, sourceId });
                    return MdrResult.Success(def.WorkflowID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ذخیره‌ی Draft (جایگزینی کامل). Warning = نتیجه‌ی اعتبارسنجی ساختاری (یا null)
        public static MdrResult Save(int workflowId, MdrDefinitionDto dto, int actorId)
        {
            if (dto == null) return MdrResult.Fail("wf.err.badinput");
            var name = (dto.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 200) return MdrResult.Fail("wf.err.name");

            var steps = dto.Steps ?? new List<MdrStepDto>();
            var trans = dto.Transitions ?? new List<MdrTransitionDto>();

            if (steps.Count == 0 || steps.Count > 100 || trans.Count > 300) return MdrResult.Fail("wf.err.badinput");
            if (steps.Any(s => s == null || !KeyRx.IsMatch(s.StepKey ?? "") || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 200
                               || !StepTypes.Contains(s.StepType) || (s.SlaHours.HasValue && (s.SlaHours < 1 || s.SlaHours > 8760))))
                return MdrResult.Fail("wf.err.badstep");
            if (steps.Select(s => s.StepKey).Distinct().Count() != steps.Count) return MdrResult.Fail("wf.err.dupkey");

            var keys = new HashSet<string>(steps.Select(s => s.StepKey));
            if (trans.Any(t => t == null || !keys.Contains(t.From ?? "") || !keys.Contains(t.To ?? "") || !ActionKeys.Contains(t.ActionKey)))
                return MdrResult.Fail("wf.err.badtransition");
            if (trans.GroupBy(t => t.From + "|" + t.ActionKey).Any(g => g.Count() > 1)) return MdrResult.Fail("wf.err.duptransition");

            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction())
                {
                    var def = db.WorkflowDefinitions.FirstOrDefault(d => d.WorkflowID == workflowId);
                    if (def == null) return MdrResult.Fail("wf.err.notfound");
                    if (def.Status != MdrWorkflowService.Statuses.Draft) return MdrResult.Fail("wf.err.notdraft");

                    var assignees = steps.Where(s => s.AssigneeUserId.HasValue).Select(s => s.AssigneeUserId.Value).Distinct().ToList();
                    foreach (var uid in assignees)
                        if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0", uid).First() == 0)
                            return MdrResult.Fail("wf.err.baduser");

                    db.WorkflowTransitions.RemoveRange(db.WorkflowTransitions.Where(t => t.WorkflowID == workflowId));
                    db.SaveChanges();
                    db.WorkflowSteps.RemoveRange(db.WorkflowSteps.Where(s => s.WorkflowID == workflowId));
                    db.SaveChanges();

                    def.Name = name;

                    var byKey = new Dictionary<string, MdrWorkflowStep>();
                    var order = 1;
                    foreach (var s in steps)
                    {
                        var e = new MdrWorkflowStep
                        {
                            WorkflowID = workflowId, StepKey = s.StepKey, Name = s.Name.Trim(), StepType = s.StepType,
                            AssigneeType = s.AssigneeUserId.HasValue ? "User" : null, AssigneeRef = s.AssigneeUserId,
                            SlaHours = s.SlaHours, SortOrder = order++
                        };
                        byKey[s.StepKey] = e;
                        db.WorkflowSteps.Add(e);
                    }
                    db.SaveChanges();

                    foreach (var t in trans)
                        db.WorkflowTransitions.Add(new MdrWorkflowTransition
                        {
                            WorkflowID = workflowId, FromStepID = byKey[t.From].StepID, ToStepID = byKey[t.To].StepID, ActionKey = t.ActionKey
                        });
                    db.SaveChanges();
                    tx.Commit();

                    var warning = MdrWorkflowService.Validate(
                        db.WorkflowSteps.Where(s => s.WorkflowID == workflowId).ToList(),
                        db.WorkflowTransitions.Where(t => t.WorkflowID == workflowId).ToList());

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ذخیره‌ی طراحی گردش کار",
                        entityName: "MDR_WorkflowDefinitions", entityId: workflowId, entityTitle: name,
                        newValues: new { steps = steps.Count, transitions = trans.Count, warning });

                    var ok = MdrResult.Success(workflowId);
                    ok.Warning = warning;
                    return ok;
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای طراحی گردش کار",
                newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
