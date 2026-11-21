using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 6 | جستجوهای ذخیره‌شده (DDL: MDR_10.1.1_Search.sql)
    [Table("MDR_SavedSearches")]
    public class MdrSavedSearch
    {
        [Key] public long SearchID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; }
        [Required, MaxLength(100)] public string QueryText { get; set; }
        [MaxLength(10)] public string Source { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
