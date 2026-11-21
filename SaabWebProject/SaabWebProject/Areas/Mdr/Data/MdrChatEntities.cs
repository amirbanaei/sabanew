using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 6 | موجودیت‌های چت داخلی (DDL: MDR_12.1.1_Chat.sql)

    [Table("MDR_ChatThreads")]
    public class MdrChatThread
    {
        [Key] public long ThreadID { get; set; }
        [Required, MaxLength(10)] public string Kind { get; set; }
        [MaxLength(200)] public string Title { get; set; }
        [MaxLength(10)] public string EntityType { get; set; }
        public long? EntityID { get; set; }
        [MaxLength(30)] public string DirectKey { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public bool IsArchived { get; set; }
    }

    [Table("MDR_ChatMembers")]
    public class MdrChatMember
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public long ThreadID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int UserID { get; set; }
        [Required, MaxLength(10)] public string Role { get; set; }
        public DateTime JoinedAt { get; set; }
        public long? LastReadMessageID { get; set; }
        public bool IsMuted { get; set; }
        public bool IsActive { get; set; }
    }

    [Table("MDR_ChatMessages")]
    public class MdrChatMessage
    {
        [Key] public long MessageID { get; set; }
        public long ThreadID { get; set; }
        public int SenderUserID { get; set; }
        [Required, MaxLength(4000)] public string Body { get; set; }
        public long? ReplyToMessageID { get; set; }
        public long? ForwardedFromMessageID { get; set; }
        public bool HasAttachment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EditedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedBy { get; set; }
    }

    [Table("MDR_ChatMentions")]
    public class MdrChatMention
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public long MessageID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int UserID { get; set; }
    }
}
