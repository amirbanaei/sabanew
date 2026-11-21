using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 8 | آموزش و ماتریس مهارت (DDL: MDR_15.1.1_Training.sql)

    [Table("MDR_Skills")]
    public class MdrSkill
    {
        [Key] public int SkillID { get; set; }
        [Required, MaxLength(150)] public string Name { get; set; }
        [MaxLength(60)] public string Category { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_Courses")]
    public class MdrCourse
    {
        [Key] public int CourseID { get; set; }
        [Required, MaxLength(30)] public string Code { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; }
        [MaxLength(1000)] public string Description { get; set; }
        public int? SkillID { get; set; }
        public byte? TargetLevel { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public int Capacity { get; set; }
        public int? PrereqCourseID { get; set; }
        [MaxLength(150)] public string InstructorName { get; set; }
        [MaxLength(200)] public string Location { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_CourseEnrollments")]
    public class MdrCourseEnrollment
    {
        [Key] public long EnrollmentID { get; set; }
        public int CourseID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        public DateTime EnrolledAt { get; set; }
        public int EnrolledBy { get; set; }
        public DateTime? CancelledAt { get; set; }
        [MaxLength(300)] public string CancelReason { get; set; }
        public decimal? Score { get; set; }
        [MaxLength(40)] public string CertificateNo { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    [Table("MDR_CourseAttendance")]
    public class MdrCourseAttendance
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public int CourseID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int UserID { get; set; }
        [Key, Column(Order = 2, TypeName = "date"), DatabaseGenerated(DatabaseGeneratedOption.None)] public DateTime SessionDate { get; set; }
        public bool Present { get; set; }
        public int MarkedBy { get; set; }
        public DateTime MarkedAt { get; set; }
    }

    [Table("MDR_SkillProfiles")]
    public class MdrSkillProfile
    {
        [Key] public int ProfileID { get; set; }
        [Required, MaxLength(150)] public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_ProfileSkills")]
    public class MdrProfileSkill
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public int ProfileID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int SkillID { get; set; }
        public byte RequiredLevel { get; set; }
    }

    [Table("MDR_UserProfiles")]
    public class MdrUserProfile
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public int UserID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int ProfileID { get; set; }
        public int AssignedBy { get; set; }
        public DateTime AssignedAt { get; set; }
    }

    [Table("MDR_UserSkills")]
    public class MdrUserSkill
    {
        [Key, Column(Order = 0), DatabaseGenerated(DatabaseGeneratedOption.None)] public int UserID { get; set; }
        [Key, Column(Order = 1), DatabaseGenerated(DatabaseGeneratedOption.None)] public int SkillID { get; set; }
        [Key, Column(Order = 2), MaxLength(10), DatabaseGenerated(DatabaseGeneratedOption.None)] public string Source { get; set; }
        public byte Level { get; set; }
        public int AssessedBy { get; set; }
        public DateTime AssessedAt { get; set; }
        [MaxLength(300)] public string Note { get; set; }
    }
}
