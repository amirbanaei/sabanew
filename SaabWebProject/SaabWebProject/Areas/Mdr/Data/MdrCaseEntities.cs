using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 5 | موجودیت‌های پرونده‌ی الکترونیکی (DDL: MDR_9.3.1_CaseFiles.sql)

    [Table("MDR_Cases")]
    public class MdrCase
    {
        [Key] public long CaseID { get; set; }
        [MaxLength(30)] public string Number { get; set; }
        [Required, MaxLength(300)] public string Title { get; set; }
        [MaxLength(2000)] public string Description { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        [Required, MaxLength(12)] public string Confidentiality { get; set; }
        public int OwnerUserID { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        [MaxLength(500)] public string CloseReason { get; set; }
        public long? MergedIntoCaseID { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_CaseMembers")]
    public class MdrCaseMember
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public long CaseID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int UserID { get; set; }
        [Required, MaxLength(10)] public string Role { get; set; }
        public int AddedBy { get; set; }
        public DateTime AddedAt { get; set; }
    }

    [Table("MDR_CaseLinks")]
    public class MdrCaseLink
    {
        [Key] public long LinkID { get; set; }
        public long CaseID { get; set; }
        [Required, MaxLength(10)] public string EntityType { get; set; }
        public long EntityID { get; set; }
        [MaxLength(300)] public string Note { get; set; }
        public int LinkedBy { get; set; }
        public DateTime LinkedAt { get; set; }
    }

    [Table("MDR_CaseNotes")]
    public class MdrCaseNote
    {
        [Key] public long NoteID { get; set; }
        public long CaseID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(2000)] public string Body { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_CaseHistory")]
    public class MdrCaseHistory
    {
        [Key] public long HistoryID { get; set; }
        public long CaseID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Detail { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
