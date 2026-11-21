using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 6 | زمان‌بندی گزارش دوره‌ای (DDL: MDR_11.1.1_Reports.sql)
    [Table("MDR_ReportSchedules")]
    public class MdrReportSchedule
    {
        [Key] public long ScheduleID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(20)] public string ReportKey { get; set; }
        public bool Personal { get; set; }
        [Required, MaxLength(10)] public string Frequency { get; set; }
        public int RangeDays { get; set; }
        public DateTime NextRunAt { get; set; }
        public DateTime? LastRunAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
