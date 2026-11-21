using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 4 | موجودیت‌های مکاتبات و دبیرخانه (DDL: MDR_5.1.1_Letters.sql)

    [Table("MDR_Letters")]
    public class MdrLetter
    {
        [Key] public long LetterID { get; set; }
        [Required, MaxLength(10)] public string Direction { get; set; }      // Incoming | Outgoing | Internal
        [MaxLength(40)] public string Number { get; set; }
        public int? SeqYear { get; set; }
        public int? SeqNo { get; set; }
        [Required, MaxLength(300)] public string Subject { get; set; }
        public string Body { get; set; }
        [Required, MaxLength(12)] public string Status { get; set; }         // Draft | Registered | Archived | Cancelled
        [Required, MaxLength(10)] public string Urgency { get; set; }        // Normal | Urgent | Immediate
        [Required, MaxLength(12)] public string Confidentiality { get; set; } // Normal | Confidential | Secret
        [MaxLength(200)] public string ExternalParty { get; set; }
        public DateTime? DueAt { get; set; }
        public long? ParentLetterID { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? RegisteredAt { get; set; }
        public DateTime? ArchivedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_LetterRecipients")]
    public class MdrLetterRecipient
    {
        [Key] public long RecipientID { get; set; }
        public long LetterID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(10)] public string Kind { get; set; }           // To | Cc | Referral
        public int? FromUserID { get; set; }
        [MaxLength(500)] public string Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime? RepliedAt { get; set; }
    }

    [Table("MDR_LetterHistory")]
    public class MdrLetterHistory
    {
        [Key] public long HistoryID { get; set; }
        public long LetterID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Detail { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
