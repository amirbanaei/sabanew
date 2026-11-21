using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 6 | خواندن چت (فهرست گفتگوها، پیام‌ها، مشخصات گفتگو)
    // همه‌ی خروجی‌ها فقط برای عضو فعال است و برای گفتگوی Entity فقط تا وقتی که خود کاربر موجودیت را می‌بیند.
    // پیش‌نمایش آخرین پیام برای گفتگوهای قابل‌دسترس است؛ گفتگوی بدون دسترسی اصلاً در فهرست نمی‌آید.
    // =====================================================
    public class ChatThreadRow
    {
        public long ThreadID { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string EntityType { get; set; }
        public long? EntityID { get; set; }
        public string EntityUrl { get; set; }
        public int Unread { get; set; }
        public string LastPreview { get; set; }
        public string LastSender { get; set; }
        public string LastAt { get; set; }
        public int Members { get; set; }
        public bool IsMuted { get; set; }
        public bool IsArchived { get; set; }
    }

    public class ChatFileRow { public Guid FileGuid { get; set; } public string Name { get; set; } public long Size { get; set; } }

    public class ChatMessageRow
    {
        public long MessageID { get; set; }
        public int SenderUserID { get; set; }
        public string Sender { get; set; }
        public string Body { get; set; }
        public bool Deleted { get; set; }
        public string At { get; set; }
        public bool Edited { get; set; }
        public bool Mine { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public long? ReplyTo { get; set; }
        public string ReplySender { get; set; }
        public string ReplySnippet { get; set; }
        public bool Forwarded { get; set; }
        public bool HasAttachment { get; set; }
        public List<ChatFileRow> Files { get; set; }
        public List<string> Mentions { get; set; }
    }

    public class ChatMemberRow { public int UserID { get; set; } public string Name { get; set; } public string Role { get; set; } }

    public class ChatThreadInfo
    {
        public long ThreadID { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string EntityType { get; set; }
        public long? EntityID { get; set; }
        public string EntityUrl { get; set; }
        public string MyRole { get; set; }
        public bool IsMuted { get; set; }
        public bool IsArchived { get; set; }
        public bool CanAdd { get; set; }
        public bool CanRename { get; set; }
        public bool CanArchive { get; set; }
        public List<ChatMemberRow> Members { get; set; }
    }

    public class ChatMessagesResult { public bool HasMore { get; set; } public long? LastId { get; set; } public List<ChatMessageRow> Items { get; set; } }

    public static class MdrChatReadService
    {
        private class UnreadRow { public long ThreadID { get; set; } public int N { get; set; } }

        private static string Snip(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " "); return s.Length > n ? s.Substring(0, n) + "…" : s; }

        // عنوان و لینک موجودیت برای هر گفتگوی Entity (فقط آن‌هایی که خود کاربر می‌بیند)
        private static Dictionary<string, MdrSearchService.EntityInfo> VisibleEntities(MdrContext db, int uid, IEnumerable<MdrChatThread> threads)
        {
            var res = new Dictionary<string, MdrSearchService.EntityInfo>();
            foreach (var g in threads.Where(t => t.Kind == "Entity").GroupBy(t => t.EntityType))
            {
                if (g.Key == "cas")
                {
                    if (!PermissionHelper.HasAccess(uid, "MDR.Cases")) continue;
                    var mgr = PermissionHelper.CanEdit(uid, "MDR.Cases");
                    var ids = g.Select(t => t.EntityID.Value).ToList();
                    foreach (var c in db.Cases.Where(c => ids.Contains(c.CaseID)).ToList())
                        if (MdrCaseService.AccessFor(db, c, uid, mgr).CanView)
                            res["cas:" + c.CaseID] = new MdrSearchService.EntityInfo { Key = "cas", Id = c.CaseID, Title = c.Number + " — " + c.Title, Status = c.Status, Url = "/Mdr/Cases#id=" + c.CaseID };
                }
                else
                    foreach (var e in MdrSearchService.Lookup(uid, g.Key, g.Select(t => t.EntityID.Value))) res[g.Key + ":" + e.Id] = e;
            }
            return res;
        }

        public static List<ChatThreadRow> Threads(int uid)
        {
            using (var db = new MdrContext())
            {
                var mine = db.ChatMembers.Where(m => m.UserID == uid && m.IsActive).ToList();
                var ids = mine.Select(m => m.ThreadID).ToList();
                if (ids.Count == 0) return new List<ChatThreadRow>();

                var threads = db.ChatThreads.Where(t => ids.Contains(t.ThreadID)).OrderByDescending(t => t.LastMessageAt ?? t.CreatedAt).ThenByDescending(t => t.ThreadID).Take(200).ToList();
                var tids = threads.Select(t => t.ThreadID).ToList();
                var ents = VisibleEntities(db, uid, threads);

                var others = db.ChatMembers.Where(m => tids.Contains(m.ThreadID) && m.IsActive && m.UserID != uid).Select(m => new { m.ThreadID, m.UserID }).ToList();
                var counts = others.GroupBy(o => o.ThreadID).ToDictionary(g => g.Key, g => g.Count() + 1);
                var names = MdrChatService.Names(db, others.Select(o => o.UserID));

                var unread = db.Database.SqlQuery<UnreadRow>(
                    "SELECT m.ThreadID AS ThreadID, COUNT(*) AS N FROM dbo.MDR_ChatMessages m JOIN dbo.MDR_ChatMembers mem ON mem.ThreadID = m.ThreadID AND mem.UserID = @p0 AND mem.IsActive = 1 " +
                    "WHERE m.MessageID > ISNULL(mem.LastReadMessageID, 0) AND m.SenderUserID <> @p0 AND m.DeletedAt IS NULL GROUP BY m.ThreadID", uid).ToDictionary(r => r.ThreadID, r => r.N);

                var last = db.ChatMessages.Where(m => tids.Contains(m.ThreadID) && m.DeletedAt == null &&
                        m.MessageID == db.ChatMessages.Where(z => z.ThreadID == m.ThreadID && z.DeletedAt == null).Max(z => z.MessageID))
                    .Select(m => new { m.ThreadID, m.Body, m.SenderUserID, m.CreatedAt }).ToList().ToDictionary(x => x.ThreadID);
                var senderNames = MdrChatService.Names(db, last.Values.Select(x => x.SenderUserID));

                var rows = new List<ChatThreadRow>();
                foreach (var t in threads)
                {
                    var r = new ChatThreadRow
                    {
                        ThreadID = t.ThreadID, Kind = t.Kind, Title = t.Title, EntityType = t.EntityType, EntityID = t.EntityID, IsArchived = t.IsArchived,
                        Members = counts.ContainsKey(t.ThreadID) ? counts[t.ThreadID] : 1,
                        IsMuted = mine.First(m => m.ThreadID == t.ThreadID).IsMuted,
                        Unread = unread.ContainsKey(t.ThreadID) ? unread[t.ThreadID] : 0
                    };
                    if (t.Kind == "Direct")
                    {
                        var o = others.FirstOrDefault(x => x.ThreadID == t.ThreadID);
                        r.Title = o != null && names.ContainsKey(o.UserID) ? names[o.UserID] : "—";
                    }
                    else if (t.Kind == "Entity")
                    {
                        MdrSearchService.EntityInfo e;
                        if (!ents.TryGetValue(t.EntityType + ":" + t.EntityID, out e)) continue;   // دسترسی به موجودیت ندارد → گفتگو نمایش داده نمی‌شود
                        r.Title = e.Title; r.EntityUrl = e.Url;
                    }
                    if (last.ContainsKey(t.ThreadID))
                    {
                        var l = last[t.ThreadID];
                        r.LastPreview = Snip(l.Body, 70); r.LastAt = l.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                        r.LastSender = senderNames.ContainsKey(l.SenderUserID) ? senderNames[l.SenderUserID] : null;
                    }
                    rows.Add(r);
                }
                return rows;
            }
        }

        // null: دسترسی ندارد
        public static ChatThreadInfo Thread(int uid, long threadId)
        {
            using (var db = new MdrContext())
            {
                MdrChatThread t;
                var me = MdrChatService.Access(db, threadId, uid, out t);
                if (me == null) return null;

                var ms = db.ChatMembers.Where(m => m.ThreadID == threadId && m.IsActive).OrderBy(m => m.JoinedAt).ToList();
                var names = MdrChatService.Names(db, ms.Select(m => m.UserID));
                var info = new ChatThreadInfo
                {
                    ThreadID = t.ThreadID, Kind = t.Kind, Title = t.Title, EntityType = t.EntityType, EntityID = t.EntityID, MyRole = me.Role, IsMuted = me.IsMuted, IsArchived = t.IsArchived,
                    Members = ms.Select(m => new ChatMemberRow { UserID = m.UserID, Name = names.ContainsKey(m.UserID) ? names[m.UserID] : "#" + m.UserID, Role = m.Role }).ToList()
                };
                info.CanAdd = !t.IsArchived && ((t.Kind == "Group" && me.Role == "Owner") || t.Kind == "Entity");
                info.CanRename = !t.IsArchived && t.Kind == "Group" && me.Role == "Owner";
                info.CanArchive = !t.IsArchived && t.Kind == "Group" && me.Role == "Owner";
                if (t.Kind == "Direct") info.Title = info.Members.Where(m => m.UserID != uid).Select(m => m.Name).FirstOrDefault() ?? "—";
                if (t.Kind == "Entity")
                {
                    var e = VisibleEntities(db, uid, new[] { t }).Values.FirstOrDefault();
                    info.Title = e == null ? null : e.Title; info.EntityUrl = e == null ? null : e.Url;
                }
                return info;
            }
        }

        // afterId > 0: پیام‌های تازه‌تر (برای polling)؛ beforeId > 0: صفحه‌ی قدیمی‌تر؛ هیچ‌کدام: آخرین پیام‌ها
        public static ChatMessagesResult Messages(int uid, long threadId, long afterId, long beforeId, int take = 50)
        {
            take = Math.Max(1, Math.Min(100, take));
            using (var db = new MdrContext())
            {
                MdrChatThread t;
                var me = MdrChatService.Access(db, threadId, uid, out t);
                if (me == null) return null;

                var q = db.ChatMessages.Where(m => m.ThreadID == threadId);
                List<MdrChatMessage> rows; var hasMore = false;
                if (afterId > 0) rows = q.Where(m => m.MessageID > afterId).OrderBy(m => m.MessageID).Take(take).ToList();
                else
                {
                    if (beforeId > 0) q = q.Where(m => m.MessageID < beforeId);
                    var desc = q.OrderByDescending(m => m.MessageID).Take(take + 1).ToList();
                    hasMore = desc.Count > take;
                    rows = desc.Take(take).OrderBy(m => m.MessageID).ToList();
                }

                var ids = rows.Select(m => m.MessageID).ToList();
                var replyIds = rows.Where(m => m.ReplyToMessageID.HasValue).Select(m => m.ReplyToMessageID.Value).Distinct().ToList();
                var replies = replyIds.Count == 0 ? new Dictionary<long, MdrChatMessage>() : db.ChatMessages.Where(m => replyIds.Contains(m.MessageID) && m.ThreadID == threadId).ToDictionary(m => m.MessageID);
                var mentions = db.ChatMentions.Where(m => ids.Contains(m.MessageID)).ToList();
                var nameIds = rows.Select(m => m.SenderUserID).Concat(replies.Values.Select(r => r.SenderUserID)).Concat(mentions.Select(m => m.UserID)).ToList();
                var names = MdrChatService.Names(db, nameIds);
                Func<int, string> nm = x => names.ContainsKey(x) ? names[x] : "#" + x;
                var isOwner = t.Kind == "Group" && me.Role == "Owner";
                var now = DateTime.Now;

                var items = rows.Select(m =>
                {
                    var deleted = m.DeletedAt != null;
                    MdrChatMessage rep = null;
                    if (m.ReplyToMessageID.HasValue) replies.TryGetValue(m.ReplyToMessageID.Value, out rep);
                    var r = new ChatMessageRow
                    {
                        MessageID = m.MessageID, SenderUserID = m.SenderUserID, Sender = nm(m.SenderUserID), Deleted = deleted, Body = deleted ? null : m.Body,
                        At = m.CreatedAt.ToString("yyyy-MM-dd HH:mm"), Edited = m.EditedAt != null && !deleted, Mine = m.SenderUserID == uid,
                        CanEdit = !deleted && !t.IsArchived && m.SenderUserID == uid && (now - m.CreatedAt).TotalMinutes <= MdrChatService.EditMinutes,
                        CanDelete = !deleted && (m.SenderUserID == uid || isOwner), ReplyTo = m.ReplyToMessageID,
                        ReplySender = rep != null ? nm(rep.SenderUserID) : null, ReplySnippet = rep != null && rep.DeletedAt == null ? Snip(rep.Body, 80) : null,
                        Forwarded = m.ForwardedFromMessageID.HasValue && !deleted, HasAttachment = m.HasAttachment && !deleted,
                        Mentions = deleted ? new List<string>() : mentions.Where(x => x.MessageID == m.MessageID).Select(x => nm(x.UserID)).ToList(),
                        Files = new List<ChatFileRow>()
                    };
                    if (r.HasAttachment)
                    {
                        try
                        {
                            r.Files = FileManager.GetFiles("MDR_ChatMessages", m.MessageID.ToString())
                                .Select(f => new ChatFileRow { FileGuid = f.FileGuid, Name = f.Title ?? f.OriginalFileName, Size = f.FileSize }).ToList();
                        }
                        catch (Exception) { /* خطای فایل نباید خواندن پیام‌ها را بشکند */ }
                    }
                    return r;
                }).ToList();

                return new ChatMessagesResult { HasMore = hasMore, Items = items, LastId = rows.Count == 0 ? (long?)null : rows[rows.Count - 1].MessageID };
            }
        }
    }
}
