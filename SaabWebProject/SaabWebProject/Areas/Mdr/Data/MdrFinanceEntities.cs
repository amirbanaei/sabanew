using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 7 | موجودیت‌های مالی (DDL: MDR_13.1.1_Finance.sql)

    [Table("MDR_FinDocuments")]
    public class MdrFinDocument
    {
        [Key] public long DocumentID { get; set; }
        [MaxLength(30)] public string Number { get; set; }
        [Required, MaxLength(10)] public string Direction { get; set; }
        [Required, MaxLength(300)] public string Title { get; set; }
        public long Amount { get; set; }
        [Column(TypeName = "date")] public DateTime DocDate { get; set; }
        public int? CostCenterID { get; set; }
        public int? PeymanID { get; set; }
        [MaxLength(200)] public string Payee { get; set; }
        [MaxLength(50)] public string ReferenceNo { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        public int? CurrentLevel { get; set; }
        public int RequesterUserID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public int? PaidBy { get; set; }
        [MaxLength(50)] public string PaymentRef { get; set; }
        [MaxLength(500)] public string ReturnReason { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_FinApprovers")]
    public class MdrFinApprover
    {
        [Key] public int ApproverID { get; set; }
        public int? CostCenterID { get; set; }
        public int Level { get; set; }
        public int UserID { get; set; }
        public long? MinAmount { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_FinApprovals")]
    public class MdrFinApproval
    {
        [Key] public long ApprovalID { get; set; }
        public long DocumentID { get; set; }
        public int Level { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        public int? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
        [MaxLength(500)] public string Comment { get; set; }
    }

    [Table("MDR_FinHistory")]
    public class MdrFinHistory
    {
        [Key] public long HistoryID { get; set; }
        public long DocumentID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Detail { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_FinBudgets")]
    public class MdrFinBudget
    {
        [Key] public int BudgetID { get; set; }
        public int CostCenterID { get; set; }
        public int FiscalYear { get; set; }
        public long Amount { get; set; }
        public int UpdatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    [Table("MDR_FinPeriods")]
    public class MdrFinPeriod
    {
        [Key] public int PeriodID { get; set; }
        public int FiscalYear { get; set; }
        public int Month { get; set; }
        public DateTime ClosedAt { get; set; }
        public int ClosedBy { get; set; }
        [MaxLength(300)] public string Reason { get; set; }
    }
}
