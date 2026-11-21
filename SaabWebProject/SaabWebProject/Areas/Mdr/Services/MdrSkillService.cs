using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 8 | ماتریس مهارت (پنل ۱۵)
    // - سطح فعلی هر مهارت از سه منبع می‌آید: Manager (ارزیابی مدیر)، Self (خوداظهاری)، Course (تکمیل دوره‌ی دارای سطح هدف).
    //   «سطح مؤثر» = ارزیابی مدیر اگر هست؛ وگرنه بیشینه‌ی Self و Course؛ اگر هیچ‌کدام نبود «داده ناکافی».
    // - تعارض ارزیابی: اختلاف Self و Manager حداقل ۲ سطح.
    // - نیازمندی از «پروفایل» می‌آید که مدیر صراحتاً به کاربر نسبت می‌دهد (عضویت واحد سازمانی در دیتابیس نیست و حدس زده نمی‌شود).
    // - هر کاربر فقط سطر خودش را می‌بیند و فقط برای خودش خوداظهاری ثبت می‌کند؛ ارزیابی دیگران و تعریف مهارت/پروفایل فقط مدیر (CanEdit صفحه‌ی MDR.Skills).
    // =====================================================
    public class SkillCell
    {
        public int SkillID { get; set; }
        public int? Required { get; set; }
        public int? Effective { get; set; }
        public int? Manager { get; set; }
        public int? Self { get; set; }
        public int? Course { get; set; }
        public int Gap { get; set; }
        public bool NoData { get; set; }
        public bool Conflict { get; set; }
    }
    public class SkillRow { public int UserID { get; set; } public string Name { get; set; } public List<string> Profiles { get; set; } public List<SkillCell> Cells { get; set; } }
    public class SkillCol { public int SkillID { get; set; } public string Name { get; set; } public string Category { get; set; } }
    public class SkillMatrix
    {
        public bool IsManager { get; set; }
        public List<SkillCol> Skills { get; set; }
        public List<SkillRow> Rows { get; set; }
        public int Gaps { get; set; }
        public int NoData { get; set; }
        public int Conflicts { get; set; }
    }
    public class SkillSuggestion { public int SkillID { get; set; } public string Skill { get; set; } public int Required { get; set; } public int? Effective { get; set; } public List<SuggestedCourse> Courses { get; set; } }
    public class SuggestedCourse { public int CourseID { get; set; } public string Code { get; set; } public string Title { get; set; } public string StartAt { get; set; } }
    public class SkillLookup { public List<SkillCol> Skills { get; set; } public List<NamedId> Profiles { get; set; } }
    public class NamedId { public int Id { get; set; } public string Name { get; set; } }

    public static class MdrSkillService
    {
        private const string Module = "Skills";
        public const string Page = "MDR.Skills";

        public static bool IsManager(int uid) { return PermissionHelper.HasAccess(uid, Page) && PermissionHelper.CanEdit(uid, Page); }

        // ---------------- تعریف‌ها ----------------
        public static MdrResult CreateSkill(int actor, string name, string category)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 150) return MdrResult.Fail("skill.err.name");
            try
            {
                using (var db = new MdrContext())
                {
                    var n = MdrCaseService.Norm(name);
                    if (db.Skills.ToList().Any(s => MdrCaseService.Norm(s.Name) == n)) return MdrResult.Fail("skill.err.dup");
                    var s2 = new MdrSkill { Name = name, Category = Trim(category, 60), IsActive = true, CreatedAt = DateTime.Now };
                    db.Skills.Add(s2); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "تعریف مهارت", entityName: "MDR_Skills", entityId: s2.SkillID, entityTitle: name);
                    return MdrResult.Success(s2.SkillID);
                }
            }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("skill.err.dup") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult CreateProfile(int actor, string name)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 150) return MdrResult.Fail("skill.err.name");
            try
            {
                using (var db = new MdrContext())
                {
                    var n = MdrCaseService.Norm(name);
                    if (db.SkillProfiles.ToList().Any(s => MdrCaseService.Norm(s.Name) == n)) return MdrResult.Fail("skill.err.dup");
                    var p = new MdrSkillProfile { Name = name, CreatedAt = DateTime.Now };
                    db.SkillProfiles.Add(p); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Create, Module, "تعریف پروفایل مهارتی", entityName: "MDR_SkillProfiles", entityId: p.ProfileID, entityTitle: name);
                    return MdrResult.Success(p.ProfileID);
                }
            }
            catch (DbUpdateException ex) { return IsDup(ex) ? MdrResult.Fail("skill.err.dup") : Failed(ex); }
            catch (Exception ex) { return Failed(ex); }
        }

        // level 0 = حذف نیازمندی
        public static MdrResult SetProfileSkill(int actor, int profileId, int skillId, int level)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            if (level < 0 || level > 5) return MdrResult.Fail("skill.err.level");
            try
            {
                using (var db = new MdrContext())
                {
                    if (!db.SkillProfiles.Any(p => p.ProfileID == profileId)) return MdrResult.Fail("wf.err.notfound");
                    if (!db.Skills.Any(s => s.SkillID == skillId && s.IsActive)) return MdrResult.Fail("skill.err.skill");
                    var ps = db.ProfileSkills.FirstOrDefault(x => x.ProfileID == profileId && x.SkillID == skillId);
                    if (level == 0) { if (ps != null) db.ProfileSkills.Remove(ps); }
                    else if (ps == null) db.ProfileSkills.Add(new MdrProfileSkill { ProfileID = profileId, SkillID = skillId, RequiredLevel = (byte)level });
                    else ps.RequiredLevel = (byte)level;
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "نیازمندی پروفایل", entityName: "MDR_ProfileSkills", entityId: profileId, newValues: new { skillId, level });
                    return MdrResult.Success(profileId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult AssignProfile(int actor, int userId, int profileId)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    if (!db.SkillProfiles.Any(p => p.ProfileID == profileId)) return MdrResult.Fail("wf.err.notfound");
                    if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", userId).First() != 1) return MdrResult.Fail("wf.err.baduser");
                    if (db.UserProfiles.Any(x => x.UserID == userId && x.ProfileID == profileId)) return MdrResult.Fail("skill.err.assigned");
                    db.UserProfiles.Add(new MdrUserProfile { UserID = userId, ProfileID = profileId, AssignedBy = actor, AssignedAt = DateTime.Now });
                    db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "نسبت‌دادن پروفایل", entityName: "MDR_UserProfiles", entityId: userId, newValues: new { profileId });
                    return MdrResult.Success(userId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult UnassignProfile(int actor, int userId, int profileId)
        {
            if (!IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    var x = db.UserProfiles.FirstOrDefault(u => u.UserID == userId && u.ProfileID == profileId);
                    if (x == null) return MdrResult.Fail("wf.err.notfound");
                    db.UserProfiles.Remove(x); db.SaveChanges();
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "برداشتن پروفایل", entityName: "MDR_UserProfiles", entityId: userId, oldValues: new { profileId });
                    return MdrResult.Success(userId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- ارزیابی ----------------
        public static MdrResult Assess(int actor, int userId, int skillId, int level, string note)
        {
            if (level < 0 || level > 5) return MdrResult.Fail("skill.err.level");
            var self = actor == userId;
            if (!self && !IsManager(actor)) return MdrResult.Fail("tsk.err.notallowed");
            try
            {
                using (var db = new MdrContext())
                {
                    if (!db.Skills.Any(s => s.SkillID == skillId && s.IsActive)) return MdrResult.Fail("skill.err.skill");
                    if (!self && db.Database.SqlQuery<int>("SELECT COUNT(*) FROM Usser.tbUsers WHERE usr_ID = @p0 AND usr_IsActive = 1", userId).First() != 1) return MdrResult.Fail("wf.err.baduser");
                    var src = self ? "Self" : "Manager";
                    var us = db.UserSkills.FirstOrDefault(x => x.UserID == userId && x.SkillID == skillId && x.Source == src);
                    var old = us == null ? (int?)null : us.Level;
                    if (us == null) { us = new MdrUserSkill { UserID = userId, SkillID = skillId, Source = src }; db.UserSkills.Add(us); }
                    us.Level = (byte)level; us.AssessedBy = actor; us.AssessedAt = DateTime.Now; us.Note = Trim(note, 300);
                    db.SaveChanges();
                    if (!self) NotificationService.Send(userId, "سطح مهارت شما ارزیابی شد", null, moduleName: Module, entityName: "MDR_UserSkills", entityId: skillId, linkUrl: "/Mdr/Training/Skills");
                    AuditLogger.Log(AuditLogger.Actions.Update, Module, self ? "خوداظهاری مهارت" : "ارزیابی مهارت", entityName: "MDR_UserSkills", entityId: userId, oldValues: new { Level = old }, newValues: new { skillId, level, source = src });
                    return MdrResult.Success(skillId);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- خواندن ----------------
        public static SkillLookup Lookups()
        {
            using (var db = new MdrContext())
                return new SkillLookup
                {
                    Skills = db.Skills.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new SkillCol { SkillID = s.SkillID, Name = s.Name, Category = s.Category }).ToList(),
                    Profiles = db.SkillProfiles.OrderBy(p => p.Name).Select(p => new NamedId { Id = p.ProfileID, Name = p.Name }).ToList()
                };
        }

        // سطح مؤثر: ارزیابی مدیر؛ وگرنه بیشینه‌ی Self/Course؛ وگرنه null
        public static int? Effective(int? manager, int? self, int? course)
        {
            if (manager.HasValue) return manager;
            if (self.HasValue || course.HasValue) return Math.Max(self ?? 0, course ?? 0);
            return null;
        }

        public static SkillMatrix Matrix(int uid, int? profileId)
        {
            using (var db = new MdrContext())
            {
                var mgr = IsManager(uid);
                var up = db.UserProfiles.AsQueryable();
                if (profileId.HasValue) up = up.Where(x => x.ProfileID == profileId.Value);
                var assigned = (mgr ? up : up.Where(x => x.UserID == uid)).ToList();
                var userIds = assigned.Select(x => x.UserID).Distinct().ToList();
                if (!mgr && !userIds.Contains(uid)) userIds.Add(uid);          // بدون پروفایل هم سطر خودش (از ارزیابی‌هایش) نمایش داده می‌شود
                if (userIds.Count > 500) userIds = userIds.Take(500).ToList();

                var profIds = assigned.Select(x => x.ProfileID).Distinct().ToList();
                var reqs = db.ProfileSkills.Where(p => profIds.Contains(p.ProfileID)).ToList();
                var us = db.UserSkills.Where(x => userIds.Contains(x.UserID)).ToList();

                var skillIds = (profileId.HasValue ? reqs.Select(r => r.SkillID) : reqs.Select(r => r.SkillID).Union(us.Select(u => u.SkillID))).Distinct().ToList();   // با فیلتر پروفایل فقط ستون‌های همان پروفایل
                var skills = db.Skills.Where(s => skillIds.Contains(s.SkillID) && s.IsActive).OrderBy(s => s.Category).ThenBy(s => s.Name).ToList();
                var names = MdrChatService.Names(db, userIds);
                var pnames = db.SkillProfiles.Where(p => profIds.Contains(p.ProfileID)).ToDictionary(p => p.ProfileID, p => p.Name);

                var m = new SkillMatrix { IsManager = mgr, Skills = skills.Select(s => new SkillCol { SkillID = s.SkillID, Name = s.Name, Category = s.Category }).ToList(), Rows = new List<SkillRow>() };
                foreach (var u in userIds)
                {
                    var myProf = assigned.Where(a => a.UserID == u).Select(a => a.ProfileID).ToList();
                    var row = new SkillRow { UserID = u, Name = names.ContainsKey(u) ? names[u] : "#" + u, Profiles = myProf.Select(p => pnames.ContainsKey(p) ? pnames[p] : "#" + p).ToList(), Cells = new List<SkillCell>() };
                    foreach (var s in skills)
                    {
                        int? required = reqs.Where(r => myProf.Contains(r.ProfileID) && r.SkillID == s.SkillID).Select(r => (int?)r.RequiredLevel).Max();
                        Func<string, int?> lv = src => us.Where(x => x.UserID == u && x.SkillID == s.SkillID && x.Source == src).Select(x => (int?)x.Level).FirstOrDefault();
                        var man = lv("Manager"); var slf = lv("Self"); var crs = lv("Course");
                        var eff = Effective(man, slf, crs);
                        var cell = new SkillCell { SkillID = s.SkillID, Required = required, Effective = eff, Manager = man, Self = slf, Course = crs, Conflict = man.HasValue && slf.HasValue && Math.Abs(man.Value - slf.Value) >= 2 };
                        if (required.HasValue)
                        {
                            if (!eff.HasValue) { cell.NoData = true; cell.Gap = required.Value; }
                            else cell.Gap = Math.Max(0, required.Value - eff.Value);
                        }
                        if (cell.Gap > 0 && !cell.NoData) m.Gaps++;
                        if (cell.NoData) m.NoData++;
                        if (cell.Conflict) m.Conflicts++;
                        row.Cells.Add(cell);
                    }
                    m.Rows.Add(row);
                }
                return m;
            }
        }

        // پیشنهاد آموزش برای شکاف‌های یک کاربر (خودش یا مدیر)
        public static List<SkillSuggestion> Suggestions(int uid, int userId)
        {
            if (uid != userId && !IsManager(uid)) return null;
            using (var db = new MdrContext())
            {
                var profIds = db.UserProfiles.Where(x => x.UserID == userId).Select(x => x.ProfileID).ToList();
                var reqs = db.ProfileSkills.Where(p => profIds.Contains(p.ProfileID)).GroupBy(p => p.SkillID).Select(g => new { SkillID = g.Key, Req = g.Max(x => (int)x.RequiredLevel) }).ToList();
                var us = db.UserSkills.Where(x => x.UserID == userId).ToList();
                var taken = db.CourseEnrollments.Where(e => e.UserID == userId && e.Status != "Cancelled" && e.Status != "Failed").Select(e => e.CourseID).ToList();
                var now = DateTime.Now;
                var res = new List<SkillSuggestion>();
                foreach (var r in reqs)
                {
                    Func<string, int?> lv = src => us.Where(x => x.SkillID == r.SkillID && x.Source == src).Select(x => (int?)x.Level).FirstOrDefault();
                    var eff = Effective(lv("Manager"), lv("Self"), lv("Course"));
                    if (eff.HasValue && eff.Value >= r.Req) continue;
                    var sk = db.Skills.Where(s => s.SkillID == r.SkillID).Select(s => s.Name).FirstOrDefault();
                    var courses = db.Courses.Where(c => c.SkillID == r.SkillID && c.Status == "Open" && c.StartAt > now && (c.TargetLevel == null || c.TargetLevel >= r.Req) && !taken.Contains(c.CourseID))
                        .OrderBy(c => c.StartAt).Take(3).ToList().Select(c => new SuggestedCourse { CourseID = c.CourseID, Code = c.Code, Title = c.Title, StartAt = c.StartAt.ToString("yyyy-MM-dd HH:mm") }).ToList();
                    res.Add(new SkillSuggestion { SkillID = r.SkillID, Skill = sk, Required = r.Req, Effective = eff, Courses = courses });
                }
                return res;
            }
        }

        private static string Trim(string s, int max) { if (string.IsNullOrWhiteSpace(s)) return null; s = s.Trim(); return s.Length > max ? s.Substring(0, max) : s; }
        private static bool IsDup(Exception ex) { for (var e = ex; e != null; e = e.InnerException) { var s = e as System.Data.SqlClient.SqlException; if (s != null && (s.Number == 2601 || s.Number == 2627)) return true; } return false; }
        private static MdrResult Failed(Exception ex) { AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای ماتریس مهارت", newValues: new { ex.Message }, isSuccess: false); return MdrResult.Fail("wf.err.unexpected"); }
    }
}
