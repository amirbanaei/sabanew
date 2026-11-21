using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 5 | موجودیت‌های اسناد و مستندسازی (DDL: MDR_8.1.1_Documents.sql)

    [Table("MDR_DocFolders")]
    public class MdrDocFolder
    {
        [Key] public int FolderID { get; set; }
        public int? ParentFolderID { get; set; }
        [Required, MaxLength(150)] public string Name { get; set; }
        public int OwnerUserID { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_Documents")]
    public class MdrDocument
    {
        [Key] public long DocumentID { get; set; }
        public int? FolderID { get; set; }
        [Required, MaxLength(300)] public string Title { get; set; }
        [MaxLength(1000)] public string Description { get; set; }
        [Required, MaxLength(12)] public string Classification { get; set; }   // Normal | Confidential | Secret
        public int OwnerUserID { get; set; }
        public Guid? FileGuid { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }           // Active | Archived
        public DateTime CreatedAt { get; set; }
        public DateTime? ArchivedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_DocumentTags")]
    public class MdrDocumentTag
    {
        [Key, Column(Order = 0)] public long DocumentID { get; set; }
        [Key, Column(Order = 1), MaxLength(50)] public string Tag { get; set; }
    }

    [Table("MDR_DocumentShares")]
    public class MdrDocumentShare
    {
        [Key] public long ShareID { get; set; }
        public long DocumentID { get; set; }
        public int UserID { get; set; }
        [Required, MaxLength(6)] public string Permission { get; set; }        // View | Edit
        public int SharedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_DocumentLinks")]
    public class MdrDocumentLink
    {
        [Key] public long LinkID { get; set; }
        public long DocumentID { get; set; }
        [Required, MaxLength(30)] public string EntityName { get; set; }
        [Required, MaxLength(30)] public string EntityID { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_DocumentHistory")]
    public class MdrDocumentHistory
    {
        [Key] public long HistoryID { get; set; }
        public long DocumentID { get; set; }
        [Required, MaxLength(30)] public string ActionKey { get; set; }
        public int? ActorUserID { get; set; }
        [MaxLength(500)] public string Detail { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
