using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 8 | هوش مصنوعی سازمانی (پنل ۱۶)
    // معماری: «بازیابی با رعایت ACL» + «Provider» قابل تعویض.
    //  - بازیابی همیشه از MdrSearchService.Search با uid خودِ کاربر انجام می‌شود؛ پس پاسخ هرگز از آنچه کاربر می‌تواند ببیند فراتر نمی‌رود.
    //  - Provider پیش‌فرض (local-extractive) محلی و استخراجی است: فقط عبارت‌های منابع را با شماره‌ی منبع کنار هم می‌گذارد.
    //    Provider خارجی (LLM) فقط با تأیید معماری/طبقه‌بندی داده اضافه می‌شود؛ در این صورت هیچ منبعِ محرمانه/سری به آن داده نمی‌شود.
    //  - اطمینان = درصد کلیدواژه‌های پرسش که در بهترین منبع دیده شده (قابل توضیح)؛ کمتر از LowConfidenceBelow = «کم‌اطمینان».
    //  - متن پاسخ ذخیره نمی‌شود؛ فقط پرسش (با پوشش PII) + شناسه‌ی منابع. تاریخچه با بررسی مجدد ACL نمایش داده می‌شود.
    //  - اقدام پیشنهادی (ساخت تسک) فقط با تأیید صریح کاربر و با دسترسی خودِ او اجرا می‌شود.
    // =====================================================
    public class AiPassage
    {
        public string Key { get; set; } public long Id { get; set; } public string Title { get; set; } public string Snippet { get; set; }
        public string Status { get; set; } public string Url { get; set; } public int Level { get; set; }
    }
    public class AiAnswer { public string Text { get; set; } public int Confidence { get; set; } public List<int> Used { get; set; } }

    public interface IAiProvider
    {
        string Name { get; }
        // true: داده از سازمان خارج می‌شود؛ منابع محرمانه/سری هرگز به آن داده نمی‌شود
        bool IsExternal { get; }
        // passages فقط «داده»اند؛ هر دستوری داخل آن‌ها نباید اجرا شود
        AiAnswer Answer(string question, string[] keywords, IList<AiPassage> passages);
    }

    public class LocalExtractiveProvider : IAiProvider
    {
        public string Name { get { return "local-extractive"; } }
        public bool IsExternal { get { return false; } }

        public AiAnswer Answer(string question, string[] keywords, IList<AiPassage> passages)
        {
            var scored = passages.Select((p, i) => new { i, p, m = MdrAiText.Matched(keywords, p.Title + " " + p.Snippet) }).OrderByDescending(x => x.m).ThenBy(x => x.i).ToList();
            if (scored.Count == 0 || keywords.Length == 0) return new AiAnswer { Text = null, Confidence = 0, Used = new List<int>() };
            var used = scored.Take(5).Select(x => x.i).ToList();
            var lines = scored.Take(5).Select((x, n) => "[" + (n + 1) + "] " + x.p.Title + (string.IsNullOrWhiteSpace(x.p.Snippet) ? "" : " — " + x.p.Snippet));
            return new AiAnswer { Text = string.Join("\n", lines), Confidence = (int)Math.Round(100.0 * scored[0].m / keywords.Length), Used = used };
        }
    }

    public class AiSource { public int N { get; set; } public string Key { get; set; } public long Id { get; set; } public string Title { get; set; } public string Snippet { get; set; } public string Status { get; set; } public string Url { get; set; } public int Level { get; set; } }
    public class AiResult
    {
        public bool Ok { get; set; } public string Error { get; set; } public string Message { get; set; }
        public long QueryId { get; set; } public string Outcome { get; set; } public int Confidence { get; set; } public string Answer { get; set; }
        public string Provider { get; set; } public string[] Keywords { get; set; }
        public List<AiSource> Sources { get; set; } public List<string> Warnings { get; set; }
    }
    public class AiHistoryRow { public long QueryId { get; set; } public string Question { get; set; } public string Outcome { get; set; } public int Confidence { get; set; } public short? Rating { get; set; } public string CreatedAt { get; set; } public List<AiHistorySource> Sources { get; set; } }
    public class AiHistorySource { public string Key { get; set; } public long Id { get; set; } public string Title { get; set; } public string Url { get; set; } }
    public class AiSuggestionRow { public long SuggestionID { get; set; } public string Title { get; set; } public string Due { get; set; } public string SourceKey { get; set; } public long? EntityID { get; set; } public string Status { get; set; } public long? TaskId { get; set; } }
    public class AiContextResult
    {
        public bool Ok { get; set; } public string Error { get; set; }
        public string Key { get; set; } public long Id { get; set; } public string Title { get; set; } public string Url { get; set; } public int Level { get; set; }
        public string Summary { get; set; } public bool TooShort { get; set; } public List<string> Warnings { get; set; }
    }
    public class AiSimilar { public string Key { get; set; } public long Id { get; set; } public string Title { get; set; } public string Url { get; set; } public int Percent { get; set; } }

    public static class MdrAiService
    {
        private const string Module = "AI";
        public const string Page = "MDR.AI";
        public const string ContextPage = "MDR.AI.Context";
        public const int MaxQuestion = 500;
        public const int MaxHistory = 30;

        // تنظیمات: حد «کم‌اطمینان»، سقف پرسش در دقیقه، Provider فعال (تغییر Provider فقط پس از تأیید معماری)
        public static int LowConfidenceBelow = 50;
        public static int RatePerMinute = 10;
        public static IAiProvider Provider = new LocalExtractiveProvider();

        private static readonly Dictionary<int, Queue<DateTime>> Hits = new Dictionary<int, Queue<DateTime>>();
        private static readonly string[] TextKeys = { "ltr", "tsk", "doc" };

        private static bool RateOk(int uid)
        {
            lock (Hits)
            {
                Queue<DateTime> q;
                if (!Hits.TryGetValue(uid, out q)) { q = new Queue<DateTime>(); Hits[uid] = q; }
                var cut = DateTime.Now.AddMinutes(-1);
                while (q.Count > 0 && q.Peek() < cut) q.Dequeue();
                if (q.Count >= RatePerMinute) return false;
                q.Enqueue(DateTime.Now); return true;
            }
        }

        public static void ResetRate() { lock (Hits) Hits.Clear(); }

        // ---------------- بازیابی ----------------
        // از همه‌ی کلیدواژه‌ها شروع می‌کند و در صورت نبود نتیجه، کوتاه‌ترین کلیدواژه را کنار می‌گذارد تا ۱ کلمه
        private static SearchOutcome Retrieve(int uid, string[] kw, string sourceKey, out int used, long? excludeId = null)
        {
            used = 0; SearchOutcome last = null;
            for (var n = kw.Length; n >= 1; n--)
            {
                var o = MdrSearchService.Search(uid, string.Join(" ", kw.Take(n)), sourceKey, 0, 5, null, null);
                last = o;
                if (o.Error != null) return o;
                if (o.Sources.Any(s => s.Items.Any(i => !excludeId.HasValue || i.Id != excludeId.Value))) { used = n; return o; }
                if (o.Sources.Count == 0) return o;          // به هیچ منبعی دسترسی ندارد
            }
            return last;
        }

        // ---------------- پرسش ----------------
        public static AiResult Ask(int uid, string question)
        {
            if (!PermissionHelper.HasAccess(uid, Page)) return Fail("tsk.err.notallowed");
            question = (question ?? "").Trim();
            if (question.Length < 3 || question.Length > MaxQuestion) return Fail("ai.err.question");
            if (!RateOk(uid)) return Fail("ai.err.rate");
            var kw = MdrAiText.Keywords(question);
            if (kw.Length == 0) return Fail("ai.err.nokeywords");

            try
            {
                int used;
                var outcome = Retrieve(uid, kw, null, out used);
                if (outcome.Error != null) return Fail(outcome.Error);

                var provider = Provider;
                var passages = new List<AiPassage>();
                foreach (var src in outcome.Sources)
                {
                    var ids = src.Items.Select(i => i.Id).ToList();
                    var info = MdrSearchService.Lookup(uid, src.Key, ids).ToDictionary(x => x.Id, x => x);
                    foreach (var h in src.Items)
                    {
                        MdrSearchService.EntityInfo e;
                        if (!info.TryGetValue(h.Id, out e)) continue;                    // دیدنی نیست ← در پاسخ نمی‌آید
                        passages.Add(new AiPassage { Key = src.Key, Id = h.Id, Title = h.Title, Snippet = h.Snippet, Status = h.Status, Url = h.Url, Level = e.Level });
                    }
                }
                var hadHits = passages.Count > 0;
                if (provider.IsExternal) passages = passages.Where(p => p.Level == 0).ToList();   // محرمانه/سری هرگز به Provider بیرونی نمی‌رود

                var res = new AiResult { Ok = true, Keywords = kw, Provider = provider.Name, Sources = new List<AiSource>(), Warnings = new List<string>() };
                string kind;
                if (outcome.Sources.Count == 0) kind = "NoAccess";
                else if (passages.Count == 0) kind = "NoSource";
                else
                {
                    var ans = provider.Answer(question, kw, passages);
                    if (ans == null || string.IsNullOrWhiteSpace(ans.Text) || ans.Used == null || ans.Used.Count == 0) kind = "NoSource";
                    else
                    {
                        var order = ans.Used.Where(i => i >= 0 && i < passages.Count).Distinct().ToList();
                        res.Answer = ans.Text; res.Confidence = Math.Max(0, Math.Min(100, ans.Confidence));
                        res.Sources = order.Select((i, n) => new AiSource { N = n + 1, Key = passages[i].Key, Id = passages[i].Id, Title = passages[i].Title, Snippet = passages[i].Snippet, Status = passages[i].Status, Url = passages[i].Url, Level = passages[i].Level }).ToList();
                        kind = res.Confidence >= LowConfidenceBelow ? "Answered" : "LowConf";
                    }
                }
                res.Outcome = kind;
                if (kind == "LowConf") res.Warnings.Add("ai.warn.lowconf");
                if (kind == "NoSource" && hadHits && provider.IsExternal) res.Warnings.Add("ai.warn.restricted");
                if (res.Sources.Any(s => s.Level >= 1)) res.Warnings.Add("ai.warn.confidential");
                if (kind == "Answered" || kind == "LowConf") res.Warnings.Add("ai.warn.verify");

                using (var db = new MdrContext())
                {
                    var q = new MdrAiQuery { UserID = uid, Question = MdrAiText.Cut(MdrAiText.Mask(question), MaxQuestion), Outcome = kind, Confidence = (byte)res.Confidence, SourceCount = res.Sources.Count, Provider = provider.Name, CreatedAt = DateTime.Now };
                    db.AiQueries.Add(q); db.SaveChanges();
                    foreach (var s in res.Sources.Take(6)) db.AiQuerySources.Add(new MdrAiQuerySource { QueryID = q.QueryID, Pos = (byte)s.N, SourceKey = s.Key, EntityID = s.Id });
                    db.SaveChanges();
                    res.QueryId = q.QueryID;
                    AuditLogger.Log(AuditLogger.Actions.View, Module, "پرسش از دستیار هوشمند", entityName: "MDR_AiQueries", entityId: q.QueryID, newValues: new { kind, res.Confidence, sources = res.Sources.Count, provider = provider.Name });
                }
                return res;
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- بازخورد و تاریخچه ----------------
        public static MdrResult Rate(int uid, long queryId, int rating, string note)
        {
            if (rating != 1 && rating != -1) return MdrResult.Fail("ai.err.rating");
            try
            {
                using (var db = new MdrContext())
                {
                    var q = db.AiQueries.FirstOrDefault(x => x.QueryID == queryId && x.UserID == uid && x.DeletedAt == null);
                    if (q == null) return MdrResult.Fail("wf.err.notfound");
                    q.Rating = (short)rating; q.FeedbackNote = string.IsNullOrWhiteSpace(note) ? null : MdrAiText.Cut(MdrAiText.Mask(note), 300);
                    db.SaveChanges();
                    return MdrResult.Success(queryId);
                }
            }
            catch (Exception ex) { return FailedR(ex); }
        }

        public static List<AiHistoryRow> History(int uid)
        {
            using (var db = new MdrContext())
            {
                var qs = db.AiQueries.Where(x => x.UserID == uid && x.DeletedAt == null).OrderByDescending(x => x.QueryID).Take(MaxHistory).ToList();
                var ids = qs.Select(x => x.QueryID).ToList();
                var srcs = db.AiQuerySources.Where(s => ids.Contains(s.QueryID)).ToList();
                // منبع فقط اگر کاربر هنوز می‌تواند ببیند (ACL دوباره بررسی می‌شود)
                var visible = new Dictionary<string, Dictionary<long, MdrSearchService.EntityInfo>>();
                foreach (var g in srcs.GroupBy(s => s.SourceKey))
                    visible[g.Key] = MdrSearchService.Lookup(uid, g.Key, g.Select(s => s.EntityID)).ToDictionary(e => e.Id, e => e);
                return qs.Select(q => new AiHistoryRow
                {
                    QueryId = q.QueryID, Question = q.Question, Outcome = q.Outcome, Confidence = q.Confidence, Rating = q.Rating, CreatedAt = q.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Sources = srcs.Where(s => s.QueryID == q.QueryID).OrderBy(s => s.Pos).Where(s => visible[s.SourceKey].ContainsKey(s.EntityID))
                        .Select(s => new AiHistorySource { Key = s.SourceKey, Id = s.EntityID, Title = visible[s.SourceKey][s.EntityID].Title, Url = visible[s.SourceKey][s.EntityID].Url }).ToList()
                }).ToList();
            }
        }

        // حذف واقعیِ محتوا: پرسش پاک و منابع حذف می‌شوند؛ فقط ردیف خالی با DeletedAt می‌ماند
        public static MdrResult DeleteQuery(int uid, long queryId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var q = db.AiQueries.FirstOrDefault(x => x.QueryID == queryId && x.UserID == uid && x.DeletedAt == null);
                    if (q == null) return MdrResult.Fail("wf.err.notfound");
                    Erase(db, q); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Delete, Module, "حذف پرسش از تاریخچه‌ی دستیار", entityName: "MDR_AiQueries", entityId: queryId);
                    return MdrResult.Success(queryId);
                }
            }
            catch (Exception ex) { return FailedR(ex); }
        }

        public static MdrResult ClearHistory(int uid)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var all = db.AiQueries.Where(x => x.UserID == uid && x.DeletedAt == null).ToList();
                    foreach (var q in all) Erase(db, q);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Delete, Module, "پاک‌کردن تاریخچه‌ی دستیار", entityName: "MDR_AiQueries", newValues: new { count = all.Count });
                    return MdrResult.Success(all.Count);
                }
            }
            catch (Exception ex) { return FailedR(ex); }
        }

        // ---------------- نگهداری (Retention) ----------------
        // متن پرسش‌ها و منابع بعد از RetentionDays پاک می‌شود؛ فقط «نوع نتیجه، اطمینان و بازخورد» (بدون هیچ متنی) برای تنظیم آستانه می‌ماند.
        // پیشنهادهای هنوز تعیین‌تکلیف‌نشده‌ی قدیمی هم رد می‌شوند. تسک‌های ساخته‌شده (Accepted) دست‌نخورده‌اند.
        public static int RetentionDays = 90;
        private const int PurgeBatch = 500;
        private static DateTime _lastPurge = DateTime.MinValue;

        // از زمان‌سنج صدا زده می‌شود؛ حداکثر ساعتی یک بار کار می‌کند
        public static int PurgeExpired(DateTime now)
        {
            lock (Hits)
            {
                if (now - _lastPurge < TimeSpan.FromHours(1)) return 0;
                _lastPurge = now;
            }
            return PurgeOlderThan(now.AddDays(-RetentionDays));
        }

        public static int PurgeOlderThan(DateTime cutoff)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var old = db.AiQueries.Where(x => x.DeletedAt == null && x.CreatedAt < cutoff).OrderBy(x => x.QueryID).Take(PurgeBatch).ToList();
                    foreach (var q in old) Erase(db, q);
                    var expired = db.AiSuggestions.Where(x => x.Status == "Proposed" && x.CreatedAt < cutoff).Take(PurgeBatch).ToList();
                    foreach (var s in expired) { s.Status = "Rejected"; s.DecidedAt = DateTime.Now; }
                    db.SaveChanges();
                    if (old.Count + expired.Count > 0)
                        AuditLogger.Log(AuditLogger.Actions.Delete, Module, "پاک‌سازی خودکار تاریخچه‌ی دستیار (Retention)", entityName: "MDR_AiQueries", newValues: new { queries = old.Count, suggestions = expired.Count, cutoff });
                    return old.Count + expired.Count;
                }
            }
            catch (Exception ex) { FailedR(ex); return 0; }
        }

        private static void Erase(MdrContext db, MdrAiQuery q)
        {
            db.AiQuerySources.RemoveRange(db.AiQuerySources.Where(s => s.QueryID == q.QueryID));
            q.Question = "[حذف‌شده]"; q.FeedbackNote = null; q.DeletedAt = DateTime.Now;
        }

        // ---------------- پیشنهاد اقدام (Human-in-the-loop) ----------------
        // پیش‌نمایش را می‌سازد؛ اجرا فقط با Decide(accept)
        public static MdrResult Propose(int uid, long queryId, string key, long entityId)
        {
            if (!PermissionHelper.HasAccess(uid, Page)) return MdrResult.Fail("tsk.err.notallowed");
            if (!PermissionHelper.HasAccess(uid, "MDR.Tasks")) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    if (!db.AiQueries.Any(x => x.QueryID == queryId && x.UserID == uid && x.DeletedAt == null)) return MdrResult.Fail("wf.err.notfound");
                    if (!db.AiQuerySources.Any(s => s.QueryID == queryId && s.SourceKey == key && s.EntityID == entityId)) return MdrResult.Fail("wf.err.notfound");
                    var info = MdrSearchService.Lookup(uid, key, new[] { entityId }).FirstOrDefault();
                    if (info == null) return MdrResult.Fail("wf.err.notfound");
                    var title = MdrAiText.Cut("پیگیری: " + info.Title, 200);
                    var ex = db.AiSuggestions.FirstOrDefault(x => x.UserID == uid && x.SourceKey == key && x.EntityID == entityId && x.Status == "Proposed" && x.Title == title);
                    if (ex != null) return MdrResult.Success(ex.SuggestionID);
                    var s2 = new MdrAiSuggestion { UserID = uid, QueryID = queryId, Kind = "Task", SourceKey = key, EntityID = entityId, Title = title, Status = "Proposed", CreatedAt = DateTime.Now };
                    db.AiSuggestions.Add(s2); db.SaveChanges();
                    return MdrResult.Success(s2.SuggestionID);
                }
            }
            catch (Exception ex) { return FailedR(ex); }
        }

        public static List<AiSuggestionRow> Suggestions(int uid, bool onlyProposed)
        {
            using (var db = new MdrContext())
            {
                var q = db.AiSuggestions.Where(x => x.UserID == uid);
                if (onlyProposed) q = q.Where(x => x.Status == "Proposed");
                return q.OrderByDescending(x => x.SuggestionID).Take(50).ToList().Select(Row).ToList();
            }
        }

        private static AiSuggestionRow Row(MdrAiSuggestion s)
        {
            return new AiSuggestionRow { SuggestionID = s.SuggestionID, Title = s.Title, Due = s.DueDate.HasValue ? s.DueDate.Value.ToString("yyyy-MM-dd") : null, SourceKey = s.SourceKey, EntityID = s.EntityID, Status = s.Status, TaskId = s.ResultTaskID };
        }

        // accept=false: رد. accept=true: ساخت تسک برای خودِ کاربر با ویرایش اختیاری عنوان/مهلت
        public static MdrResult Decide(int uid, long suggestionId, bool accept, string title, DateTime? due)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var s = db.AiSuggestions.FirstOrDefault(x => x.SuggestionID == suggestionId && x.UserID == uid);
                    if (s == null) return MdrResult.Fail("wf.err.notfound");
                    if (s.Status != "Proposed") return MdrResult.Fail("ai.err.decided");

                    if (!accept)
                    {
                        var n = db.Database.ExecuteSqlCommand("UPDATE dbo.MDR_AiSuggestions SET Status = N'Rejected', DecidedAt = GETDATE() WHERE SuggestionID = @p0 AND UserID = @p1 AND Status = N'Proposed'", suggestionId, uid);
                        return n == 1 ? MdrResult.Success(suggestionId) : MdrResult.Fail("ai.err.decided");
                    }

                    if (!PermissionHelper.HasAccess(uid, "MDR.Tasks")) return MdrResult.Fail("tsk.err.notallowed");
                    var t = string.IsNullOrWhiteSpace(title) ? s.Title : title.Trim();
                    if (t.Length == 0 || t.Length > 200) return MdrResult.Fail("tsk.err.title");
                    if (due.HasValue && due.Value.Date < DateTime.Today) return MdrResult.Fail("tsk.err.due");
                    string url = null, desc = "ایجادشده با تأیید کاربر بر پایه‌ی پیشنهاد هوش مصنوعی.";
                    if (s.SourceKey != null && s.EntityID.HasValue)
                    {
                        var info = MdrSearchService.Lookup(uid, s.SourceKey, new[] { s.EntityID.Value }).FirstOrDefault();
                        if (info == null) return MdrResult.Fail("wf.err.notfound");      // دسترسی به مبدأ از بین رفته
                        url = info.Url; desc += " مبدأ: " + MdrAiText.Cut(info.Title, 200);
                    }

                    // ادعای اتمیک: فقط یک بار Accept می‌شود
                    var claimed = db.Database.ExecuteSqlCommand("UPDATE dbo.MDR_AiSuggestions SET Status = N'Accepted', DecidedAt = GETDATE() WHERE SuggestionID = @p0 AND UserID = @p1 AND Status = N'Proposed'", suggestionId, uid);
                    if (claimed != 1) return MdrResult.Fail("ai.err.decided");

                    var r = MdrTaskService.Save(new MdrTaskService.TaskInput
                    {
                        Id = 0, Title = t, Description = desc, Priority = "Normal", AssigneeUserId = uid, DueAt = due,
                        SourceType = "AI", SourceRef = s.SourceKey == null ? null : s.SourceKey + ":" + s.EntityID, SourceUrl = url
                    }, uid, false, null);
                    if (!r.Ok)
                    {
                        db.Database.ExecuteSqlCommand("UPDATE dbo.MDR_AiSuggestions SET Status = N'Proposed', DecidedAt = NULL WHERE SuggestionID = @p0 AND UserID = @p1", suggestionId, uid);
                        return r;
                    }
                    db.Database.ExecuteSqlCommand("UPDATE dbo.MDR_AiSuggestions SET ResultTaskID = @p2, Title = @p3, DueDate = @p4 WHERE SuggestionID = @p0 AND UserID = @p1", suggestionId, uid, r.Id.Value, t, due.HasValue ? (object)due.Value.Date : DBNull.Value);
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "تأیید پیشنهاد هوش مصنوعی و ساخت تسک", entityName: "MDR_AiSuggestions", entityId: suggestionId, newValues: new { taskId = r.Id });
                    return MdrResult.Success(r.Id);
                }
            }
            catch (Exception ex) { return FailedR(ex); }
        }

        // ---------------- AI در متن کار ----------------
        private class TextRow { public string T { get; set; } }

        private static string TextOf(MdrContext db, string key, long id)
        {
            string sql;
            switch (key)
            {
                case "ltr": sql = "SELECT ISNULL(Subject, N'') + CHAR(10) + ISNULL(Body, N'') AS T FROM dbo.MDR_Letters WHERE LetterID = @p0"; break;
                case "tsk": sql = "SELECT ISNULL(Title, N'') + CHAR(10) + ISNULL(Description, N'') + CHAR(10) + ISNULL(ResultText, N'') AS T FROM dbo.MDR_Tasks WHERE TaskID = @p0"; break;
                case "doc": sql = "SELECT ISNULL(Title, N'') + CHAR(10) + ISNULL(Description, N'') AS T FROM dbo.MDR_Documents WHERE DocumentID = @p0"; break;
                default: return null;
            }
            var r = db.Database.SqlQuery<TextRow>(sql, id).FirstOrDefault();
            return r == null ? null : r.T;
        }

        // دسترسی: صفحه‌ی MDR.AI.Context + دیدن خودِ موجودیت (همان ACL جستجو). عدم دسترسی = «پیدا نشد»
        private static MdrSearchService.EntityInfo Visible(int uid, string key, long id, out string err)
        {
            err = null;
            if (!PermissionHelper.HasAccess(uid, ContextPage)) { err = "tsk.err.notallowed"; return null; }
            if (!TextKeys.Contains(key)) { err = "ai.err.unsupported"; return null; }
            var info = MdrSearchService.Lookup(uid, key, new[] { id }).FirstOrDefault();
            if (info == null) err = "wf.err.notfound";
            return info;
        }

        public static AiContextResult Context(int uid, string key, long id)
        {
            string err; var info = Visible(uid, key, id, out err);
            if (info == null) return new AiContextResult { Ok = false, Error = err };
            using (var db = new MdrContext())
            {
                var text = TextOf(db, key, id) ?? "";
                var sum = MdrAiText.Summarize(text);
                var w = new List<string> { "ai.warn.verify" };
                if (info.Level >= 1) w.Add("ai.warn.confidential");
                AuditLogger.Log(AuditLogger.Actions.View, Module, "خلاصه‌سازی در متن کار", entityName: key, entityId: id);
                return new AiContextResult { Ok = true, Key = key, Id = id, Title = info.Title, Url = info.Url, Level = info.Level, Summary = sum, TooShort = sum == null, Warnings = w };
            }
        }

        // کاندیدهای تسک از متن؛ هر کدام پیشنهاد «Proposed» ثبت می‌شود (تکراری ساخته نمی‌شود) و فقط با Decide اجرا می‌شود
        public static List<AiSuggestionRow> GenerateTasks(int uid, string key, long id, out string error)
        {
            error = null; string err; var info = Visible(uid, key, id, out err);
            if (info == null) { error = err; return null; }
            if (!PermissionHelper.HasAccess(uid, "MDR.Tasks")) { error = "tsk.err.notallowed"; return null; }
            using (var db = new MdrContext())
            {
                var items = MdrAiText.ActionItems(TextOf(db, key, id) ?? "");
                var res = new List<AiSuggestionRow>();
                foreach (var it in items)
                {
                    var ex = db.AiSuggestions.FirstOrDefault(x => x.UserID == uid && x.SourceKey == key && x.EntityID == id && x.Title == it.Title && x.Status == "Proposed");
                    if (ex == null)
                    {
                        ex = new MdrAiSuggestion { UserID = uid, Kind = "Task", SourceKey = key, EntityID = id, Title = it.Title, DueDate = it.Due, Status = "Proposed", CreatedAt = DateTime.Now };
                        db.AiSuggestions.Add(ex); db.SaveChanges();
                    }
                    res.Add(Row(ex));
                }
                AuditLogger.Log(AuditLogger.Actions.View, Module, "استخراج تسک در متن کار", entityName: key, entityId: id, newValues: new { candidates = res.Count });
                return res;
            }
        }

        // موارد مشابه در همان منبع، فقط از میان آنچه کاربر می‌بیند؛ ملاک: شباهت واژگانی عنوان+متن (Jaccard)
        public static List<AiSimilar> Similar(int uid, string key, long id, out string error)
        {
            error = null; string err; var info = Visible(uid, key, id, out err);
            if (info == null) { error = err; return null; }
            using (var db = new MdrContext())
            {
                var text = (info.Title + " " + (TextOf(db, key, id) ?? ""));
                var kw = MdrAiText.Keywords(info.Title);
                if (kw.Length == 0) return new List<AiSimilar>();
                int used;
                var o = Retrieve(uid, kw, key, out used, id);
                if (o == null || o.Error != null) return new List<AiSimilar>();
                var hits = o.Sources.SelectMany(s => s.Items).Where(h => h.Id != id).ToList();
                var visible = MdrSearchService.Lookup(uid, key, hits.Select(h => h.Id)).Select(e => e.Id).ToList();
                return hits.Where(h => visible.Contains(h.Id))
                    .Select(h => new AiSimilar { Key = key, Id = h.Id, Title = h.Title, Url = h.Url, Percent = MdrAiText.Similarity(text, h.Title + " " + h.Snippet) })
                    .OrderByDescending(x => x.Percent).Take(5).ToList();
            }
        }

        // ---------------- helpers ----------------
        private static AiResult Fail(string code) { return new AiResult { Ok = false, Error = code, Message = MdrTextOrNull(code) }; }
        private static string MdrTextOrNull(string code) { return Infrastructure.MdrText.T(code); }

        private static AiResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای دستیار هوشمند", newValues: new { ex.Message }, isSuccess: false);
            return Fail("wf.err.unexpected");
        }

        private static MdrResult FailedR(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای دستیار هوشمند", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
