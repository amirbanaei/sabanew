using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Web;
using System.Web.Mvc;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOIWorkbook = NPOI.SS.UserModel.IWorkbook;
using SaabWebProject.Models.DomainModels;
// using ...Models.DomainModels;   // همان using کنترلر وصول

/*
   پیش‌نیاز: بعد از اجرای اسکریپت SQL، جدول‌های جدید را به EDMX / DbContext اضافه کنید:
   tbLossActivityType, tbLossAssignment, tbLossAssignmentUser, tbLossAssignmentActivity,
   tbLossAgentScope, tbLossAgentScopeRegion, tbLossAgentScopeWorkDay, tbLossAgentScopeTariff,
   tbLossAgentScopeArea, tbLossAgentScopeZoneType
   (نام و نوع پراپرتی‌ها مثل معادل tbOrder... است؛ tbLossAgentScopeZoneType یک فیلد ZoneType از نوع byte دارد.)

   db و Utility.Base64 همان‌هایی هستند که در کنترلر وصول استفاده می‌کنید.
   اگر این کد را داخل همان کنترلر Orders می‌گذارید، متدهای تکراری
   GetCurrentUserId / GetAllowedUserIds / GetAllowedRegions را حذف کنید.
*/

namespace SaabWebProject.Areas.Setting.Controllers
{
    public class LossesController : Controller
    {
        private readonly SaabEntities db = new SaabEntities();   // ← نام DbContext پروژه خودتان

        #region مدیریت تخصیص نیرو - تلفات

        /* =========================================================
           CONSTANTS
           ========================================================= */

        private const string ROLE_ASSIGNMENT_TITLE = "تعیین نقش";

        private const int AREA_MIN = 0;
        private const int AREA_MAX = 45;
        private const int AREA_TOTAL = 46;

        // نوع محدوده: 1 = شهری ، 2 = روستایی
        private const byte ZONE_URBAN = 1;
        private const byte ZONE_RURAL = 2;
        private const int ZONE_TOTAL = 2;

        /* =========================================================
           CURRENT USER / ALLOWED USERS / REGIONS
           ========================================================= */

        private int? GetCurrentUserId()
        {
            try
            {
                var cookie_user = Request.Cookies[Utility.Base64.Base64Encode("CodeMeli")];
                if (cookie_user == null) return null;

                var nationalcode = Utility.Base64.Base64Decode(cookie_user.Value);

                var user = db.tbUsers.FirstOrDefault(x => x.usr_NationalCode == nationalcode);
                if (user == null) return null;

                return user.usr_ID;
            }
            catch
            {
                return null;
            }
        }

        private List<int> GetAllowedUserIds(int currentUserId)
        {
            return db.tbAdamAdam
                .Where(x => x.Name == currentUserId && x.Value == 1 && x.nam.HasValue)
                .Select(x => x.nam.Value)
                .Distinct()
                .ToList();
        }

        private void AddRegionValue(HashSet<int> result, int? value)
        {
            if (value.HasValue && value.Value > 0) result.Add(value.Value);
        }

        private List<int> GetAllowedRegions(int? cityId)
        {
            var result = new HashSet<int>();

            if (!cityId.HasValue) return result.ToList();

            var links = db.tblink_mantegeh_city
                .Where(x => x.FK_city == cityId.Value && (x.vaz == 1 || x.vaz == null))
                .ToList();

            foreach (var item in links)
            {
                AddRegionValue(result, item.mantaghe);
                AddRegionValue(result, item.mantaghe1);
                AddRegionValue(result, item.mantaghe2);
                AddRegionValue(result, item.mantaghe3);
                AddRegionValue(result, item.mantaghe4);
                AddRegionValue(result, item.mantaghe5);
                AddRegionValue(result, item.mantaghe6);
                AddRegionValue(result, item.mantaghe7);
                AddRegionValue(result, item.mantaghe8);
                AddRegionValue(result, item.mantaghe9);
                AddRegionValue(result, item.mantaghe10);
            }

            return result.OrderBy(x => x).ToList();
        }

        /* =========================================================
           TAB 2 ELIGIBLE USERS
           ========================================================= */

        private List<int> GetEligibleTab2UserIds(int currentUserId)
        {
            var allowedUserIds = GetAllowedUserIds(currentUserId);

            var activeAssignmentId = db.tbLossAssignment
                .Where(x => x.IsActive == true && x.Title != null && x.Title.Trim() == ROLE_ASSIGNMENT_TITLE)
                .OrderByDescending(x => x.ID)
                .Select(x => (int?)x.ID)
                .FirstOrDefault();

            if (!activeAssignmentId.HasValue) return new List<int>();

            var withActivity = db.tbLossAssignmentActivity
                .Where(x => x.AssignmentID == activeAssignmentId.Value)
                .Select(x => x.UserID)
                .Distinct()
                .ToList();

            return withActivity.Where(x => allowedUserIds.Contains(x)).Distinct().ToList();
        }

        /* =========================================================
           PARSE USER / ACTIVITY PAIRS   (مثال: 120_1)
           ========================================================= */

        private List<KeyValuePair<int, int>> ParseUserActivityPairs(
            List<int> userIds, List<string> userActivityPairs, List<int> validActivityIds)
        {
            var result = new List<KeyValuePair<int, int>>();

            if (userIds == null || userActivityPairs == null) return result;

            foreach (var pairText in userActivityPairs.Distinct())
            {
                if (string.IsNullOrWhiteSpace(pairText)) continue;

                var parts = pairText.Split('_');
                if (parts.Length != 2) continue;

                int userId, activityId;

                if (!int.TryParse(parts[0], out userId)) continue;
                if (!int.TryParse(parts[1], out activityId)) continue;

                if (!userIds.Contains(userId)) continue;
                if (!validActivityIds.Contains(activityId)) continue;

                result.Add(new KeyValuePair<int, int>(userId, activityId));
            }

            return result
                .GroupBy(x => new { UserID = x.Key, ActivityID = x.Value })
                .Select(x => x.First())
                .ToList();
        }

        /* =========================================================
           PREPARE PAGE DATA
           ========================================================= */

        private string PrepareLossAssignmentPageData(int currentUserId)
        {
            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId);
            if (currentUser == null) return "کاربر پیدا نشد";

            var allowedUserIds = GetAllowedUserIds(currentUserId);

