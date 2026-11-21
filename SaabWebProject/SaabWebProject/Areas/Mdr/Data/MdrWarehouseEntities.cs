using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaabWebProject.Areas.Mdr.Data
{
    // MDR | Phase 7 | انبار و اموال (DDL: MDR_14.1.1_WarehouseAssets.sql)

    [Table("MDR_WhStores")]
    public class MdrWhStore
    {
        [Key] public int StoreID { get; set; }
        [Required, MaxLength(150)] public string Name { get; set; }
        public int? ManagerUserID { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_WhItems")]
    public class MdrWhItem
    {
        [Key] public int ItemID { get; set; }
        [Required, MaxLength(40)] public string Code { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; }
        [Required, MaxLength(20)] public string Unit { get; set; }
        public decimal? ReorderPoint { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_WhMovements")]
    public class MdrWhMovement
    {
        [Key] public long MovementID { get; set; }
        [MaxLength(30)] public string DocNo { get; set; }
        [Required, MaxLength(12)] public string Kind { get; set; }
        public int ItemID { get; set; }
        public int StoreID { get; set; }
        public decimal Qty { get; set; }
        public Guid? PairKey { get; set; }
        public long? RequestID { get; set; }
        [MaxLength(100)] public string Reference { get; set; }
        [MaxLength(500)] public string Note { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [Table("MDR_WhRequests")]
    public class MdrWhRequest
    {
        [Key] public long RequestID { get; set; }
        [MaxLength(30)] public string Number { get; set; }
        public int RequesterUserID { get; set; }
        public int StoreID { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        [MaxLength(500)] public string Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public int? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
        [MaxLength(500)] public string DecisionNote { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_WhRequestLines")]
    public class MdrWhRequestLine
    {
        [Key] public long LineID { get; set; }
        public long RequestID { get; set; }
        public int ItemID { get; set; }
        public decimal Qty { get; set; }
    }

    [Table("MDR_Assets")]
    public class MdrAsset
    {
        [Key] public long AssetID { get; set; }
        [Required, MaxLength(40)] public string Tag { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; }
        [MaxLength(60)] public string Category { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; }
        public int? HolderUserID { get; set; }
        [MaxLength(200)] public string Location { get; set; }
        [Column(TypeName = "date")] public DateTime? AcquiredAt { get; set; }
        public long? Cost { get; set; }
        [MaxLength(500)] public string Note { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        [Timestamp] public byte[] RowVer { get; set; }
    }

    [Table("MDR_AssetEvents")]
    public class MdrAssetEvent
    {
        [Key] public long EventID { get; set; }
        public long AssetID { get; set; }
        [Required, MaxLength(12)] public string EventType { get; set; }
        public int? FromUserID { get; set; }
        public int? ToUserID { get; set; }
        [MaxLength(200)] public string FromLocation { get; set; }
        [MaxLength(200)] public string ToLocation { get; set; }
        [MaxLength(500)] public string Reason { get; set; }
        public int ActorUserID { get; set; }
        public Guid CorrelationID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
