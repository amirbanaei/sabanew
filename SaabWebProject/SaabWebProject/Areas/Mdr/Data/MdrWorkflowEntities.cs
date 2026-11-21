using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 2 | موجودیت‌های موتور گردش کار (DDL: MDR_2.1.1_Workflow.sql)

    [Table("MDR_WorkflowDefinitions")]
    public class MdrWorkflowDefinition
    {
        [Key] public int WorkflowID { get; set; }
        [Required, MaxLength(50)] public string Code { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; }
        public int Version { get; set; }
        [Required, MaxLength(20)] public string Status { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
    }

    [Table("MDR_WorkflowSteps")]
    public class MdrWorkflowStep
    {
        [Key] public int StepID { get; set; }
        public int WorkflowID { get; set; }
        [Required, MaxLength(50)] public string StepKey { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; }
        [Required, MaxLength(20)] public string StepType { get; set; }
        [MaxLength(20)] public string AssigneeType { get; set; }
        public int? AssigneeRef { get; set; }
        public int? SlaHours { get; set; }
        public int SortOrder { get; set; }
    }

    [Table("MDR_WorkflowTransitions")]
    public class MdrWorkflowTransition
    {
        [Key] public int TransitionID { get; set; }
        public int WorkflowID { get; set; }
        public int FromStepID { get; set; }
        public int ToStepID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
    }

    [Table("MDR_WorkflowInstances")]
    public class MdrWorkflowInstance
    {
        [Key] public long InstanceID { get; set; }
        public int WorkflowID { get; set; }
        [Required, MaxLength(300)] public string Subject { get; set; }
        [MaxLength(100)] public string EntityName { get; set; }
        [MaxLength(50)] public string EntityID { get; set; }
        [Required, MaxLength(20)] public string Status { get; set; }
        public int? CurrentStepID { get; set; }
        public int? StartedBy { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? DueAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_WorkflowHistory")]
    public class MdrWorkflowHistory
    {
        [Key] public long HistoryID { get; set; }
        public long InstanceID { get; set; }
        public int? StepID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Reason { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
