using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 6 | جستجوی جامع (پنل ۱۰)
    // - جستجوی زنده‌ی پارامتریک (بدون ایندکس جدا → هیچ‌وقت کهنه نیست).
    // - دسترسی (ACL) داخل خود SQL و مطابق قواعد هر ماژول اعمال می‌شود؛ نتیجه‌ای که کاربر
    //   نباید ببیند هیچ‌وقت از پایگاه داده بیرون نمی‌آید (نه عنوان، نه شمارش).
    // - هر منبع فقط وقتی جستجو می‌شود که کاربر به صفحه‌ی آن ماژول دسترسی دارد.
    // - عبارت: حداکثر ۵ کلمه (AND)، هر کلمه ≥ ۲ حرف؛ ی/ي، ک/ك و ارقام فارسی/عربی/لاتین یکسان‌اند.
    // - سقف: شمارش تا ۱۰۰۰؛ هر صفحه حداکثر ۵۰ نتیجه.
    // =====================================================

    public class SearchHit
    {
        public string Source { get; set; }
        public long Id { get; set; }
        public string Title { get; set; }
        public string Snippet { get; set; }
        public string Status { get; set; }
        public string Url { get; set; }
        public DateTime? Date { get; set; }
    }

    public class SearchSourceResult
    {
        public string Key { get; set; }
        public int Total { get; set; }          // تا سقف CountCap
        public bool Capped { get; set; }
        public List<SearchHit> Items { get; set; }
    }

    public class SearchOutcome
    {
        public string Error { get; set; }
        public List<SearchSourceResult> Sources { get; set; }
        public List<string> SkippedForAccess { get; set; }
    }

    public static class MdrSearchService
    {
        public const int CountCap = 1000;
        private const int MaxWords = 5;

        private class Row
        {
            public long Id { get; set; }
            public string Title { get; set; }
            public string Status { get; set; }
            public string Text { get; set; }
            public DateTime? Dt { get; set; }
        }

        private class Source
        {
            public string Key, PageCode, From, OrderBy, TitleExpr, StatusExpr, TextExpr, DateCol, UrlFormat, Acl;
            public string LevelExpr = "0";          // سطح محرمانگی: ۰ عادی، ۱ محرمانه، ۲ سری (برای پرونده‌ی الکترونیکی)
            public string OpenExpr = "0";           // شرط «موضوع باز» (۱ = باز)
            public string[] Columns;
            public string[] ExtraExists;           // الگوی EXISTS با {P} برای پارامتر LIKE
        }

        // Acl: شرط دسترسی؛ پارامترها: @uid و @mgr (۱ = مدیر آن ماژول)
        private static readonly Source[] Sources =
        {
            new Source
            {
                Key = "req", PageCode = "MDR.Requests", OpenExpr = "CASE WHEN s.Status IN (N'Draft', N'Submitted') THEN 1 ELSE 0 END", UrlFormat = "/Mdr/Requests#id={0}",
                From = "dbo.MDR_Requests s JOIN dbo.MDR_RequestTypes ty ON ty.TypeID = s.TypeID", OrderBy = "s.RequestID DESC",
                Acl = "(s.RequesterUserID = @uid OR (@mgr = 1 AND s.Status <> N'Draft'))",
                Columns = new[] { "s.Title", "s.Number", "s.DataJson", "ty.Name" },
                TitleExpr = "s.Number + N' — ' + s.Title", StatusExpr = "s.Status", TextExpr = "ty.Name", DateCol = "s.CreatedAt", ExtraExists = new string[0]
            },
            new Source
            {
                Key = "tsk", PageCode = "MDR.Tasks", OpenExpr = "CASE WHEN s.Status IN (N'Todo', N'InProgress', N'Blocked') THEN 1 ELSE 0 END", UrlFormat = "/Mdr/Tasks#id={0}",
                From = "dbo.MDR_Tasks s", OrderBy = "s.TaskID DESC",
                Acl = "(s.AssigneeUserID = @uid OR s.CreatedBy = @uid OR @mgr = 1)",
                Columns = new[] { "s.Title", "s.Description", "s.ResultText" },
                TitleExpr = "s.Title", StatusExpr = "s.Status", TextExpr = "s.Description", DateCol = "s.CreatedAt", ExtraExists = new string[0]
            },
            new Source
            {
                Key = "ltr", PageCode = "MDR.Letters", OpenExpr = "CASE WHEN s.Status = N'Draft' THEN 1 ELSE 0 END",
                LevelExpr = "CASE s.Confidentiality WHEN N'Secret' THEN 2 WHEN N'Confidential' THEN 1 ELSE 0 END", UrlFormat = "/Mdr/Letters#id={0}",
                From = "dbo.MDR_Letters s", OrderBy = "s.LetterID DESC",
                Acl = "((s.Status IN (N'Draft', N'Cancelled') AND s.CreatedBy = @uid) OR (s.Status IN (N'Registered', N'Archived') AND (s.CreatedBy = @uid " +
                      "OR EXISTS (SELECT 1 FROM dbo.MDR_LetterRecipients x WHERE x.LetterID = s.LetterID AND x.UserID = @uid) " +
                      "OR (@mgr = 1 AND s.Confidentiality <> N'Secret'))))",
                Columns = new[] { "s.Subject", "s.Number", "s.ExternalParty", "s.Body" },
                TitleExpr = "ISNULL(s.Number, N'پیش‌نویس') + N' — ' + s.Subject", StatusExpr = "s.Status", TextExpr = "s.Body", DateCol = "s.CreatedAt", ExtraExists = new string[0]
            },
            new Source
            {
                Key = "doc", PageCode = "MDR.Documents",
                LevelExpr = "CASE s.Classification WHEN N'Secret' THEN 2 WHEN N'Confidential' THEN 1 ELSE 0 END", UrlFormat = "/Mdr/Documents#id={0}",
                From = "dbo.MDR_Documents s", OrderBy = "s.DocumentID DESC",
                Acl = "(s.OwnerUserID = @uid OR EXISTS (SELECT 1 FROM dbo.MDR_DocumentShares x WHERE x.DocumentID = s.DocumentID AND x.UserID = @uid) " +
                      "OR (@mgr = 1 AND s.Classification <> N'Secret'))",
                Columns = new[] { "s.Title", "s.Description" },
                ExtraExists = new[] { "EXISTS (SELECT 1 FROM dbo.MDR_DocumentTags g WHERE g.DocumentID = s.DocumentID AND g.Tag LIKE {P})" },
                TitleExpr = "s.Title", StatusExpr = "s.Status", TextExpr = "s.Description", DateCol = "s.CreatedAt"
            },
            new Source
            {
                Key = "wf", PageCode = "MDR.Workflow.Monitor", OpenExpr = "CASE WHEN s.Status IN (N'Running', N'Paused') THEN 1 ELSE 0 END", UrlFormat = "/Mdr/Workflow/Monitor?id={0}",
                From = "dbo.MDR_WorkflowInstances s JOIN dbo.MDR_WorkflowDefinitions df ON df.WorkflowID = s.WorkflowID", OrderBy = "s.InstanceID DESC",
                Acl = "1 = 1",   // مانیتور فرایند برای هر کاربرِ دارای دسترسی به همان صفحه، همه‌ی Instance ها را نشان می‌دهد
                Columns = new[] { "s.Subject", "df.Name" },
                TitleExpr = "s.Subject", StatusExpr = "s.Status", TextExpr = "df.Name", DateCol = "s.StartedAt", ExtraExists = new string[0]
            },
            // قراردادهای پیمانی (فقط‌خواندنی). قاعده‌ی دسترسی = همان قاعده‌ی قدیمی: دسترسی به صفحه‌ی «مدیریت پیمان‌ها» (PAGE_017)
            // و مدیر شرکتِ طرف دوم فقط پیمان‌های شرکت خودش را می‌بیند. مبلغ و فایل هرگز خوانده نمی‌شود. حداقل سطح: محرمانه.
            new Source
            {
                Key = "ctr", PageCode = "PAGE_017", UrlFormat = "/Contracts/Peyman/ManagePeymans", LevelExpr = "1",
                From = "Contract.tbPeymanContracts s", OrderBy = "s.pec_ID DESC",
                Acl = "(NOT EXISTS (SELECT 1 FROM Usser.tbCompanies c WHERE c.FK_ManagerID = @uid) " +
                      "OR s.FK_UserTarafDovvom IN (SELECT c.ID FROM Usser.tbCompanies c WHERE c.FK_ManagerID = @uid))",
                Columns = new[] { "s.pec_Title", "s.pec_BriefTitle", "s.pec_ContractNumber" },
                TitleExpr = "ISNULL(s.pec_ContractNumber, N'') + N' — ' + ISNULL(s.pec_BriefTitle, s.pec_Title)",
                StatusExpr = "CASE WHEN s.Inactive = 1 THEN N'Inactive' WHEN s.pec_EndTime < CAST(GETDATE() AS date) THEN N'Expired' ELSE N'Active' END",
                TextExpr = "s.pec_Title", DateCol = "s.pec_StartTime", ExtraExists = new string[0]
            }
        };

        public static string[] SourceKeys { get { return Sources.Select(s => s.Key).ToArray(); } }

        // ---------------- عبارت ----------------
        // ارقام فارسی/عربی → لاتین؛ فاصله‌ها فشرده؛ null اگر نامعتبر
        public static string[] ParseWords(string q, out string error)
        {
            error = null;
            q = Normalize(q ?? "").Trim();
            if (q.Length == 0) { error = "srch.err.empty"; return null; }
            if (q.Length > 100) { error = "srch.err.long"; return null; }

            var words = q.Split(new[] { ' ', '\t', '‌' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
            if (words.Length > MaxWords) { error = "srch.err.words"; return null; }
            if (words.Any(w => w.Length < 2)) { error = "srch.err.short"; return null; }
            return words;
        }

        public static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));      // ارقام فارسی
                else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠'))); // ارقام عربی
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // شکل‌های هم‌ارز یک کلمه: ی/ي و ک/ك
        public static string[] Variants(string w)
        {
            var set = new HashSet<string> { w };
            foreach (var a in set.ToList()) { set.Add(a.Replace('ی', 'ي')); }
            foreach (var a in set.ToList()) { set.Add(a.Replace('ک', 'ك')); }
            foreach (var a in set.ToList()) { set.Add(a.Replace('ي', 'ی')); }
            foreach (var a in set.ToList()) { set.Add(a.Replace('ك', 'ک')); }
            return set.ToArray();
        }

        private static string LikeValue(string v) { return "%" + v.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%"; }

        // شرط عبارت: AND روی کلمه‌ها، OR روی ستون‌ها و شکل‌های هم‌ارز
        private static string TermClause(Source s, string[] words, List<SqlParameter> ps)
        {
            var and = new List<string>();
            foreach (var w in words)
            {
                var or = new List<string>();
                foreach (var v in Variants(w))
                {
                    var name = "@t" + ps.Count;
                    ps.Add(new SqlParameter(name, LikeValue(v)));
                    foreach (var col in s.Columns) or.Add(col + " LIKE " + name);
                    foreach (var ex in s.ExtraExists) or.Add(ex.Replace("{P}", name));
                }
                and.Add("(" + string.Join(" OR ", or) + ")");
            }
            return string.Join(" AND ", and);
        }

        public static string Snippet(string text, string[] words, int len = 160)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length <= len) return text;

            var idx = -1;
            foreach (var w in words) foreach (var v in Variants(w))
            {
                var i = text.IndexOf(v, StringComparison.OrdinalIgnoreCase);
                if (i >= 0 && (idx < 0 || i < idx)) idx = i;
            }
            var start = Math.Max(0, idx < 0 ? 0 : idx - 40);
            var piece = text.Substring(start, Math.Min(len, text.Length - start));
            return (start > 0 ? "…" : "") + piece + (start + piece.Length < text.Length ? "…" : "");
        }

        // ---------------- جستجو ----------------
        // sourceKey خالی = همه‌ی منابع (هر کدام صفحه‌ی اول)؛ پر = فقط همان منبع با skip/take
        public static SearchOutcome Search(int uid, string q, string sourceKey, int skip, int take, DateTime? from, DateTime? to)
        {
            string error;
            var words = ParseWords(q, out error);
            if (words == null) return new SearchOutcome { Error = error };

            take = Math.Max(1, Math.Min(50, take));
            skip = Math.Max(0, skip);
            var outcome = new SearchOutcome { Sources = new List<SearchSourceResult>(), SkippedForAccess = new List<string>() };

            using (var db = new MdrContext())
            {
                foreach (var s in Sources)
                {
                    if (!string.IsNullOrEmpty(sourceKey) && s.Key != sourceKey) continue;
                    if (!PermissionHelper.HasAccess(uid, s.PageCode)) { outcome.SkippedForAccess.Add(s.Key); continue; }

                    var manager = PermissionHelper.CanEdit(uid, s.PageCode);
                    var ps = new List<SqlParameter>();
                    var where = new List<string> { s.Acl, TermClause(s, words, ps) };
                    if (from.HasValue) { where.Add(s.DateCol + " >= @from"); ps.Add(new SqlParameter("@from", from.Value.Date)); }
                    if (to.HasValue) { where.Add(s.DateCol + " < @to"); ps.Add(new SqlParameter("@to", to.Value.Date.AddDays(1))); }
                    ps.Add(new SqlParameter("@uid", uid));
                    ps.Add(new SqlParameter("@mgr", manager ? 1 : 0));
                    var whereSql = string.Join(" AND ", where);

                    int total = db.Database.SqlQuery<int>(
                        "SELECT COUNT(*) FROM (SELECT TOP (" + CountCap + ") 1 AS x FROM " + s.From + " WHERE " + whereSql + ") c",
                        Clone(ps)).First();

                    var rows = new List<Row>();
                    if (total > skip)
                    {
                        var pageParams = Clone(ps).ToList();
                        pageParams.Add(new SqlParameter("@skip", skip)); pageParams.Add(new SqlParameter("@take", take));
                        rows = db.Database.SqlQuery<Row>(
                            "SELECT CAST(s." + IdColumn(s) + " AS BIGINT) AS Id, " + s.TitleExpr + " AS Title, " + s.StatusExpr + " AS Status, " +
                            "LEFT(" + s.TextExpr + ", 4000) AS Text, " + s.DateCol + " AS Dt FROM " + s.From + " WHERE " + whereSql +
                            " ORDER BY " + s.OrderBy + " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", pageParams.ToArray()).ToList();
                    }

                    outcome.Sources.Add(new SearchSourceResult
                    {
                        Key = s.Key, Total = total, Capped = total >= CountCap,
                        Items = rows.Select(r => new SearchHit
                        {
                            Source = s.Key, Id = r.Id, Title = r.Title, Status = r.Status, Date = r.Dt,
                            Snippet = Snippet(r.Text, words), Url = string.Format(s.UrlFormat, r.Id)
                        }).ToList()
                    });
                }
            }

            return outcome;
        }

        private static string IdColumn(Source s)
        {
            switch (s.Key)
            {
                case "req": return "RequestID";
                case "tsk": return "TaskID";
                case "ltr": return "LetterID";
                case "doc": return "DocumentID";
                case "ctr": return "pec_ID";
                default: return "InstanceID";
            }
        }

        private static object[] Clone(List<SqlParameter> ps)
        {
            return ps.Select(p => (object)new SqlParameter(p.ParameterName, p.Value)).ToArray();
        }

        // ---------------- scope گزارش‌ها ----------------
        // همان قاعده‌ی دسترسی جستجو برای گزارش‌ها: گزارش هرگز بیشتر از آنچه کاربر در خود ماژول می‌بیند نشان نمی‌دهد.
        // personal = true: گزارش شخصی (حتی مدیر فقط موارد خودش را می‌بیند)؛ false: دید مدیر ماژول (اگر مدیر باشد)
        public class SourceScope
        {
            public string Key, PageCode, From, Acl, IdColumn, TitleExpr, StatusExpr, DateCol, UrlFormat;
            public bool Manager;
        }

        // null: کاربر به صفحه‌ی آن ماژول دسترسی ندارد (یا منبع ناشناخته است)
        public static SourceScope ScopeFor(int uid, string key, bool personal)
        {
            var s = Sources.FirstOrDefault(x => x.Key == key);
            if (s == null || !PermissionHelper.HasAccess(uid, s.PageCode)) return null;
            return new SourceScope
            {
                Key = s.Key, PageCode = s.PageCode, From = s.From, Acl = s.Acl, IdColumn = "s." + IdColumn(s), TitleExpr = s.TitleExpr,
                StatusExpr = s.StatusExpr, DateCol = s.DateCol, UrlFormat = s.UrlFormat,
                Manager = !personal && PermissionHelper.CanEdit(uid, s.PageCode)
            };
        }

        // ---------------- شناسایی موجودیت (برای پرونده‌ی الکترونیکی) ----------------
        public class EntityInfo
        {
            public string Key { get; set; }
            public long Id { get; set; }
            public string Title { get; set; }
            public string Status { get; set; }
            public string Url { get; set; }
            public int Level { get; set; }      // ۰ عادی، ۱ محرمانه، ۲ سری
            public bool IsOpen { get; set; }
        }

        private class InfoRow
        {
            public long Id { get; set; }
            public string Title { get; set; }
            public string Status { get; set; }
            public int Level { get; set; }
            public int IsOpen { get; set; }
        }

        // uid = null: بدون بررسی دسترسی (فقط برای قواعد داخلی سرویس‌ها مثل «موضوع باز» و سطح محرمانگی؛ هیچ‌وقت به کاربر برنگردد)
        // uid مشخص: فقط موجودیت‌هایی که همان کاربر طبق قواعد جستجو می‌تواند ببیند (شامل دسترسی به صفحه‌ی ماژول)
        public static List<EntityInfo> Lookup(int? uid, string key, IEnumerable<long> ids)
        {
            var src = Sources.FirstOrDefault(s => s.Key == key);
            var list = (ids ?? new long[0]).Distinct().Take(500).ToList();
            if (src == null || list.Count == 0) return new List<EntityInfo>();
            if (uid.HasValue && !PermissionHelper.HasAccess(uid.Value, src.PageCode)) return new List<EntityInfo>();

            var idCol = "s." + IdColumn(src);
            var sql = "SELECT CAST(" + idCol + " AS BIGINT) AS Id, " + src.TitleExpr + " AS Title, " + src.StatusExpr + " AS Status, " +
                      src.LevelExpr + " AS Level, " + src.OpenExpr + " AS IsOpen FROM " + src.From +
                      " WHERE " + idCol + " IN (" + string.Join(",", list) + ")";   // فقط long در SQL می‌آید
            var ps = new List<object>();
            if (uid.HasValue)
            {
                sql += " AND " + src.Acl;
                ps.Add(new SqlParameter("@uid", uid.Value));
                ps.Add(new SqlParameter("@mgr", PermissionHelper.CanEdit(uid.Value, src.PageCode) ? 1 : 0));
            }

            using (var db = new MdrContext())
            {
                return db.Database.SqlQuery<InfoRow>(sql, ps.ToArray()).ToList().Select(r => new EntityInfo
                {
                    Key = key, Id = r.Id, Title = r.Title, Status = r.Status, Level = r.Level, IsOpen = r.IsOpen == 1,
                    Url = string.Format(src.UrlFormat, r.Id)
                }).ToList();
            }
        }

        // ---------------- جستجوهای ذخیره‌شده ----------------
        public static MdrResult SaveQuery(int uid, string name, string q, string source, DateTime? from, DateTime? to)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 100) return MdrResult.Fail("srch.err.name");
            string error;
            if (ParseWords(q, out error) == null) return MdrResult.Fail(error);
            if (!string.IsNullOrEmpty(source) && !SourceKeys.Contains(source)) return MdrResult.Fail("srch.err.source");

            try
            {
                using (var db = new MdrContext())
                {
                    var existing = db.SavedSearches.FirstOrDefault(x => x.UserID == uid && x.Name == name);
                    if (existing == null)
                    {
                        if (db.SavedSearches.Count(x => x.UserID == uid) >= 20) return MdrResult.Fail("srch.err.limit");
                        existing = new MdrSavedSearch { UserID = uid, Name = name, CreatedAt = DateTime.Now };
                        db.SavedSearches.Add(existing);
                    }
                    existing.QueryText = q.Trim(); existing.Source = string.IsNullOrEmpty(source) ? null : source;
                    existing.DateFrom = from; existing.DateTo = to;
                    db.SaveChanges();
                    return MdrResult.Success(existing.SearchID);
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditLogger.Actions.Error, "Search", "خطای ذخیره‌ی جستجو", newValues: new { ex.Message }, isSuccess: false);
                return MdrResult.Fail("wf.err.unexpected");
            }
        }

        public static MdrResult DeleteQuery(int uid, long id)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var s = db.SavedSearches.FirstOrDefault(x => x.SearchID == id && x.UserID == uid);   // فقط مال خودش
                    if (s == null) return MdrResult.Fail("wf.err.notfound");
                    db.SavedSearches.Remove(s);
                    db.SaveChanges();
                    return MdrResult.Success(id);
                }
            }
            catch (Exception)
            {
                return MdrResult.Fail("wf.err.unexpected");
            }
        }
    }
}
