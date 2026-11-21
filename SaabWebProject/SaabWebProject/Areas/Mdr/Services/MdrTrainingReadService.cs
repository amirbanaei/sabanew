using System;
using System.Collections.Generic;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 8 | آموزش — خواندن
    // کاربر عادی: دوره‌های «Open» و دوره‌هایی که در آن‌ها ثبت‌نام کرده؛ پیش‌نویس فقط مدیر آموزش.
    // فهرست شرکت‌کنندگان، نمره و حضور فقط برای مدیر آموزش؛ هر کاربر فقط وضعیت و گواهی خودش را می‌بیند.
    // =====================================================
    public class CourseRow
    {
        public int CourseID { get; set; } public string Code { get; set; } public string Title { get; set; } public string Status { get; set; }
        public string StartAt { get; set; } public string EndAt { get; set; } public int Capacity { get; set; } public int Enrolled { get; set; } public int Waitlisted { get; set; } public int SeatsLeft { get; set; }
        public string MyStatus { get; set; } public string Skill { get; set; } public int? TargetLevel { get; set; } public string Prereq { get; set; } public string Location { get; set; } public string Instructor { get; set; }
    }
    public class EnrollRow { public long EnrollmentID { get; set; } public int UserID { get; set; } public string Name { get; set; } public string Status { get; set; } public decimal? Score { get; set; } public string CertificateNo { get; set; } public int Present { get; set; } public int Sessions { get; set; } public string EnrolledAt { get; set; } }
    public class CourseDetail
    {
        public CourseRow Course { get; set; } public string Description { get; set; } public int? SkillID { get; set; } public int? PrereqCourseID { get; set; } public string RowVer { get; set; }
        public long? MyEnrollmentID { get; set; } public decimal? MyScore { get; set; } public string MyCertificate { get; set; }
        public bool CanEnroll { get; set; } public bool CanCancelMine { get; set; }
        public bool CanEdit { get; set; } public bool CanPublish { get; set; } public bool CanClose { get; set; } public bool CanCancelCourse { get; set; } public bool CanComplete { get; set; }
        public List<EnrollRow> Enrollments { get; set; }
        public List<string> SessionDates { get; set; }
    }

    public static class MdrTrainingReadService
    {
        private static string D(DateTime d) { return d.ToString("yyyy-MM-dd HH:mm"); }

        private static List<CourseRow> Rows(MdrContext db, List<MdrCourse> list, int uid)
        {
            var ids = list.Select(c => c.CourseID).ToList();
            var ens = db.CourseEnrollments.Where(e => ids.Contains(e.CourseID) && e.Status != "Cancelled").Select(e => new { e.CourseID, e.UserID, e.Status }).ToList();
            var skills = db.Skills.ToDictionary(s => s.SkillID, s => s.Name);
            var courses = db.Courses.Select(c => new { c.CourseID, c.Title }).ToDictionary(c => c.CourseID, c => c.Title);
            return list.Select(c =>
            {
                var enrolled = ens.Count(e => e.CourseID == c.CourseID && (e.Status == "Enrolled" || e.Status == "Completed" || e.Status == "Failed"));
                var mine = ens.FirstOrDefault(e => e.CourseID == c.CourseID && e.UserID == uid);
                return new CourseRow
                {
                    CourseID = c.CourseID, Code = c.Code, Title = c.Title, Status = c.Status, StartAt = D(c.StartAt), EndAt = D(c.EndAt), Capacity = c.Capacity, Enrolled = enrolled,
                    Waitlisted = ens.Count(e => e.CourseID == c.CourseID && e.Status == "Waitlisted"), SeatsLeft = Math.Max(0, c.Capacity - enrolled), MyStatus = mine == null ? null : mine.Status,
                    Skill = c.SkillID.HasValue && skills.ContainsKey(c.SkillID.Value) ? skills[c.SkillID.Value] : null, TargetLevel = c.TargetLevel, Location = c.Location, Instructor = c.InstructorName,
                    Prereq = c.PrereqCourseID.HasValue && courses.ContainsKey(c.PrereqCourseID.Value) ? courses[c.PrereqCourseID.Value] : null
                };
            }).ToList();
        }

        // scope: open | mine | all (فقط مدیر آموزش)
        public static List<CourseRow> List(int uid, string scope, string q)
        {
            using (var db = new MdrContext())
            {
                var mgr = MdrTrainingService.IsManager(uid);
                var qry = db.Courses.AsQueryable();
                if (scope == "all" && mgr) { }
                else if (scope == "mine") qry = qry.Where(c => db.CourseEnrollments.Any(e => e.CourseID == c.CourseID && e.UserID == uid && e.Status != "Cancelled"));
                else qry = qry.Where(c => c.Status == "Open" && c.EndAt >= DateTime.Now);   // دوره‌ی پایان‌یافته «باز» حساب نمی‌شود
                if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); qry = qry.Where(c => c.Title.Contains(s) || c.Code.Contains(s)); }
                return Rows(db, qry.OrderBy(c => c.StartAt).Take(300).ToList(), uid);
            }
        }

        public static CourseDetail Get(int uid, int id)
        {
            using (var db = new MdrContext())
            {
                var c = db.Courses.FirstOrDefault(x => x.CourseID == id);
                if (c == null) return null;
                var mgr = MdrTrainingService.IsManager(uid);
                var mine = db.CourseEnrollments.FirstOrDefault(e => e.CourseID == id && e.UserID == uid && e.Status != "Cancelled");
                if (!mgr && c.Status != "Open" && mine == null) return null;

                var row = Rows(db, new List<MdrCourse> { c }, uid)[0];
                var d = new CourseDetail
                {
                    Course = row, Description = c.Description, SkillID = c.SkillID, PrereqCourseID = c.PrereqCourseID, RowVer = Convert.ToBase64String(c.RowVer),
                    MyEnrollmentID = mine == null ? (long?)null : mine.EnrollmentID, MyScore = mine == null ? null : mine.Score, MyCertificate = mine == null ? null : mine.CertificateNo,
                    CanEnroll = c.Status == "Open" && c.StartAt > DateTime.Now && mine == null,
                    CanCancelMine = mine != null && (mine.Status == "Enrolled" || mine.Status == "Waitlisted") && c.StartAt > DateTime.Now,
                    CanEdit = mgr && (c.Status == "Draft" || c.Status == "Open"), CanPublish = mgr && c.Status == "Draft", CanClose = mgr && c.Status == "Open", CanCancelCourse = mgr && c.Status != "Cancelled",
                    CanComplete = mgr && c.Status != "Cancelled" && c.EndAt <= DateTime.Now, Enrollments = new List<EnrollRow>(), SessionDates = new List<string>()
                };
                if (mgr)
                {
                    var last = c.EndAt.Date < DateTime.Today ? c.EndAt.Date : DateTime.Today;
                    for (var day = c.StartAt.Date; day <= last && d.SessionDates.Count < 60; day = day.AddDays(1)) d.SessionDates.Add(day.ToString("yyyy-MM-dd"));
                    var ens = db.CourseEnrollments.Where(e => e.CourseID == id).OrderBy(e => e.EnrolledAt).ToList();
                    var names = MdrChatService.Names(db, ens.Select(e => e.UserID));
                    var att = db.CourseAttendance.Where(a => a.CourseID == id).ToList();
                    d.Enrollments = ens.Where(e => e.Status != "Cancelled").Select(e => new EnrollRow
                    {
                        EnrollmentID = e.EnrollmentID, UserID = e.UserID, Name = names.ContainsKey(e.UserID) ? names[e.UserID] : "#" + e.UserID, Status = e.Status, Score = e.Score, CertificateNo = e.CertificateNo,
                        Present = att.Count(a => a.UserID == e.UserID && a.Present), Sessions = att.Count(a => a.UserID == e.UserID), EnrolledAt = D(e.EnrolledAt)
                    }).ToList();
                }
                return d;
            }
        }

        // گواهی‌های خودِ کاربر
        public static List<EnrollRow> MyCertificates(int uid)
        {
            using (var db = new MdrContext())
            {
                var rows = (from e in db.CourseEnrollments join c in db.Courses on e.CourseID equals c.CourseID where e.UserID == uid && e.Status == "Completed" orderby e.CompletedAt descending select new { e, c.Title }).Take(100).ToList();
                return rows.Select(x => new EnrollRow { EnrollmentID = x.e.EnrollmentID, UserID = uid, Name = x.Title, Status = x.e.Status, Score = x.e.Score, CertificateNo = x.e.CertificateNo, EnrolledAt = x.e.CompletedAt.HasValue ? D(x.e.CompletedAt.Value) : null }).ToList();
            }
        }
    }
}
