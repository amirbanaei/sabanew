using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 7 | اموال — خواندن
    // مدیر اموال همه را می‌بیند؛ هر کاربر دیگر فقط دارایی‌ای که نزد اوست یا قبلاً نزد او بوده (رویداد دارد).
    // مبلغ خرید فقط برای مدیر اموال نمایش داده می‌شود.
    // =====================================================
    public class AssetRow
    {
        public long AssetID { get; set; } public string Tag { get; set; } public string Name { get; set; } public string Category { get; set; }
        public string Status { get; set; } public string Holder { get; set; } public string Location { get; set; }
    }
    public class AssetEventRow { public string EventType { get; set; } public string From { get; set; } public string To { get; set; } public string FromLocation { get; set; } public string ToLocation { get; set; } public string Reason { get; set; } public string Actor { get; set; } public string At { get; set; } public string CorrelationID { get; set; } }
    public class AssetDetail
    {
        public long AssetID { get; set; } public string Tag { get; set; } public string Name { get; set; } public string Category { get; set; } public string Status { get; set; }
        public int? HolderUserID { get; set; } public string Holder { get; set; } public string Location { get; set; } public string AcquiredAt { get; set; } public long? Cost { get; set; } public string Note { get; set; }
        public string RowVer { get; set; } public bool Commitment { get; set; }
        public bool CanAssign { get; set; } public bool CanReturn { get; set; } public bool CanTransfer { get; set; } public bool CanMove { get; set; }
        public bool CanRepairStart { get; set; } public bool CanRepairEnd { get; set; } public bool CanDispose { get; set; }
        public List<AssetEventRow> Events { get; set; }
    }
    public class AssetSummary { public Dictionary<string, int> ByStatus { get; set; } public List<NameCount> ByCategory { get; set; } public List<NameCount> ByHolder { get; set; } }

    public static class MdrAssetReadService
    {
        public static List<AssetRow> List(int uid, string scope, string status, string category, string q)
        {
            using (var db = new MdrContext())
            {
                var mgr = MdrAssetService.IsManager(uid);
                var qry = db.Assets.AsQueryable();
                if (scope == "all" && mgr) { }
                else qry = qry.Where(a => a.HolderUserID == uid || db.AssetEvents.Any(e => e.AssetID == a.AssetID && (e.FromUserID == uid || e.ToUserID == uid)));
                if (!string.IsNullOrEmpty(status)) qry = qry.Where(a => a.Status == status);
                if (!string.IsNullOrWhiteSpace(category)) { var c = category.Trim(); qry = qry.Where(a => a.Category == c); }
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(a => a.Tag.Contains(s) || a.Name.Contains(s) || a.Location.Contains(s)); }
                var rows = qry.OrderByDescending(a => a.AssetID).Take(300).ToList();
                var names = MdrChatService.Names(db, rows.Where(r => r.HolderUserID.HasValue).Select(r => r.HolderUserID.Value));
                return rows.Select(a => new AssetRow { AssetID = a.AssetID, Tag = a.Tag, Name = a.Name, Category = a.Category, Status = a.Status, Holder = a.HolderUserID.HasValue && names.ContainsKey(a.HolderUserID.Value) ? names[a.HolderUserID.Value] : null, Location = a.Location }).ToList();
            }
        }

        public static AssetDetail Get(int uid, long id)
        {
            using (var db = new MdrContext())
            {
                var a = db.Assets.FirstOrDefault(x => x.AssetID == id);
                if (a == null) return null;
                var mgr = MdrAssetService.IsManager(uid);
                var involved = a.HolderUserID == uid || db.AssetEvents.Any(e => e.AssetID == id && (e.FromUserID == uid || e.ToUserID == uid));
                if (!mgr && !involved) return null;

                var ev = db.AssetEvents.Where(e => e.AssetID == id).OrderBy(e => e.CreatedAt).ThenBy(e => e.EventID).ToList();
                var uids = ev.SelectMany(e => new[] { e.FromUserID, e.ToUserID, (int?)e.ActorUserID }).Where(x => x.HasValue).Select(x => x.Value).ToList();
                if (a.HolderUserID.HasValue) uids.Add(a.HolderUserID.Value);
                var names = MdrChatService.Names(db, uids);
                Func<int?, string> nm = x => x.HasValue && names.ContainsKey(x.Value) ? names[x.Value] : null;
                var holder = a.HolderUserID == uid;
                var disposed = a.Status == "Disposed";

                return new AssetDetail
                {
                    AssetID = a.AssetID, Tag = a.Tag, Name = a.Name, Category = a.Category, Status = a.Status, HolderUserID = a.HolderUserID, Holder = nm(a.HolderUserID), Location = a.Location,
                    AcquiredAt = a.AcquiredAt.HasValue ? a.AcquiredAt.Value.ToString("yyyy-MM-dd") : null, Cost = mgr ? a.Cost : null, Note = a.Note, RowVer = Convert.ToBase64String(a.RowVer),
                    Commitment = a.Status == "Assigned" || a.Status == "InRepair",
                    CanAssign = mgr && a.Status == "InStock", CanReturn = a.Status == "Assigned" && (holder || mgr), CanTransfer = a.Status == "Assigned" && (holder || mgr),
                    CanMove = mgr && !disposed, CanRepairStart = mgr && (a.Status == "InStock" || a.Status == "Assigned"), CanRepairEnd = mgr && a.Status == "InRepair",
                    CanDispose = a.Status == "InStock" && MdrAssetService.CanDispose(uid) && (mgr || involved),
                    Events = ev.Select(e => new AssetEventRow { EventType = e.EventType, From = nm(e.FromUserID), To = nm(e.ToUserID), FromLocation = e.FromLocation, ToLocation = e.ToLocation, Reason = e.Reason, Actor = nm(e.ActorUserID), At = e.CreatedAt.ToString("yyyy-MM-dd HH:mm"), CorrelationID = e.CorrelationID.ToString() }).ToList()
                };
            }
        }

        public static AssetSummary Summary(int uid)
        {
            if (!MdrAssetService.IsManager(uid)) return null;
            using (var db = new MdrContext())
            {
                var all = db.Assets.Select(a => new { a.Status, a.Category, a.HolderUserID }).ToList();
                var holders = all.Where(a => a.HolderUserID.HasValue).GroupBy(a => a.HolderUserID.Value).OrderByDescending(g => g.Count()).Take(10).ToList();
                var names = MdrChatService.Names(db, holders.Select(h => h.Key));
                return new AssetSummary
                {
                    ByStatus = new[] { "InStock", "Assigned", "InRepair", "Disposed" }.ToDictionary(s => s, s => all.Count(a => a.Status == s)),
                    ByCategory = all.Where(a => a.Status != "Disposed").GroupBy(a => a.Category ?? "—").Select(g => new NameCount { Name = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToList(),
                    ByHolder = holders.Select(h => new NameCount { Name = names.ContainsKey(h.Key) ? names[h.Key] : "#" + h.Key, Count = h.Count() }).ToList()
                };
            }
        }

        public static List<string> Categories()
        {
            using (var db = new MdrContext()) return db.Assets.Where(a => a.Category != null).Select(a => a.Category).Distinct().OrderBy(c => c).Take(100).ToList();
        }
    }
}
