using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 7 | انبار — خواندن
    // موجودی برای هر کاربر دارای دسترسی به صفحه قابل‌دیدن است؛ دفتر گردش (چه کسی، چه مرجعی) فقط برای انباردار.
    // درخواست کالا فقط برای درخواست‌کننده و انباردار.
    // =====================================================
    public class WhStockStore { public int StoreID { get; set; } public string Name { get; set; } public decimal Qty { get; set; } }
    public class WhStockRow
    {
        public int ItemID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal Total { get; set; }
        public decimal? ReorderPoint { get; set; }
        public bool Low { get; set; }
        public bool IsActive { get; set; }
        public List<WhStockStore> Stores { get; set; }
    }
    public class WhLedgerRow { public long MovementID { get; set; } public string DocNo { get; set; } public string Kind { get; set; } public string Store { get; set; } public decimal Qty { get; set; } public string Reference { get; set; } public string Note { get; set; } public string By { get; set; } public string At { get; set; } }
    public class WhReqRow { public long RequestID { get; set; } public string Number { get; set; } public string Status { get; set; } public string Store { get; set; } public string Requester { get; set; } public int Lines { get; set; } public string At { get; set; } }
    public class WhReqLine { public int ItemID { get; set; } public string Code { get; set; } public string Name { get; set; } public string Unit { get; set; } public decimal Qty { get; set; } public decimal? Available { get; set; } }
    public class WhReqDetail
    {
        public long RequestID { get; set; } public string Number { get; set; } public string Status { get; set; } public int StoreID { get; set; } public string Store { get; set; } public string Requester { get; set; }
        public string Note { get; set; } public string DecisionNote { get; set; } public string At { get; set; } public string RowVer { get; set; }
        public List<WhReqLine> Lines { get; set; }
        public bool CanEdit { get; set; } public bool CanCancel { get; set; } public bool CanIssue { get; set; }
    }
    public class WhLookup { public List<WhNamed> Stores { get; set; } public List<WhItemRef> Items { get; set; } }
    public class WhNamed { public int Id { get; set; } public string Name { get; set; } }
    public class WhItemRef { public int Id { get; set; } public string Code { get; set; } public string Name { get; set; } public string Unit { get; set; } }

    public static class MdrWarehouseReadService
    {
        private class Sum { public int ItemID { get; set; } public int StoreID { get; set; } public decimal Q { get; set; } }

        public static WhLookup Lookups()
        {
            using (var db = new MdrContext())
                return new WhLookup
                {
                    Stores = db.WhStores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new WhNamed { Id = s.StoreID, Name = s.Name }).ToList(),
                    Items = db.WhItems.Where(i => i.IsActive).OrderBy(i => i.Code).Select(i => new WhItemRef { Id = i.ItemID, Code = i.Code, Name = i.Name, Unit = i.Unit }).Take(2000).ToList()
                };
        }

        public static List<WhStockRow> Stock(string q, bool onlyLow)
        {
            using (var db = new MdrContext())
            {
                var items = db.WhItems.AsQueryable();
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); items = items.Where(i => i.Code.Contains(s) || i.Name.Contains(s)); }
                var list = items.OrderBy(i => i.Code).Take(500).ToList();
                var ids = list.Select(i => i.ItemID).ToList();
                var sums = db.Database.SqlQuery<Sum>("SELECT ItemID, StoreID, SUM(Qty) AS Q FROM dbo.MDR_WhMovements GROUP BY ItemID, StoreID").ToList().Where(x => ids.Contains(x.ItemID)).ToList();
                var stores = db.WhStores.ToDictionary(s => s.StoreID, s => s.Name);

                var rows = list.Select(i =>
                {
                    var per = sums.Where(x => x.ItemID == i.ItemID && x.Q != 0).Select(x => new WhStockStore { StoreID = x.StoreID, Name = stores.ContainsKey(x.StoreID) ? stores[x.StoreID] : "#" + x.StoreID, Qty = x.Q }).ToList();
                    var total = per.Sum(x => x.Qty);
                    return new WhStockRow { ItemID = i.ItemID, Code = i.Code, Name = i.Name, Unit = i.Unit, Total = total, ReorderPoint = i.ReorderPoint, IsActive = i.IsActive, Stores = per, Low = i.IsActive && i.ReorderPoint.HasValue && total <= i.ReorderPoint.Value };
                }).ToList();
                return onlyLow ? rows.Where(r => r.Low).ToList() : rows;
            }
        }

        // فقط انباردار؛ null = اجازه ندارد
        public static List<WhLedgerRow> Ledger(int uid, int itemId, int? storeId, int take = 100)
        {
            if (!MdrWarehouseService.IsManager(uid)) return null;
            using (var db = new MdrContext())
            {
                var q = db.WhMovements.Where(m => m.ItemID == itemId);
                if (storeId.HasValue) q = q.Where(m => m.StoreID == storeId.Value);
                var rows = q.OrderByDescending(m => m.MovementID).Take(Math.Max(1, Math.Min(500, take))).ToList();
                var names = MdrChatService.Names(db, rows.Select(r => r.CreatedBy)); var stores = db.WhStores.ToDictionary(s => s.StoreID, s => s.Name);
                return rows.Select(m => new WhLedgerRow
                {
                    MovementID = m.MovementID, DocNo = m.DocNo, Kind = m.Kind, Store = stores.ContainsKey(m.StoreID) ? stores[m.StoreID] : null, Qty = m.Qty, Reference = m.Reference, Note = m.Note,
                    By = names.ContainsKey(m.CreatedBy) ? names[m.CreatedBy] : null, At = m.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                }).ToList();
            }
        }

        // scope: mine | todo (فقط انباردار: در انتظار صدور) | all (فقط انباردار)
        public static List<WhReqRow> Requests(int uid, string scope)
        {
            using (var db = new MdrContext())
            {
                var mgr = MdrWarehouseService.IsManager(uid);
                var q = db.WhRequests.AsQueryable();
                if (scope == "todo" && mgr) q = q.Where(r => r.Status == "Submitted");
                else if (scope == "all" && mgr) { }
                else q = q.Where(r => r.RequesterUserID == uid);
                var rows = q.OrderByDescending(r => r.RequestID).Take(300).ToList();
                var names = MdrChatService.Names(db, rows.Select(r => r.RequesterUserID)); var stores = db.WhStores.ToDictionary(s => s.StoreID, s => s.Name);
                var ids = rows.Select(r => r.RequestID).ToList();
                var counts = db.WhRequestLines.Where(l => ids.Contains(l.RequestID)).GroupBy(l => l.RequestID).ToDictionary(g => g.Key, g => g.Count());
                return rows.Select(r => new WhReqRow
                {
                    RequestID = r.RequestID, Number = r.Number, Status = r.Status, Store = stores.ContainsKey(r.StoreID) ? stores[r.StoreID] : null, Requester = names.ContainsKey(r.RequesterUserID) ? names[r.RequesterUserID] : null,
                    Lines = counts.ContainsKey(r.RequestID) ? counts[r.RequestID] : 0, At = r.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                }).ToList();
            }
        }

        public static WhReqDetail Request(int uid, long id)
        {
            using (var db = new MdrContext())
            {
                var r = db.WhRequests.FirstOrDefault(x => x.RequestID == id);
                var mgr = MdrWarehouseService.IsManager(uid);
                if (r == null || (r.RequesterUserID != uid && !mgr)) return null;
                var names = MdrChatService.Names(db, new[] { r.RequesterUserID });
                var lines = (from l in db.WhRequestLines join i in db.WhItems on l.ItemID equals i.ItemID where l.RequestID == id orderby i.Code select new { l.ItemID, i.Code, i.Name, i.Unit, l.Qty }).ToList();
                return new WhReqDetail
                {
                    RequestID = r.RequestID, Number = r.Number, Status = r.Status, StoreID = r.StoreID, Store = db.WhStores.Where(s => s.StoreID == r.StoreID).Select(s => s.Name).FirstOrDefault(),
                    Requester = names.ContainsKey(r.RequesterUserID) ? names[r.RequesterUserID] : null, Note = r.Note, DecisionNote = r.DecisionNote, At = r.CreatedAt.ToString("yyyy-MM-dd HH:mm"), RowVer = Convert.ToBase64String(r.RowVer),
                    Lines = lines.Select(l => new WhReqLine { ItemID = l.ItemID, Code = l.Code, Name = l.Name, Unit = l.Unit, Qty = l.Qty, Available = mgr ? (decimal?)MdrWarehouseService.Stock(db, l.ItemID, r.StoreID) : null }).ToList(),
                    CanEdit = r.RequesterUserID == uid && r.Status == "Draft", CanCancel = r.RequesterUserID == uid && (r.Status == "Draft" || r.Status == "Submitted"), CanIssue = mgr && r.Status == "Submitted"
                };
            }
        }
    }
}
