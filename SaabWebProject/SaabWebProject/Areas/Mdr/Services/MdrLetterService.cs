using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Infrastructure;
using System.Globalization;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 4 | سرویس مکاتبات و دبیرخانه (پنل ۵)
    // - وضعیت: Draft → Registered → Archived ؛ Draft → Cancelled
    // - شماره‌ی ثبت ترتیبی برای هر جهت در هر سال شمسی (I/O/N-1405-00001)،
    //   با Transaction سریالایز + ایندکس یکتا + تلاش مجدد.
    // - دسترسی: پیش‌نویس فقط سازنده؛ «سری» فقط سازنده/گیرندگان (حتی مدیر هم نه)؛
    //   «محرمانه» و «عادی» برای درگیرها و مدیر.
    // - ارجاع: فقط گیرنده‌ی اصلی/ارجاع‌شده (یا مدیر برای نامه‌ی غیرسری)؛ خوانده‌شدن و پاسخ ردیابی می‌شود.
    // =====================================================
    public static class MdrLetterService
    {
        private const string Module = "Letters";
        private static readonly string[] Directions = { "Incoming", "Outgoing", "Internal" };
        private static readonly string[] Urgencies = { "Normal", "Urgent", "Immediate" };
        private static readonly string[] Confidentialities = { "Normal", "Confidential", "Secret" };

        public class LetterInput
        {
            public long Id { get; set; }
            public string Direction { get; set; }
            public string Subject { get; set; }
            public string Body { get; set; }
            public string Urgency { get; set; }
            public string Confidentiality { get; set; }
            public string ExternalParty { get; set; }
            public DateTime? DueAt { get; set; }
            public long? ParentId { get; set; }
            public List<int> To { get; set; }
            public List<int> Cc { get; set; }
        }

        public class AccessInfo
        {
            public bool CanView { get; set; }
            public bool IsCreator { get; set; }
            public bool IsRecipient { get; set; }       // To یا Referral (می‌تواند ارجاع دهد)
            public bool Involved { get; set; }
        }

        // ---------------- دسترسی ----------------
        public static AccessInfo GetAccess(MdrContext db, MdrLetter l, int uid, bool isManager)
        {
            var a = new AccessInfo { IsCreator = l.CreatedBy == uid };
            var rows = db.LetterRecipients.Where(r => r.LetterID == l.LetterID && r.UserID == uid).Select(r => r.Kind).ToList();
            a.IsRecipient = rows.Any(k => k == "To" || k == "Referral");
            a.Involved = a.IsCreator || rows.Count > 0;

            if (l.Status == "Draft" || l.Status == "Cancelled") a.CanView = a.IsCreator;
            else if (l.Confidentiality == "Secret") a.CanView = a.Involved;
            else a.CanView = a.Involved || isManager;
            return a;
        }

        // ---------------- پیش‌نویس ----------------
        public static MdrResult Save(LetterInput t, int actorId, byte[] rowVer)
        {
            var subject = (t.Subject ?? "").Trim();
            if (subject.Length == 0 || subject.Length > 300) return MdrResult.Fail("ltr.err.subject");
            var urgency = string.IsNullOrEmpty(t.Urgency) ? "Normal" : t.Urgency;
            var conf = string.IsNullOrEmpty(t.Confidentiality) ? "Normal" : t.Confidentiality;
            if (!Urgencies.Contains(urgency) || !Confidentialities.Contains(conf)) return MdrResult.Fail("ltr.err.enum");
            if (!Directions.Contains(t.Direction ?? "")) return MdrResult.Fail("ltr.err.direction");

            var to = Clean(t.To); var cc = Clean(t.Cc).Except(to).ToList();
            if (to.Count + cc.Count > 50) return MdrResult.Fail("ltr.err.toomany");
            var ext = string.IsNullOrWhiteSpace(t.ExternalParty) ? null : Cut(t.ExternalParty, 200);

            try
            {
                using (var db = new MdrContext())
                {
                    var all = to.Concat(cc).ToList();
                    if (all.Count > 0 && db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID IN (" + string.Join(",", all) + ")").First() != all.Count)
                        return MdrResult.Fail("wf.err.baduser");

                    MdrLetter l;
                    if (t.Id == 0)
                    {
                        if (t.ParentId.HasValue)
                        {
                            if (t.Direction == "Incoming") return MdrResult.Fail("ltr.err.replydir");
                            var parent = db.Letters.FirstOrDefault(p => p.LetterID == t.ParentId.Value);
                            if (parent == null || parent.Status == "Draft" || !GetAccess(db, parent, actorId, false).CanView) return MdrResult.Fail("wf.err.notfound");
                        }

                        l = new MdrLetter { CreatedBy = actorId, CreatedAt = DateTime.Now, Status = "Draft", ParentLetterID = t.ParentId };
                        db.Letters.Add(l);
                    }
                    else
                    {
                        l = db.Letters.FirstOrDefault(x => x.LetterID == t.Id);
                        if (l == null) return MdrResult.Fail("wf.err.notfound");
                        if (l.CreatedBy != actorId) return MdrResult.Fail("tsk.err.notallowed");
                        if (l.Status != "Draft") return MdrResult.Fail("ltr.err.notdraft");
                        if (rowVer != null && !l.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    }

                    l.Direction = t.Direction; l.Subject = subject; l.Body = Cut(t.Body, 20000); l.Urgency = urgency; l.Confidentiality = conf;
                    l.ExternalParty = ext; l.DueAt = t.DueAt;
                    db.SaveChanges();

                    db.LetterRecipients.RemoveRange(db.LetterRecipients.Where(r => r.LetterID == l.LetterID));
                    db.SaveChanges();
                    foreach (var u in to) db.LetterRecipients.Add(new MdrLetterRecipient { LetterID = l.LetterID, UserID = u, Kind = "To", CreatedAt = DateTime.Now });
                    foreach (var u in cc) db.LetterRecipients.Add(new MdrLetterRecipient { LetterID = l.LetterID, UserID = u, Kind = "Cc", CreatedAt = DateTime.Now });
                    Log(db, l.LetterID, t.Id == 0 ? "Create" : "Edit", actorId, null);
                    db.SaveChanges();

                    AuditLogger.Log(t.Id == 0 ? AuditLogger.Actions.Create : AuditLogger.Actions.Update, Module, "ذخیره‌ی پیش‌نویس نامه",
                        entityName: "MDR_Letters", entityId: l.LetterID, entityTitle: l.Subject);
                    return MdrResult.Success(l.LetterID);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- ثبت (شماره‌دهی و ارسال) ----------------
        public static MdrResult Register(long id, int actorId, byte[] rowVer)
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using (var db = new MdrContext())
                    using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
                    {
                        var l = db.Letters.FirstOrDefault(x => x.LetterID == id);
                        if (l == null) return MdrResult.Fail("wf.err.notfound");
                        if (l.CreatedBy != actorId) return MdrResult.Fail("tsk.err.notallowed");
                        if (l.Status != "Draft") return MdrResult.Fail("ltr.err.notdraft");
                        if (rowVer != null && !l.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");

                        var recipients = db.LetterRecipients.Where(r => r.LetterID == id).ToList();
                        var hasTo = recipients.Any(r => r.Kind == "To");
                        if (string.IsNullOrWhiteSpace(l.Body)) return MdrResult.Fail("ltr.err.nobody");
                        if (l.Direction == "Incoming" && string.IsNullOrWhiteSpace(l.ExternalParty)) return MdrResult.Fail("ltr.err.nosender");
                        if (l.Direction == "Incoming" && !hasTo) return MdrResult.Fail("ltr.err.norecipient");
                        if (l.Direction == "Internal" && !hasTo) return MdrResult.Fail("ltr.err.norecipient");
                        if (l.Direction == "Outgoing" && !hasTo && string.IsNullOrWhiteSpace(l.ExternalParty)) return MdrResult.Fail("ltr.err.norecipient");

                        var year = new PersianCalendar().GetYear(DateTime.Now);
                        var dir = l.Direction;
                        // UPDLOCK+HOLDLOCK: ثبت‌کننده‌های هم‌زمان صف می‌شوند (بدون بن‌بست ارتقای قفل)
                        var max = db.Database.SqlQuery<int?>("SELECT MAX(SeqNo) FROM dbo.MDR_Letters WITH (UPDLOCK, HOLDLOCK) WHERE Direction = @p0 AND SeqYear = @p1", dir, year).First() ?? 0;
                        l.SeqYear = year; l.SeqNo = max + 1;
                        l.Number = (dir == "Incoming" ? "I" : dir == "Outgoing" ? "O" : "N") + "-" + year + "-" + l.SeqNo.Value.ToString("D5");
                        l.Status = "Registered"; l.RegisteredAt = DateTime.Now;

                        if (l.ParentLetterID.HasValue)
                            foreach (var pr in db.LetterRecipients.Where(r => r.LetterID == l.ParentLetterID.Value && r.UserID == actorId && r.RepliedAt == null).ToList())
                                pr.RepliedAt = DateTime.Now;

                        Log(db, l.LetterID, "Register", actorId, l.Number);
                        db.SaveChanges();
                        tx.Commit();

                        foreach (var r in recipients.Where(x => x.Kind == "To" || x.Kind == "Cc"))
                            Notify(r.UserID, "نامه‌ی جدید " + l.Number, l, actorId);

                        AuditLogger.Log(AuditLogger.Actions.Update, Module, "ثبت نامه", entityName: "MDR_Letters", entityId: l.LetterID,
                            entityTitle: l.Subject, newValues: new { l.Number, l.Direction, l.Confidentiality });
                        return MdrResult.Success(l.LetterID);
                    }
                }
                catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
                catch (Exception ex)
                {
                    if (!IsTransient(ex)) return Failed(ex);
                    System.Threading.Thread.Sleep(20 * (attempt + 1));   // بن‌بست/شماره‌ی تکراری: تلاش مجدد
                }
            }
            return MdrResult.Fail("ltr.err.numbering");
        }

        // ---------------- ارجاع ----------------
        public static MdrResult Refer(long id, int toUserId, string note, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var l = db.Letters.FirstOrDefault(x => x.LetterID == id);
                    if (l == null) return MdrResult.Fail("wf.err.notfound");
                    if (l.Status != "Registered") return MdrResult.Fail("ltr.err.state");

                    var a = GetAccess(db, l, actorId, isManager);
                    if (!a.CanView || !(a.IsRecipient || (isManager && l.Confidentiality != "Secret"))) return MdrResult.Fail("tsk.err.notallowed");
                    if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0", toUserId).First() == 0) return MdrResult.Fail("wf.err.baduser");
                    if (l.CreatedBy == toUserId || db.LetterRecipients.Any(r => r.LetterID == id && r.UserID == toUserId)) return MdrResult.Fail("ltr.err.alreadyrecipient");

                    db.LetterRecipients.Add(new MdrLetterRecipient { LetterID = id, UserID = toUserId, Kind = "Referral", FromUserID = actorId, Note = Cut(note, 500), CreatedAt = DateTime.Now });
                    Log(db, id, "Refer", actorId, "to=" + toUserId + (string.IsNullOrWhiteSpace(note) ? "" : " " + Cut(note, 200)));
                    db.SaveChanges();

                    Notify(toUserId, "نامه‌ی ارجاعی " + l.Number, l, actorId);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ارجاع نامه", entityName: "MDR_Letters", entityId: id, entityTitle: l.Subject, newValues: new { toUserId });
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Archive(long id, int actorId, bool isManager)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var l = db.Letters.FirstOrDefault(x => x.LetterID == id);
                    if (l == null) return MdrResult.Fail("wf.err.notfound");
                    if (l.Status != "Registered") return MdrResult.Fail("ltr.err.state");
                    var a = GetAccess(db, l, actorId, isManager);
                    if (!a.CanView || !(a.IsCreator || isManager)) return MdrResult.Fail("tsk.err.notallowed");

                    l.Status = "Archived"; l.ArchivedAt = DateTime.Now;
                    Log(db, id, "Archive", actorId, null);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "بایگانی نامه", entityName: "MDR_Letters", entityId: id, entityTitle: l.Subject);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult CancelDraft(long id, int actorId)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var l = db.Letters.FirstOrDefault(x => x.LetterID == id);
                    if (l == null) return MdrResult.Fail("wf.err.notfound");
                    if (l.CreatedBy != actorId) return MdrResult.Fail("tsk.err.notallowed");
                    if (l.Status != "Draft") return MdrResult.Fail("ltr.err.notdraft");
                    l.Status = "Cancelled";
                    Log(db, id, "Cancel", actorId, null);
                    db.SaveChanges();
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // اولین بازشدن توسط گیرنده: ReadAt (همه‌ی ردیف‌های همان کاربر)
        public static void MarkRead(long id, int uid)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var rows = db.LetterRecipients.Where(r => r.LetterID == id && r.UserID == uid && r.ReadAt == null).ToList();
                    if (rows.Count == 0) return;
                    foreach (var r in rows) r.ReadAt = DateTime.Now;
                    Log(db, id, "Read", uid, null);
                    db.SaveChanges();
                }
            }
            catch { /* ثبت خوانده‌شدن نباید نمایش نامه را خراب کند */ }
        }

        // بن‌بست (1205)، تایم‌اوت قفل (1222) و نقض یکتایی (2601/2627) در هر لایه‌ی InnerException
        private static bool IsTransient(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sql = e as System.Data.SqlClient.SqlException;
                if (sql != null && (sql.Number == 1205 || sql.Number == 1222 || sql.Number == 2601 || sql.Number == 2627)) return true;
            }
            return false;
        }

        // ---------------- helpers ----------------
        private static List<int> Clean(List<int> ids) { return (ids ?? new List<int>()).Where(x => x > 0).Distinct().ToList(); }

        private static string Cut(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        private static void Log(MdrContext db, long id, string action, int? actor, string detail)
        {
            db.LetterHistory.Add(new MdrLetterHistory
            {
                LetterID = id, ActionKey = action, ActorUserID = actor, Detail = Cut(detail, 500), CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now
            });
        }

        private static void Notify(int userId, string title, MdrLetter l, int actorId)
        {
            if (userId == actorId) return;
            NotificationService.Send(userId, title, l.Confidentiality == "Secret" ? null : l.Subject,
                severity: l.Urgency == "Normal" ? NotificationService.Severities.Info : NotificationService.Severities.Warning,
                important: l.Urgency == "Immediate", moduleName: Module, entityName: "MDR_Letters", entityId: l.LetterID,
                linkUrl: "/Mdr/Letters#id=" + l.LetterID);
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای مکاتبات", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
