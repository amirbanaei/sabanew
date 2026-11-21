using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 8 | آموزش (پنل ۱۵)
    // دوره: Draft → Open → Closed | Cancelled.   ثبت‌نام: Enrolled | Waitlisted | Cancelled | Completed | Failed.
    // قواعد کلیدی (هشدارهای سند: ظرفیت، تداخل، پیش‌نیاز):
    //  - ظرفیت: پر شدن ظرفیت = لیست انتظار (نه رد)؛ با لغو یک نفر، اولین نفر لیست انتظار (بدون تداخل) جایش می‌آید.
    //    ثبت‌نام هم‌زمان زیر قفل (UPDLOCK) همان دوره انجام می‌شود تا ظرفیت بیش از حد پر نشود.
    //  - تداخل: کاربر در دو دوره‌ی هم‌زمان (Enrolled) ثبت‌نام نمی‌شود.
    //  - پیش‌نیاز: فقط با «Completed» بودن دوره‌ی پیش‌نیاز.
    //  - ثبت‌نام بعد از شروع دوره ممکن نیست؛ لغو توسط خود کاربر فقط تا قبل از شروع؛ مدیر آموزش با دلیل.
    //  - تکمیل فقط بعد از پایان دوره و فقط برای Enrolled؛ قبول = شماره‌ی گواهی + (اگر دوره مهارت/سطح هدف دارد) سطح مهارت منبع Course.
    //  - ثبت‌نام/حضور/تکمیل دیگران فقط مدیر آموزش (CanEdit صفحه‌ی MDR.Training). حداقل درصد حضور تعیین نشده و حدس زده نمی‌شود.
    // =====================================================
    public class CourseInput
    {
        public string Code { get; set; } public string Title { get; set; } public string Description { get; set; } public int? SkillId { get; set; } public int? TargetLevel { get; set; }
        public DateTime StartAt { get; set; } public DateTime EndAt { get; set; } public int Capacity { get; set; } public int? PrereqCourseId { get; set; }
        public string InstructorName { get; set; } public string Location { get; set; }
    }

    public static class MdrTrainingService
    {
        private const string Module = "Training";
        public const string Page = "MDR.Training";

        public static bool IsManager(int uid) { return PermissionHelper.HasAccess(uid, Page) && PermissionHelper.CanEdit(uid, Page); }

        // ---------------- دوره ----------------
        private static string Validate(CourseInput i, bool isNew)
        {
            var code = (i.Code ?? "").Trim(); var title = (i.Title ?? "").Trim();
            if (code.Length == 0 || code.Length > 30) return "train.err.code";
            if (title.Length == 0 || title.Length > 200) return "train.err.title";
            if (i.EndAt <= i.StartAt) return "train.err.dates";
            if (i.Capacity < 1 || i.Capacity > 5000) return "train.err.capacity";
            if (i.TargetLevel.HasValue && (i.TargetLevel.Value < 1 || i.TargetLevel.Value > 5)) return "train.err.level";
            if (i.TargetLevel.HasValue && !i.SkillId.HasValue) return "train.err.skill";
            return null;
        }

        public static MdrResult CreateCourse(int actor, CourseInput i)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            var v = Validate(i, true); if (v != null) return MdrResult.Fail(v);
            try
            {
                using (var db = new MdrContext())
                {
                    var code = i.Code.Trim().ToUpperInvariant();
                    if (db.Courses.Any(c => c.Code == code)) return MdrResult.Fail("train.err.dupcode");
                    var r = Refs(db, i, null); if (r != null) return MdrResult.Fail(r);
                    var c2 = new MdrCourse { Code = code, Status = "Draft", CreatedBy = actor, CreatedAt = DateTime.Now };
                    Apply(c2, i);
                    db.Courses.Add(c2); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "ایجاد دوره", entityName: "MDR_Courses", entityId: c2.CourseID, entityTitle: code);
                    return MdrResult.Success(c2.CourseID);
                }
            }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("train.err.dupcode") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult UpdateCourse(int actor, int id, CourseInput i, byte[] rowVer)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            var v = Validate(i, false); if (v != null) return MdrResult.Fail(v);
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Courses.FirstOrDefault(x => x.CourseID == id);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    if (rowVer != null && !c.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (c.Status == "Cancelled" || c.Status == "Closed") return MdrResult.Fail("train.err.state");
                    var r = Refs(db, i, id); if (r != null) return MdrResult.Fail(r);
                    var active = db.CourseEnrollments.Count(e => e.CourseID == id && (e.Status == "Enrolled" || e.Status == "Waitlisted"));
                    if (active > 0 && (i.StartAt != c.StartAt || i.EndAt != c.EndAt)) return MdrResult.Fail("train.err.hasenroll");
                    if (i.Capacity < db.CourseEnrollments.Count(e => e.CourseID == id && e.Status == "Enrolled")) return MdrResult.Fail("train.err.capacitylow");
                    var newCode = i.Code.Trim().ToUpperInvariant();
                    if (newCode != c.Code && db.Courses.Any(x => x.Code == newCode)) return MdrResult.Fail("train.err.dupcode");
                    c.Code = newCode; Apply(c, i);
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ویرایش دوره", entityName: "MDR_Courses", entityId: id, entityTitle: c.Code);
                    return MdrResult.Success(id);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("train.err.dupcode") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        private static void Apply(MdrCourse c, CourseInput i)
        {
            c.Title = i.Title.Trim(); c.Description = Trim(i.Description, 1000); c.SkillID = i.SkillId; c.TargetLevel = i.TargetLevel.HasValue ? (byte?)i.TargetLevel.Value : null;
            c.StartAt = i.StartAt; c.EndAt = i.EndAt; c.Capacity = i.Capacity; c.PrereqCourseID = i.PrereqCourseId; c.InstructorName = Trim(i.InstructorName, 150); c.Location = Trim(i.Location, 200);
        }

        private static string Refs(MdrContext db, CourseInput i, int? selfId)
        {
            if (i.SkillId.HasValue && !db.Skills.Any(s => s.SkillID == i.SkillId.Value && s.IsActive)) return "train.err.skill";
            if (i.PrereqCourseId.HasValue)
            {
                if (selfId.HasValue && i.PrereqCourseId.Value == selfId.Value) return "train.err.prereq.self";
                if (!db.Courses.Any(c => c.CourseID == i.PrereqCourseId.Value)) return "train.err.prereq.missing";
            }
            return null;
        }

        public static MdrResult Publish(int actor, int id)
        {
            return Transition(actor, id, "Draft", "Open", (db, c) => c.StartAt <= DateTime.Now ? "train.err.started" : null, "انتشار دوره");
        }

        public static MdrResult CloseEnrollment(int actor, int id)
        {
            return Transition(actor, id, "Open", "Closed", null, "بستن ثبت‌نام دوره");
        }

        private static MdrResult Transition(int actor, int id, string from, string to, Func<MdrContext, MdrCourse, string> guard, string title)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Courses.FirstOrDefault(x => x.CourseID == id);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    if (c.Status != from) return MdrResult.Fail("train.err.state");
                    var g = guard == null ? null : guard(db, c); if (g != null) return MdrResult.Fail(g);
                    c.Status = to; db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, title, entityName: "MDR_Courses", entityId: id, entityTitle: c.Code);
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // لغو دوره: همه‌ی ثبت‌نام‌های فعال لغو و اعلان می‌شوند؛ دلیل اجباری
        public static MdrResult CancelCourse(int actor, int id, string reason)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Courses.FirstOrDefault(x => x.CourseID == id);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    if (c.Status == "Cancelled") return MdrResult.Fail("train.err.state");
                    if (db.CourseEnrollments.Any(e => e.CourseID == id && (e.Status == "Completed" || e.Status == "Failed"))) return MdrResult.Fail("train.err.hasresult");
                    var act = db.CourseEnrollments.Where(e => e.CourseID == id && (e.Status == "Enrolled" || e.Status == "Waitlisted")).ToList();
                    foreach (var e in act) { e.Status = "Cancelled"; e.CancelledAt = DateTime.Now; e.CancelReason = "لغو دوره: " + Trim(reason, 250); }
                    c.Status = "Cancelled"; db.SaveChanges();
                    foreach (var e in act) Notify(e.UserID, "دوره‌ی «" + c.Title + "» لغو شد", c.CourseID);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "لغو دوره", entityName: "MDR_Courses", entityId: id, entityTitle: c.Code, newValues: new { reason, affected = act.Count });
                    return MdrResult.Success(id);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- ثبت‌نام ----------------
        // خروجی: Id = شناسه‌ی ثبت‌نام؛ Warning = "waitlisted" اگر ظرفیت پر بود
        public static MdrResult Enroll(int actor, int courseId, int? userId)
        {
            var target = userId ?? actor;
            if (target != actor && !IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    db.Database.SqlQuery<int>("SELECT CourseID FROM dbo.MDR_Courses WITH (UPDLOCK, ROWLOCK) WHERE CourseID = @p0", courseId).FirstOrDefault();
                    var c = db.Courses.FirstOrDefault(x => x.CourseID == courseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    if (c.Status != "Open") return MdrResult.Fail("train.err.notopen");
                    if (c.StartAt <= DateTime.Now) return MdrResult.Fail("train.err.started");
                    if (target != actor && db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", target).First() != 1) return MdrResult.Fail("wf.err.baduser");
                    if (db.CourseEnrollments.Any(e => e.CourseID == courseId && e.UserID == target && e.Status != "Cancelled")) return MdrResult.Fail("train.err.already");

                    if (c.PrereqCourseID.HasValue && !db.CourseEnrollments.Any(e => e.CourseID == c.PrereqCourseID.Value && e.UserID == target && e.Status == "Completed")) return MdrResult.Fail("train.err.prereq");
                    if (Conflicts(db, target, c)) return MdrResult.Fail("train.err.conflict");

                    var enrolled = db.CourseEnrollments.Count(e => e.CourseID == courseId && e.Status == "Enrolled");
                    var status = enrolled >= c.Capacity ? "Waitlisted" : "Enrolled";
                    var en = new MdrCourseEnrollment { CourseID = courseId, UserID = target, Status = status, EnrolledAt = DateTime.Now, EnrolledBy = actor };
                    db.CourseEnrollments.Add(en); db.SaveChanges(); tx.Commit();
                    if (target != actor) Notify(target, (status == "Enrolled" ? "شما در دوره‌ی «" : "شما در لیست انتظار دوره‌ی «") + c.Title + "» ثبت شدید", courseId);
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, status == "Enrolled" ? "ثبت‌نام دوره" : "ثبت در لیست انتظار", entityName: "MDR_CourseEnrollments", entityId: en.EnrollmentID, entityTitle: c.Code, newValues: new { target, status });
                    var res = MdrResult.Success(en.EnrollmentID);
                    if (status == "Waitlisted") res.Warning = "waitlisted";
                    return res;
                }
            }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("train.err.already") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        // تداخل زمانی با سایر دوره‌های Enrolled همان کاربر
        private static bool Conflicts(MdrContext db, int userId, MdrCourse c)
        {
            return (from e in db.CourseEnrollments join o in db.Courses on e.CourseID equals o.CourseID
                    where e.UserID == userId && e.Status == "Enrolled" && o.CourseID != c.CourseID && o.Status != "Cancelled" && o.StartAt < c.EndAt && c.StartAt < o.EndAt
                    select o.CourseID).Any();
        }

        public static MdrResult CancelEnrollment(int actor, long enrollmentId, string reason)
        {
            try
            {
                using (var db = new MdrContext())
                using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var e = db.CourseEnrollments.FirstOrDefault(x => x.EnrollmentID == enrollmentId);
                    if (e == null) return MdrResult.Fail("wf.err.notfound");
                    var mgr = IsManager(actor);
                    if (e.UserID != actor && !mgr) return MdrResult.Fail("wf.err.notfound");
                    if (e.Status != "Enrolled" && e.Status != "Waitlisted") return MdrResult.Fail("train.err.state");
                    db.Database.SqlQuery<int>("SELECT CourseID FROM dbo.MDR_Courses WITH (UPDLOCK, ROWLOCK) WHERE CourseID = @p0", e.CourseID).FirstOrDefault();
                    var c = db.Courses.First(x => x.CourseID == e.CourseID);
                    reason = (reason ?? "").Trim();
                    if (e.UserID == actor && !mgr) { if (c.StartAt <= DateTime.Now) return MdrResult.Fail("train.err.started"); }
                    else if (reason.Length == 0) return MdrResult.Fail("wf.err.reason");

                    var wasEnrolled = e.Status == "Enrolled";
                    e.Status = "Cancelled"; e.CancelledAt = DateTime.Now; e.CancelReason = Trim(reason, 300);
                    db.SaveChanges();

                    int? promoted = null;
                    if (wasEnrolled && c.Status != "Cancelled")
                    {
                        foreach (var w in db.CourseEnrollments.Where(x => x.CourseID == c.CourseID && x.Status == "Waitlisted").OrderBy(x => x.EnrolledAt).ThenBy(x => x.EnrollmentID).ToList())
                        {
                            if (Conflicts(db, w.UserID, c)) continue;      // تداخل جدید پیدا کرده؛ نفر بعدی
                            w.Status = "Enrolled"; promoted = w.UserID; db.SaveChanges(); break;
                        }
                    }
                    tx.Commit();
                    if (promoted.HasValue) Notify(promoted.Value, "ظرفیت باز شد؛ شما در دوره‌ی «" + c.Title + "» ثبت‌نام شدید", c.CourseID);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "لغو ثبت‌نام", entityName: "MDR_CourseEnrollments", entityId: enrollmentId, entityTitle: c.Code, newValues: new { e.UserID, reason, promoted });
                    return MdrResult.Success(enrollmentId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- حضور / تکمیل ----------------
        public static MdrResult MarkAttendance(int actor, int courseId, int userId, DateTime date, bool present)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Courses.FirstOrDefault(x => x.CourseID == courseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    if (!db.CourseEnrollments.Any(e => e.CourseID == courseId && e.UserID == userId && e.Status == "Enrolled")) return MdrResult.Fail("train.err.notenrolled");
                    var d = date.Date;
                    if (d < c.StartAt.Date || d > c.EndAt.Date || d > DateTime.Today) return MdrResult.Fail("train.err.session");
                    var a = db.CourseAttendance.FirstOrDefault(x => x.CourseID == courseId && x.UserID == userId && x.SessionDate == d);
                    if (a == null) { a = new MdrCourseAttendance { CourseID = courseId, UserID = userId, SessionDate = d }; db.CourseAttendance.Add(a); }
                    a.Present = present; a.MarkedBy = actor; a.MarkedAt = DateTime.Now;
                    db.SaveChanges();
                    return MdrResult.Success(courseId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // قبول/ردِ نهایی؛ فقط بعد از پایان دوره
        public static MdrResult Complete(int actor, int courseId, int userId, bool passed, decimal? score)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            if (score.HasValue && (score.Value < 0 || score.Value > 100)) return MdrResult.Fail("train.err.score");
            try
            {
                using (var db = new MdrContext())
                {
                    var c = db.Courses.FirstOrDefault(x => x.CourseID == courseId);
                    if (c == null) return MdrResult.Fail("wf.err.notfound");
                    var e = db.CourseEnrollments.FirstOrDefault(x => x.CourseID == courseId && x.UserID == userId && x.Status == "Enrolled");
                    if (e == null) return MdrResult.Fail("train.err.notenrolled");
                    if (c.Status == "Cancelled") return MdrResult.Fail("train.err.state");
                    if (c.EndAt > DateTime.Now) return MdrResult.Fail("train.err.notended");

                    e.Score = score; e.CompletedAt = DateTime.Now;
                    if (passed)
                    {
                        e.Status = "Completed";
                        db.SaveChanges();
                        e.CertificateNo = "CRT-" + new System.Globalization.PersianCalendar().GetYear(DateTime.Now) + "-" + e.EnrollmentID.ToString("D6");
                        if (c.SkillID.HasValue && c.TargetLevel.HasValue)
                        {
                            var us = db.UserSkills.FirstOrDefault(x => x.UserID == userId && x.SkillID == c.SkillID.Value && x.Source == "Course");
                            if (us == null) db.UserSkills.Add(new MdrUserSkill { UserID = userId, SkillID = c.SkillID.Value, Source = "Course", Level = c.TargetLevel.Value, AssessedBy = actor, AssessedAt = DateTime.Now, Note = "دوره: " + c.Code });
                            else if (us.Level < c.TargetLevel.Value) { us.Level = c.TargetLevel.Value; us.AssessedBy = actor; us.AssessedAt = DateTime.Now; us.Note = "دوره: " + c.Code; }
                        }
                    }
                    else e.Status = "Failed";
                    db.SaveChanges();
                    Notify(userId, passed ? "دوره‌ی «" + c.Title + "» با موفقیت تکمیل شد" : "نتیجه‌ی دوره‌ی «" + c.Title + "» ثبت شد", courseId);
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, passed ? "تکمیل دوره (قبول)" : "تکمیل دوره (مردود)", entityName: "MDR_CourseEnrollments", entityId: e.EnrollmentID, entityTitle: c.Code, newValues: new { userId, passed, score, e.CertificateNo });
                    return MdrResult.Success(e.EnrollmentID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- helpers ----------------
        private static string Trim(string s, int max) { if (string.IsNullOrWhiteSpace(s)) return null; s = s.Trim(); return s.Length > max ? s.Substring(0, max) : s; }

        private static void Notify(int userId, string title, int courseId)
        {
            NotificationService.Send(userId, title, null, moduleName: Module, entityName: "MDR_Courses", entityId: courseId, linkUrl: "/Mdr/Training#c=" + courseId);
        }

        private static bool IsDup(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException) { var s = e as System.Data.SqlClient.SqlException; if (s != null && (s.Number == 2601 || s.Number == 2627)) return true; }
            return false;
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای آموزش", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
