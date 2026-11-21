using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 6 | چت داخلی Context-aware (پنل ۱۲)
    // انواع گفتگو: Direct (دقیقاً یکی برای هر دو نفر)، Group (مالک دارد)، Entity (یکی برای هر موجودیت).
    // قواعد کلیدی:
    //  - فقط عضو فعال می‌خواند/می‌نویسد؛ وجود/عدم وجود گفتگو برای غیرعضو یکسان است (پیام «پیدا نشد»).
    //  - گفتگوی Entity: هر عملی فقط تا وقتی که خود کاربر به موجودیت دسترسی دارد؛ عضو جدید هم باید موجودیت را ببیند
    //    («عضو بدون دسترسی» رد می‌شود) و اگر دسترسی کاربر بعداً از بین برود گفتگو برایش بسته می‌شود.
    //  - Mention فقط برای عضو فعال؛ تسک از پیام فقط برای عضو فعال گفتگو؛
    //    Forward از گفتگوی موجودیتِ محرمانه/سری به جای دیگر ممنوع (نشت محرمانگی).
    //  - اعلان‌ها هرگز متن پیام ندارند؛ برای هر نفر فقط «Mention» یا «اولین پیام خوانده‌نشده» اعلان می‌شود.
    //  - حذف پیام نرم است (فقط فرستنده یا مالک گروه)؛ ویرایش فقط فرستنده تا ۱۵ دقیقه؛ حداکثر ۳۰ پیام در دقیقه برای هر کاربر.
    // =====================================================
    public static class MdrChatService
    {
        private const string Module = "Chat";
        public const int MaxBody = 4000;
        public const int MaxMembers = 100;
        public const int EditMinutes = 15;
        // پیش‌فرض ۳۰ پیام در دقیقه برای هر کاربر؛ قابل تنظیم فقط برای تست
        public static int RatePerMinute = 30;
        public static readonly string[] EntityTypes = { "req", "tsk", "ltr", "doc", "wf", "ctr", "cas" };

        private class UserName { public int Id { get; set; } public string Name { get; set; } }

        public static Dictionary<int, string> Names(MdrContext db, IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            // فقط عدد صحیح در SQL می‌آید
            return db.Database.SqlQuery<UserName>(
                "SELECT usr_ID AS Id, LTRIM(RTRIM(ISNULL(usr_Name,'') + ' ' + ISNULL(usr_Family,''))) AS Name FROM Usser.tbUsers WHERE usr_ID IN (" + string.Join(",", list) + ")")
                .ToDictionary(r => r.Id, r => r.Name);
        }

        private static bool ActiveUserExists(MdrContext db, int userId)
        {
            return db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", userId).First() > 0;
        }

        // ---------------- موجودیت ----------------
        public static bool CanSeeEntity(int uid, string type, long id)
        {
            if (type == "cas")
            {
                if (!PermissionHelper.HasAccess(uid, "MDR.Cases")) return false;
                using (var db = new MdrContext())
                {
                    var c = db.Cases.FirstOrDefault(x => x.CaseID == id);
                    return c != null && MdrCaseService.AccessFor(db, c, uid, PermissionHelper.CanEdit(uid, "MDR.Cases")).CanView;
                }
            }
            return MdrSearchService.Lookup(uid, type, new[] { id }).Count > 0;
        }

        // سطح محرمانگی موجودیت (۰ عادی، ۱ محرمانه، ۲ سری)؛ بدون بررسی دسترسی
        private static int EntityLevel(string type, long id)
        {
            if (type == "cas")
            {
                using (var db = new MdrContext())
                {
                    var c = db.Cases.Where(x => x.CaseID == id).Select(x => x.Confidentiality).FirstOrDefault();
                    return c == null ? 0 : MdrCaseService.Level(c);
                }
            }
            var e = MdrSearchService.Lookup(null, type, new[] { id }).FirstOrDefault();
            return e == null ? 0 : e.Level;
        }

        // عضو فعال گفتگو با رعایت دسترسی به موجودیت؛ در غیر این صورت null (علت لو نمی‌رود)
        public static MdrChatMember Access(MdrContext db, long threadId, int uid, out MdrChatThread thread)
        {
            thread = db.ChatThreads.FirstOrDefault(t => t.ThreadID == threadId);
            if (thread == null) return null;
            var m = db.ChatMembers.FirstOrDefault(x => x.ThreadID == threadId && x.UserID == uid && x.IsActive);
            if (m == null) return null;
            if (thread.Kind == "Entity" && !CanSeeEntity(uid, thread.EntityType, thread.EntityID.Value)) return null;
            return m;
        }

        public static bool HasThreadAccess(int uid, long threadId)
        {
            using (var db = new MdrContext()) { MdrChatThread t; return Access(db, threadId, uid, out t) != null; }
        }

        // ---------------- ایجاد گفتگو ----------------
        public static MdrResult OpenDirect(int uid, int otherUserId)
        {
            if (uid == otherUserId) return MdrResult.Fail("chat.err.self");
            var key = Math.Min(uid, otherUserId) + "_" + Math.Max(uid, otherUserId);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var db = new MdrContext())
                    {
                        var t = db.ChatThreads.FirstOrDefault(x => x.DirectKey == key);
                        if (t == null)
                        {
                            if (!ActiveUserExists(db, otherUserId)) return MdrResult.Fail("wf.err.baduser");
                            t = new MdrChatThread { Kind = "Direct", DirectKey = key, CreatedBy = uid, CreatedAt = DateTime.Now };
                            db.ChatThreads.Add(t);
                            db.SaveChanges();
                        }
                        // عضویت هر دو نفر (اگر قبلاً ساخته شده و یکی غیرفعال شده، دوباره فعال)
                        EnsureMember(db, t.ThreadID, uid, "Member");
                        EnsureMember(db, t.ThreadID, otherUserId, "Member");
                        db.SaveChanges();
                        return MdrResult.Success(t.ThreadID);
                    }
                }
                catch (DbUpdateException ex) { if (!IsDuplicate(ex) || attempt == 1) return Failed(ex); }
                catch (Exception ex) { return Failed(ex); }
            }
            return MdrResult.Fail("wf.err.unexpected");
        }

        private static void EnsureMember(MdrContext db, long threadId, int userId, string role)
        {
            var m = db.ChatMembers.FirstOrDefault(x => x.ThreadID == threadId && x.UserID == userId);
            if (m == null) db.ChatMembers.Add(new MdrChatMember { ThreadID = threadId, UserID = userId, Role = role, JoinedAt = DateTime.Now, IsActive = true });
            else if (!m.IsActive) { m.IsActive = true; m.JoinedAt = DateTime.Now; }
        }

        public static MdrResult CreateGroup(int uid, string title, IEnumerable<int> memberIds)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 200) return MdrResult.Fail("chat.err.title");
            var others = (memberIds ?? new int[0]).Where(x => x != uid).Distinct().ToList();
            if (others.Count == 0) return MdrResult.Fail("chat.err.nomembers");
            if (others.Count + 1 > MaxMembers) return MdrResult.Fail("chat.err.limit");

            try
            {
                using (var db = new MdrContext())
                {
                    foreach (var o in others) if (!ActiveUserExists(db, o)) return MdrResult.Fail("wf.err.baduser");

                    var t = new MdrChatThread { Kind = "Group", Title = title, CreatedBy = uid, CreatedAt = DateTime.Now };
                    db.ChatThreads.Add(t);
                    db.SaveChanges();
                    db.ChatMembers.Add(new MdrChatMember { ThreadID = t.ThreadID, UserID = uid, Role = "Owner", JoinedAt = DateTime.Now, IsActive = true });
                    foreach (var o in others) db.ChatMembers.Add(new MdrChatMember { ThreadID = t.ThreadID, UserID = o, Role = "Member", JoinedAt = DateTime.Now, IsActive = true });
                    db.SaveChanges();

                    foreach (var o in others) Notify(o, "شما به گروه «" + title + "» افزوده شدید", t.ThreadID);
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد گروه چت", entityName: "MDR_ChatThreads", entityId: t.ThreadID, entityTitle: title, newValues: new { members = others.Count + 1 });
                    return MdrResult.Success(t.ThreadID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // گفتگوی مرتبط با یک موجودیت: یکی برای هر موجودیت؛ کاربر باید خود موجودیت را ببیند
        public static MdrResult OpenEntity(int uid, string type, long entityId)
        {
            if (!EntityTypes.Contains(type ?? "")) return MdrResult.Fail("case.err.type");
            if (entityId <= 0 || !CanSeeEntity(uid, type, entityId)) return MdrResult.Fail("case.err.cantsee");

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var db = new MdrContext())
                    {
                        var t = db.ChatThreads.FirstOrDefault(x => x.Kind == "Entity" && x.EntityType == type && x.EntityID == entityId);
                        var created = false;
                        if (t == null)
                        {
                            t = new MdrChatThread { Kind = "Entity", EntityType = type, EntityID = entityId, CreatedBy = uid, CreatedAt = DateTime.Now };
                            db.ChatThreads.Add(t);
                            db.SaveChanges();
                            created = true;
                        }
                        EnsureMember(db, t.ThreadID, uid, created ? "Owner" : "Member");
                        db.SaveChanges();
                        if (created) AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد گفتگوی موجودیت", entityName: "MDR_ChatThreads", entityId: t.ThreadID, entityTitle: type + ":" + entityId);
                        return MdrResult.Success(t.ThreadID);
                    }
                }
                catch (DbUpdateException ex) { if (!IsDuplicate(ex) || attempt == 1) return Failed(ex); }
                catch (Exception ex) { return Failed(ex); }
            }
            return MdrResult.Fail("wf.err.unexpected");
        }

        // ---------------- اعضا ----------------
        public static MdrResult AddMember(int uid, long threadId, int userId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    if (t.Kind == "Direct") return MdrResult.Fail("chat.err.direct");
                    if (t.IsArchived) return MdrResult.Fail("chat.err.archived");
                    if (t.Kind == "Group" && me.Role != "Owner") return MdrResult.Fail("tsk.err.notallowed");
                    if (!ActiveUserExists(db, userId)) return MdrResult.Fail("wf.err.baduser");
                    if (t.Kind == "Entity" && !CanSeeEntity(userId, t.EntityType, t.EntityID.Value)) return MdrResult.Fail("chat.err.noaccess");

                    var existing = db.ChatMembers.FirstOrDefault(x => x.ThreadID == threadId && x.UserID == userId);
                    if (existing != null && existing.IsActive) return MdrResult.Fail("chat.err.already");
                    if (db.ChatMembers.Count(x => x.ThreadID == threadId && x.IsActive) >= MaxMembers) return MdrResult.Fail("chat.err.limit");

                    EnsureMember(db, threadId, userId, "Member");
                    db.SaveChanges();
                    Notify(userId, t.Kind == "Group" ? "شما به گروه «" + t.Title + "» افزوده شدید" : "شما به یک گفتگوی کاری افزوده شدید", threadId);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "افزودن عضو به گفتگو", entityName: "MDR_ChatThreads", entityId: threadId, newValues: new { userId });
                    return MdrResult.Success(threadId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // خودِ کاربر همیشه می‌تواند ترک کند؛ حذف دیگران فقط مالک گروه. مالک که ترک کند، قدیمی‌ترین عضو مالک می‌شود.
        public static MdrResult RemoveMember(int uid, long threadId, int userId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    if (t.Kind == "Direct") return MdrResult.Fail("chat.err.direct");
                    if (userId != uid && !(t.Kind == "Group" && me.Role == "Owner")) return MdrResult.Fail("tsk.err.notallowed");

                    var m = db.ChatMembers.FirstOrDefault(x => x.ThreadID == threadId && x.UserID == userId && x.IsActive);
                    if (m == null) return MdrResult.Fail("wf.err.notfound");
                    var wasOwner = m.Role == "Owner";
                    m.IsActive = false; m.Role = "Member";

                    if (wasOwner)
                    {
                        var next = db.ChatMembers.Where(x => x.ThreadID == threadId && x.IsActive && x.UserID != userId).OrderBy(x => x.JoinedAt).FirstOrDefault();
                        if (next != null) next.Role = "Owner";
                        else if (t.Kind == "Group") t.IsArchived = true;      // کسی نمانده
                    }
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, userId == uid ? "ترک گفتگو" : "حذف عضو از گفتگو", entityName: "MDR_ChatThreads", entityId: threadId, newValues: new { userId });
                    return MdrResult.Success(threadId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Rename(int uid, long threadId, string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 200) return MdrResult.Fail("chat.err.title");
            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    if (t.Kind != "Group" || me.Role != "Owner") return MdrResult.Fail("tsk.err.notallowed");
                    if (t.IsArchived) return MdrResult.Fail("chat.err.archived");
                    var old = t.Title; t.Title = title;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "تغییر نام گروه", entityName: "MDR_ChatThreads", entityId: threadId, oldValues: new { Title = old }, newValues: new { Title = title });
                    return MdrResult.Success(threadId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // بایگانی گروه (خواندنی می‌ماند، ارسال بسته می‌شود)؛ دلیل اجباری
        public static MdrResult Archive(int uid, long threadId, string reason)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    if (t.Kind != "Group" || me.Role != "Owner") return MdrResult.Fail("tsk.err.notallowed");
                    if (t.IsArchived) return MdrResult.Fail("chat.err.archived");
                    t.IsArchived = true;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "بایگانی گروه", entityName: "MDR_ChatThreads", entityId: threadId, newValues: new { reason });
                    return MdrResult.Success(threadId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult SetMuted(int uid, long threadId, bool muted)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    me.IsMuted = muted;
                    db.SaveChanges();
                    return MdrResult.Success(threadId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- پیام ----------------
        public static MdrResult Send(int uid, long threadId, string body, long? replyTo, IEnumerable<int> mentions)
        {
            body = (body ?? "").Trim();
            if (body.Length == 0 || body.Length > MaxBody) return MdrResult.Fail("chat.err.body");

            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    if (t.IsArchived) return MdrResult.Fail("chat.err.archived");

                    var since = DateTime.Now.AddMinutes(-1);
                    if (db.ChatMessages.Count(m => m.SenderUserID == uid && m.CreatedAt > since) >= RatePerMinute) return MdrResult.Fail("chat.err.rate");

                    if (replyTo.HasValue && !db.ChatMessages.Any(m => m.MessageID == replyTo.Value && m.ThreadID == threadId && m.DeletedAt == null)) return MdrResult.Fail("chat.err.reply");

                    var mentionIds = (mentions ?? new int[0]).Where(x => x != uid).Distinct().ToList();
                    if (mentionIds.Count > 0)
                    {
                        var active = db.ChatMembers.Where(x => x.ThreadID == threadId && x.IsActive).Select(x => x.UserID).ToList();
                        if (mentionIds.Any(x => !active.Contains(x))) return MdrResult.Fail("chat.err.mention");
                        if (t.Kind == "Entity" && mentionIds.Any(x => !CanSeeEntity(x, t.EntityType, t.EntityID.Value))) return MdrResult.Fail("chat.err.noaccess");
                    }

                    // وضعیت خوانده‌نشده‌ی هر عضو «قبل از» این پیام (برای اعلان فقط اولین پیام)
                    var members = db.ChatMembers.Where(x => x.ThreadID == threadId && x.IsActive && x.UserID != uid).ToList();
                    var hadUnread = new Dictionary<int, bool>();
                    foreach (var m in members)
                    {
                        var lr = m.LastReadMessageID ?? 0;
                        hadUnread[m.UserID] = db.ChatMessages.Any(x => x.ThreadID == threadId && x.MessageID > lr && x.SenderUserID != m.UserID && x.DeletedAt == null);
                    }

                    var msg = new MdrChatMessage { ThreadID = threadId, SenderUserID = uid, Body = body, ReplyToMessageID = replyTo, CreatedAt = DateTime.Now };
                    db.ChatMessages.Add(msg);
                    t.LastMessageAt = msg.CreatedAt;
                    db.SaveChanges();
                    foreach (var mid in mentionIds) db.ChatMentions.Add(new MdrChatMention { MessageID = msg.MessageID, UserID = mid });
                    me.LastReadMessageID = msg.MessageID;
                    db.SaveChanges();

                    var senderName = Names(db, new[] { uid }).Values.FirstOrDefault();
                    foreach (var m in members)
                    {
                        if (m.IsMuted && !mentionIds.Contains(m.UserID)) continue;
                        if (mentionIds.Contains(m.UserID)) Notify(m.UserID, "شما در یک گفتگو نام برده شدید", threadId, senderName);
                        else if (!hadUnread[m.UserID]) Notify(m.UserID, "پیام جدید در گفتگو", threadId, senderName);
                    }
                    return MdrResult.Success(msg.MessageID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Edit(int uid, long messageId, string body)
        {
            body = (body ?? "").Trim();
            if (body.Length == 0 || body.Length > MaxBody) return MdrResult.Fail("chat.err.body");
            try
            {
                using (var db = new MdrContext())
                {
                    var msg = db.ChatMessages.FirstOrDefault(m => m.MessageID == messageId);
                    if (msg == null) return MdrResult.Fail("wf.err.notfound");
                    MdrChatThread t;
                    if (Access(db, msg.ThreadID, uid, out t) == null) return MdrResult.Fail("wf.err.notfound");
                    if (msg.SenderUserID != uid) return MdrResult.Fail("tsk.err.notallowed");
                    if (msg.DeletedAt != null) return MdrResult.Fail("chat.err.deleted");
                    if (t.IsArchived) return MdrResult.Fail("chat.err.archived");
                    if ((DateTime.Now - msg.CreatedAt).TotalMinutes > EditMinutes) return MdrResult.Fail("chat.err.editwindow");
                    msg.Body = body; msg.EditedAt = DateTime.Now;
                    db.SaveChanges();
                    return MdrResult.Success(messageId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // حذف نرم: فرستنده یا مالک گروه؛ حذف پیام دیگران توسط مالک گروه دلیل اجباری دارد
        public static MdrResult Delete(int uid, long messageId, string reason)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var msg = db.ChatMessages.FirstOrDefault(m => m.MessageID == messageId);
                    if (msg == null) return MdrResult.Fail("wf.err.notfound");
                    MdrChatThread t;
                    var me = Access(db, msg.ThreadID, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    if (msg.DeletedAt != null) return MdrResult.Fail("chat.err.deleted");

                    var mine = msg.SenderUserID == uid;
                    var moderator = t.Kind == "Group" && me.Role == "Owner";
                    if (!mine && !moderator) return MdrResult.Fail("tsk.err.notallowed");
                    if (!mine && string.IsNullOrWhiteSpace(reason)) return MdrResult.Fail("wf.err.reason");

                    msg.DeletedAt = DateTime.Now; msg.DeletedBy = uid;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Delete, Module, mine ? "حذف پیام" : "حذف پیام توسط مالک گروه", entityName: "MDR_ChatMessages", entityId: messageId,
                        newValues: new { threadId = msg.ThreadID, sender = msg.SenderUserID, reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() });
                    return MdrResult.Success(messageId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult MarkRead(int uid, long threadId, long upTo)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    MdrChatThread t;
                    var me = Access(db, threadId, uid, out t);
                    if (me == null) return MdrResult.Fail("wf.err.notfound");
                    // فقط پیام‌های همین گفتگو و هرگز به عقب
                    var max = db.ChatMessages.Where(m => m.ThreadID == threadId && m.MessageID <= upTo).Select(m => (long?)m.MessageID).Max();
                    if (max.HasValue && (!me.LastReadMessageID.HasValue || max.Value > me.LastReadMessageID.Value))
                    {
                        me.LastReadMessageID = max.Value;
                        db.SaveChanges();
                    }
                    return MdrResult.Success(threadId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- Forward ----------------
        public static MdrResult Forward(int uid, long messageId, long targetThreadId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var src = db.ChatMessages.FirstOrDefault(m => m.MessageID == messageId);
                    if (src == null) return MdrResult.Fail("wf.err.notfound");
                    MdrChatThread st, tt;
                    if (Access(db, src.ThreadID, uid, out st) == null) return MdrResult.Fail("wf.err.notfound");
                    if (src.DeletedAt != null) return MdrResult.Fail("chat.err.deleted");
                    if (Access(db, targetThreadId, uid, out tt) == null) return MdrResult.Fail("chat.err.target");
                    if (tt.IsArchived) return MdrResult.Fail("chat.err.archived");

                    // محتوای گفتگوی موجودیتِ محرمانه/سری به جای دیگری نشت نکند
                    if (st.Kind == "Entity" && st.ThreadID != tt.ThreadID && EntityLevel(st.EntityType, st.EntityID.Value) > 0) return MdrResult.Fail("chat.err.forwardlevel");
                }
                var r = Send(uid, targetThreadId, src_Body(messageId), null, null);
                if (!r.Ok) return r;

                using (var db = new MdrContext())
                {
                    var m = db.ChatMessages.First(x => x.MessageID == r.Id.Value);
                    m.ForwardedFromMessageID = messageId;
                    db.SaveChanges();
                }
                AuditLogger.Log(AuditLogger.Actions.Update, Module, "Forward پیام", entityName: "MDR_ChatMessages", entityId: r.Id.Value, newValues: new { from = messageId, toThread = targetThreadId });
                return r;
            }
            catch (Exception ex) { return Failed(ex); }
        }

        private static string src_Body(long messageId)
        {
            using (var db = new MdrContext()) return db.ChatMessages.Where(m => m.MessageID == messageId).Select(m => m.Body).First();
        }

        // ---------------- تسک از پیام ----------------
        public static MdrResult CreateTask(int uid, long messageId, string title, int? assigneeUserId, DateTime? dueAt)
        {
            try
            {
                MdrChatMessage msg; MdrChatThread t;
                using (var db = new MdrContext())
                {
                    msg = db.ChatMessages.FirstOrDefault(m => m.MessageID == messageId);
                    if (msg == null) return MdrResult.Fail("wf.err.notfound");
                    if (Access(db, msg.ThreadID, uid, out t) == null) return MdrResult.Fail("wf.err.notfound");
                    if (msg.DeletedAt != null) return MdrResult.Fail("chat.err.deleted");
                    if (assigneeUserId.HasValue)
                    {
                        // مسئول باید عضو فعال همین گفتگو باشد (متن پیام فقط به اعضا می‌رسد)
                        if (!db.ChatMembers.Any(x => x.ThreadID == msg.ThreadID && x.UserID == assigneeUserId.Value && x.IsActive)) return MdrResult.Fail("chat.err.assignee");
                        if (t.Kind == "Entity" && !CanSeeEntity(assigneeUserId.Value, t.EntityType, t.EntityID.Value)) return MdrResult.Fail("chat.err.noaccess");
                    }
                }
                if (!PermissionHelper.HasAccess(uid, "MDR.Tasks")) return MdrResult.Fail("tsk.err.notallowed");

                var text = (title ?? "").Trim();
                if (text.Length == 0) text = msg.Body.Length > 100 ? msg.Body.Substring(0, 100) : msg.Body;
                var r = MdrTaskService.Save(new MdrTaskService.TaskInput
                {
                    Title = text, Description = msg.Body, AssigneeUserId = assigneeUserId ?? uid, DueAt = dueAt,
                    SourceType = "Chat", SourceRef = "T" + msg.ThreadID, SourceUrl = "/Mdr/Chat#t=" + msg.ThreadID + "&m=" + msg.MessageID
                }, uid, false, null);
                if (r.Ok) AuditLogger.Log(AuditLogger.Actions.Create, Module, "تسک از پیام چت", entityName: "MDR_Tasks", entityId: r.Id, newValues: new { messageId, threadId = msg.ThreadID });
                return r;
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // پس از بارگذاری موفق پیوست توسط ChatController
        public static MdrResult MarkAttachment(int uid, long messageId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var msg = db.ChatMessages.FirstOrDefault(m => m.MessageID == messageId);
                    if (msg == null) return MdrResult.Fail("wf.err.notfound");
                    MdrChatThread t;
                    if (Access(db, msg.ThreadID, uid, out t) == null) return MdrResult.Fail("wf.err.notfound");
                    if (msg.SenderUserID != uid) return MdrResult.Fail("tsk.err.notallowed");
                    if (msg.DeletedAt != null) return MdrResult.Fail("chat.err.deleted");
                    if ((DateTime.Now - msg.CreatedAt).TotalMinutes > EditMinutes) return MdrResult.Fail("chat.err.editwindow");
                    if (msg.HasAttachment) return MdrResult.Fail("chat.err.attached");
                    return MdrResult.Success(messageId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static void SetHasAttachment(long messageId)
        {
            using (var db = new MdrContext())
            {
                var m = db.ChatMessages.FirstOrDefault(x => x.MessageID == messageId);
                if (m != null) { m.HasAttachment = true; db.SaveChanges(); }
            }
        }

        // ---------------- helpers ----------------
        private static string Trim(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // اعلان هرگز متن پیام ندارد (فقط فرستنده)
        private static void Notify(int userId, string title, long threadId, string from = null)
        {
            NotificationService.Send(userId, title, from == null ? null : "از طرف " + from, moduleName: Module, entityName: "MDR_ChatThreads", entityId: threadId,
                linkUrl: "/Mdr/Chat#t=" + threadId);
        }

        private static bool IsDuplicate(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sql = e as System.Data.SqlClient.SqlException;
                if (sql != null && (sql.Number == 2601 || sql.Number == 2627)) return true;
            }
            return false;
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای چت داخلی", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