            var users = db.tbUsers
                .Where(x => x.usr_IsActive == true && allowedUserIds.Contains(x.usr_ID))
                .OrderBy(x => x.usr_Family)
                .ThenBy(x => x.usr_Name)
                .ToList();

            /* ---------- TAB 1 ---------- */

            var activityTypes = db.tbLossActivityType
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ToList();

            var assignments = db.tbLossAssignment
                .Where(x => x.IsActive == true && x.Title != null && x.Title.Trim() == ROLE_ASSIGNMENT_TITLE)
                .OrderByDescending(x => x.ID)
                .Take(1)
                .ToList();

            var assignmentIds = assignments.Select(x => x.ID).ToList();

            var assignmentUsers = db.tbLossAssignmentUser
                .Where(x => assignmentIds.Contains(x.AssignmentID))
                .ToList();

            var assignmentActivities = db.tbLossAssignmentActivity
                .Where(x => assignmentIds.Contains(x.AssignmentID))
                .ToList();

            /* ---------- TAB 2 ---------- */

            var tab2UserIds = GetEligibleTab2UserIds(currentUserId);

            var tab2Users = users
                .Where(x => tab2UserIds.Contains(x.usr_ID))
                .OrderBy(x => x.usr_Family)
                .ThenBy(x => x.usr_Name)
                .ToList();

            int? currentCityId = currentUser.usr_City_Dutysystem;

            var allowedRegions = GetAllowedRegions(currentCityId);

            var agentScopes = db.tbLossAgentScope
                .Where(x => x.IsActive == true && tab2UserIds.Contains(x.UserID))
                .OrderByDescending(x => x.ID)
                .ToList();

            var scopeIds = agentScopes.Select(x => x.ID).ToList();

            var agentScopeRegions = db.tbLossAgentScopeRegion.Where(x => scopeIds.Contains(x.ScopeID)).ToList();
            var agentScopeWorkDays = db.tbLossAgentScopeWorkDay.Where(x => scopeIds.Contains(x.ScopeID)).ToList();
            var agentScopeTariffs = db.tbLossAgentScopeTariff.Where(x => scopeIds.Contains(x.ScopeID)).ToList();
            var agentScopeAreas = db.tbLossAgentScopeArea.Where(x => scopeIds.Contains(x.ScopeID)).ToList();
            var agentScopeZoneTypes = db.tbLossAgentScopeZoneType.Where(x => scopeIds.Contains(x.ScopeID)).ToList();

            var tariffTypes = db.tbOrderTariffType
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.ID)
                .ToList();

            /* ---------- TAB 3 (استثناها) ---------- */

            var excelImportBatches = new List<tbLossExcelImportBatch>();

            if (currentCityId.HasValue)
            {
                excelImportBatches = db.tbLossExcelImportBatch
                    .Where(x => x.UserID == currentUserId && x.CityID == currentCityId.Value && x.IsActive == true)
                    .OrderByDescending(x => x.ID)
                    .ToList();
            }

            ViewBag.ExcelImportBatches = excelImportBatches;

            /* ---------- VIEWBAG ---------- */

            ViewBag.Users = users;
            ViewBag.ActivityTypes = activityTypes;
            ViewBag.Assignments = assignments;
            ViewBag.AssignmentUsers = assignmentUsers;
            ViewBag.AssignmentActivities = assignmentActivities;

            ViewBag.Tab2Users = tab2Users;
            ViewBag.AllowedRegions = allowedRegions;
            ViewBag.AgentScopes = agentScopes;
            ViewBag.ElectricalSettings = agentScopes.SelectMany(s => LoadElectrical(s.ID)).ToList();
            ViewBag.AgentScopeRegions = agentScopeRegions;
            ViewBag.AgentScopeWorkDays = agentScopeWorkDays;
            ViewBag.TariffTypes = tariffTypes;
            ViewBag.AgentScopeTariffs = agentScopeTariffs;
            ViewBag.AgentScopeAreas = agentScopeAreas;
            ViewBag.AgentScopeZoneTypes = agentScopeZoneTypes;
            ViewBag.CurrentCityID = currentCityId;

            ViewBag.UserCount = users.Count;
            ViewBag.ActivityTypeCount = activityTypes.Count;
            ViewBag.AssignmentCount = assignments.Count;

