using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // =====================================================
    // MDR | زیرساخت مشترک (Code-First، بدون وابستگی به DomainModel.edmx)
    // پنج جدول MDR_* زیرساخت + خلاصه‌ی (فقط‌خواندنی) سه جدول قدیمی سامانه که دسترسی/کاربر/عنوان شغلی از آن‌ها خوانده می‌شود.
    // DDL فقط در MDR_SQL است؛ این کلاس‌ها هیچ‌وقت جدولی نمی‌سازند.
    // نام کلاس‌ها عمداً با نام جدول یکی است (MDR_Files …) تا کد زیرساخت بدون تغییر منطق به MdrContext منتقل شود.
    // =====================================================

    [Table("MDR_OrganizationUnits")]
    public class MdrOrganizationUnit
    {
        [Key] public int OrganizationUnitID { get; set; }
        public string UnitName { get; set; }
        public string UnitType { get; set; }
        public int? ParentID { get; set; }
        public int? ManagerUserID { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_AuditLogs")]
    public class MdrAuditLog
    {
        [Key] public long AuditLogID { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PersianDate { get; set; }
        public int? UserID { get; set; }
        public string UserFullName { get; set; }
        public string ActionType { get; set; }
        public string ModuleName { get; set; }
        public string Description { get; set; }
        public string EntityName { get; set; }
        public string EntityID { get; set; }
        public string EntityTitle { get; set; }
        public string OldValues { get; set; }
        public string NewValues { get; set; }
        public string IPAddress { get; set; }
        public string UserAgent { get; set; }
        public string PageUrl { get; set; }
        public bool IsSuccess { get; set; }
    }

    [Table("MDR_Files")]
    public class MdrFile
    {
        public MdrFile() { MDR_FileVersions = new HashSet<MdrFileVersion>(); }

        [Key] public long FileID { get; set; }
        public Guid FileGuid { get; set; }
        public string ModuleName { get; set; }
        public string EntityName { get; set; }
        public string EntityID { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int CurrentVersionNo { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PersianDate { get; set; }
        public int? CreatedByUserID { get; set; }
        public string CreatedByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedByUserID { get; set; }
        public string UpdatedByName { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int? DeletedByUserID { get; set; }
        public string DeletedByName { get; set; }

        public virtual ICollection<MdrFileVersion> MDR_FileVersions { get; set; }
    }

    [Table("MDR_FileVersions")]
    public class MdrFileVersion
    {
        [Key] public long FileVersionID { get; set; }
        public long FileID { get; set; }
        public int VersionNo { get; set; }
        public string OriginalFileName { get; set; }
        public string Extension { get; set; }
        public string ContentType { get; set; }
        public long FileSize { get; set; }
        public string StoredFileName { get; set; }
        public string RelativePath { get; set; }
        public string FileHash { get; set; }
        public string ChangeNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PersianDate { get; set; }
        public int? UploadedByUserID { get; set; }
        public string UploadedByName { get; set; }

        [ForeignKey("FileID")] public virtual MdrFile MDR_Files { get; set; }
    }

    [Table("MDR_Notifications")]
    public class MdrNotification
    {
        [Key] public long NotificationID { get; set; }
        public int RecipientUserID { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public bool IsImportant { get; set; }
        public string ModuleName { get; set; }
        public string EntityName { get; set; }
        public string EntityID { get; set; }
        public string LinkUrl { get; set; }
        public int? SenderUserID { get; set; }
        public string SenderName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PersianDate { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? SnoozedUntil { get; set; }
        public bool IsArchived { get; set; }
    }

    // ---------- جدول‌های قدیمی سامانه (فقط خواندن؛ فقط ستون‌های لازم) ----------
    [Table("tbUsers", Schema = "Usser")]
    public class MdrUser
    {
        [Key] public int usr_ID { get; set; }
        public string usr_Name { get; set; }
        public string usr_Family { get; set; }
        public string usr_NationalCode { get; set; }
        public string usr_Picture { get; set; }
        public bool? usr_IsActive { get; set; }
        public int? usr_job { get; set; }
    }

    [Table("tbdetailjob", Schema = "User")]
    public class MdrDetailJob
    {
        [Key] public int ID { get; set; }
        public string detail_title { get; set; }
    }

    [Table("tbPages", Schema = "Security")]
    public class MdrPage
    {
        [Key] public int PageID { get; set; }
        public string PageCode { get; set; }
        public bool IsActive { get; set; }
    }

    [Table("tbUserPageAccess", Schema = "Security")]
    public class MdrUserPageAccess
    {
        [Key] public int ID { get; set; }
        public int UserID { get; set; }
        public int PageID { get; set; }
        public bool CanView { get; set; }
        public bool CanInsert { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }

        [ForeignKey("PageID")] public virtual MdrPage tbPages { get; set; }
    }
}
