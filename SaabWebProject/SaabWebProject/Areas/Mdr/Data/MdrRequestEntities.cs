using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 4 | موجودیت‌های درخواست‌های سازمانی (DDL: MDR_6.1.1_Requests.sql)

    [Table("MDR_RequestTypes")]
    public class MdrRequestType
    {
        [Key] public int TypeID { get; set; }
        [Required, MaxLength(50)] public string Code { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; }
        [MaxLength(1000)] public string Description { get; set; }
        [MaxLength(50)] public string WorkflowCode { get; set; }
        [Required] public string FieldsJson { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_Requests")]
    public class MdrRequest
    {
        [Key] public long RequestID { get; set; }
        public int TypeID { get; set; }
        [MaxLength(30)] public string Number { get; set; }
        [Required, MaxLength(300)] public string Title { get; set; }
        [Required] public string DataJson { get; set; }
        [Required, MaxLength(20)] public string Status { get; set; }
        public int RequesterUserID { get; set; }
        public long? WorkflowInstanceID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_RequestHistory")]
    public class MdrRequestHistory
    {
        [Key] public long HistoryID { get; set; }
        public long RequestID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Detail { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