            return null;
        }

        /* =========================================================
           PAGES
           ========================================================= */

        [HttpGet]
        public ActionResult LossAssignments()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Content("دسترسی غیر مجاز");

            var error = PrepareLossAssignmentPageData(currentUserId.Value);
            if (!string.IsNullOrWhiteSpace(error)) return Content(error);

            return View();
        }

        /* =========================================================
           TAB 1 SAVE (فقط UserIds ارسال‌شده تغییر می‌کنند)
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateLossAssignment(
            string Title, string Description, List<int> UserIds, List<string> UserActivityPairs)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "کاربر معتبر نیست." });

            UserIds = (UserIds ?? new List<int>()).Distinct().ToList();

            if (UserIds.Count == 0)
                return Json(new { success = false, message = "هیچ نیرویی برای ثبت وجود ندارد." });

            var allowedUserIds = new HashSet<int>(GetAllowedUserIds(currentUserId.Value));

            if (UserIds.Any(x => !allowedUserIds.Contains(x)))
                return Json(new { success = false, message = "یک یا چند نیرو برای شما مجاز نیست." });

            var validUsers = new HashSet<int>(
                db.tbUsers
                    .Where(x => UserIds.Contains(x.usr_ID) && x.usr_IsActive == true)
                    .Select(x => x.usr_ID)
                    .ToList());

            if (validUsers.Count != UserIds.Count)
                return Json(new { success = false, message = "یک یا چند نیروی انتخاب‌شده معتبر یا فعال نیست." });

            var validActivityIds = db.tbLossActivityType
                .Where(x => x.IsActive)
                .Select(x => x.ID)
                .ToList();

            var parsedPairs = ParseUserActivityPairs(UserIds, UserActivityPairs ?? new List<string>(), validActivityIds);

            // هر نیرو فقط یک نقش می‌تواند داشته باشد
            if (parsedPairs.GroupBy(x => x.Key).Any(g => g.Count() > 1))
                return Json(new { success = false, message = "هر نیرو فقط می‌تواند یک نقش داشته باشد." });

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var now = DateTime.Now;

                    var assignment = db.tbLossAssignment
                        .Where(x => x.Title != null && x.Title.Trim() == ROLE_ASSIGNMENT_TITLE)
                        .OrderByDescending(x => x.IsActive)
                        .ThenByDescending(x => x.ID)
                        .FirstOrDefault();

                    var created = assignment == null;

                    if (created)
                    {
                        assignment = new tbLossAssignment
                        {
                            Title = ROLE_ASSIGNMENT_TITLE,
                            Description = "",
                            IsActive = true,
                            CreateDate = now,
                            CreatedByUserID = currentUserId.Value
                        };

                        db.tbLossAssignment.Add(assignment);
                        db.SaveChanges();
                    }
                    else
                    {
                        assignment.Title = ROLE_ASSIGNMENT_TITLE;
                        assignment.Description = "";
                        assignment.IsActive = true;
                    }

                    var existingUserIds = new HashSet<int>(
                        db.tbLossAssignmentUser
                            .Where(x => x.AssignmentID == assignment.ID && UserIds.Contains(x.UserID))
                            .Select(x => x.UserID)
                            .Distinct()
                            .ToList());

                    foreach (var userId in UserIds)
                    {
                        if (!existingUserIds.Contains(userId))
                        {
                            db.tbLossAssignmentUser.Add(new tbLossAssignmentUser
                            {
                                AssignmentID = assignment.ID,
                                UserID = userId
                            });
                        }
                    }

                    var desiredKeys = new HashSet<string>(
                        parsedPairs.Select(x => x.Key.ToString() + "_" + x.Value.ToString()));

                    var oldActivities = db.tbLossAssignmentActivity
                        .Where(x => x.AssignmentID == assignment.ID && UserIds.Contains(x.UserID))
                        .ToList();

                    var oldKeys = new HashSet<string>();

                    foreach (var group in oldActivities.GroupBy(x => x.UserID.ToString() + "_" + x.ActivityTypeID.ToString()))
                    {
                        oldKeys.Add(group.Key);

                        if (!desiredKeys.Contains(group.Key))
                        {
                            db.tbLossAssignmentActivity.RemoveRange(group);
                        }
                        else
                        {
                            var duplicateRows = group.Skip(1).ToList();
                            if (duplicateRows.Count > 0) db.tbLossAssignmentActivity.RemoveRange(duplicateRows);
                        }
                    }

                    // حذف‌ها قبل از درج (Unique Index)
                    db.SaveChanges();

                    foreach (var pair in parsedPairs)
                    {
                        var key = pair.Key.ToString() + "_" + pair.Value.ToString();

                        if (!oldKeys.Contains(key))
                        {
                            db.tbLossAssignmentActivity.Add(new tbLossAssignmentActivity
                            {
                                AssignmentID = assignment.ID,
                                UserID = pair.Key,
                                ActivityTypeID = pair.Value
                            });
                        }
                    }

                    assignment.ModifiedDate = now;
                    assignment.ModifiedByUserID = currentUserId.Value;

                    db.SaveChanges();
                    transaction.Commit();

                    return Json(new
                    {
                        success = true,
                        updated = !created,
                        id = assignment.ID,
                        affectedUsers = UserIds.Count,
                        message = desiredKeys.Count == 0
                            ? "همه نقش‌ها با موفقیت خالی و ذخیره شدند."
                            : "نقش نیروها با موفقیت ذخیره شد."
                    });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    var root = ex;
                    while (root.InnerException != null) root = root.InnerException;

                    return Json(new { success = false, message = "خطا در ثبت یا بروزرسانی تعیین نقش: " + root.Message });
                }
            }
        }

        /* =========================================================
           TAB 2 - RULES  (هر منطقه ← روزکار، تعرفه، محدوده، شهری/روستایی)
           RegionCode = NULL → برای همه مناطق مامور
           ========================================================= */

        public class LossElectricalSetting
        {
            public long ScopeID { get; set; }
            public int RegionCode { get; set; }
            public int SupplyType { get; set; } // 1 عادی، 2 دیماند، 3 هر دو
            public int PhaseMask { get; set; } // 1 تک فاز، 2 سه فاز، 3 هر دو
        }

        private List<LossElectricalSetting> LoadElectrical(long scopeId)
        {
            return db.Database.SqlQuery<LossElectricalSetting>(
                "SELECT ScopeID, RegionCode, SupplyType, PhaseMask FROM dbo.tbLossScopeElectrical WHERE ScopeID = @p0", scopeId).ToList();
        }

        public class LossScopeRuleDto
        {
            public int SupplyType { get; set; }
            public int PhaseMask { get; set; }
            public int Region { get; set; }          // 0 = همه مناطق
            public List<int> Days { get; set; }      // خالی = همه روزها
            public List<int> Tariffs { get; set; }   // خالی = همه تعرفه‌ها
            public List<int> Areas { get; set; }     // خالی (یا 0 تا 45 کامل) = همه محدوده‌ها
            public List<int> ZoneTypes { get; set; } // خالی (یا هر دو) = شهری و روستایی
        }

        private List<LossScopeRuleDto> LoadScopeRules(long scopeId)
        {
            var regions = db.tbLossAgentScopeRegion
                .Where(x => x.ScopeID == scopeId)
                .Select(x => x.RegionCode)
                .ToList()
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var days = db.tbLossAgentScopeWorkDay
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.WorkDay })
                .ToList();

            var tariffs = db.tbLossAgentScopeTariff
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.TariffTypeID })
                .ToList();

            var areas = db.tbLossAgentScopeArea
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.AreaCode })
                .ToList();

            var zones = db.tbLossAgentScopeZoneType
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.ZoneType })
                .ToList();

            Func<int?, List<int>> daysOf = r => days.Where(x => x.RegionCode == r).Select(x => x.WorkDay).Distinct().OrderBy(x => x).ToList();
            Func<int?, List<int>> tariffsOf = r => tariffs.Where(x => x.RegionCode == r).Select(x => x.TariffTypeID).Distinct().OrderBy(x => x).ToList();
            Func<int?, List<int>> areasOf = r => areas.Where(x => x.RegionCode == r).Select(x => x.AreaCode).Distinct().OrderBy(x => x).ToList();
            Func<int?, List<int>> zonesOf = r => zones.Where(x => x.RegionCode == r).Select(x => (int)x.ZoneType).Distinct().OrderBy(x => x).ToList();

            var generalDays = daysOf(null);
            var generalTariffs = tariffsOf(null);
            var generalAreas = areasOf(null);
            var generalZones = zonesOf(null);

            // کامل بودن = بدون محدودیت → خالی برگردانده می‌شود
            Func<List<int>, List<int>> normalizeAreas = l => l.Count >= AREA_TOTAL ? new List<int>() : l;
            Func<List<int>, List<int>> normalizeZones = l => l.Count >= ZONE_TOTAL ? new List<int>() : l;

            var result = new List<LossScopeRuleDto>();

            if (regions.Count == 0
                && (generalDays.Count > 0 || generalTariffs.Count > 0
                    || normalizeAreas(generalAreas).Count > 0 || normalizeZones(generalZones).Count > 0))
            {
                result.Add(new LossScopeRuleDto
                {
                    Region = 0,
                    Days = generalDays,
                    Tariffs = generalTariffs,
                    Areas = normalizeAreas(generalAreas),
                    ZoneTypes = normalizeZones(generalZones)
                });
            }

            foreach (var r in regions)
            {
                var ownDays = daysOf(r);
                var ownTariffs = tariffsOf(r);
                var ownAreas = areasOf(r);
                var ownZones = zonesOf(r);

                result.Add(new LossScopeRuleDto
                {
                    Region = r,
                    Days = ownDays.Count > 0 ? ownDays : generalDays,
                    Tariffs = ownTariffs.Count > 0 ? ownTariffs : generalTariffs,
                    Areas = normalizeAreas(ownAreas.Count > 0 ? ownAreas : generalAreas),
                    ZoneTypes = normalizeZones(ownZones.Count > 0 ? ownZones : generalZones)
                });
            }

            var electrical = LoadElectrical(scopeId);
            if (result.Count == 0 && electrical.Any(x => x.RegionCode == 0))
                result.Add(new LossScopeRuleDto { Region = 0 });
            foreach (var rule in result)
            {
                var setting = electrical.FirstOrDefault(x => x.RegionCode == rule.Region)
                    ?? electrical.FirstOrDefault(x => x.RegionCode == 0);
                rule.SupplyType = setting == null ? 1 : setting.SupplyType;
                rule.PhaseMask = setting == null ? 3 : setting.PhaseMask;
            }
            return result;
        }

        private void ClearScopeRules(long scopeId)
        {
            db.Database.ExecuteSqlCommand("DELETE FROM dbo.tbLossScopeElectrical WHERE ScopeID = @p0", scopeId);
            var t = db.tbLossAgentScopeTariff.Where(x => x.ScopeID == scopeId).ToList();
            if (t.Count > 0) db.tbLossAgentScopeTariff.RemoveRange(t);

            var a = db.tbLossAgentScopeArea.Where(x => x.ScopeID == scopeId).ToList();
            if (a.Count > 0) db.tbLossAgentScopeArea.RemoveRange(a);

            var z = db.tbLossAgentScopeZoneType.Where(x => x.ScopeID == scopeId).ToList();
            if (z.Count > 0) db.tbLossAgentScopeZoneType.RemoveRange(z);

            var r = db.tbLossAgentScopeRegion.Where(x => x.ScopeID == scopeId).ToList();
            if (r.Count > 0) db.tbLossAgentScopeRegion.RemoveRange(r);

            var d = db.tbLossAgentScopeWorkDay.Where(x => x.ScopeID == scopeId).ToList();
            if (d.Count > 0) db.tbLossAgentScopeWorkDay.RemoveRange(d);

            db.SaveChanges();
        }

        /*
           Upsert بر اساس (مامور + منطقه): فقط همان منطقه ویرایش/اضافه می‌شود.
           Region = 0 → «همه مناطق» (RegionCode = NULL)
        */
        private void UpsertScopeRules(long scopeId, List<LossScopeRuleDto> rules)
        {
            var existingRegions = new HashSet<int>(
                db.tbLossAgentScopeRegion
                    .Where(x => x.ScopeID == scopeId)
                    .Select(x => x.RegionCode)
                    .ToList());

            foreach (var rule in rules)
            {
                if (rule.Region > 0 && !existingRegions.Contains(rule.Region))
                {
                    db.tbLossAgentScopeRegion.Add(new tbLossAgentScopeRegion
                    {
                        ScopeID = scopeId,
                        RegionCode = rule.Region
                    });

                    existingRegions.Add(rule.Region);
                }

                db.Database.ExecuteSqlCommand(
                    "DELETE FROM dbo.tbLossScopeElectrical WHERE ScopeID = @p0 AND RegionCode = @p1", scopeId, rule.Region);
                db.Database.ExecuteSqlCommand(
                    "INSERT INTO dbo.tbLossScopeElectrical (ScopeID, RegionCode, SupplyType, PhaseMask) VALUES (@p0, @p1, @p2, @p3)",
                    scopeId, rule.Region, rule.SupplyType, rule.PhaseMask);
                int? regionKey = rule.Region > 0 ? (int?)rule.Region : null;

                /* روزکارها */
                var oldDays = db.tbLossAgentScopeWorkDay
                    .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey).ToList();
                if (oldDays.Count > 0) { db.tbLossAgentScopeWorkDay.RemoveRange(oldDays); db.SaveChanges(); }

                foreach (var day in rule.Days ?? new List<int>())
                    db.tbLossAgentScopeWorkDay.Add(new tbLossAgentScopeWorkDay
                    { ScopeID = scopeId, WorkDay = day, RegionCode = regionKey });

                /* تعرفه‌ها */
                var oldTariffs = db.tbLossAgentScopeTariff
                    .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey).ToList();
                if (oldTariffs.Count > 0) { db.tbLossAgentScopeTariff.RemoveRange(oldTariffs); db.SaveChanges(); }

                foreach (var tariffId in rule.Tariffs ?? new List<int>())
                    db.tbLossAgentScopeTariff.Add(new tbLossAgentScopeTariff
                    { ScopeID = scopeId, RegionCode = regionKey, TariffTypeID = tariffId });

                /* محدوده (0 تا 45) */
                var oldAreas = db.tbLossAgentScopeArea
                    .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey).ToList();
                if (oldAreas.Count > 0) { db.tbLossAgentScopeArea.RemoveRange(oldAreas); db.SaveChanges(); }

                foreach (var areaCode in rule.Areas ?? new List<int>())
                    db.tbLossAgentScopeArea.Add(new tbLossAgentScopeArea
                    { ScopeID = scopeId, RegionCode = regionKey, AreaCode = areaCode });

                /* شهری / روستایی */
                var oldZones = db.tbLossAgentScopeZoneType
                    .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey).ToList();
                if (oldZones.Count > 0) { db.tbLossAgentScopeZoneType.RemoveRange(oldZones); db.SaveChanges(); }

                foreach (var zone in rule.ZoneTypes ?? new List<int>())
                    db.tbLossAgentScopeZoneType.Add(new tbLossAgentScopeZoneType
                    { ScopeID = scopeId, RegionCode = regionKey, ZoneType = (byte)zone });
            }

            db.SaveChanges();
        }

        /* =========================================================
           TAB 2 SAVE
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult SaveLossAgentScope(List<int> UserIds, string RulesJson, bool AddOnly = false)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId.Value);
            if (currentUser == null)
                return Json(new { success = false, message = "کاربر پیدا نشد." });

            UserIds = (UserIds ?? new List<int>()).Distinct().ToList();

            if (UserIds.Count == 0)
                return Json(new { success = false, message = "حداقل یک مامور انتخاب کنید." });

            List<LossScopeRuleDto> parsed;

            try
            {
                parsed = string.IsNullOrWhiteSpace(RulesJson)
                    ? new List<LossScopeRuleDto>()
                    : new System.Web.Script.Serialization.JavaScriptSerializer()
                          .Deserialize<List<LossScopeRuleDto>>(RulesJson)
                      ?? new List<LossScopeRuleDto>();
            }
            catch
            {
                return Json(new { success = false, message = "فرمت اطلاعات منطقه و روزکار معتبر نیست." });
            }

            var allowedRegions = GetAllowedRegions(currentUser.usr_City_Dutysystem);

            if (parsed.Any(x => x == null) || parsed.GroupBy(x => x.Region).Any(g => g.Count() > 1))
                return Json(new { success = false, message = "اطلاعات منطقه تکراری یا نامعتبر است." });
            if (parsed.Count == 0 || parsed.Any(x => (x.SupplyType < 1 || x.SupplyType > 3)
                || x.PhaseMask < 1 || x.PhaseMask > 3 || (x.SupplyType == 2 && x.PhaseMask != 2)))
                return Json(new { success = false, message = "نوع اشتراک و فاز را انتخاب کنید؛ دیماند فقط سه فاز است." });

            var rules = parsed
                .GroupBy(x => x.Region)
                .Select(g => new LossScopeRuleDto
                {
                    Region = g.Key,
                    SupplyType = g.First().SupplyType,
                    PhaseMask = g.First().PhaseMask,
                    Days = g.SelectMany(x => x.Days ?? new List<int>()).Distinct().OrderBy(x => x).ToList(),
                    Tariffs = g.SelectMany(x => x.Tariffs ?? new List<int>()).Distinct().OrderBy(x => x).ToList(),
                    Areas = g.SelectMany(x => x.Areas ?? new List<int>()).Distinct().OrderBy(x => x).ToList(),
                    ZoneTypes = g.SelectMany(x => x.ZoneTypes ?? new List<int>()).Distinct().OrderBy(x => x).ToList()
                })
                .ToList();

            if (rules.Any(x => x.Region != 0 && !allowedRegions.Contains(x.Region)))
                return Json(new { success = false, message = "یکی از مناطق انتخاب‌شده مجاز نیست." });

            if (rules.Any(x => x.Days.Any(d => d < 1 || d > 99)))
                return Json(new { success = false, message = "روزکار باید بین 1 تا 99 باشد." });

            if (rules.Any(x => x.Areas.Any(a => a < AREA_MIN || a > AREA_MAX)))
                return Json(new { success = false, message = "کد ناحیه باید بین 0 تا 45 باشد." });

            if (rules.Any(x => x.ZoneTypes.Any(z => z != ZONE_URBAN && z != ZONE_RURAL)))
                return Json(new { success = false, message = "محدوده (شهری/روستایی) معتبر نیست." });

            // کامل = بدون محدودیت → خالی ذخیره شود
            foreach (var rule in rules)
            {
                if (rule.Areas.Count >= AREA_TOTAL) rule.Areas = new List<int>();
                if (rule.ZoneTypes.Count >= ZONE_TOTAL) rule.ZoneTypes = new List<int>();
            }

            var eligibleUsers = GetEligibleTab2UserIds(currentUserId.Value);

            if (UserIds.Any(x => !eligibleUsers.Contains(x)))
                return Json(new { success = false, message = "یکی از ماموران انتخاب‌شده در تب اول تعیین نقش نشده است." });

            var activeTariffIds = db.tbOrderTariffType.Where(x => x.IsActive).Select(x => x.ID).ToList();

            if (rules.Any(x => x.Tariffs.Any(t => !activeTariffIds.Contains(t))))
                return Json(new { success = false, message = "یکی از تعرفه‌های انتخاب‌شده معتبر نیست." });

            /* ---------- حالت «افزودن منطقه دیگر»: فقط مناطق جدید، نه بازنویسی مناطق ثبت‌شده ---------- */

            if (AddOnly)
            {
                if (rules.Count == 0 || rules.Any(x => x.Region <= 0))
                    return Json(new { success = false, message = "برای افزودن، حداقل یک منطقه مشخص انتخاب کنید." });

                var newRegionCodes = rules.Select(x => x.Region).ToList();

                var userScopeIds = db.tbLossAgentScope
                    .Where(s => UserIds.Contains(s.UserID))
                    .Select(s => s.ID)
                    .ToList();

                var duplicated = db.tbLossAgentScopeRegion
                    .Where(r => userScopeIds.Contains(r.ScopeID) && newRegionCodes.Contains(r.RegionCode))
                    .Select(r => r.RegionCode)
                    .Distinct()
                    .ToList();

                if (duplicated.Count > 0)
                    return Json(new
                    {
                        success = false,
                        message = "منطقه " + string.Join("، ", duplicated)
                                  + " قبلاً برای مامور ثبت شده است؛ از دکمه «ویرایش» همان منطقه استفاده کنید."
                    });
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var now = DateTime.Now;

                    foreach (var userId in UserIds)
                    {
                        var scope = db.tbLossAgentScope.FirstOrDefault(x => x.UserID == userId);

                        if (scope == null)
                        {
                            scope = new tbLossAgentScope
                            {
                                UserID = userId,
                                CityID = currentUser.usr_City_Dutysystem,
                                IsActive = true,
                                CreateDate = now,
                                CreatedByUserID = currentUserId.Value
                            };

                            db.tbLossAgentScope.Add(scope);
                            db.SaveChanges();
                        }
                        else
                        {
                            // رکورد حذف‌شده قبلی: تنظیمات قدیمی دوباره زنده نشود
                            if (scope.IsActive != true) ClearScopeRules(scope.ID);

                            scope.CityID = currentUser.usr_City_Dutysystem;
                            scope.IsActive = true;
                            scope.ModifiedDate = now;
                            scope.ModifiedByUserID = currentUserId.Value;

                            db.SaveChanges();
                        }

                        UpsertScopeRules(scope.ID, rules);
                    }

                    transaction.Commit();

                    return Json(new { success = true, message = "اطلاعات " + UserIds.Count + " مامور با موفقیت ذخیره شد." });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    var root = ex;
                    while (root.InnerException != null) root = root.InnerException;

                    return Json(new { success = false, message = "خطا در ثبت محدوده: " + root.Message });
                }
            }
        }

        /* =========================================================
           TAB 2 DELETE ONE REGION  (region = 0 → «همه مناطق»)
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteLossAgentScopeRegion(long id, int region)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var eligibleUsers = GetEligibleTab2UserIds(currentUserId.Value);

            var scope = db.tbLossAgentScope.FirstOrDefault(x => x.ID == id && x.IsActive == true);

            if (scope == null || !eligibleUsers.Contains(scope.UserID))
                return Json(new { success = false, message = "رکورد یافت نشد." });

            try
            {
                int? regionKey = region > 0 ? (int?)region : null;

                if (region > 0)
                {
                    var regionRows = db.tbLossAgentScopeRegion
                        .Where(x => x.ScopeID == id && x.RegionCode == region).ToList();
                    if (regionRows.Count > 0) db.tbLossAgentScopeRegion.RemoveRange(regionRows);
                }

                var dayRows = db.tbLossAgentScopeWorkDay.Where(x => x.ScopeID == id && x.RegionCode == regionKey).ToList();
                if (dayRows.Count > 0) db.tbLossAgentScopeWorkDay.RemoveRange(dayRows);

                var tariffRows = db.tbLossAgentScopeTariff.Where(x => x.ScopeID == id && x.RegionCode == regionKey).ToList();
                if (tariffRows.Count > 0) db.tbLossAgentScopeTariff.RemoveRange(tariffRows);

                var areaRows = db.tbLossAgentScopeArea.Where(x => x.ScopeID == id && x.RegionCode == regionKey).ToList();
                if (areaRows.Count > 0) db.tbLossAgentScopeArea.RemoveRange(areaRows);

                var zoneRows = db.tbLossAgentScopeZoneType.Where(x => x.ScopeID == id && x.RegionCode == regionKey).ToList();
                if (zoneRows.Count > 0) db.tbLossAgentScopeZoneType.RemoveRange(zoneRows);

                db.Database.ExecuteSqlCommand("DELETE FROM dbo.tbLossScopeElectrical WHERE ScopeID = @p0 AND RegionCode = @p1", id, region > 0 ? region : 0);
                db.SaveChanges();

                bool hasAnything = LoadElectrical(id).Count > 0 ||
                    db.tbLossAgentScopeRegion.Any(x => x.ScopeID == id)
                    || db.tbLossAgentScopeWorkDay.Any(x => x.ScopeID == id)
                    || db.tbLossAgentScopeTariff.Any(x => x.ScopeID == id)
                    || db.tbLossAgentScopeArea.Any(x => x.ScopeID == id)
                    || db.tbLossAgentScopeZoneType.Any(x => x.ScopeID == id);

                if (!hasAnything) scope.IsActive = false;

                scope.ModifiedDate = DateTime.Now;
                scope.ModifiedByUserID = currentUserId.Value;

                db.SaveChanges();

                return Json(new { success = true, message = "منطقه از تنظیمات مامور حذف شد." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در حذف: " + ex.Message });
            }
        }

        /* =========================================================
           TAB 2 GET / DETAILS / DELETE
           ========================================================= */

        [HttpGet]
        public JsonResult GetLossAgentScope(long id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." }, JsonRequestBehavior.AllowGet);

            var eligibleUsers = GetEligibleTab2UserIds(currentUserId.Value);
            var scope = db.tbLossAgentScope.FirstOrDefault(x => x.ID == id && x.IsActive == true);

            if (scope == null || !eligibleUsers.Contains(scope.UserID))
                return Json(new { success = false, message = "رکورد یافت نشد." }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                success = true,
                data = new { ID = scope.ID, UserID = scope.UserID, Rules = LoadScopeRules(id) }
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetLossAgentScopeDetails(long id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." }, JsonRequestBehavior.AllowGet);

            var eligibleUsers = GetEligibleTab2UserIds(currentUserId.Value);
            var scope = db.tbLossAgentScope.FirstOrDefault(x => x.ID == id && x.IsActive == true);

            if (scope == null || !eligibleUsers.Contains(scope.UserID))
                return Json(new { success = false, message = "رکورد یافت نشد." }, JsonRequestBehavior.AllowGet);

            var person = db.tbUsers.FirstOrDefault(x => x.usr_ID == scope.UserID);

            string fullName = person == null
                ? ""
                : ((person.usr_Name ?? "") + " " + (person.usr_Family ?? "")).Trim();

            return Json(new
            {
                success = true,
                data = new
                {
                    scope.ID,
                    scope.UserID,
                    FullName = fullName,
                    NationalCode = person == null ? "" : person.usr_NationalCode,
                    CityID = scope.CityID,
                    Rules = LoadScopeRules(id),
                    TariffTypes = db.tbOrderTariffType
                        .OrderBy(x => x.SortOrder)
                        .Select(x => new { x.ID, x.Title })
                        .ToList()
                }
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteLossAgentScope(long id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var eligibleUsers = GetEligibleTab2UserIds(currentUserId.Value);
            var scope = db.tbLossAgentScope.FirstOrDefault(x => x.ID == id && x.IsActive == true);

            if (scope == null || !eligibleUsers.Contains(scope.UserID))
                return Json(new { success = false, message = "رکورد یافت نشد." });

            scope.IsActive = false;
            scope.ModifiedDate = DateTime.Now;
            scope.ModifiedByUserID = currentUserId.Value;

            db.SaveChanges();

            return Json(new { success = true, message = "اطلاعات محدوده مامور حذف شد." });
        }

        /* =========================================================
           TAB 3 - استثناها : EXCEL COMPONENT IMPORT
           ListType     : 1 = بالابری ، 2 = آلودگی
           ComponentType: 1 = شناسه قبض ، 2 = پرونده ، 3 = روزکار ، 4 = محدوده
           ========================================================= */

        private static readonly string[] ObstructionHeaders =
            { "اشتراک", "پرونده", "شماره سریال کنتور", "علت ممانعت" };

        private string GetExcelComponentTitle(int componentType)
        {
            switch (componentType)
            {
                case 1: return "شناسه قبض";
                case 2: return "پرونده";
                case 3: return "روزکار";
                case 4: return "محدوده";
                case 5: return "شماره اشتراک";
                default: return null;
            }
        }

        private string NormalizeExcelText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            return value.Trim().Replace("ي", "ی").Replace("ك", "ک");
        }

        private string NormalizeExcelDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            return value
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        }

        private string GetExcelCellText(ICell cell, DataFormatter formatter, IFormulaEvaluator evaluator)
        {
            if (cell == null) return "";

            try
            {
                string result = cell.CellType == CellType.Formula
                    ? formatter.FormatCellValue(cell, evaluator)
                    : formatter.FormatCellValue(cell);

                return NormalizeExcelText(result);
            }
            catch
            {
                return cell.ToString().Trim();
            }
        }

        [HttpGet]
        public ActionResult DownloadLossComponentTemplate(int componentType, int listType = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Content("دسترسی غیر مجاز");

            if (listType < 1 || listType > 3 || (listType == 3 && componentType != 5))
                return Content("نوع قالب معتبر نیست.");
            var componentTitle = GetExcelComponentTitle(componentType);
            if (string.IsNullOrWhiteSpace(componentTitle)) return Content("مولفه انتخاب‌شده معتبر نیست.");

            NPOIWorkbook workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("Data");

            // ستون Text تا صفر ابتدای شناسه قبض / پرونده حفظ شود
            var textStyle = workbook.CreateCellStyle();
            textStyle.DataFormat = workbook.CreateDataFormat().GetFormat("@");
            sheet.SetDefaultColumnStyle(0, textStyle);

            var row = sheet.CreateRow(0);
            var headers = listType == 3 ? ObstructionHeaders : new[] { componentTitle };
            for (int col = 0; col < headers.Length; col++)
            {
                sheet.SetDefaultColumnStyle(col, textStyle);
                row.CreateCell(col).SetCellValue(headers[col]);
                sheet.SetColumnWidth(col, (col == 3 ? 60 : 30) * 256);
            }

            using (var memory = new MemoryStream())
            {
                workbook.Write(memory);

                return File(
                    memory.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    (listType == 3 ? "LossObstructionTemplate" : "LossComponentTemplate_" + componentType) + ".xlsx");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult UploadLossComponentExcel(int componentType, HttpPostedFileBase excelFile, int listType = 1)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId.Value);
            if (currentUser == null)
                return Json(new { success = false, message = "کاربر پیدا نشد." });

            if (listType == 3 && componentType != 5)
                return Json(new { success = false, message = "برای ممانعت فقط قالب چهارستونی مجاز است." });
            var componentTitle = listType == 3 ? "ممانعت" : GetExcelComponentTitle(componentType);
            if (string.IsNullOrWhiteSpace(componentTitle))
                return Json(new { success = false, message = "مولفه انتخاب‌شده معتبر نیست." });

            // مولفه‌های روزکار و محدوده هنوز در حال توسعه‌اند
            if (componentType == 3 || componentType == 4)
                return Json(new { success = false, message = "بارگذاری این مولفه هنوز فعال نشده است." });

            // نوع لیست: 1 = بالابری ، 2 = آلودگی
            if (listType != 1 && listType != 2 && listType != 3)
                return Json(new { success = false, message = "نوع لیست انتخاب‌شده معتبر نیست." });

            int? cityId = currentUser.usr_City_Dutysystem;
            if (!cityId.HasValue)
                return Json(new { success = false, message = "برای کاربر جاری شهرستان مشخص نشده است." });

            var allowedRegions = GetAllowedRegions(cityId);
            if (allowedRegions == null || allowedRegions.Count == 0)
                return Json(new { success = false, message = "هیچ منطقه مجازی برای شهرستان کاربر جاری تعریف نشده است." });

            if (excelFile == null || excelFile.ContentLength <= 0)
                return Json(new { success = false, message = "فایل Excel انتخاب نشده است." });

            if (excelFile.ContentLength > 10 * 1024 * 1024)
                return Json(new { success = false, message = "حجم فایل نباید بیشتر از 10 مگابایت باشد." });

            var extension = Path.GetExtension(excelFile.FileName).ToLowerInvariant();
            if (extension != ".xlsx" && extension != ".xls")
                return Json(new { success = false, message = "فقط فایل Excel با پسوند xlsx یا xls مجاز است." });

            try
            {
                NPOIWorkbook workbook = extension == ".xlsx"
                    ? (NPOIWorkbook)new XSSFWorkbook(excelFile.InputStream)
                    : new HSSFWorkbook(excelFile.InputStream);

                if (workbook.NumberOfSheets <= 0)
                    return Json(new { success = false, message = "فایل Excel هیچ Sheet معتبری ندارد." });

                var sheet = workbook.GetSheetAt(0);
                if (sheet == null)
                    return Json(new { success = false, message = "Sheet اول فایل پیدا نشد." });

                var formatter = new DataFormatter();
                var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();

                var headerRow = sheet.GetRow(0);
                if (headerRow == null)
                    return Json(new { success = false, message = "سطر عنوان فایل Excel پیدا نشد." });

                var expectedHeaders = listType == 3 ? ObstructionHeaders : new[] { componentTitle };
                for (int col = 0; col < expectedHeaders.Length; col++)
                {
                    var actual = GetExcelCellText(headerRow.GetCell(col), formatter, evaluator);
                    if (NormalizeExcelText(actual).Replace(" ", "") != NormalizeExcelText(expectedHeaders[col]).Replace(" ", ""))
                        return Json(new { success = false, message = "عنوان ستون " + (col + 1) + " باید «" + expectedHeaders[col] + "» باشد." });
                }
                if (listType == 3 && headerRow.Cells.Any(c => c.ColumnIndex >= 4 && !string.IsNullOrWhiteSpace(GetExcelCellText(c, formatter, evaluator))))
                    return Json(new { success = false, message = "فایل ممانعت باید فقط چهار ستون داشته باشد." });

                if (sheet.LastRowNum > 50000)
                    return Json(new { success = false, message = "حداکثر 50,000 ردیف قابل بارگذاری است." });

                var obstructionRows = new Dictionary<int, string[]>();
                var importedValues = new List<KeyValuePair<int, string>>();
                var uniqueValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var errors = new List<string>();

                for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    var excelRow = sheet.GetRow(rowIndex);
                    if (excelRow == null) continue;

                    if (listType == 3)
                    {
                        var cells = Enumerable.Range(0, 4).Select(col =>
                            GetExcelCellText(excelRow.GetCell(col), formatter, evaluator).Trim()).ToArray();
                        bool extra = excelRow.Cells.Any(c => c.ColumnIndex >= 4 && !string.IsNullOrWhiteSpace(GetExcelCellText(c, formatter, evaluator)));
                        if (cells.All(string.IsNullOrWhiteSpace) && !extra) continue;
                        if (extra || cells.Any(string.IsNullOrWhiteSpace) || cells.Take(3).Any(x => x.Length > 500) || cells[3].Length > 2000)
                        {
                            errors.Add("ردیف " + (rowIndex + 1) + ": هر چهار ستون الزامی است؛ حداکثر طول شناسه‌ها ۵۰۰ و علت ۲۰۰۰ کاراکتر است و ستون اضافه مجاز نیست.");
                            if (errors.Count >= 20) break;
                            continue;
                        }
                        for (int col = 0; col < 3; col++) cells[col] = NormalizeExcelDigits(cells[col]);
                        var key = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cells);
                        if (!uniqueValues.Add(key)) continue;
                        obstructionRows.Add(rowIndex + 1, cells);
                        importedValues.Add(new KeyValuePair<int, string>(rowIndex + 1, cells[0]));
                        continue;
                    }
                    var value = GetExcelCellText(excelRow.GetCell(0), formatter, evaluator);
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    value = NormalizeExcelDigits(value.Trim());

                    if (value.Length > 500)
                    {
                        errors.Add("ردیف " + (rowIndex + 1) + ": طول مقدار بیشتر از حد مجاز است.");
                        if (errors.Count >= 20) break;
                        continue;
                    }

                    // تکراری داخل همان Excel نادیده گرفته می‌شود
                    if (!uniqueValues.Add(value)) continue;

                    importedValues.Add(new KeyValuePair<int, string>(rowIndex + 1, value));
                }

                if (errors.Count > 0)
                    return Json(new { success = false, message = "فایل دارای اطلاعات نامعتبر است.", errors = errors });

                if (importedValues.Count == 0)
                    return Json(new { success = false, message = "هیچ مقدار قابل ذخیره‌ای در فایل Excel وجود ندارد." });

                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        // فایل فعال قبلیِ همین (کاربر + شهر + مولفه + نوع لیست) غیرفعال می‌شود
                        var previousBatches = db.tbLossExcelImportBatch
                            .Where(x =>
                                x.UserID == currentUserId.Value
                                && x.CityID == cityId.Value
                                && (listType == 3 || x.ComponentType == componentType)
                                && x.ListType == listType
                                && x.IsActive == true)
                            .ToList();

                        foreach (var previous in previousBatches) previous.IsActive = false;

                        db.SaveChanges();

                        var batch = new tbLossExcelImportBatch
                        {
                            ComponentType = componentType,
                            ListType = listType,
                            ComponentTitle = componentTitle,
                            UserID = currentUserId.Value,
                            CityID = cityId.Value,
                            OriginalFileName = Path.GetFileName(excelFile.FileName),
                            RowCount = importedValues.Count,
                            IsActive = true,
                            CreateDate = DateTime.Now
                        };

                        db.tbLossExcelImportBatch.Add(batch);
                        db.SaveChanges();

                        foreach (var item in importedValues)
                        {
                            if (listType == 3)
                            {
                                var cells = obstructionRows[item.Key];
                                db.Database.ExecuteSqlCommand(
                                    "INSERT INTO dbo.tbLossObstructionImportRow (BatchID, ExcelRowNumber, SubscriptionNumber, FileNumber, MeterSerialNumber, ObstructionReason) VALUES (@p0,@p1,@p2,@p3,@p4,@p5)",
                                    batch.ID, item.Key, cells[0], cells[1], cells[2], cells[3]);
                                continue;
                            }
                            db.tbLossExcelImportValue.Add(new tbLossExcelImportValue
                            {
                                BatchID = batch.ID,
                                ValueText = item.Value,
                                ExcelRowNumber = item.Key
                            });
                        }

                        foreach (var regionCode in allowedRegions.Distinct())
                        {
                            db.tbLossExcelImportRegion.Add(new tbLossExcelImportRegion
                            {
                                BatchID = batch.ID,
                                RegionCode = regionCode
                            });
                        }

                        db.SaveChanges();
                        transaction.Commit();

                        return Json(new
                        {
                            success = true,
                            batchId = batch.ID,
                            componentTitle = componentTitle,
                            listType = listType,
                            rowCount = importedValues.Count,
                            message = importedValues.Count + " مقدار از مولفه «" + componentTitle
                                      + "» (" + (listType == 1 ? "بالابری" : listType == 2 ? "آلودگی" : "ممانعت") + ") با موفقیت ذخیره شد."
                        });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();

                        var root = ex;
                        while (root.InnerException != null) root = root.InnerException;

                        return Json(new { success = false, message = "خطا در ذخیره فایل Excel: " + root.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "فایل Excel قابل خواندن نیست: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DisableLossComponentImport(long id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId.Value);

            if (currentUser == null || !currentUser.usr_City_Dutysystem.HasValue)
                return Json(new { success = false, message = "اطلاعات کاربر یا شهرستان معتبر نیست." });

            // فقط Batch خود کاربر و شهر خودش
            var batch = db.tbLossExcelImportBatch.FirstOrDefault(x =>
                x.ID == id
                && x.UserID == currentUserId.Value
                && x.CityID == currentUser.usr_City_Dutysystem);

            if (batch == null)
                return Json(new { success = false, message = "رکورد موردنظر یافت نشد." });

            batch.IsActive = false;
            db.SaveChanges();

            return Json(new { success = true, message = "فایل بارگذاری‌شده غیرفعال شد." });
        }

        #endregion
    }
}
