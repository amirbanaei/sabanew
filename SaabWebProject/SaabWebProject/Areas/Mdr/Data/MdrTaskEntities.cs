using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 4 | موجودیت‌های مدیریت تسک (DDL: MDR_7.1.1_Tasks.sql)

    [Table("MDR_Tasks")]
    public class MdrTask
    {
        [Key] public long TaskID { get; set; }
        public long? ParentTaskID { get; set; }
        [Required, MaxLength(300)] public string Title { get; set; }
        public string Description { get; set; }
        [Required, MaxLength(20)] public string Status { get; set; }
        [Required, MaxLength(10)] public string Priority { get; set; }
        public int? AssigneeUserID { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? DueAt { get; set; }
        [MaxLength(30)] public string SourceType { get; set; }
        [MaxLength(50)] public string SourceRef { get; set; }
        [MaxLength(300)] public string SourceUrl { get; set; }
        [MaxLength(1000)] public string ResultText { get; set; }
        [MaxLength(500)] public string BlockedReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_TaskComments")]
    public class MdrTaskComment
    {
        [Key] public long CommentID { get; set; }
        public long TaskID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(1000)] public string Body { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_TaskHistory")]
    public class MdrTaskHistory
    {
        [Key] public long HistoryID { get; set; }
        public long TaskID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Detail { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
