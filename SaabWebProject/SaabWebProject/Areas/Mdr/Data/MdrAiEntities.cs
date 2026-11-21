using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 8 | هوش مصنوعی سازمانی (DDL: MDR_16.1.1_AI.sql)

    [Table("MDR_AiQueries")]
    public class MdrAiQuery
    {
        [Key] public long QueryID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(500)] public string Question { get; set; }
        [Required, MaxLength(12)] public string Outcome { get; set; }
        public byte Confidence { get; set; }
        public int SourceCount { get; set; }
        [Required, MaxLength(30)] public string Provider { get; set; }
        public short? Rating { get; set; }
        [MaxLength(300)] public string FeedbackNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    [Table("MDR_AiQuerySources")]
    public class MdrAiQuerySource
    {
        [Key, Column(Order = 0)] public long QueryID { get; set; }
        [Key, Column(Order = 1)] public byte Pos { get; set; }
        [Required, MaxLength(10)] public string SourceKey { get; set; }
        public long EntityID { get; set; }
    }

    [Table("MDR_AiSuggestions")]
    public class MdrAiSuggestion
    {
        [Key] public long SuggestionID { get; set; }
        public int UserID { get; set; }
        public long? QueryID { get; set; }
        [Required, MaxLength(10)] public string Kind { get; set; }
        [MaxLength(10)] public string SourceKey { get; set; }
        public long? EntityID { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; }
        [Column(TypeName = "date")] public DateTime? DueDate { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        public long? ResultTaskID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
    }
}
