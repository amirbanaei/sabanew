using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SaabWebProject.Models.DomainModels;
using NPOIWorkbook = NPOI.SS.UserModel.IWorkbook;

namespace SaabWebProject.Areas
{
    public class setting_orderController : Controller
    {
        #region مدیریت تخصیص نیرو + تخصیص هوشمند مأموران وصول

        /* =====================================================================
           فهرست این Region
           ---------------------------------------------------------------------
             بخش ۰ : DbContext و متدهای پایه
             بخش ۱ : متدهای کمکی مشترک
             بخش ۲ : صفحه (Index)
             بخش ۳ : تب ۱ - تعیین نقش وصول (با قانون بالابری)
             بخش ۴ : تب ۲ - تنظیمات ارجاع (منطقه / گروه / روزکار / تعرفه / محدوده)
             بخش ۵ : تب ۳ - لیست بالابری (Excel)
             بخش ۶ : تب ۴ - تخصیص هوشمند (کنترلر) + تکمیل تعرفه‌ی خالی
             بخش ۷ : موتور الگوریتم
             بخش ۸ : تب ۵ - گزارش نیروهای ثبت‌شده / ثبت‌نشده
           ===================================================================== */


        /* =====================================================================
           بخش ۰ : DbContext و متدهای پایه
           ===================================================================== */

        SaabEntities db = new SaabEntities();

        private const string JONOBI_CONTRACT_TITLE = "کارگزاری خدمات توزیع نیروی برق بیرجند";
        private bool? _isJonobi;

        private bool IsJonobiContract()
        {
            if (!_isJonobi.HasValue)
            {
                /*
                   در سرور جنوبی، دامنه مطمئن‌ترین نشانه است. تطابق صرفاً مساویِ
                   عنوان قرارداد کافی نیست؛ اختلاف ی/ي، ک/ك، فاصله و نیم‌فاصله
                   باعث می‌شد صفحه جنوبی به‌اشتباه اطلاعات سیستان را نمایش دهد.
                */
                bool detectedByHost = false;

                try
                {
                    string host = Request != null && Request.Url != null
                        ? (Request.Url.Host ?? "")
                        : "";

                    string forwardedHost = Request != null
                        ? (Request.Headers["X-Forwarded-Host"] ?? "")
                        : "";

                    detectedByHost =
                        host.IndexOf("jonobi", StringComparison.OrdinalIgnoreCase) >= 0
                        || forwardedHost.IndexOf("jonobi", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch
                {
                    detectedByHost = false;
                }

                bool detectedByContract = false;

                if (!detectedByHost)
                {
                    string expectedTitle = NormalizeOrderExcelText(JONOBI_CONTRACT_TITLE);

                    var contractTitles = db.tbPeymanContracts
                        .Select(x => x.pec_Title)
                        .ToList();

                    detectedByContract = contractTitles.Any(title =>
                    {
                        string normalized = NormalizeOrderExcelText(title);

                        return normalized.Equals(expectedTitle, StringComparison.OrdinalIgnoreCase)
                               || normalized.IndexOf(
                                      "توزیع نیروی برق بیرجند",
                                      StringComparison.OrdinalIgnoreCase) >= 0
                               || normalized.IndexOf(
                                      "خراسان جنوبی",
                                      StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                }

                _isJonobi = detectedByHost || detectedByContract;
            }

            return _isJonobi.Value;
        }

        private string GetSpecialCollectionTitle()
        {
            return IsJonobiContract() ? "وصول مذاکره‌ای" : "تلفنی";
        }
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && db != null)
                {
                    db.Dispose();
                }
            }

            finally
            {
                base.Dispose(disposing);
            }
        }
        //protected override void Dispose(bool disposing)
        //{
        //    if (disposing)
        //    {
        //        db.Dispose();
        //    }

        //    base.Dispose(disposing);
        //}

        private long GetCityValue(string city)
        {
            city = NormalizeOrderExcelText(city);

            // در جنوبی روش قدیمی نداریم؛ مقدار مثبت فقط برای کنترل‌های قدیمی
            // و مقدار اولیه LoadAllocationAgents از هدف روزانه جدید تأمین می‌شود.
            if (IsJonobiContract())
            {
                decimal target = GetCityDailyTargetRial(city);
                return target > 0 && target <= long.MaxValue ? (long)target : 0L;
            }

            long valueToman;

            switch (city)
            {
                case "ایرانشهر":
                case "سراوان":
                case "چابهار":
                    valueToman = 700000000L;
                    break;

                case "سوران":
                case "زابل":
                    valueToman = 600000000L;
                    break;

                case "جالق":
                    valueToman = 900000000L;
                    break;

                case "زاهدان 2":
                case "زاهدان 1":
                    valueToman = 550000000L;
                    break;

                case "هیرمند":
                case "قصرقند":
                    valueToman = 400000000L;
                    break;

                case "خاش":
                    valueToman = 700000000L;
                    break;

                case "مهرستان":
                case "نیکشهر":
                case "زهک":
                case "نگور":
                    valueToman = 300000000L;
                    break;

                case "دلگان":
                case "راسک":
                case "سرباز":
                case "بمپور":
                    valueToman = 250000000L;
                    break;

                case "کورین":
                case "میرجاوه":
                case "اسپکه":
                    valueToman = 200000000L;
                    break;

                case "فنوج":
                    valueToman = 180000000L;
                    break;

                case "کنارک":
                    valueToman = 150000000L;
                    break;

                default:
                    valueToman = 0L;
                    break;
            }

            // تبدیل تومان به ریال
            return checked(valueToman * 1L);
        }
        /// <summary>ارزش پایه شهر (ریال) — مبنای محاسبه ظرفیت هر مأمور در تب ۴</summary>
        //private long GetCityValue(string city)
        //{
        //    city = NormalizeOrderExcelText(city);

        //    switch (city)
        //    {
        //        case "ایرانشهر": case "سراوان": case "چابهار": return 700000000L;
        //        case "سوران": case "زابل": return 600000000L;
        //        case "جالق": return 900000000L;
        //        case "زاهدان 2": case "زاهدان 1": return 550000000L;
        //        case "هیرمند": case "قصرقند": return 400000000L;
        //        case "خاش": return 700000000L;
        //        case "مهرستان": case "نیکشهر": case "زهک": case "نگور": return 300000000L;
        //        case "دلگان": case "راسک": case "سرباز": case "بمپور": return 250000000L;
        //        case "کورین": case "میرجاوه": case "اسپکه": return 200000000L;
        //        case "فنوج": return 180000000L;
        //        case "کنارک": return 150000000L;
        //        default: return 0;
        //    }
        //}


        private const string ROLE_ASSIGNMENT_TITLE = "تعیین نقش";

        // جنوبی/بیرجند: منطقه 0 تا 60؛ سیستان: محدوده قبلی 0 تا 45
        private const int AREA_MIN = 0;

        private int GetAreaMax()
        {
            return IsJonobiContract() ? 60 : 45;
        }

        private int GetAreaTotal()
        {
            return GetAreaMax() - AREA_MIN + 1;
        }


        /* =====================================================================
           بخش ۱ : متدهای کمکی مشترک
           ===================================================================== */

        private int? GetCurrentUserId()
        {
            try
            {
                var cookie_user = Request.Cookies[Utility.Base64.Base64Encode("CodeMeli")];

                if (cookie_user == null)
                    return null;

                var nationalcode = Utility.Base64.Base64Decode(cookie_user.Value);

                var user = db.tbUsers.FirstOrDefault(x => x.usr_NationalCode == nationalcode);

                if (user == null)
                    return null;

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
                .Where(x =>
                    x.Name == currentUserId
                    && x.Value == 1
                    && x.nam.HasValue)
                .Select(x => x.nam.Value)
                .Distinct()
                .ToList();
        }


        private int? GetActiveRoleAssignmentId()
        {
            return db.tbOrderAssignment
                .Where(x =>
                    x.IsActive == true
                    && x.Title != null
                    && x.Title.Trim() == ROLE_ASSIGNMENT_TITLE)
                .OrderByDescending(x => x.ID)
                .Select(x => (int?)x.ID)
                .FirstOrDefault();
        }


        /// <summary>کاربران تب ۲: فقط کسانی که در تب ۱ حداقل یک نقش دارند</summary>
        private List<int> GetEligibleTab2UserIds(int currentUserId)
        {
            var allowedUserIds = GetAllowedUserIds(currentUserId);

            var activeAssignmentId = GetActiveRoleAssignmentId();

            if (!activeAssignmentId.HasValue)
                return new List<int>();

            var assignmentWithActivityUserIds = db.tbOrderAssignmentActivity
                .Where(x => x.AssignmentID == activeAssignmentId.Value)
                .Select(x => x.UserID)
                .Distinct()
                .ToList();

            return assignmentWithActivityUserIds
                .Where(x => allowedUserIds.Contains(x))
                .Distinct()
                .ToList();
        }


        private void AddRegionValue(HashSet<int> result, int? value)
        {
            if (value.HasValue && value.Value > 0)
                result.Add(value.Value);
        }

        private void AddLinkRegions(HashSet<int> result, params int?[] values)
        {
            foreach (var v in values)
                AddRegionValue(result, v);
        }

        /// <summary>مناطق مجاز شهر (برای ثبت Excel تب ۳)</summary>
        private List<int> GetAllowedRegions(int? cityId)
        {
            var result = new HashSet<int>();

            if (!cityId.HasValue)
                return result.ToList();

            var links = db.tblink_mantegeh_city
                .Where(x =>
                    x.FK_city == cityId.Value
                    && (x.vaz == 1 || x.vaz == null))
                .ToList();

            foreach (var item in links)
                AddLinkRegions(result, item.mantaghe, item.mantaghe1, item.mantaghe2, item.mantaghe3, item.mantaghe4, item.mantaghe5, item.mantaghe6, item.mantaghe7, item.mantaghe8, item.mantaghe9, item.mantaghe10);

            return result.OrderBy(x => x).ToList();
        }


        public class RegionItem
        {
            public int Code { get; set; }
            public string Name { get; set; }
            public string County { get; set; }
        }

        /// <summary>
        /// تب ۲: «همه مناطق» (از همه شهرستان‌ها) — جدول شهرستان/منطقه + مناطق جدول tblink_mantegeh_city
        /// </summary>
        private List<RegionItem> GetAllRegionItems()
        {
            var map = new Dictionary<int, RegionItem>();

            foreach (var x in GetActiveAllocationCountyRegions())
            {
                int code = int.Parse(x[2], CultureInfo.InvariantCulture);

                if (!map.ContainsKey(code))
                {
                    map[code] = new RegionItem
                    {
                        Code = code,
                        Name = NormalizeOrderExcelText(x[1]),
                        County = NormalizeOrderExcelText(x[0])
                    };
                }
            }

            /*
               جنوبی فقط باید بخش‌های رسمی تعریف‌شده در
               JONOBI_ALLOCATION_COUNTY_REGIONS را ببیند. جدول قدیمی
               tblink_mantegeh_city مربوط به منطق سیستان است و نباید کدهای
               اضافی آن وارد صفحه جنوبی شود.
            */
            if (IsJonobiContract())
            {
                return map.Values
                    .OrderBy(x => x.Code)
                    .ToList();
            }

            var extra = new HashSet<int>();

            foreach (var item in db.tblink_mantegeh_city.Where(x => x.vaz == 1 || x.vaz == null).ToList())
                AddLinkRegions(extra, item.mantaghe, item.mantaghe1, item.mantaghe2, item.mantaghe3, item.mantaghe4, item.mantaghe5, item.mantaghe6, item.mantaghe7, item.mantaghe8, item.mantaghe9, item.mantaghe10);

            foreach (var code in extra)
            {
                if (!map.ContainsKey(code))
                    map[code] = new RegionItem { Code = code, Name = "", County = "" };
            }

            return map.Values.OrderBy(x => x.Code).ToList();
        }


        private string NormalizeOrderExcelText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value
                .Trim()
                .Replace("ي", "ی")
                .Replace("ك", "ک")
                .Replace("‌", " ");

            return string.Join(
                " ",
                value.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));
        }


        /// <summary>
        /// معادل‌های قابل جستجوی نام شهر برای SQL؛ اختلاف «ی/ي» و «ک/ك»
        /// نباید باعث خالی شدن پرونده‌های شهر شود.
        /// </summary>
        private List<string> GetAllocationCityQueryAliases(string city)
        {
            var normalized = NormalizeOrderExcelText(city);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string> add = value =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                    aliases.Add(value.Trim());
            };

            add(city);
            add(normalized);
            add(normalized.Replace('ی', 'ي'));
            add(normalized.Replace('ک', 'ك'));
            add(normalized.Replace('ی', 'ي').Replace('ک', 'ك'));

            return aliases.ToList();
        }


        private string NormalizeOrderExcelDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        }

        private string GetJonobiUserAllocationCity(int userId, out string error)
        {
            error = null;
            var cityId = db.tbUsers.Where(x => x.usr_ID == userId)
                .Select(x => x.usr_City_Dutysystem).FirstOrDefault();
            if (!cityId.HasValue)
            {
                error = "برای حساب کاربری شما شهرستان محل خدمت ثبت نشده است.";
                return null;
            }

            var cityName = db.tbCities.Where(x => x.ID == cityId.Value)
                .Select(x => x.Name).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(cityName))
            {
                error = "شهرستان محل خدمت حساب کاربری شما در اطلاعات شهرها پیدا نشد.";
                return null;
            }

            var canonical = GetAllocationCounties().FirstOrDefault(x =>
                NormalizeOrderExcelText(x) == NormalizeOrderExcelText(cityName));
            if (canonical == null)
            {
                error = "شهرستان محل خدمت «" + cityName + "» در فهرست شهرستان‌های تخصیص بیرجند تعریف نشده است.";
                return null;
            }
            return canonical;
        }

        private bool EnforceJonobiUserAllocationCity(int userId, ref string requestedCity, out string error)
        {
            error = null;
            if (!IsJonobiContract()) return true;

            var ownCity = GetJonobiUserAllocationCity(userId, out error);
            if (ownCity == null) return false;
            if (NormalizeOrderExcelText(requestedCity) != NormalizeOrderExcelText(ownCity))
            {
                error = "شما فقط مجاز به دریافت لیست شهرستان محل خدمت خود («" + ownCity + "») هستید.";
                return false;
            }

            requestedCity = ownCity;
            return true;
        }


        /* =====================================================================
           بخش ۲ : صفحه
           ===================================================================== */

        private string PrepareOrderAssignmentPageData(int currentUserId)
        {
            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId);

            if (currentUser == null)
                return "کاربر پیدا نشد";

            var allowedUserIds = GetAllowedUserIds(currentUserId);

            var users = db.tbUsers
                .Where(x =>
                    x.usr_IsActive == true
                    && allowedUserIds.Contains(x.usr_ID))
                .OrderBy(x => x.usr_Family)
                .ThenBy(x => x.usr_Name)
                .ToList();

            /* ---------- تب ۱ ---------- */

            var activityTypes = db.tbOrderActivityType
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ToList();

            var assignments = db.tbOrderAssignment
                .Where(x =>
                    x.IsActive == true
                    && x.Title != null
                    && x.Title.Trim() == ROLE_ASSIGNMENT_TITLE)
                .OrderByDescending(x => x.ID)
                .Take(1)
                .ToList();

            var assignmentIds = assignments.Select(x => x.ID).ToList();

            var assignmentUsers = db.tbOrderAssignmentUser
                .Where(x => assignmentIds.Contains(x.AssignmentID))
                .ToList();

            var assignmentActivities = db.tbOrderAssignmentActivity
                .Where(x => assignmentIds.Contains(x.AssignmentID))
                .ToList();

            /* ---------- تب ۲ ---------- */

            var tab2UserIds = GetEligibleTab2UserIds(currentUserId);

            var tab2Users = users
                .Where(x => tab2UserIds.Contains(x.usr_ID))
                .OrderBy(x => x.usr_Family)
                .ThenBy(x => x.usr_Name)
                .ToList();

            int? currentCityId = currentUser.usr_City_Dutysystem;

            // تب ۲: همه مناطق (مستقل از شهر کاربر)
            var allRegions = GetAllRegionItems();

            var agentScopes = db.tbOrderAgentScope
                .Where(x =>
                    x.IsActive == true
                    && tab2UserIds.Contains(x.UserID))
                .OrderByDescending(x => x.ID)
                .ToList();

            var scopeIds = agentScopes.Select(x => x.ID).ToList();

            var agentScopeRegions = db.tbOrderAgentScopeRegion
                .Where(x => scopeIds.Contains(x.ScopeID))
                .ToList();

            var agentScopeWorkDays = db.tbOrderAgentScopeWorkDay
                .Where(x => scopeIds.Contains(x.ScopeID))
                .ToList();

            var tariffTypes = db.tbOrderTariffType
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.ID)
                .ToList();

            var agentScopeTariffs = db.tbOrderAgentScopeTariff
                .Where(x => scopeIds.Contains(x.ScopeID))
                .ToList();

            var agentScopeAreas = db.tbOrderAgentScopeArea
                .Where(x => scopeIds.Contains(x.ScopeID))
                .ToList();

            /* ---------- تب ۳ ---------- */

            var excelImportBatches = new List<tbOrderExcelImportBatch>();

            if (currentCityId.HasValue)
            {
                excelImportBatches = db.tbOrderExcelImportBatch
                    .Where(x =>
                        x.UserID == currentUserId
                        && x.CityID == currentCityId.Value
                        && x.IsActive == true)
                    .OrderByDescending(x => x.ID)
                    .ToList();
            }

            /* ---------- ViewBag ---------- */

            ViewBag.Users = users;
            ViewBag.ActivityTypes = activityTypes;
            ViewBag.Assignments = assignments;
            ViewBag.AssignmentUsers = assignmentUsers;
            ViewBag.AssignmentActivities = assignmentActivities;

            ViewBag.Tab2Users = tab2Users;
            ViewBag.AllowedRegions = allRegions;
            ViewBag.AgentScopes = agentScopes;
            ViewBag.AgentScopeRegions = agentScopeRegions;
            ViewBag.AgentScopeWorkDays = agentScopeWorkDays;
            ViewBag.TariffTypes = tariffTypes;
            ViewBag.AgentScopeTariffs = agentScopeTariffs;
            ViewBag.AgentScopeAreas = agentScopeAreas;
            ViewBag.CurrentCityID = currentCityId;

            ViewBag.ExcelImportBatches = excelImportBatches;

            bool isJonobi = IsJonobiContract();
            var allocationCities = GetAllocationCounties();
            ViewBag.LockedAllocationCity = null;
            if (isJonobi)
            {
                string cityError;
                var ownCity = GetJonobiUserAllocationCity(currentUserId, out cityError);
                if (ownCity == null) return cityError;
                allocationCities = new List<string> { ownCity };
                ViewBag.LockedAllocationCity = ownCity;
            }
            ViewBag.AllocationCities = allocationCities;
            ViewBag.IsJonobi = isJonobi;
            ViewBag.SpecialCollectionTitle = GetSpecialCollectionTitle();

            // تب ۵: ثبت‌شده‌ها و ثبت‌نشده‌ها (با تنظیمات ارجاع شامل محدوده)
            ViewBag.ReportUsers = GetReportUsers();

            ViewBag.UserCount = users.Count;
            ViewBag.ActivityTypeCount = activityTypes.Count;
            ViewBag.AssignmentCount = assignments.Count;

            return null;
        }


        [HttpGet]
        public ActionResult Index(bool embedded = false)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Content("دسترسی غیر مجاز");

            var error = PrepareOrderAssignmentPageData(currentUserId.Value);

            if (!string.IsNullOrWhiteSpace(error))
                return Content(error);

            ViewBag.IsEmbedded = embedded;
            return View();
        }


        /* =====================================================================
           بخش ۳ : تب ۱ - تعیین نقش وصول
           ===================================================================== */

        private List<KeyValuePair<int, int>> ParseUserActivityPairs(
            List<int> userIds,
            List<string> userActivityPairs,
            List<int> validActivityIds)
        {
            var result = new List<KeyValuePair<int, int>>();

            if (userIds == null || userActivityPairs == null)
                return result;

            foreach (var pairText in userActivityPairs.Distinct())
            {
                if (string.IsNullOrWhiteSpace(pairText))
                    continue;

                var parts = pairText.Split('_');

                if (parts.Length != 2)
                    continue;

                int userId;
                int activityId;

                if (!int.TryParse(parts[0], out userId))
                    continue;

                if (!int.TryParse(parts[1], out activityId))
                    continue;

                if (!userIds.Contains(userId))
                    continue;

                if (!validActivityIds.Contains(activityId))
                    continue;

                result.Add(new KeyValuePair<int, int>(userId, activityId));
            }

            return result
                .GroupBy(x => new { UserID = x.Key, ActivityID = x.Value })
                .Select(x => x.First())
                .ToList();
        }


        /// <summary>
        /// ذخیره تب ۱ — فقط UserIds ارسال‌شده تغییر می‌کنند.
        /// قوانین: «تلفنی» انحصاری است ؛ «بالابری» بدون «عادی» یا «دیماند» مجاز نیست.
        /// </summary>
        private ActionResult SaveOrderAssignmentPartial(
            int? assignmentId,
            List<int> UserIds,
            List<string> UserActivityPairs)
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

            var validActivities = db.tbOrderActivityType
                .Where(x => x.IsActive)
                .Select(x => new { x.ID, x.Title })
                .ToList();

            var validActivityIds = validActivities.Select(x => x.ID).ToList();

            var telephoneActivityIds = new HashSet<int>(
                validActivities
                    .Where(x => ResolveAllocationRole(x.Title) == AllocationRoleKind.Telephone)
                    .Select(x => x.ID));

            var parsedPairs = ParseUserActivityPairs(
                UserIds,
                UserActivityPairs ?? new List<string>(),
                validActivityIds);

            // نقش تلفنی برای هر کاربر انحصاری است
            if (telephoneActivityIds.Count > 0)
            {
                var normalizedPairs = new List<KeyValuePair<int, int>>();

                foreach (var userGroup in parsedPairs.GroupBy(x => x.Key))
                {
                    var telephonePairs = userGroup
                        .Where(x => telephoneActivityIds.Contains(x.Value))
                        .ToList();

                    if (telephonePairs.Count > 0)
                        normalizedPairs.AddRange(telephonePairs);
                    else
                        normalizedPairs.AddRange(userGroup);
                }

                parsedPairs = normalizedPairs;
            }

            // قانون بالابری: همراه با «عادی» یا «دیماند»
            var liftIds = new HashSet<int>(
                validActivities
                    .Where(x => ResolveAllocationRole(x.Title) == AllocationRoleKind.Balabari)
                    .Select(x => x.ID));

            var companionIds = new HashSet<int>(
                validActivities
                    .Where(x =>
                    {
                        var kind = ResolveAllocationRole(x.Title);

                        return kind == AllocationRoleKind.Normal
                            || kind == AllocationRoleKind.Demand
                            || kind == AllocationRoleKind.NormalAndDemand;
                    })
                    .Select(x => x.ID));

            if (liftIds.Count > 0 && companionIds.Count > 0)
            {
                var badUserIds = parsedPairs
                    .GroupBy(x => x.Key)
                    .Where(g =>
                        g.Any(p => liftIds.Contains(p.Value))
                        && !g.Any(p => companionIds.Contains(p.Value)))
                    .Select(g => g.Key)
                    .ToList();

                if (badUserIds.Count > 0)
                {
                    var names = db.tbUsers
                        .Where(x => badUserIds.Contains(x.usr_ID))
                        .ToList()
                        .Take(3)
                        .Select(x => ((x.usr_Name ?? "") + " " + (x.usr_Family ?? "")).Trim());

                    return Json(new
                    {
                        success = false,
                        message = "در صورت انتخاب «بالابری»، انتخاب «عادی» یا «دیماند» نیز الزامی است. نیروهای دارای مشکل: "
                                  + string.Join("، ", names)
                                  + (badUserIds.Count > 3 ? " و " + (badUserIds.Count - 3) + " نفر دیگر" : "")
                    });
                }
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var now = DateTime.Now;

                    var assignment = db.tbOrderAssignment
                        .Where(x => x.Title != null && x.Title.Trim() == ROLE_ASSIGNMENT_TITLE)
                        .OrderByDescending(x => x.IsActive)
                        .ThenByDescending(x => x.ID)
                        .FirstOrDefault();

                    var created = assignment == null;

                    if (created)
                    {
                        assignment = new tbOrderAssignment
                        {
                            Title = ROLE_ASSIGNMENT_TITLE,
                            Description = "",
                            IsActive = true,
                            CreateDate = now,
                            CreatedByUserID = currentUserId.Value
                        };

                        db.tbOrderAssignment.Add(assignment);
                        db.SaveChanges();
                    }
                    else
                    {
                        assignment.Title = ROLE_ASSIGNMENT_TITLE;
                        assignment.Description = "";
                        assignment.IsActive = true;
                    }

                    var existingUserIds = new HashSet<int>(
                        db.tbOrderAssignmentUser
                            .Where(x => x.AssignmentID == assignment.ID && UserIds.Contains(x.UserID))
                            .Select(x => x.UserID)
                            .Distinct()
                            .ToList());

                    foreach (var userId in UserIds)
                    {
                        if (!existingUserIds.Contains(userId))
                        {
                            db.tbOrderAssignmentUser.Add(new tbOrderAssignmentUser
                            {
                                AssignmentID = assignment.ID,
                                UserID = userId
                            });
                        }
                    }

                    var desiredKeys = new HashSet<string>(
                        parsedPairs.Select(x => x.Key.ToString() + "_" + x.Value.ToString()));

                    var oldActivities = db.tbOrderAssignmentActivity
                        .Where(x => x.AssignmentID == assignment.ID && UserIds.Contains(x.UserID))
                        .ToList();

                    var oldGroups = oldActivities
                        .GroupBy(x => x.UserID.ToString() + "_" + x.ActivityTypeID.ToString())
                        .ToList();

                    var oldKeys = new HashSet<string>();

                    foreach (var group in oldGroups)
                    {
                        oldKeys.Add(group.Key);

                        if (!desiredKeys.Contains(group.Key))
                        {
                            db.tbOrderAssignmentActivity.RemoveRange(group);
                        }
                        else
                        {
                            var duplicateRows = group.Skip(1).ToList();

                            if (duplicateRows.Count > 0)
                                db.tbOrderAssignmentActivity.RemoveRange(duplicateRows);
                        }
                    }

                    foreach (var pair in parsedPairs)
                    {
                        var key = pair.Key.ToString() + "_" + pair.Value.ToString();

                        if (!oldKeys.Contains(key))
                        {
                            db.tbOrderAssignmentActivity.Add(new tbOrderAssignmentActivity
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

                    return Json(new
                    {
                        success = false,
                        message = "خطا در ثبت یا بروزرسانی تعیین نقش: " + ex.Message
                    });
                }
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateOrderAssignment(
            string Title,
            string Description,
            List<int> UserIds,
            List<string> UserActivityPairs)
        {
            return SaveOrderAssignmentPartial(null, UserIds, UserActivityPairs);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditOrderAssignment(
            int ID,
            string Title,
            string Description,
            List<int> UserIds,
            List<string> UserActivityPairs)
        {
            return SaveOrderAssignmentPartial(ID, UserIds, UserActivityPairs);
        }


        [HttpGet]
        public ActionResult GetOrderAssignment(int id)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." }, JsonRequestBehavior.AllowGet);

            var assignment = db.tbOrderAssignment
                .FirstOrDefault(x => x.ID == id && x.IsActive == true);

            if (assignment == null)
                return Json(new { success = false, message = "تخصیص پیدا نشد." }, JsonRequestBehavior.AllowGet);

            var users = db.tbOrderAssignmentUser
                .Where(x => x.AssignmentID == id)
                .Select(x => x.UserID)
                .Distinct()
                .ToList();

            var activityPairs = db.tbOrderAssignmentActivity
                .Where(x => x.AssignmentID == id)
                .Select(x => new { x.UserID, x.ActivityTypeID })
                .ToList()
                .Select(x => x.UserID.ToString() + "_" + x.ActivityTypeID.ToString())
                .ToList();

            return Json(new
            {
                success = true,
                data = new
                {
                    assignment.ID,
                    Title = ROLE_ASSIGNMENT_TITLE,
                    Description = "",
                    Users = users,
                    ActivityPairs = activityPairs
                }
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdatePersonActivities(
            int assignmentId,
            int userId,
            List<int> ActivityIds)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var allowedUserIds = GetAllowedUserIds(currentUserId.Value);

            if (!allowedUserIds.Contains(userId))
                return Json(new { success = false, message = "دسترسی به این نیرو مجاز نیست." });

            ActivityIds = (ActivityIds ?? new List<int>()).Distinct().ToList();

            if (ActivityIds.Count == 0)
                return Json(new { success = false, message = "حداقل یک فعالیت برای این نیرو انتخاب کنید." });

            var validActivityIds = db.tbOrderActivityType
                .Where(x => x.IsActive)
                .Select(x => x.ID)
                .ToList();

            if (ActivityIds.Any(x => !validActivityIds.Contains(x)))
                return Json(new { success = false, message = "یک یا چند فعالیت انتخاب‌شده معتبر نیست." });

            var assignment = db.tbOrderAssignment
                .FirstOrDefault(x => x.ID == assignmentId && x.IsActive == true);

            if (assignment == null)
                return Json(new { success = false, message = "تعیین نقش پیدا نشد." });

            bool personExists = db.tbOrderAssignmentUser
                .Any(x => x.AssignmentID == assignmentId && x.UserID == userId);

            if (!personExists)
                return Json(new { success = false, message = "این نیرو عضو تعیین نقش موردنظر نیست." });

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var oldActivities = db.tbOrderAssignmentActivity
                        .Where(x => x.AssignmentID == assignmentId && x.UserID == userId)
                        .ToList();

                    if (oldActivities.Count > 0)
                    {
                        db.tbOrderAssignmentActivity.RemoveRange(oldActivities);
                        db.SaveChanges();
                    }

                    foreach (var activityId in ActivityIds)
                    {
                        db.tbOrderAssignmentActivity.Add(new tbOrderAssignmentActivity
                        {
                            AssignmentID = assignmentId,
                            UserID = userId,
                            ActivityTypeID = activityId
                        });
                    }

                    assignment.ModifiedDate = DateTime.Now;
                    assignment.ModifiedByUserID = currentUserId.Value;

                    db.SaveChanges();

                    transaction.Commit();

                    return Json(new
                    {
                        success = true,
                        message = "نقش‌های این نیرو با موفقیت بروزرسانی شد و سایر نیروها تغییری نکردند."
                    });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    return Json(new { success = false, message = "خطا در ذخیره فعالیت‌های نیرو: " + ex.Message });
                }
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RemovePersonFromAssignment(int assignmentId, int userId)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var allowedUserIds = GetAllowedUserIds(currentUserId.Value);

            if (!allowedUserIds.Contains(userId))
                return Json(new { success = false, message = "دسترسی به این نیرو مجاز نیست." });

            var assignment = db.tbOrderAssignment
                .FirstOrDefault(x => x.ID == assignmentId && x.IsActive == true);

            if (assignment == null)
                return Json(new { success = false, message = "تعیین نقش پیدا نشد." });

            var person = db.tbOrderAssignmentUser
                .FirstOrDefault(x => x.AssignmentID == assignmentId && x.UserID == userId);

            if (person == null)
                return Json(new { success = false, message = "نیرو پیدا نشد." });

            var assignmentUserCount = db.tbOrderAssignmentUser
                .Count(x => x.AssignmentID == assignmentId);

            if (assignmentUserCount <= 1)
                return Json(new { success = false, message = "آخرین نیروی تعیین نقش را نمی‌توان حذف کرد." });

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var activities = db.tbOrderAssignmentActivity
                        .Where(x => x.AssignmentID == assignmentId && x.UserID == userId)
                        .ToList();

                    if (activities.Count > 0)
                        db.tbOrderAssignmentActivity.RemoveRange(activities);

                    db.tbOrderAssignmentUser.Remove(person);

                    assignment.ModifiedDate = DateTime.Now;
                    assignment.ModifiedByUserID = currentUserId.Value;

                    db.SaveChanges();

                    transaction.Commit();

                    return Json(new { success = true, message = "نیرو از تعیین نقش حذف شد." });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    return Json(new { success = false, message = "خطا در حذف نیرو: " + ex.Message });
                }
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DisableOrderAssignment(int id)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var assignment = db.tbOrderAssignment.FirstOrDefault(x => x.ID == id);

            if (assignment == null)
                return Json(new { success = false, message = "تخصیص پیدا نشد." });

            assignment.IsActive = false;
            assignment.ModifiedDate = DateTime.Now;
            assignment.ModifiedByUserID = currentUserId.Value;

            db.SaveChanges();

            return Json(new { success = true, message = "تخصیص غیرفعال شد." });
        }


        /* =====================================================================
           بخش ۴ : تب ۲ - تنظیمات ارجاع
           هر منطقه ← چند «گروه»؛ هر گروه روزکار ، تعرفه و محدوده (کد ناحیه ۰ تا ۴۵) مخصوص خودش

             tbOrderAgentScopeRegion   : منطقه‌های مامور
             tbOrderAgentScopeWorkDay  : (WorkDay, RegionCode, GroupNo)
             tbOrderAgentScopeTariff   : (TariffTypeID, RegionCode, GroupNo)
             tbOrderAgentScopeArea     : (AreaCode, RegionCode, GroupNo)
                 RegionCode = NULL  →  فقط داده‌های قدیمی (عمومی)

           مثال: منطقه 14 ← گروه 1: روزکار 1 تا 10 و محدوده 0 تا 5
                           گروه 2: روزکار 11 تا 20 و محدوده 6 تا 45
           مامور وقتی مجاز است که «حداقل یکی از گروه‌های» همان منطقه را کامل رعایت کند.

           نکته: «همه مناطق» (Region = 0) و «بدون قانون» هنگام ذخیره به قانون صریح
           برای هر منطقه‌ی مجاز تبدیل می‌شود تا هر مامور همیشه ردیف منطقه داشته باشد.
           ===================================================================== */

        public class AgentScopeRuleDto
        {
            public int Region { get; set; }        // 0 = همه مناطق (فقط ورودی؛ سرور به مناطق صریح تبدیل می‌کند)
            public int GroupNo { get; set; }       // 0 = گروه جدید (شماره خودکار)، بزرگ‌تر از 0 = ویرایش همان گروه
            public List<int> Days { get; set; }    // خالی = همه روزها
            public List<int> Tariffs { get; set; } // خالی = همه تعرفه‌ها
            public List<int> Areas { get; set; }   // خالی یا انتخاب کامل بازه مجاز = همه مناطق/محدوده‌ها
        }


        // شماره‌ی گروه بعدی برای (مامور + منطقه)
        private int GetNextGroupNo(long scopeId, int? regionKey)
        {
            int maxDay = db.tbOrderAgentScopeWorkDay
                .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey)
                .Select(x => (int?)x.GroupNo).Max() ?? 0;

            int maxTariff = db.tbOrderAgentScopeTariff
                .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey)
                .Select(x => (int?)x.GroupNo).Max() ?? 0;

            int maxArea = db.tbOrderAgentScopeArea
                .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey)
                .Select(x => (int?)x.GroupNo).Max() ?? 0;

            return Math.Max(maxDay, Math.Max(maxTariff, maxArea)) + 1;
        }


        private List<AgentScopeRuleDto> LoadScopeRules(long scopeId)
        {
            var regions = db.tbOrderAgentScopeRegion
                .Where(x => x.ScopeID == scopeId)
                .Select(x => x.RegionCode)
                .ToList()
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var days = db.tbOrderAgentScopeWorkDay
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.GroupNo, x.WorkDay })
                .ToList();

            var tariffs = db.tbOrderAgentScopeTariff
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.GroupNo, x.TariffTypeID })
                .ToList();

            var areas = db.tbOrderAgentScopeArea
                .Where(x => x.ScopeID == scopeId)
                .Select(x => new { x.RegionCode, x.GroupNo, x.AreaCode })
                .ToList();

            var generalDays = days
                .Where(x => !x.RegionCode.HasValue)
                .Select(x => x.WorkDay)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var generalTariffs = tariffs
                .Where(x => !x.RegionCode.HasValue)
                .Select(x => x.TariffTypeID)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var generalAreas = areas
                .Where(x => !x.RegionCode.HasValue)
                .Select(x => x.AreaCode)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // انتخاب کامل بازه مجاز = بدون محدودیت → خالی برگردانده می‌شود
            Func<List<int>, List<int>> normalizeAreas = list =>
                list.Count >= GetAreaTotal() ? new List<int>() : list;

            var result = new List<AgentScopeRuleDto>();

            if (regions.Count == 0
                && (generalDays.Count > 0 || generalTariffs.Count > 0 || normalizeAreas(generalAreas).Count > 0))
            {
                result.Add(new AgentScopeRuleDto
                {
                    Region = 0,
                    GroupNo = 1,
                    Days = generalDays,
                    Tariffs = generalTariffs,
                    Areas = normalizeAreas(generalAreas)
                });
            }

            foreach (var r in regions)
            {
                var groupNos = days.Where(x => x.RegionCode == r).Select(x => x.GroupNo)
                    .Concat(tariffs.Where(x => x.RegionCode == r).Select(x => x.GroupNo))
                    .Concat(areas.Where(x => x.RegionCode == r).Select(x => x.GroupNo))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                // منطقه بدون هیچ ردیف = یک گروه بدون محدودیت (یا داده قدیمی عمومی)
                if (groupNos.Count == 0) groupNos.Add(1);

                foreach (var g in groupNos)
                {
                    var ownDays = days
                        .Where(x => x.RegionCode == r && x.GroupNo == g)
                        .Select(x => x.WorkDay)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    var ownTariffs = tariffs
                        .Where(x => x.RegionCode == r && x.GroupNo == g)
                        .Select(x => x.TariffTypeID)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    var ownAreas = areas
                        .Where(x => x.RegionCode == r && x.GroupNo == g)
                        .Select(x => x.AreaCode)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    result.Add(new AgentScopeRuleDto
                    {
                        Region = r,
                        GroupNo = g,
                        Days = ownDays.Count > 0 ? ownDays : generalDays,           // سازگاری با داده قدیمی
                        Tariffs = ownTariffs.Count > 0 ? ownTariffs : generalTariffs,
                        Areas = normalizeAreas(ownAreas.Count > 0 ? ownAreas : generalAreas)
                    });
                }
            }

            return result;
        }


        private void ClearScopeRules(long scopeId)
        {
            var oldTariffs = db.tbOrderAgentScopeTariff.Where(x => x.ScopeID == scopeId).ToList();
            if (oldTariffs.Count > 0) db.tbOrderAgentScopeTariff.RemoveRange(oldTariffs);

            var oldAreas = db.tbOrderAgentScopeArea.Where(x => x.ScopeID == scopeId).ToList();
            if (oldAreas.Count > 0) db.tbOrderAgentScopeArea.RemoveRange(oldAreas);

            var oldRegions = db.tbOrderAgentScopeRegion.Where(x => x.ScopeID == scopeId).ToList();
            if (oldRegions.Count > 0) db.tbOrderAgentScopeRegion.RemoveRange(oldRegions);

            var oldDays = db.tbOrderAgentScopeWorkDay.Where(x => x.ScopeID == scopeId).ToList();
            if (oldDays.Count > 0) db.tbOrderAgentScopeWorkDay.RemoveRange(oldDays);

            db.SaveChanges();
        }


        /*
           ردیف‌های عمومی قدیمی (RegionCode = NULL) را قبل از حذف،
           به مناطقی که در این ذخیره دست نخورده‌اند منتقل می‌کند (به‌صورت گروه 1)
           تا محدودیت آن‌ها از بین نرود؛ سپس ردیف‌های عمومی پاک می‌شوند.
        */
        private void FoldGeneralRulesIntoRegions(long scopeId, HashSet<int> submittedRegions)
        {
            var gDays = db.tbOrderAgentScopeWorkDay
                .Where(x => x.ScopeID == scopeId && x.RegionCode == null).ToList();
            var gTariffs = db.tbOrderAgentScopeTariff
                .Where(x => x.ScopeID == scopeId && x.RegionCode == null).ToList();
            var gAreas = db.tbOrderAgentScopeArea
                .Where(x => x.ScopeID == scopeId && x.RegionCode == null).ToList();

            if (gDays.Count == 0 && gTariffs.Count == 0 && gAreas.Count == 0) return;

            var otherRegions = db.tbOrderAgentScopeRegion
                .Where(x => x.ScopeID == scopeId)
                .Select(x => x.RegionCode)
                .ToList()
                .Where(r => !submittedRegions.Contains(r))
                .ToList();

            foreach (var r in otherRegions)
            {
                var region = r;

                if (gDays.Count > 0
                    && !db.tbOrderAgentScopeWorkDay.Any(x => x.ScopeID == scopeId && x.RegionCode == region))
                {
                    foreach (var g in gDays)
                        db.tbOrderAgentScopeWorkDay.Add(new tbOrderAgentScopeWorkDay
                        {
                            ScopeID = scopeId,
                            WorkDay = g.WorkDay,
                            RegionCode = region,
                            GroupNo = 1
                        });
                }

                if (gTariffs.Count > 0
                    && !db.tbOrderAgentScopeTariff.Any(x => x.ScopeID == scopeId && x.RegionCode == region))
                {
                    foreach (var g in gTariffs)
                        db.tbOrderAgentScopeTariff.Add(new tbOrderAgentScopeTariff
                        {
                            ScopeID = scopeId,
                            TariffTypeID = g.TariffTypeID,
                            RegionCode = region,
                            GroupNo = 1
                        });
                }

                if (gAreas.Count > 0
                    && !db.tbOrderAgentScopeArea.Any(x => x.ScopeID == scopeId && x.RegionCode == region))
                {
                    foreach (var g in gAreas)
                        db.tbOrderAgentScopeArea.Add(new tbOrderAgentScopeArea
                        {
                            ScopeID = scopeId,
                            AreaCode = g.AreaCode,
                            RegionCode = region,
                            GroupNo = 1
                        });
                }
            }

            if (gDays.Count > 0) db.tbOrderAgentScopeWorkDay.RemoveRange(gDays);
            if (gTariffs.Count > 0) db.tbOrderAgentScopeTariff.RemoveRange(gTariffs);
            if (gAreas.Count > 0) db.tbOrderAgentScopeArea.RemoveRange(gAreas);

            db.SaveChanges();
        }


        // همه‌ی ردیف‌های یک منطقه (همه گروه‌ها) یا فقط یک گروه (group > 0) پاک می‌شود
        private void RemoveRegionRows(long scopeId, int? regionKey, int group)
        {
            var dayRows = db.tbOrderAgentScopeWorkDay
                .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey && (group <= 0 || x.GroupNo == group))
                .ToList();
            if (dayRows.Count > 0) db.tbOrderAgentScopeWorkDay.RemoveRange(dayRows);

            var tariffRows = db.tbOrderAgentScopeTariff
                .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey && (group <= 0 || x.GroupNo == group))
                .ToList();
            if (tariffRows.Count > 0) db.tbOrderAgentScopeTariff.RemoveRange(tariffRows);

            var areaRows = db.tbOrderAgentScopeArea
                .Where(x => x.ScopeID == scopeId && x.RegionCode == regionKey && (group <= 0 || x.GroupNo == group))
                .ToList();
            if (areaRows.Count > 0) db.tbOrderAgentScopeArea.RemoveRange(areaRows);

            // حذف باید قبل از درج جدید اجرا شود (Unique Index)
            db.SaveChanges();
        }


        /*
           Upsert بر اساس (مامور + منطقه + گروه):
             - GroupNo > 0 → همان گروه ویرایش (جایگزین) می‌شود
             - GroupNo = 0 → گروه جدید با شماره‌ی بعدی همان منطقه اضافه می‌شود
             - اگر منطقه برای مامور ثبت نشده باشد، اضافه می‌شود
             - سایر مناطق و گروه‌های مامور دست نمی‌خورند
             - قانون کاملاً خالی (بدون روزکار/تعرفه/محدوده) یعنی کل آن منطقه بدون محدودیت:
               همه‌ی گروه‌های قبلی آن منطقه پاک می‌شود
             - Region = 0 → فقط سازگاری با داده قدیمی (RegionCode = NULL)
        */
        private void UpsertScopeRules(long scopeId, List<AgentScopeRuleDto> rules)
        {
            var existingRegions = new HashSet<int>(
                db.tbOrderAgentScopeRegion
                    .Where(x => x.ScopeID == scopeId)
                    .Select(x => x.RegionCode)
                    .ToList());

            // ردیف‌های عمومی قدیمی با قانون‌های صریح منطقه‌ای قاطی نشوند
            if (rules.Any(x => x.Region > 0))
            {
                FoldGeneralRulesIntoRegions(
                    scopeId,
                    new HashSet<int>(rules.Where(x => x.Region > 0).Select(x => x.Region)));
            }

            foreach (var rule in rules)
            {
                if (rule.Region > 0 && !existingRegions.Contains(rule.Region))
                {
                    db.tbOrderAgentScopeRegion.Add(new tbOrderAgentScopeRegion
                    {
                        ScopeID = scopeId,
                        RegionCode = rule.Region
                    });

                    existingRegions.Add(rule.Region);
                    db.SaveChanges();
                }

                int? regionKey = rule.Region > 0 ? (int?)rule.Region : null;

                var days = rule.Days ?? new List<int>();
                var tariffs = rule.Tariffs ?? new List<int>();
                var areas = rule.Areas ?? new List<int>();

                // کل منطقه بدون محدودیت: همه گروه‌ها پاک می‌شوند و گروهی ساخته نمی‌شود
                if (days.Count == 0 && tariffs.Count == 0 && areas.Count == 0)
                {
                    RemoveRegionRows(scopeId, regionKey, 0);
                    continue;
                }

                int group = rule.GroupNo > 0 ? rule.GroupNo : GetNextGroupNo(scopeId, regionKey);

                // جایگزینی همان گروه
                RemoveRegionRows(scopeId, regionKey, group);

                foreach (var day in days)
                {
                    db.tbOrderAgentScopeWorkDay.Add(new tbOrderAgentScopeWorkDay
                    {
                        ScopeID = scopeId,
                        WorkDay = day,
                        RegionCode = regionKey,
                        GroupNo = group
                    });
                }

                foreach (var tariffId in tariffs)
                {
                    db.tbOrderAgentScopeTariff.Add(new tbOrderAgentScopeTariff
                    {
                        ScopeID = scopeId,
                        RegionCode = regionKey,
                        TariffTypeID = tariffId,
                        GroupNo = group
                    });
                }

                foreach (var areaCode in areas)
                {
                    db.tbOrderAgentScopeArea.Add(new tbOrderAgentScopeArea
                    {
                        ScopeID = scopeId,
                        RegionCode = regionKey,
                        AreaCode = areaCode,
                        GroupNo = group
                    });
                }

                db.SaveChanges();
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult SaveAgentScope(List<int> UserIds, string RulesJson)
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

            /* ---------- RULES ---------- */

            List<AgentScopeRuleDto> parsed;

            try
            {
                parsed = string.IsNullOrWhiteSpace(RulesJson)
                    ? new List<AgentScopeRuleDto>()
                    : new System.Web.Script.Serialization.JavaScriptSerializer()
                          .Deserialize<List<AgentScopeRuleDto>>(RulesJson)
                      ?? new List<AgentScopeRuleDto>();
            }
            catch
            {
                return Json(new { success = false, message = "فرمت اطلاعات منطقه و روزکار معتبر نیست." });
            }

            // تب ۲: همه مناطق (مستقل از شهر کاربر)
            var allowedRegions = new HashSet<int>(GetAllRegionItems().Select(x => x.Code));

            // Region = 0 (همه مناطق) یا نبودن هیچ قانون (بدون محدودیت)
            // به قانون صریح برای هر منطقه تبدیل می‌شود تا هر مامور همیشه ردیف منطقه داشته باشد
            var expanded = new List<AgentScopeRuleDto>();

            foreach (var p in parsed)
            {
                if (p.Region == 0)
                {
                    foreach (var r in allowedRegions.OrderBy(x => x))
                    {
                        expanded.Add(new AgentScopeRuleDto
                        {
                            Region = r,
                            GroupNo = 0,
                            Days = p.Days ?? new List<int>(),
                            Tariffs = p.Tariffs ?? new List<int>(),
                            Areas = p.Areas ?? new List<int>()
                        });
                    }
                }
                else
                {
                    expanded.Add(p);
                }
            }

            if (expanded.Count == 0)
            {
                foreach (var r in allowedRegions.OrderBy(x => x))
                {
                    expanded.Add(new AgentScopeRuleDto
                    {
                        Region = r,
                        GroupNo = 0,
                        Days = new List<int>(),
                        Tariffs = new List<int>(),
                        Areas = new List<int>()
                    });
                }
            }

            parsed = expanded;

            var rules = parsed
                .GroupBy(x => new { x.Region, x.GroupNo })
                .Select(g => new AgentScopeRuleDto
                {
                    Region = g.Key.Region,
                    GroupNo = g.Key.GroupNo,
                    Days = g.SelectMany(x => x.Days ?? new List<int>()).Distinct().OrderBy(x => x).ToList(),
                    Tariffs = g.SelectMany(x => x.Tariffs ?? new List<int>()).Distinct().OrderBy(x => x).ToList(),
                    Areas = g.SelectMany(x => x.Areas ?? new List<int>()).Distinct().OrderBy(x => x).ToList()
                })
                .ToList();

            if (rules.Any(x => x.GroupNo < 0 || x.GroupNo > 999))
                return Json(new { success = false, message = "شماره گروه معتبر نیست." });

            if (rules.Any(x => x.Region != 0 && !allowedRegions.Contains(x.Region)))
                return Json(new { success = false, message = "یکی از مناطق انتخاب‌شده معتبر نیست." });

            if (rules.Any(x => x.Days.Any(d => d < 1 || d > 99)))
                return Json(new { success = false, message = "روزکار باید بین 1 تا 99 باشد." });

            int areaMax = GetAreaMax();

            if (rules.Any(x => x.Areas.Any(a => a < AREA_MIN || a > areaMax)))
                return Json(new
                {
                    success = false,
                    message = (IsJonobiContract() ? "منطقه" : "محدوده")
                              + " باید بین " + AREA_MIN + " تا " + areaMax + " باشد."
                });

            // انتخاب کامل بازه مجاز = بدون محدودیت → خالی ذخیره شود
            foreach (var rule in rules)
            {
                if (rule.Areas.Count >= GetAreaTotal())
                    rule.Areas = new List<int>();
            }

            /* ---------- USERS ---------- */

            var eligibleUsers = GetEligibleTab2UserIds(currentUserId.Value);

            if (UserIds.Any(x => !eligibleUsers.Contains(x)))
                return Json(new { success = false, message = "یکی از ماموران انتخاب‌شده در تب اول تعیین نقش نشده است." });

            /* ---------- TARIFFS ---------- */

            var activeTariffIds = db.tbOrderTariffType
                .Where(x => x.IsActive)
                .Select(x => x.ID)
                .ToList();

            if (rules.Any(x => x.Tariffs.Any(t => !activeTariffIds.Contains(t))))
                return Json(new { success = false, message = "یکی از تعرفه‌های انتخاب‌شده معتبر نیست." });

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var now = DateTime.Now;

                    foreach (var userId in UserIds)
                    {
                        var scope = db.tbOrderAgentScope.FirstOrDefault(x => x.UserID == userId);

                        if (scope == null)
                        {
                            scope = new tbOrderAgentScope
                            {
                                UserID = userId,
                                CityID = currentUser.usr_City_Dutysystem,
                                IsActive = true,
                                CreateDate = now,
                                CreatedByUserID = currentUserId.Value
                            };

                            db.tbOrderAgentScope.Add(scope);
                            db.SaveChanges();
                        }
                        else
                        {
                            // رکورد حذف‌شده قبلی: تنظیمات قدیمی نباید دوباره زنده شود
                            if (scope.IsActive != true)
                                ClearScopeRules(scope.ID);

                            scope.CityID = currentUser.usr_City_Dutysystem;
                            scope.IsActive = true;
                            scope.ModifiedDate = now;
                            scope.ModifiedByUserID = currentUserId.Value;

                            db.SaveChanges();
                        }

                        UpsertScopeRules(scope.ID, rules);
                    }

                    transaction.Commit();

                    return Json(new
                    {
                        success = true,
                        message = "اطلاعات " + UserIds.Count + " مامور با موفقیت ذخیره شد."
                    });
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


        /// <summary>Scope فعال که کاربر جاری به آن دسترسی دارد</summary>
        private tbOrderAgentScope FindAccessibleScope(long id, int currentUserId)
        {
            var eligibleUsers = GetEligibleTab2UserIds(currentUserId);

            var scope = db.tbOrderAgentScope.FirstOrDefault(x => x.ID == id && x.IsActive == true);

            if (scope == null || !eligibleUsers.Contains(scope.UserID))
                return null;

            return scope;
        }


        /// <summary>حذف یک گروه (یا کل یک منطقه) از تنظیمات یک مامور (region = 0 → تنظیم «همه مناطق»)</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteAgentScopeRegion(long id, int region, int group = 0)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var scope = FindAccessibleScope(id, currentUserId.Value);

            if (scope == null)
                return Json(new { success = false, message = "رکورد یافت نشد." });

            try
            {
                int? regionKey = region > 0 ? (int?)region : null;

                // فقط همان گروه (group > 0) یا کل منطقه (group = 0)
                RemoveRegionRows(id, regionKey, region > 0 ? group : 0);

                if (region > 0)
                {
                    // اگر از منطقه هیچ گروهی نماند، خود منطقه هم حذف می‌شود
                    bool regionHasRows =
                        db.tbOrderAgentScopeWorkDay.Any(x => x.ScopeID == id && x.RegionCode == region)
                        || db.tbOrderAgentScopeTariff.Any(x => x.ScopeID == id && x.RegionCode == region)
                        || db.tbOrderAgentScopeArea.Any(x => x.ScopeID == id && x.RegionCode == region);

                    if (!regionHasRows)
                    {
                        var regionRows = db.tbOrderAgentScopeRegion
                            .Where(x => x.ScopeID == id && x.RegionCode == region)
                            .ToList();

                        if (regionRows.Count > 0) db.tbOrderAgentScopeRegion.RemoveRange(regionRows);

                        db.SaveChanges();
                    }
                }

                // اگر چیزی برای مامور باقی نماند، رکورد غیرفعال شود (= بدون محدودیت)
                bool hasAny =
                    db.tbOrderAgentScopeRegion.Any(x => x.ScopeID == id)
                    || db.tbOrderAgentScopeWorkDay.Any(x => x.ScopeID == id)
                    || db.tbOrderAgentScopeTariff.Any(x => x.ScopeID == id)
                    || db.tbOrderAgentScopeArea.Any(x => x.ScopeID == id);

                if (!hasAny)
                    scope.IsActive = false;

                scope.ModifiedDate = DateTime.Now;
                scope.ModifiedByUserID = currentUserId.Value;

                db.SaveChanges();

                return Json(new { success = true, message = group > 0 ? "گروه از تنظیمات مامور حذف شد." : "منطقه از تنظیمات مامور حذف شد." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در حذف: " + ex.Message });
            }
        }


        [HttpGet]
        public JsonResult GetAgentScope(long id)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." }, JsonRequestBehavior.AllowGet);

            var scope = FindAccessibleScope(id, currentUserId.Value);

            if (scope == null)
                return Json(new { success = false, message = "رکورد یافت نشد." }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                success = true,
                data = new
                {
                    ID = scope.ID,
                    UserID = scope.UserID,
                    Rules = LoadScopeRules(id)
                }
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        public JsonResult GetAgentScopeDetails(long id)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." }, JsonRequestBehavior.AllowGet);

            var scope = FindAccessibleScope(id, currentUserId.Value);

            if (scope == null)
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
        public JsonResult DeleteAgentScope(long id)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var scope = FindAccessibleScope(id, currentUserId.Value);

            if (scope == null)
                return Json(new { success = false, message = "رکورد یافت نشد." });

            scope.IsActive = false;
            scope.ModifiedDate = DateTime.Now;
            scope.ModifiedByUserID = currentUserId.Value;

            db.SaveChanges();

            return Json(new { success = true, message = "اطلاعات محدوده مامور حذف شد." });
        }


        /* =====================================================================
           بخش ۵ : تب ۳ - لیست بالابری (Excel)
           ===================================================================== */

        private string GetOrderExcelComponentTitle(int componentType)
        {
            switch (componentType)
            {
                case 1: return "شناسه قبض";
                case 2: return "پرونده";
                case 3: return "روزکار";
                case 4: return "محدوده";
                default: return null;
            }
        }


        private string GetOrderExcelCellText(ICell cell, DataFormatter formatter, IFormulaEvaluator evaluator)
        {
            if (cell == null)
                return "";

            try
            {
                string result = cell.CellType == CellType.Formula
                    ? formatter.FormatCellValue(cell, evaluator)
                    : formatter.FormatCellValue(cell);

                return NormalizeOrderExcelText(result);
            }
            catch
            {
                return cell.ToString().Trim();
            }
        }


        [HttpGet]
        public ActionResult DownloadOrderComponentTemplate(int componentType)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Content("دسترسی غیر مجاز");

            var componentTitle = GetOrderExcelComponentTitle(componentType);

            if (string.IsNullOrWhiteSpace(componentTitle))
                return Content("مولفه انتخاب‌شده معتبر نیست.");

            NPOIWorkbook workbook = new XSSFWorkbook();

            var sheet = workbook.CreateSheet("Data");

            var textStyle = workbook.CreateCellStyle();
            textStyle.DataFormat = workbook.CreateDataFormat().GetFormat("@");
            sheet.SetDefaultColumnStyle(0, textStyle);

            sheet.CreateRow(0).CreateCell(0).SetCellValue(componentTitle);
            sheet.SetColumnWidth(0, 35 * 256);

            using (var memory = new MemoryStream())
            {
                workbook.Write(memory);

                return File(
                    memory.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "OrderComponentTemplate_" + componentType + ".xlsx");
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult UploadOrderComponentExcel(int componentType, HttpPostedFileBase excelFile)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId.Value);

            if (currentUser == null)
                return Json(new { success = false, message = "کاربر پیدا نشد." });

            var componentTitle = GetOrderExcelComponentTitle(componentType);

            if (string.IsNullOrWhiteSpace(componentTitle))
                return Json(new { success = false, message = "مولفه انتخاب‌شده معتبر نیست." });

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

                var uploadedHeader = NormalizeOrderExcelText(
                    GetOrderExcelCellText(headerRow.GetCell(0), formatter, evaluator)).Replace(" ", "");

                var requiredHeader = NormalizeOrderExcelText(componentTitle).Replace(" ", "");

                if (uploadedHeader != requiredHeader)
                {
                    return Json(new
                    {
                        success = false,
                        message = "عنوان ستون Excel باید دقیقاً «" + componentTitle + "» باشد."
                    });
                }

                if (sheet.LastRowNum > 50000)
                    return Json(new { success = false, message = "حداکثر 50,000 ردیف قابل بارگذاری است." });

                var importedValues = new List<KeyValuePair<int, string>>();
                var uniqueValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var errors = new List<string>();

                for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    var excelRow = sheet.GetRow(rowIndex);

                    if (excelRow == null)
                        continue;

                    var value = GetOrderExcelCellText(excelRow.GetCell(0), formatter, evaluator);

                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    value = value.Trim();

                    if (value.Length > 500)
                    {
                        errors.Add("ردیف " + (rowIndex + 1) + ": طول مقدار بیشتر از حد مجاز است.");

                        if (errors.Count >= 20)
                            break;

                        continue;
                    }

                    if (componentType == 3)
                    {
                        int day;

                        if (!int.TryParse(NormalizeOrderExcelDigits(value), out day) || day < 1 || day > 99)
                        {
                            errors.Add("ردیف " + (rowIndex + 1) + ": روزکار باید عددی بین 1 تا 99 باشد.");

                            if (errors.Count >= 20)
                                break;

                            continue;
                        }

                        value = day.ToString();
                    }

                    if (componentType == 4)
                    {
                        int mahdodeh;

                        if (!int.TryParse(NormalizeOrderExcelDigits(value), out mahdodeh) || mahdodeh < 0 || mahdodeh > 99)
                        {
                            errors.Add("ردیف " + (rowIndex + 1) + ": محدوده باید عددی بین 0 تا 99 باشد (رقم ۶ و ۷ اشتراک).");

                            if (errors.Count >= 20)
                                break;

                            continue;
                        }

                        value = mahdodeh.ToString();
                    }

                    if (!uniqueValues.Add(value))
                        continue;

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
                        var previousBatches = db.tbOrderExcelImportBatch
                            .Where(x =>
                                x.UserID == currentUserId.Value
                                && x.CityID == cityId.Value
                                && x.ComponentType == componentType
                                && x.IsActive == true)
                            .ToList();

                        foreach (var previous in previousBatches)
                            previous.IsActive = false;

                        db.SaveChanges();

                        var batch = new tbOrderExcelImportBatch
                        {
                            ComponentType = componentType,
                            ComponentTitle = componentTitle,
                            UserID = currentUserId.Value,
                            CityID = cityId.Value,
                            OriginalFileName = Path.GetFileName(excelFile.FileName),
                            RowCount = importedValues.Count,
                            IsActive = true,
                            CreateDate = DateTime.Now
                        };

                        db.tbOrderExcelImportBatch.Add(batch);
                        db.SaveChanges();

                        foreach (var item in importedValues)
                        {
                            db.tbOrderExcelImportValue.Add(new tbOrderExcelImportValue
                            {
                                BatchID = batch.ID,
                                ValueText = item.Value,
                                ExcelRowNumber = item.Key
                            });
                        }

                        foreach (var regionCode in allowedRegions.Distinct())
                        {
                            db.tbOrderExcelImportRegion.Add(new tbOrderExcelImportRegion
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
                            rowCount = importedValues.Count,
                            cityId = cityId.Value,
                            regionCount = allowedRegions.Count,
                            message = importedValues.Count + " مقدار از مولفه «" + componentTitle
                                      + "» با موفقیت برای کاربر جاری ذخیره شد."
                        });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();

                        return Json(new { success = false, message = "خطا در ذخیره فایل Excel: " + ex.Message });
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
        public JsonResult DisableOrderComponentImport(long id)
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Json(new { success = false, message = "دسترسی غیر مجاز." });

            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId.Value);

            if (currentUser == null || !currentUser.usr_City_Dutysystem.HasValue)
                return Json(new { success = false, message = "اطلاعات کاربر یا شهرستان معتبر نیست." });

            var batch = db.tbOrderExcelImportBatch
                .FirstOrDefault(x =>
                    x.ID == id
                    && x.UserID == currentUserId.Value
                    && x.CityID == currentUser.usr_City_Dutysystem);

            if (batch == null)
                return Json(new { success = false, message = "رکورد موردنظر یافت نشد." });

            batch.IsActive = false;

            db.SaveChanges();

            return Json(new { success = true, message = "فایل بارگذاری‌شده غیرفعال شد." });
        }


        /* =====================================================================
           بخش ۶ : تب ۴ - تخصیص هوشمند (کنترلر)
           ===================================================================== */

        // اگر true باشد، لیست بالابری فقط وقتی اعمال می‌شود که پرونده با «همه» مؤلفه‌های Excel تطابق داشته باشد
        private const bool BALABARI_MATCH_ALL_COMPONENTS = false;


        // سقف مجاز بار سیستان = ۱۲۰٪ ظرفیت (رفتار قبلی سیستان بدون تغییر)
        private const decimal ALLOCATION_CAPACITY_OVERFLOW = 1.2m;

        // در جنوبی عبور جزئی تا ۱۰۳٪ مجاز است.
        private const decimal JONOBI_ALLOCATION_CAPACITY_OVERFLOW = 1.03m;

        // «برش» دور اول: چند درصد از ظرفیت (خالیِ) مأموران جدا شود؟
        //   100 = به اندازه‌ی کل ظرفیت   |   50 = نصف ظرفیت (فقط مبالغ بالا) و بقیه را حلقه‌ی تکمیلی پر می‌کند
        private const decimal ALLOCATION_CUT_PERCENT = 100m;

        // آستانه‌ی «کامل» بودن کارتابل: بار مأمور از چند درصد ظرفیت که رد شد دیگر پرونده نمی‌گیرد
        private const decimal ALLOCATION_COMPLETE_PERCENT = 100m;

        /// <summary>
        /// cutPercent / completePercent اختیاری‌اند؛ اگر از صفحه یا درخواست ارسال نشوند مقدار ثابت‌های بالا اعمال می‌شود.
        /// </summary>
        private AllocationSettings GetSmartAllocationSettings(decimal? cutPercent = null, decimal? completePercent = null)
        {
            decimal cut = cutPercent.HasValue ? cutPercent.Value : ALLOCATION_CUT_PERCENT;
            decimal complete = completePercent.HasValue ? completePercent.Value : ALLOCATION_COMPLETE_PERCENT;

            // محدوده‌ی مجاز: برش بین 10 تا 120 درصد ، کامل بین 50 تا 100 درصد
            cut = Math.Min(120m, Math.Max(10m, cut));
            complete = Math.Min(100m, Math.Max(50m, complete));

            return new AllocationSettings
            {
                DemandWeight = IsJonobiContract() ? 1m : 0.5m,

                CompleteThreshold = complete / 100m,

                // جنوبی حداکثر ۱۰۳٪؛ سیستان همان سقف قبلی ۱۲۰٪
                MaxOverflow = IsJonobiContract()
                    ? JONOBI_ALLOCATION_CAPACITY_OVERFLOW
                    : ALLOCATION_CAPACITY_OVERFLOW,

                // اندازه‌ی برش دور اول (نسبت به ظرفیت خالی)
                CutoffFactor = cut / 100m,

                MaxRounds = 10,
                DemandFallbackToNormal = false
            };
        }


        private decimal GetSmartAllocationCapacity(AllocationRoleKind role, long cityValue)
        {
            decimal cv = cityValue;

            switch (role)
            {
                case AllocationRoleKind.Normal:
                case AllocationRoleKind.Demand:
                case AllocationRoleKind.NormalAndDemand:
                case AllocationRoleKind.Balabari:
                    return 12m * ((cv * 0.20m) + cv);

                case AllocationRoleKind.Telephone:
                    return 12m * ((cv * 0.20m) + cv);

                case AllocationRoleKind.Other:
                    return 6m * ((cv * 0.20m) + cv);

                default:
                    return 0;
            }
        }



        /* =====================================================================
           استاندارد روزانه‌ی هر شهرستان بر اساس «هدف وصول روزانه ابلاغی (ریال)»

           مراحل:
             ۱. هدف وصول روزانه هر شهرستان مستقیماً به ریال از CITY_DAILY_TARGETS_RIAL خوانده می‌شود.
                در این بخش تفریق ماهانه، تقسیم بر روزهای باقیمانده یا تبدیل به ریال نداریم.
             ۲. تعداد مأموران شهرستان از تب ۱:
                  عادی (یا عادی + دیماند)  = J   ← ضریب 1
                  فقط دیماند               = K   ← ضریب 2
                  تلفنی / وصول مذاکره‌ای   = L   ← ضریب 0.5
             ۳. مبلغ پایه‌ی روزانه‌ی هر مأمور = ROUND( هدف روزانه ÷ (J×1 + K×2 + L×0.5) )
                استاندارد مأمور عادی = مبلغ پایه و استاندارد مأمور دیماند = دو برابر مبلغ پایه.
                ضریب وزنی پرونده دیماند در موتور تخصیص برای جنوبی 1 است.
             ۴. استاندارد قبلی (12 روزه) را به یک روز برمی‌گردانیم (÷ 12)
             ۵. اگر استاندارد جدید «بیشتر از دو برابر مقدار روزانه قبلی» بود ← ظرفیت = جدید × 6
                اگر «کمتر یا مساوی دو برابر مقدار روزانه قبلی» بود          ← ظرفیت = قبلی × 12
           ===================================================================== */

        // ضرایب نقش‌ها (در Excel خانه‌های N1 و O1 و P1)
        // در جنوبی تمام نیروهای دارای نقش مرتبط، مستقل از نوع و عنوان شغل، شمرده می‌شوند.
        // در سیستان همان محدودیت قبلی نوع شغل 21 حفظ می‌شود.
        private const int STANDARD_COUNTED_JOB_ID = 21;
        private const decimal STANDARD_COEF_NORMAL = 1m;
        private const decimal STANDARD_COEF_DEMAND = 2m;
        private const decimal STANDARD_COEF_PHONE_JONOBI = 0.5m;
        private const decimal STANDARD_COEF_PHONE_SISTAN = 1m;

        // ضریب ظرفیت وقتی استاندارد جدید بیشتر از دو برابر مقدار روزانه قبلی است (جدید × 6) و در غیر این صورت (قبلی × 12)
        private const decimal STANDARD_DAYS_WHEN_NEW_HIGHER = 6m;
        private const decimal STANDARD_DAYS_WHEN_OLD = 12m;

        // فقط برای نمایش مقادیر تومان در خروجی فعلی فرانت‌اند
        private const decimal RIAL_PER_TOMAN = 10m;

        // هدف وصول روزانه آبان 1405 ابلاغی، مستقیماً به ریال
        private static readonly Dictionary<string, decimal> CITY_DAILY_TARGETS_RIAL =
            new Dictionary<string, decimal>
        {
            { "زاهدان 1", 40739948843m },
            { "زاهدان 2", 41882343651m },
            { "کورین", 2402044197m },
            { "میرجاوه", 3073176981m },
            { "زابل", 18572346932m },
            { "هیرمند", 1736183206m },
            { "زهک", 3102999647m },
            { "خاش", 10902938406m },
            { "سراوان", 19898113103m },
            { "جالق", 4378612403m },
            { "سوران", 9257096577m },
            { "مهرستان", 3248104580m },
            { "ایرانشهر", 27146008835m },
            { "بمپور", 3580607607m },
            { "دلگان", 4129430057m },
            { "قصرقند", 1042901034m },
            { "نیکشهر", 3874398807m },
            { "فنوج", 2668899349m },
            { "اسپکه", 1239160842m },
            //{ "چابهار", 27161341647m },
                                    { "چابهار", 19012939152m },

            { "نگور", 4015628686m },
            { "پلان", 983746588m },
            { "زرآباد", 517511855m },
            { "کنارک", 3772036605m },
            { "راسک", 3154290192m },
            { "سرباز", 1408744315m },
        };

        // هدف روزانه جنوبی؛ مجموع = 96,667,000,000 ریال
        private static readonly Dictionary<string, decimal> JONOBI_CITY_DAILY_TARGETS_RIAL =
            new Dictionary<string, decimal>
        {
            { "خوسف",       6240000000m  },
            { "درمیان",     3672000000m  },
            { "زیرکوه",     4498000000m  },
            //{ "بیرجند",    34328000000m  },
                        { "بیرجند",    11442666666m  },

            { "قائنات",     9344000000m  },
            { "فردوس",      5332000000m  },
            { "طبس",       15975000000m  },
            { "عشق آباد",    914000000m  },
            { "نهبندان",    5868000000m  },
            { "سرایان",     2374000000m  },
            { "بشرویه",     1800000000m  },
            { "سربیشه",     6322000000m  }
        };


        /// <summary>هدف وصول روزانه شهرستان به «ریال» (0 = برای این شهرستان هدفی تعریف نشده)</summary>
        private decimal GetCityDailyTargetRial(string city)
        {
            city = NormalizeOrderExcelText(city);

            if (city == "قاین" || city == "قائن" || city == "قاینات")
                city = "قائنات";

            var targets = IsJonobiContract()
                ? JONOBI_CITY_DAILY_TARGETS_RIAL
                : CITY_DAILY_TARGETS_RIAL;

            decimal dailyTargetRial;

            return targets.TryGetValue(city, out dailyTargetRial) && dailyTargetRial > 0
                ? dailyTargetRial
                : 0;
        }


        public class CityStandard
        {
            public string City;
            public bool IsJonobi;
            public int? AllocationDays;
            public long? RecordLimit
            {
                get
                {
                    return IsJonobi && AllocationDays.HasValue
                    ? (long?)((long)AllocationDays.Value * 60L) : null;
                }
            }

            // تعداد مأموران
            public int NormalCount;     // عادی یا عادی+دیماند
            public int DemandCount;     // فقط دیماند
            public int PhoneCount;      // تلفنی

            public decimal DailyRemainingRial;      // هدف وصول روزانه ابلاغی (ریال)
            public decimal NewDailyRial;            // استاندارد جدید هر مأمور برای یک روز (ریال) — 0 = قابل محاسبه نبود
            public decimal OldDailyRial;            // استاندارد قبلی برای یک روز (ریال) = استاندارد 12 روزه ÷ 12
            public bool UsesNew;                    // true: جدید × 6 ؛ false: قبلی × 12

            public decimal Capacity;                // ظرفیت پایه مأمور عادی (ریال)
            public decimal OtherCapacity;           // ظرفیت نقش «سایر» (نصف)

            public decimal CapacityFor(AllocationRoleKind role)
            {
                switch (role)
                {
                    case AllocationRoleKind.Normal:
                    case AllocationRoleKind.Balabari:
                        return Capacity;

                    case AllocationRoleKind.Demand:
                    case AllocationRoleKind.NormalAndDemand:
                        // جنوبی: استاندارد دیماند دقیقاً دو برابر استاندارد عادی است.
                        return IsJonobi ? Capacity * 2m : Capacity;

                    case AllocationRoleKind.Telephone:
                        // جنوبی: ظرفیت قطع و وصل دقیقاً دو برابر وصول مذاکره‌ای است.
                        return IsJonobi ? Capacity / 2m : Capacity;

                    case AllocationRoleKind.Other:
                        return OtherCapacity;

                    default:
                        return 0;
                }
            }
        }


        /// <summary>
        /// استاندارد روزانه‌ی شهرستان را از روی «همه‌ی مأموران همان شهرستان» (قبل از فیلتر تکی) محاسبه می‌کند.
        /// </summary>
        private CityStandard ComputeCityStandard(string city, List<AllocationAgent> agents, int? allocationDays = null)
        {
            agents = agents ?? new List<AllocationAgent>();

            var std = new CityStandard
            {
                City = city,
                IsJonobi = IsJonobiContract(),
                AllocationDays = IsJonobiContract() ? allocationDays : null
            };

            bool isJonobi = std.IsJonobi;

            // جنوبی: همه نیروهای دارای نقش مرتبط، بدون توجه به نوع شغل.
            // سیستان: دقیقاً منطق قبلی؛ عادی/تلفنی فقط نوع شغل 21 و دیماند با هر نوع شغل.
            // هر کاربر فقط یک بار و بدون تکرار وارد محاسبه می‌شود.
            var countedAgents =
                agents
                .Where(a =>
                    isJonobi
                        ? (a.ServesNormal
                           || a.ServesDemand
                           || a.Roles.Contains(AllocationRoleKind.Telephone))
                        : ((a.JobId == STANDARD_COUNTED_JOB_ID
                            && (a.ServesNormal || a.Roles.Contains(AllocationRoleKind.Telephone)))
                           || a.ServesDemand))
                .GroupBy(a => a.UserId)
                .Select(g => g.First())
                .ToList();

            std.NormalCount = countedAgents.Count(a => a.ServesNormal);
            std.DemandCount = countedAgents.Count(a => a.ServesDemand && !a.ServesNormal);

            // تلفنیِ خالص (بدون نقش عادی/دیماند) ؛ تلفنی که عادی یا دیماند هم دارد قبلاً در همان گروه شمرده شده است
            std.PhoneCount = countedAgents.Count(a =>
                !a.ServesNormal && !a.ServesDemand && a.Roles.Contains(AllocationRoleKind.Telephone));

            if (isJonobi)
            {
                // جنوبی فقط روش جدید دارد.
                std.OldDailyRial = 0;
            }
            else
            {
                long oldCityValue = GetCityValue(city);
                std.OldDailyRial = GetSmartAllocationCapacity(AllocationRoleKind.Normal, oldCityValue) / 12m;
            }

            // استاندارد جدید: هدف وصول روزانه ابلاغی (مستقیماً به ریال) ÷ مجموع (تعداد × ضریب)
            std.DailyRemainingRial = GetCityDailyTargetRial(city);

            decimal weights =
                std.NormalCount * STANDARD_COEF_NORMAL
                + std.DemandCount * STANDARD_COEF_DEMAND
                + std.PhoneCount * (isJonobi
                    ? STANDARD_COEF_PHONE_JONOBI
                    : STANDARD_COEF_PHONE_SISTAN);

            if (std.DailyRemainingRial > 0 && weights > 0)
            {
                // ROUND(..., -5) در Excel روی تومان = گرد کردن به صدهزار تومان = یک میلیون ریال
                std.NewDailyRial = Math.Round(std.DailyRemainingRial / weights / 1000000m, 0, MidpointRounding.AwayFromZero) * 1000000m;
            }

            if (isJonobi)
            {
                // در اجرای تخصیص، تعداد روز را کاربر تعیین می‌کند؛ گزارش‌های قدیمی مقدار پیشین را دارند.
                std.UsesNew = true;
                std.Capacity = std.NewDailyRial * (allocationDays.HasValue
                    ? (decimal)allocationDays.Value : STANDARD_DAYS_WHEN_NEW_HIGHER);
            }
            else if (std.NewDailyRial > std.OldDailyRial * 2m)
            {
                std.UsesNew = true;
                std.Capacity = std.NewDailyRial * STANDARD_DAYS_WHEN_NEW_HIGHER;
            }
            else
            {
                std.UsesNew = false;
                std.Capacity = std.OldDailyRial * STANDARD_DAYS_WHEN_OLD;
            }

            // «سایر» مثل قبل نصف ظرفیت (6 روز در برابر 12 روز)
            std.OtherCapacity = std.Capacity / 2m;

            return std;
        }


        /// <summary>ظرفیت هر مأمور = بیشترین ظرفیت میان نقش‌هایش</summary>
        private void ApplyCityStandard(List<AllocationAgent> agents, CityStandard std)
        {
            foreach (var a in agents)
            {
                a.RecordLimit = std.RecordLimit;
                a.Capacity = a.Roles.Count == 0
                    ? 0
                    : a.Roles.Max(r => std.CapacityFor(r));
            }
        }


        private object BuildCityStandardSummary(CityStandard std)
        {
            if (std == null)
                return null;

            return new
            {
                std.City,
                std.AllocationDays,
                std.RecordLimit,
                std.NormalCount,
                std.DemandCount,
                std.PhoneCount,
                DailyRemainingToman = Math.Round(std.DailyRemainingRial / RIAL_PER_TOMAN, 0),
                NewDailyToman = Math.Round(std.NewDailyRial / RIAL_PER_TOMAN, 0),
                OldDailyToman = Math.Round(std.OldDailyRial / RIAL_PER_TOMAN, 0),
                CapacityToman = Math.Round(std.Capacity / RIAL_PER_TOMAN, 0),
                NormalCapacityToman = Math.Round(
                    std.CapacityFor(AllocationRoleKind.Normal) / RIAL_PER_TOMAN, 0),
                DemandCapacityToman = Math.Round(
                    std.CapacityFor(AllocationRoleKind.Demand) / RIAL_PER_TOMAN, 0),
                std.UsesNew,
                Rule = IsJonobiContract()
                    ? "جنوبی: ظرفیت = استاندارد روزانه × "
                      + (std.AllocationDays.HasValue ? std.AllocationDays.Value.ToString(CultureInfo.InvariantCulture) : STANDARD_DAYS_WHEN_NEW_HIGHER.ToString(CultureInfo.InvariantCulture))
                      + " روز؛ استاندارد دیماند دو برابر عادی و ضریب بار دیماند 1 است"
                    : (std.UsesNew
                        ? "استاندارد جدید بیشتر از دو برابر استاندارد روزانه قبلی است ← ظرفیت = جدید × 6"
                        : "استاندارد جدید کمتر یا مساوی دو برابر استاندارد روزانه قبلی است ← ظرفیت = قبلی × 12")
            };
        }


        private AllocationRoleKind ResolveAllocationRole(string title)
        {
            var t = NormalizeOrderExcelText(title).Replace(" ", "").Replace("‌", "");

            if (t.Contains("تلفنی") || t.Contains("وصولمذاکره") || t.Contains("مذاکره"))
                return AllocationRoleKind.Telephone;

            if (t.Contains("بالابری"))
                return AllocationRoleKind.Balabari;

            if (t.Contains("سایر"))
                return AllocationRoleKind.Other;

            bool hasDemand = t.Contains("دیماند");
            bool hasNormal = t.Contains("عادی");

            if (hasDemand && hasNormal)
                return AllocationRoleKind.NormalAndDemand;

            if (hasDemand)
                return AllocationRoleKind.Demand;

            if (hasNormal)
                return AllocationRoleKind.Normal;

            return AllocationRoleKind.None;
        }


        private string GetAllocationRoleTitle(AllocationRoleKind role)
        {
            switch (role)
            {
                case AllocationRoleKind.Normal: return "عادی";
                case AllocationRoleKind.Demand: return "دیماند";
                case AllocationRoleKind.NormalAndDemand: return "دیماند/عادی";
                case AllocationRoleKind.Telephone: return GetSpecialCollectionTitle();
                case AllocationRoleKind.Balabari: return "بالابری";
                case AllocationRoleKind.Other: return "سایر";
                default: return "-";
            }
        }


        private string GetAllocationPhaseTitle(string phaseKey)
        {
            switch (phaseKey)
            {
                case SmartAllocationEngine.PHASE_BALABARI: return "بالابری";
                case SmartAllocationEngine.PHASE_FIELD: return "میدانی";
                case SmartAllocationEngine.PHASE_OTHER: return "سایر";
                case SmartAllocationEngine.PHASE_TELEPHONE: return GetSpecialCollectionTitle();
                default: return "-";
            }
        }


        private string NormalizeAllocationKey(object value)
        {
            return NormalizeOrderExcelDigits(NormalizeOrderExcelText(Convert.ToString(value))).Trim();
        }


        /// <summary>فقط ارقام و بدون صفر ابتدایی: "0123" = "123" ، "12.00" = "12"</summary>
        private string NormalizeAllocationIdKey(object value)
        {
            var text = NormalizeAllocationKey(value);

            decimal dec;

            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out dec)
                && dec == Math.Truncate(dec))
            {
                text = dec.ToString("0", CultureInfo.InvariantCulture);
            }

            var digits = new string(text.Where(char.IsDigit).ToArray());

            if (digits.Length == 0)
                return text;

            digits = digits.TrimStart('0');

            return digits.Length > 0 ? digits : "0";
        }


        private int? ParseAllocationInt(object value)
        {
            var text = NormalizeAllocationKey(value);

            if (string.IsNullOrWhiteSpace(text))
                return null;

            int number;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;

            decimal dec;

            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out dec))
                return (int)dec;

            return null;
        }


        /// <summary>فقط ارقامِ شماره اشتراک</summary>
        private string EshterakDigits(object eshterak)
        {
            return new string(NormalizeAllocationKey(eshterak).Where(char.IsDigit).ToArray());
        }


        /// <summary>روزکار = رقم ۴ و ۵ شماره اشتراک (مثال 2651701038000 ← 17)</summary>
        private int? GetAllocationWorkDay(object eshterak)
        {
            var digits = EshterakDigits(eshterak);

            if (digits.Length < 5)
                return null;

            int day;

            if (!int.TryParse(digits.Substring(3, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out day))
                return null;

            if (day < 1 || day > 99)
                return null;

            return day;
        }


        /// <summary>
        /// جنوبی: روزکار مستقیماً از ستون roz (مقدار FK_db2=9) خوانده می‌شود.
        /// سیستان: منطق قبلی، یعنی رقم چهارم و پنجم شماره اشتراک، حفظ می‌شود.
        /// </summary>
        private int? GetAllocationWorkDay(object eshterak, object storedDay)
        {
            if (!IsJonobiContract())
                return GetAllocationWorkDay(eshterak);

            var day = ParseAllocationInt(storedDay);

            return day.HasValue && day.Value >= 1 && day.Value <= 99
                ? day
                : null;
        }


        /// <summary>محدوده (کد ناحیه) = رقم ۶ و ۷ شماره اشتراک (مثال 2651712038000 ← 12)</summary>
        private int? GetAllocationMahdodeh(object eshterak)
        {
            var digits = EshterakDigits(eshterak);

            if (digits.Length < 7)
                return null;

            int mahdodeh;

            if (!int.TryParse(digits.Substring(5, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out mahdodeh))
                return null;

            return mahdodeh;
        }


        /// <summary>
        /// منبع محدوده:
        /// جنوبی = فقط item2؛ سیستان = همان رقم ۶ و ۷ شماره اشتراک.
        /// </summary>
        private int? GetAllocationMahdodeh(object eshterak, object item2)
        {
            return IsJonobiContract()
                ? ParseAllocationInt(item2)
                : GetAllocationMahdodeh(eshterak);
        }


        /// <summary>
        /// روش قدیمی سیستان: نوع اشتراک فقط از روی رقم اول (چپ) شماره اشتراک؛
        /// 1 = دیماند و هر رقم دیگری = عادی. این تابع برای جنوبی مستقیماً استفاده نمی‌شود.
        /// </summary>
        private bool IsDemandEshterak(object eshterak)
        {
            var digits = EshterakDigits(eshterak);

            return digits.Length > 0 && digits[0] == '1';
        }


        /// <summary>
        /// منبع نوع اشتراک:
        /// جنوبی = ستون noeshtrak؛ سیستان = همان رقم اول شماره اشتراک.
        /// </summary>
        private bool IsAllocationDemand(object eshterak, object noeshtrak)
        {
            if (IsJonobiContract())
            {
                return NormalizeOrderExcelText(Convert.ToString(noeshtrak))
                    .Contains("دیماند");
            }

            return IsDemandEshterak(eshterak);
        }


        private string AllocationKindTitle(object eshterak, object noeshtrak)
        {
            return IsAllocationDemand(eshterak, noeshtrak) ? "دیماند" : "عادی";
        }


        private bool IsAllocationDemand(ValuunicPriorityResult x)
        {
            return IsAllocationDemand(x.eshterak, x.noeshtrak);
        }


        /// <summary>«ویژه»: دیماند با تعرفه عمومی ، یا مبلغ بیشتر از سقف (valu)</summary>
        private bool IsAllocationSpecial(ValuunicPriorityResult x, decimal specialThreshold)
        {
            if (IsAllocationDemand(x)
                && NormalizeOrderExcelText(x.nametarefeh) == "عمومی")
            {
                return true;
            }

            return specialThreshold > 0 && x.P5 > specialThreshold;
        }


        /* =====================================================================
           تکمیل تعرفه‌ی خالی از جدول dbergharelimsertfk1_7_1218
           ---------------------------------------------------------------------
           اگر nametarefeh پرونده خالی بود:
             ۱. valuunic پرونده در جدول dbergharelimsertfk1_7_1218 با شرط FK_db2 = 187 پیدا می‌شود
             ۲. از ستون valueFK_db2 «رقم اول» برداشته می‌شود
             ۳. رقم اول ← تعرفه:
                  1 = خانگی | 2 = عمومی | 3 = کشاورزی | 4 = صنعتی
                  5 = سایر مصارف (تجاری) | 9 = روشنایی معابر
           ===================================================================== */

        // اگر جدول در اسکیمای دیگری است مثلاً "[dbo].[dbergharelimsertfk1_7_1218]" بنویسید
        private const string TARIFF_LOOKUP_TABLE = "dbergharelimsertfk1_7_1218";
        private const int TARIFF_LOOKUP_FK_DB2 = 187;
        private const int TARIFF_LOOKUP_CHUNK = 800;


        private class TariffLookupRow
        {
            public string K { get; set; }   // valuunic
            public string V { get; set; }   // valueFK_db2
        }


        /// <summary>
        /// رقم اول ← عنوان تعرفه. عنوان‌ها تا حد امکان از tbOrderTariffType (تب ۲) برداشته می‌شود
        /// تا با فیلتر «نوع تعرفه» مأموران دقیقاً یکی باشد؛ در غیر این صورت از نام ثابت استفاده می‌شود.
        /// </summary>
        private Dictionary<char, string> GetTariffTitleByFirstDigit()
        {
            var titles = db.tbOrderTariffType
                .Where(x => x.IsActive)
                .ToList()
                .Select(x => NormalizeAllocationKey(x.Title))
                .Where(t => t != "")
                .ToList();

            Func<string, string[], string> pick = (fallback, keywords) =>
            {
                var exact = titles.FirstOrDefault(t => t == fallback);

                if (exact != null)
                    return exact;

                foreach (var k in keywords)
                {
                    var found = titles.FirstOrDefault(t => t.Contains(k));

                    if (found != null)
                        return found;
                }

                return fallback;
            };

            return new Dictionary<char, string>
            {
                { '1', pick("خانگی", new[] { "خانگی" }) },
                { '2', pick("عمومی", new[] { "عمومی" }) },
                { '3', pick("کشاورزی", new[] { "کشاورزی" }) },
                { '4', pick("صنعتی", new[] { "صنعتی" }) },
                { '5', pick("سایر مصارف (تجاری)", new[] { "تجاری", "سایر" }) },
                { '9', pick("روشنایی معابر", new[] { "روشنایی" }) }
            };
        }


        /// <summary>
        /// برای پرونده‌هایی که nametarefeh خالی دارند، تعرفه را از جدول کمکی تکمیل می‌کند.
        /// پرونده‌ها با AsNoTracking خوانده شده‌اند، پس تغییر فقط در حافظه است و در دیتابیس منبع ذخیره نمی‌شود؛
        /// ولی چون Source همین موجودیت است، در تخصیص ، فیلتر تعرفه ، شیت‌های Excel و ذخیره‌ی Result3 اعمال می‌شود.
        /// خروجی: تعداد پرونده‌هایی که تعرفه‌شان تکمیل شد.
        /// </summary>
        private int FillMissingTariffs(List<ValuunicPriorityResult> rows)
        {
            if (rows == null || rows.Count == 0)
                return 0;

            var empty = rows
                .Where(x => string.IsNullOrWhiteSpace(Convert.ToString(x.nametarefeh)))
                .ToList();

            if (empty.Count == 0)
                return 0;

            var digitToTitle = GetTariffTitleByFirstDigit();

            // کلید نرمال‌شده ← رقم اول valueFK_db2
            var firstDigitByKey = new Dictionary<string, char>();

            // فقط مقدارهای عددی (جلوگیری از SQL Injection چون داخل رشته‌ی SQL گذاشته می‌شوند)
            var rawKeys = empty
                .Select(x => (Convert.ToString(x.valuunic) ?? "").Trim())
                .Where(v => v.Length > 0 && v.All(char.IsDigit))
                .Distinct()
                .ToList();

            for (int i = 0; i < rawKeys.Count; i += TARIFF_LOOKUP_CHUNK)
            {
                var chunk = rawKeys.Skip(i).Take(TARIFF_LOOKUP_CHUNK).ToList();

                var inList = string.Join(",", chunk.Select(k => "'" + k + "'"));

                var sql =
                    "SELECT CAST(valuunic AS nvarchar(100)) AS K, CAST(valueFK_db2 AS nvarchar(100)) AS V "
                    + "FROM " + TARIFF_LOOKUP_TABLE + " "
                    + "WHERE FK_db2 = " + TARIFF_LOOKUP_FK_DB2 + " "
                    + "AND valuunic IN (" + inList + ")";

                var lookup = db.Database.SqlQuery<TariffLookupRow>(sql).ToList();

                foreach (var row in lookup)
                {
                    var key = NormalizeAllocationIdKey(row.K);

                    if (firstDigitByKey.ContainsKey(key))
                        continue;

                    var digits = EshterakDigits(row.V);          // فقط ارقام valueFK_db2

                    if (digits.Length == 0 || !digitToTitle.ContainsKey(digits[0]))
                        continue;

                    firstDigitByKey[key] = digits[0];
                }
            }

            int filled = 0;

            foreach (var x in empty)
            {
                char first;

                if (firstDigitByKey.TryGetValue(NormalizeAllocationIdKey(x.valuunic), out first))
                {
                    x.nametarefeh = digitToTitle[first];
                    filled++;
                }
            }

            return filled;
        }


        private class SmartAllocationContext
        {
            public string SourceCity;
            public string TargetCity;
            public long CityValue;
            public decimal SpecialThreshold;

            /// <summary>فقط مناطق انتخاب‌شده در همین اجرای تخصیص</summary>
            public HashSet<int> SelectedRegions = new HashSet<int>();

            public AllocationResult Result;

            /// <summary>استاندارد روزانه‌ی شهرستان (تعداد مأموران ، مانده روزانه ، ظرفیت)</summary>
            public CityStandard Standard;

            /// <summary>تنظیمات مؤثر این اجرا (اندازه‌ی برش ، آستانه‌ی کامل ، اضافه‌بار)</summary>
            public AllocationSettings Settings;

            public List<ValuunicPriorityResult> SpecialRecords;

            /// <summary>پرونده‌هایی که قبل از تخصیص حذف شدند (تکراری لیست قبلی ، سخت وصول ، ...) همراه علت</summary>
            public List<AllocationRecord> RemovedRecords = new List<AllocationRecord>();

            public int SourceCount;
            public int ExcludedCount;
            public int BalabariCount;

            /// <summary>پرونده‌های سخت وصول که کنار گذاشته شدند</summary>
            public int HardCount;

            /// <summary>پرونده‌هایی که به‌علت حضور در لیست «تاریخ قبلی» حذف شدند</summary>
            public int PreviousCount;

            /// <summary>پرونده‌هایی که تعرفه‌ی خالی‌شان از جدول dbergharelimsertfk1_7_1218 تکمیل شد</summary>
            public int TariffFilledCount;

            public int DateYear;
            public int DateMonth;
            public int DateDay;
        }


        /* ---------- جدول شهرستان / منطقه / کد ---------- */

        private static readonly string[] ALLOCATION_COUNTIES =
        {
            "اسپکه", "ایرانشهر", "بمپور", "جالق", "چابهار", "خاش", "دلگان", "راسک",
            "زابل", "زاهدان 1", "زاهدان 2", "زهک", "سراوان", "سرباز", "سوران", "فنوج",
            "قصرقند", "کنارک", "کورین", "مهرستان", "میرجاوه", "نگور", "نیکشهر", "هیرمند"
        };

        private static readonly string[][] ALLOCATION_COUNTY_REGIONS =
        {
            new[] { "اسپکه", "لاشار", "65" },

            new[] { "ایرانشهر", "ايرانشهر", "50" },
            new[] { "ایرانشهر", "مركزي ایرانشهر", "51" },
            new[] { "ایرانشهر", "بزمان", "53" },
            new[] { "ایرانشهر", "دامن ايرانشهر", "54" },

            new[] { "بمپور", "محمدان", "52" },
            new[] { "بمپور", "بمپور", "58" },
            new[] { "بمپور", "حومه بمپور", "59" },

            new[] { "جالق", "جالق", "43" },
            new[] { "جالق", "بم پشت", "46" },

            new[] { "چابهار", "چابهار", "70" },
            new[] { "چابهار", "گلشهروزيباشهر", "71" },
            new[] { "چابهار", "مرکزي چابهار", "73" },
            new[] { "چابهار", "پسابندر", "75" },

            new[] { "خاش", "خاش", "30" },
            new[] { "خاش", "نوك آباد", "31" },
            new[] { "خاش", "ايرندگان", "32" },
            new[] { "خاش", "پشتكوه-سنگان", "33" },

            new[] { "دلگان", "دلگان", "55" },
            new[] { "دلگان", "جلگه چاه هاشم", "56" },
            new[] { "دلگان", "گلمورتي", "57" },

            new[] { "راسک", "راسک", "90" },
            new[] { "راسک", "پارود", "92" },
            new[] { "راسک", "پيشین", "93" },

            new[] { "زابل", "زابل", "20" },
            new[] { "زابل", "نيمروز", "23" },
            new[] { "زابل", "هامون", "24" },

            new[] { "زاهدان 1", "زاهدان1", "10" },
            new[] { "زاهدان 1", "نصرت آباد", "16" },

            new[] { "زاهدان 2", "زاهدان2", "14" },
            new[] { "زاهدان 2", "حومه زاهدان", "15" },

            new[] { "زهک", "شهرستان زهک", "26" },

            new[] { "سراوان", "سراوان", "40" },
            new[] { "سراوان", "دزک", "41" },
            new[] { "سراوان", "کوهک", "42" },
            new[] { "سراوان", "گشت", "45" },
            new[] { "سراوان", "ناهوک", "44" },

            new[] { "سرباز", "سرباز", "91" },

            new[] { "سوران", "هيدوچ", "47" },
            new[] { "سوران", "سوران", "48" },

            new[] { "فنوج", "فنوج", "64" },

            new[] { "قصرقند", "ساربوك", "60" },
            new[] { "قصرقند", "قصرقند", "61" },

            new[] { "کنارک", "كنارك", "80" },
            new[] { "کنارک", "كهير", "81" },
            new[] { "کنارک", "زرآباد", "82" },

            new[] { "کورین", "کورين و سرجنگل", "17" },

            new[] { "مهرستان", "مهرستان", "49" },

            new[] { "میرجاوه", "ميرجاوه", "18" },
            new[] { "میرجاوه", "لاديز و ريگ ملك", "19" },

            new[] { "نگور", "نگورودشتیاری", "76" },
            new[] { "نگور", "پلان", "77" },
            new[] { "نگور", "مرکزی نگور", "78" },
            new[] { "نگور", "تلنگ", "79" },

            new[] { "نیکشهر", "نيكشهر", "62" },
            new[] { "نیکشهر", "بنت", "63" },
            new[] { "نیکشهر", "مهبان و هيچان", "66" },
            new[] { "نیکشهر", "چاهان", "67" }
        };

        private static readonly string[] JONOBI_ALLOCATION_COUNTIES =
        {
            // دقیقاً مطابق مقدار ستون city در دیتابیس جنوبی
            "بيرجند", "خوسف", "درميان", "زيرکوه", "سرايان",
            "سربيشه", "طبس", "فردوس", "قائنات", "نهبندان"
        };

        private static readonly string[][] JONOBI_ALLOCATION_COUNTY_REGIONS =
        {
            new[] { "بيرجند",   "بيرجند",          "68" },
            new[] { "خوسف",     "خوسف",            "46" },
            new[] { "درميان",   "درميان",          "47" },
            new[] { "زيرکوه",   "زيرکوه",          "48" },
            new[] { "زيرکوه",   "زهان",            "51" },
            new[] { "سرايان",   "سرايان",          "76" },
            new[] { "سربيشه",   "سربيشه",          "91" },
            new[] { "سربيشه",   "مود",              "93" },
            new[] { "طبس",      "طبس",             "71" },
            new[] { "طبس",      "دیهوک",            "73" },
            new[] { "فردوس",    "فردوس",           "72" },
            new[] { "قائنات",   "قائن",             "49" },
            new[] { "قائنات",   "سده (آرین شهر)",   "50" },
            new[] { "قائنات",   "نیمبلوک (خضری)",   "70" },
            new[] { "نهبندان",  "نهبندان",          "75" }
        };

        private string[][] GetActiveAllocationCountyRegions()
        {
            return IsJonobiContract()
                ? JONOBI_ALLOCATION_COUNTY_REGIONS
                : ALLOCATION_COUNTY_REGIONS;
        }


        private List<string> GetAllocationCounties()
        {
            return (IsJonobiContract()
                    ? JONOBI_ALLOCATION_COUNTIES
                    : ALLOCATION_COUNTIES)
                .ToList();
        }


        private List<KeyValuePair<int, string>> GetAllocationRegionsForCity(string city)
        {
            var normalizedCity = NormalizeOrderExcelText(city);

            var result =
                GetActiveAllocationCountyRegions()
                .Where(x => NormalizeOrderExcelText(x[0]) == normalizedCity)
                .Select(x => new KeyValuePair<int, string>(
                    int.Parse(x[2], CultureInfo.InvariantCulture),
                    NormalizeOrderExcelText(x[1])))
                .GroupBy(x => x.Key)
                .Select(g => g.First())
                .OrderBy(x => x.Key)
                .ToList();

            if (result.Count > 0)
                return result;

            // شهرستان بدون ردیف در جدول (مثلاً هیرمند): مناطق موجود در خود داده
            var cityAliases = GetAllocationCityQueryAliases(city);

            return db.ValuunicPriorityResult
                .AsNoTracking()
                .Where(x => cityAliases.Contains(x.city))
                .Select(x => x.item4)
                .Distinct()
                .ToList()
                .Select(x => ParseAllocationInt(x))
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x.Value)
                .Distinct()
                .OrderBy(x => x)
                .Select(x => new KeyValuePair<int, string>(x, ""))
                .ToList();
        }


        private List<int> GetCountyCityIds(ICollection<int> regionCodes)
        {
            if (regionCodes == null || regionCodes.Count == 0)
                return new List<int>();

            var links = db.tblink_mantegeh_city
                .Where(x => x.vaz == 1 || x.vaz == null)
                .ToList();

            var result = new HashSet<int>();

            foreach (var item in links)
            {
                if (item.FK_city == null)
                    continue;

                var codes = new HashSet<int>();

                AddLinkRegions(codes, item.mantaghe, item.mantaghe1, item.mantaghe2, item.mantaghe3, item.mantaghe4, item.mantaghe5, item.mantaghe6, item.mantaghe7, item.mantaghe8, item.mantaghe9, item.mantaghe10);

                if (codes.Any(regionCodes.Contains))
                    result.Add(Convert.ToInt32(item.FK_city));
            }

            return result.ToList();
        }


        /* ---------- شغل کاربر (tbUsers.usr_typeshoghl → [User].[tbjob]) ---------- */

        private const string JOB_TITLE_COLUMN = null;


        private Dictionary<int, string> LoadJobTitles()
        {
            var result = new Dictionary<int, string>();

            var connection = db.Database.Connection;
            bool openedHere = false;

            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                    openedHere = true;
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM [User].[tbjob]";

                    var currentTransaction = db.Database.CurrentTransaction;

                    if (currentTransaction != null)
                        command.Transaction = currentTransaction.UnderlyingTransaction;

                    using (var reader = command.ExecuteReader())
                    {
                        int idIndex = -1;
                        int titleIndex = -1;

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);

                            if (idIndex < 0 && string.Equals(name, "ID", StringComparison.OrdinalIgnoreCase))
                                idIndex = i;

                            if (JOB_TITLE_COLUMN != null)
                            {
                                if (string.Equals(name, JOB_TITLE_COLUMN, StringComparison.OrdinalIgnoreCase))
                                    titleIndex = i;
                            }
                            else if (titleIndex < 0 && reader.GetFieldType(i) == typeof(string))
                            {
                                titleIndex = i;
                            }
                        }

                        if (idIndex < 0)
                            idIndex = 0;

                        if (titleIndex < 0)
                            return result;

                        while (reader.Read())
                        {
                            if (reader.IsDBNull(idIndex))
                                continue;

                            int id = Convert.ToInt32(reader.GetValue(idIndex));

                            string title = reader.IsDBNull(titleIndex)
                                ? ""
                                : NormalizeOrderExcelText(Convert.ToString(reader.GetValue(titleIndex)));

                            result[id] = title;
                        }
                    }
                }
            }
            catch
            {
                // اگر جدول در دسترس نبود، ستون شغل فقط با کد شغل پر می‌شود
            }
            finally
            {
                if (openedHere)
                    connection.Close();
            }

            return result;
        }


        private string GetAllocationJobTitle(int? jobId, Dictionary<int, string> jobTitles)
        {
            if (!jobId.HasValue)
                return "";

            string title;

            if (jobTitles != null
                && jobTitles.TryGetValue(jobId.Value, out title)
                && !string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            return jobId.Value.ToString();
        }


        /* =========================================================
           مأموران تخصیص برای یک شهر مقصد

           مأموری وارد تخصیص می‌شود که:
             ۱. در آخرین «تعیین نقش» فعال (تب ۱) حداقل یک نقش داشته باشد
             ۲. فعال باشد
             ۳. اگر در تب ۲ منطقه صریح دارد، حداقل یکی از همان مناطق با محدوده اجرا هم‌پوشانی داشته باشد.
             ۴. در سیستان، اگر تب ۲ منطقه ندارد ولی در tbzarfiat_usr منطقه دارد، همان مناطق اعمال شوند.
             ۵. اگر هیچ‌کدام منطقه ندارند، شهر محل خدمت او دقیقاً با شهر مقصد انتخاب‌شده یکی باشد؛
                در این حالت «همه» فقط مناطق همان شهر از tblink_mantegeh_city است.

           محدودیت‌ها از تب ۲ خوانده می‌شود: منطقه / گروه‌ها (روزکار / تعرفه / محدوده).
           مأمور بدون منطقه در تب ۲ و tbzarfiat_usr = همه مناطق شهر خودش، نه همه مناطق شهرستان مادر.
           ========================================================= */

        private List<AllocationAgent> LoadAllocationAgents(
            ICollection<int> countyRegionCodes,
            long cityValue,
            string selectedCityName,
            out string error)
        {
            error = null;

            countyRegionCodes = countyRegionCodes ?? new List<int>();

            var normalizedSelectedCity = NormalizeOrderExcelText(selectedCityName);

            // تطبیق نام شهر به‌صورت دقیق بعد از یکسان‌سازی حروف/فاصله‌ها؛
            // بنابراین «پلان» و «نگور» دو مقصد مستقل باقی می‌مانند.
            var selectedCityIds = new HashSet<int>(
                db.tbCities
                    .AsNoTracking()
                    .Select(x => new { x.ID, x.Name })
                    .ToList()
                    .Where(x => NormalizeOrderExcelText(x.Name) == normalizedSelectedCity)
                    .Select(x => Convert.ToInt32(x.ID)));

            var selectedCityRegionCodes = new HashSet<int>();

            foreach (var selectedCityId in selectedCityIds)
            {
                foreach (var regionCode in GetAllowedRegions(selectedCityId))
                {
                    if (countyRegionCodes.Contains(regionCode))
                        selectedCityRegionCodes.Add(regionCode);
                }
            }

            var jobTitles = LoadJobTitles();

            var activeAssignmentId = GetActiveRoleAssignmentId();

            if (!activeAssignmentId.HasValue)
            {
                error = "هنوز تعیین نقش (تب ۱) ذخیره نشده است.";
                return null;
            }

            var activityKinds =
                db.tbOrderActivityType
                .Where(x => x.IsActive)
                .ToList()
                .ToDictionary(x => x.ID, x => ResolveAllocationRole(x.Title));

            var roleRows =
                db.tbOrderAssignmentActivity
                .Where(x => x.AssignmentID == activeAssignmentId.Value)
                .Select(x => new { x.UserID, x.ActivityTypeID })
                .ToList();

            var roleUserIds = roleRows.Select(x => x.UserID).Distinct().ToList();

            var roleUsers =
                db.tbUsers
                .Where(x => roleUserIds.Contains(x.usr_ID) && x.usr_IsActive == true)
                .ToList();

            var roleActiveUserIds = roleUsers.Select(x => x.usr_ID).ToList();

            /*
               منبع قدیمی منطقه نیرو در سیستان:
               اگر تب ۲ برای نیرو منطقه صریح نداشته باشد، قبل از fallback شهر محل خدمت
               مناطق mantagheh1..6 و mantagheh8..9 از [User].[tbzarfiat_usr] بررسی می‌شوند.
               طبق ویوی فعلی، mantagheh7 وضعیت فعال رکورد است (۱ = فعال) و کد منطقه نیست.
               این پرس‌وجو برای جنوبی اجرا نمی‌شود.
            */
            var zarfiatRegionsByUser = new Dictionary<int, HashSet<int>>();

            if (!IsJonobiContract())
            {
                var zarfiatRows = db.tbzarfiat_usr
                    .AsNoTracking()
                    .Where(x =>
                        x.FK_usr.HasValue
                        && x.mantagheh7 == 1
                        && roleActiveUserIds.Contains(x.FK_usr.Value))
                    .Select(x => new
                    {
                        UserId = x.FK_usr.Value,
                        x.mantagheh1,
                        x.mantagheh2,
                        x.mantagheh3,
                        x.mantagheh4,
                        x.mantagheh5,
                        x.mantagheh6,
                        x.mantagheh8,
                        x.mantagheh9
                    })
                    .ToList();

                foreach (var row in zarfiatRows)
                {
                    HashSet<int> userRegions;

                    if (!zarfiatRegionsByUser.TryGetValue(row.UserId, out userRegions))
                    {
                        userRegions = new HashSet<int>();
                        zarfiatRegionsByUser[row.UserId] = userRegions;
                    }

                    AddRegionValue(userRegions, row.mantagheh1);
                    AddRegionValue(userRegions, row.mantagheh2);
                    AddRegionValue(userRegions, row.mantagheh3);
                    AddRegionValue(userRegions, row.mantagheh4);
                    AddRegionValue(userRegions, row.mantagheh5);
                    AddRegionValue(userRegions, row.mantagheh6);
                    AddRegionValue(userRegions, row.mantagheh8);
                    AddRegionValue(userRegions, row.mantagheh9);
                }
            }

            /* ---------- محدوده تب ۲ (آخرین Scope فعال هر مأمور) ---------- */

            var scopes =
                db.tbOrderAgentScope
                .Where(x => x.IsActive == true && roleActiveUserIds.Contains(x.UserID))
                .ToList()
                .GroupBy(x => x.UserID)
                .Select(g => g.OrderByDescending(x => x.ID).First())
                .ToList();

            var scopeIds = scopes.Select(x => x.ID).ToList();

            var scopeRegions =
                db.tbOrderAgentScopeRegion
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode })
                .ToList();

            var scopeDays =
                db.tbOrderAgentScopeWorkDay
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode, x.GroupNo, x.WorkDay })
                .ToList();

            var scopeTariffs =
                db.tbOrderAgentScopeTariff
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode, x.GroupNo, x.TariffTypeID })
                .ToList();

            var scopeAreas =
                db.tbOrderAgentScopeArea
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode, x.GroupNo, x.AreaCode })
                .ToList();

            var tariffTitles =
                db.tbOrderTariffType
                .ToList()
                .ToDictionary(x => x.ID, x => NormalizeAllocationKey(x.Title));

            // ساخت قانون از لیست روزکار / تعرفه / محدوده (خالی = همه)
            Func<List<int>, List<int>, List<int>, AgentRegionRule> buildRule = (days, tariffIds, areas) =>
            {
                var rule = new AgentRegionRule();

                if (days.Count > 0)
                    rule.Days = new HashSet<int>(days);

                if (tariffIds.Count > 0)
                {
                    var titles =
                        new HashSet<string>(
                            tariffIds
                            .Select(id => tariffTitles.ContainsKey(id) ? tariffTitles[id] : id.ToString())
                            .Where(t => t != ""),
                            StringComparer.OrdinalIgnoreCase);

                    // همه‌ی تعرفه‌ها انتخاب شده = بدون محدودیت تعرفه ؛ پرونده‌های با تعرفه‌ی خالی در دیتابیس
                    // فقط به همین مأموران می‌رسد (مأمور دارای تعرفه‌ی مشخص آن‌ها را نمی‌گیرد)
                    var allTitles = new HashSet<string>(tariffTitles.Values.Where(t => t != ""), StringComparer.OrdinalIgnoreCase);

                    if (titles.Count > 0 && !(allTitles.Count > 0 && allTitles.IsSubsetOf(titles)))
                        rule.Tariffs = titles;
                }

                var distinctAreas = areas.Distinct().ToList();

                // انتخاب کامل بازه مجاز = بدون محدودیت
                if (distinctAreas.Count > 0 && distinctAreas.Count < GetAreaTotal())
                    rule.Areas = new HashSet<int>(distinctAreas);

                return rule;
            };

            var agents = new List<AllocationAgent>();

            foreach (var user in roleUsers)
            {
                var roles =
                    new HashSet<AllocationRoleKind>(
                        roleRows
                        .Where(x => x.UserID == user.usr_ID)
                        .Select(x =>
                            activityKinds.ContainsKey(x.ActivityTypeID)
                            ? activityKinds[x.ActivityTypeID]
                            : AllocationRoleKind.None)
                        .Where(x => x != AllocationRoleKind.None));

                if (roles.Count == 0)
                    continue;

                var scope = scopes.FirstOrDefault(x => x.UserID == user.usr_ID);

                HashSet<int> regions = null;                       // null = همه مناطق
                var defaultRule = new AgentRegionRule();           // قانون عمومی (RegionCode = NULL)
                var rules = new Dictionary<int, List<AgentRegionRule>>();

                if (scope != null)
                {
                    var myRegions = scopeRegions.Where(x => x.ScopeID == scope.ID).Select(x => x.RegionCode).Distinct().ToList();
                    var myDays = scopeDays.Where(x => x.ScopeID == scope.ID).ToList();
                    var myTariffs = scopeTariffs.Where(x => x.ScopeID == scope.ID).ToList();
                    var myAreas = scopeAreas.Where(x => x.ScopeID == scope.ID).ToList();

                    var generalDays = myDays.Where(x => !x.RegionCode.HasValue).Select(x => x.WorkDay).Distinct().ToList();
                    var generalTariffIds = myTariffs.Where(x => !x.RegionCode.HasValue).Select(x => x.TariffTypeID).Distinct().ToList();
                    var generalAreas = myAreas.Where(x => !x.RegionCode.HasValue).Select(x => x.AreaCode).Distinct().ToList();

                    defaultRule = buildRule(generalDays, generalTariffIds, generalAreas);

                    if (myRegions.Count > 0)
                    {
                        regions = new HashSet<int>(myRegions);

                        foreach (var region in myRegions)
                        {
                            // هر منطقه چند گروه دارد؛ هر گروه یک قانون مستقل (روزکار + تعرفه + محدوده)
                            var groupNos = myDays.Where(x => x.RegionCode == region).Select(x => x.GroupNo)
                                .Concat(myTariffs.Where(x => x.RegionCode == region).Select(x => x.GroupNo))
                                .Concat(myAreas.Where(x => x.RegionCode == region).Select(x => x.GroupNo))
                                .Distinct()
                                .OrderBy(x => x)
                                .ToList();

                            // منطقه بدون هیچ ردیف = یک گروه بدون محدودیت (یا همان قانون عمومی قدیمی)
                            if (groupNos.Count == 0)
                                groupNos.Add(1);

                            var regionRules = new List<AgentRegionRule>();

                            foreach (var g in groupNos)
                            {
                                var d = myDays.Where(x => x.RegionCode == region && x.GroupNo == g).Select(x => x.WorkDay).Distinct().ToList();
                                if (d.Count == 0) d = generalDays;

                                var t = myTariffs.Where(x => x.RegionCode == region && x.GroupNo == g).Select(x => x.TariffTypeID).Distinct().ToList();
                                if (t.Count == 0) t = generalTariffIds;

                                var a = myAreas.Where(x => x.RegionCode == region && x.GroupNo == g).Select(x => x.AreaCode).Distinct().ToList();
                                if (a.Count == 0) a = generalAreas;

                                regionRules.Add(buildRule(d, t, a));
                            }

                            rules[region] = regionRules;
                        }
                    }
                }

                /* ---------- تعلق مأمور به شهر مقصد ---------- */

                bool exactCityMatch =
                    user.usr_City_Dutysystem.HasValue
                    && selectedCityIds.Contains(user.usr_City_Dutysystem.Value);

                bool hasExplicitRegionScope = regions != null && regions.Count > 0;

                HashSet<int> zarfiatRegions;
                bool hasZarfiatRegion =
                    zarfiatRegionsByUser.TryGetValue(user.usr_ID, out zarfiatRegions)
                    && zarfiatRegions != null
                    && zarfiatRegions.Count > 0;

                bool regionMatch =
                    hasExplicitRegionScope
                    && regions.Any(countyRegionCodes.Contains);

                if (hasExplicitRegionScope)
                {
                    // تنظیم صریح تب دوم مقدم است.
                    if (!regionMatch)
                        continue;
                }
                else if (hasZarfiatRegion)
                {
                    // اولویت دوم در سیستان: منطقه‌های قدیمی ثبت‌شده برای خود نیرو.
                    regions = new HashSet<int>(zarfiatRegions.Where(countyRegionCodes.Contains));

                    if (regions.Count == 0)
                        continue;

                    // اگر تب ۲ فقط محدودیت عمومی روزکار/تعرفه/محدوده داشته باشد، حفظ می‌شود.
                    foreach (var region in regions)
                    {
                        rules[region] = new List<AgentRegionRule> { defaultRule };
                    }
                }
                else
                {
                    // «همه مناطق» فقط برای شهر واقعی خود مأمور است.
                    if (!exactCityMatch)
                        continue;

                    // اگر ارتباط شهر/منطقه تعریف نشده، به‌هیچ‌وجه آن را به همه مناطق شهرستان
                    // گسترش نده؛ این همان حالتی بود که پلان و نگور را با هم مخلوط می‌کرد.
                    if (selectedCityRegionCodes.Count == 0)
                        continue;

                    regions = new HashSet<int>(selectedCityRegionCodes);

                    // محدودیت عمومی روزکار/تعرفه/محدوده را برای هر منطقه شهر حفظ کن.
                    foreach (var region in regions)
                    {
                        rules[region] = new List<AgentRegionRule> { defaultRule };
                    }
                }

                agents.Add(
                    new AllocationAgent
                    {
                        UserId = user.usr_ID,
                        FullName = ((user.usr_Name ?? "") + " " + (user.usr_Family ?? "")).Trim(),
                        NationalCode = user.usr_NationalCode,
                        // کد پرسنلی در بعضی رکوردهای قدیمی خالی است؛
                        // خروجی کل روز نباید به خاطر یک مقدار null متوقف شود.
                        PersonalId = user.usr_Personal_ID.HasValue
                            ? user.usr_Personal_ID.Value
                            : 0,
                        JobId = user.usr_typeshoghl,
                        JobTitle = GetAllocationJobTitle(user.usr_typeshoghl, jobTitles),
                        Roles = roles,
                        Regions = regions,
                        DefaultRule = defaultRule,
                        RegionRules = rules,
                        Capacity = roles.Max(r => GetSmartAllocationCapacity(r, cityValue))
                    });
            }

            return agents;
        }


        /* =========================================================
           TAB 4 - مناطق و روزکارهای قابل انتخاب
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GetAllocationFilterData(string sourceCity, List<int> RegionIds)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (!currentUserId.HasValue)
                    return Json(new { success = false, message = "دسترسی غیر مجاز." });

                sourceCity = (sourceCity ?? "").Trim();

                if (string.IsNullOrWhiteSpace(sourceCity))
                    return Json(new { success = false, message = "شهرستان انتخاب نشده است." });

                string cityAccessError;
                if (!EnforceJonobiUserAllocationCity(currentUserId.Value, ref sourceCity, out cityAccessError))
                    return Json(new { success = false, message = cityAccessError });

                var countyRegions = GetAllocationRegionsForCity(sourceCity);

                if (countyRegions.Count == 0)
                    return Json(new { success = false, message = "برای شهرستان «" + sourceCity + "» منطقه‌ای تعریف نشده است." });

                var countyRegionSet = new HashSet<int>(countyRegions.Select(x => x.Key));

                var selectedRegions =
                    new HashSet<int>(
                        (RegionIds ?? new List<int>())
                        .Where(x => countyRegionSet.Contains(x)));

                if (selectedRegions.Count == 0)
                    selectedRegions = new HashSet<int>(countyRegionSet);

                long cityValue = GetCityValue(sourceCity);

                string agentError;

                var agents =
                    LoadAllocationAgents(selectedRegions, cityValue, sourceCity, out agentError)
                    ?? new List<AllocationAgent>();

                var cityAliases = GetAllocationCityQueryAliases(sourceCity);

                var rows =
                    db.ValuunicPriorityResult
                    .AsNoTracking()
                    .Where(x => cityAliases.Contains(x.city))
                    .Select(x => new { x.item4, x.eshterak, x.roz, x.P5 })
                    .ToList()
                    .Select(x => new
                    {
                        Region = ParseAllocationInt(x.item4),
                        Day = GetAllocationWorkDay(x.eshterak, x.roz),
                        Amount = x.P5
                    })
                    .Where(x =>
                        x.Region.HasValue
                        && countyRegionSet.Contains(x.Region.Value)
                        && x.Amount > 0)
                    .ToList();

                var regions =
                    countyRegions
                    .Select(region => new
                    {
                        Code = region.Key,
                        Name = region.Value,
                        Count = rows.Count(x => x.Region.Value == region.Key),
                        Amount = rows.Where(x => x.Region.Value == region.Key).Sum(x => x.Amount),
                        Agents = agents.Count(a => a.AllowsRegion(region.Key))
                    })
                    .ToList();

                var selectedRows =
                    rows
                    .Where(x => selectedRegions.Contains(x.Region.Value))
                    .ToList();

                var workDays =
                    selectedRows
                    .Where(x => x.Day.HasValue)
                    .GroupBy(x => x.Day.Value)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        Day = g.Key,
                        Count = g.Count(),
                        Amount = g.Sum(x => x.Amount),
                        Regions = g.Select(x => x.Region.Value).Distinct().OrderBy(x => x).ToList(),
                        Agents = agents.Count(a =>
                            g.Any(x => a.AllowsRegionDay(x.Region.Value, g.Key)))
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        sourceCity = sourceCity,
                        cityValue = cityValue,
                        agentCount = agents.Count,
                        agentMessage = agentError,
                        selectedRegions = selectedRegions.OrderBy(x => x).ToList(),
                        regions = regions,
                        workDays = workDays,
                        noWorkDayCount = selectedRows.Count(x => !x.Day.HasValue)
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در دریافت مناطق و روزکارها: " + ex.Message });
            }
        }


        /* =========================================================
           تاریخ شمسی: «1405/07/12» یا «1405-7-12» یا ارقام فارسی
           ========================================================= */

        private bool TryParseAllocationDate(string text, out int year, out int month, out int day)
        {
            year = month = day = 0;

            var parts =
                NormalizeOrderExcelDigits(text ?? "")
                .Trim()
                .Split(new[] { '/', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 3)
                return false;

            return
                int.TryParse(parts[0], out year)
                && int.TryParse(parts[1], out month)
                && int.TryParse(parts[2], out day)
                && year >= 1300 && year <= 1600
                && month >= 1 && month <= 12
                && day >= 1 && day <= 31;
        }


        /// <summary>مقدار عددی تاریخ را هم‌نوعِ ستون جدول می‌سازد (int / int? / string با صفر ابتدایی)</summary>
        private static T DatePart<T>(T sample, int value)
        {
            var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (type == typeof(string))
            {
                var length = Math.Max(1, (sample as string ?? "").Length);

                return (T)(object)value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0');
            }

            return (T)Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }


        /// <summary>
        /// شناسه قبض‌های (valuunic) ثبت‌شده در ValuunicPriorityResult3 برای یک تاریخ (همه شهرستان‌ها).
        /// این پرونده‌ها در لیست «تاریخ جدید» نمی‌آیند.
        /// </summary>
        private HashSet<string> LoadPreviousListValuunics(int year, int month, int day)
        {
            var result = new HashSet<string>();

            var sample = db.ValuunicPriorityResult3.AsNoTracking().FirstOrDefault();

            if (sample == null)
                return result;

            var y = DatePart(sample.yaer, year);
            var m = DatePart(sample.month, month);
            var dd = DatePart(sample.day, day);

            var list =
                db.ValuunicPriorityResult3
                .AsNoTracking()
                .Where(s => s.yaer == y && s.month == m && s.day == dd)
                .Select(s => s.valuunic)
                .ToList();

            foreach (var v in list)
                result.Add(NormalizeAllocationIdKey(v));

            return result;
        }


        /// <summary>
        /// تاریخ لیست قبلی (اختیاری). خروجی null و error=null → بدون حذف ؛ error پر → تاریخ نامعتبر.
        /// </summary>
        private HashSet<string> ResolvePreviousList(
            string excludeDateText,
            int curYear, int curMonth, int curDay,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(excludeDateText))
                return null;

            int py, pm, pd;

            if (!TryParseAllocationDate(excludeDateText, out py, out pm, out pd))
            {
                error = "تاریخ لیست قبلی را به‌صورت شمسی وارد کنید (مثال: 1405/07/11).";
                return null;
            }

            if (curYear > 0 && py == curYear && pm == curMonth && pd == curDay)
            {
                error = "تاریخ لیست قبلی نباید با تاریخ ثبت تخصیص یکی باشد.";
                return null;
            }

            return LoadPreviousListValuunics(py, pm, pd);
        }


        /* =========================================================
           CORE: خواندن داده‌ها + ساخت مأموران و پرونده‌ها + اجرای موتور

           singleUserId = null  : تخصیص گروهی
           singleUserId = مقدار : تخصیص تکی (فقط از پرونده‌های باقیمانده ، بدون پاک کردن قبلی‌ها)
           excludeDateText      : تاریخ لیست قبلی (اختیاری)؛ شناسه قبض‌های ثبت‌شده در آن تاریخ حذف می‌شوند
           ========================================================= */

        private SmartAllocationContext BuildSmartAllocation(
            string sourceCity,
            string targetCity,
            string specialThresholdText,
            List<int> regionIds,
            List<int> workDays,
            string dateText,
            string excludeDateText,
            decimal? cutPercent,
            decimal? completePercent,
            int? singleUserId,
            out string error,
            string allocationDaysText = null,
            string manualDailyStandardText = null)
        {
            error = null;

            int? selectedAllocationDays = null;
            if (IsJonobiContract())
            {
                int parsedDays;
                if (!int.TryParse(NormalizeOrderExcelDigits(allocationDaysText ?? "").Trim(),
                    NumberStyles.None, CultureInfo.InvariantCulture, out parsedDays) || parsedDays < 1)
                {
                    error = "تعداد روز لیست را به‌صورت عدد صحیح مثبت وارد کنید (مثلاً 7).";
                    return null;
                }
                selectedAllocationDays = parsedDays;
            }

            /* ---------------- تاریخ‌ها ---------------- */

            int dateYear, dateMonth, dateDay;

            if (!TryParseAllocationDate(dateText, out dateYear, out dateMonth, out dateDay))
            {
                error = "تاریخ را به‌صورت شمسی وارد کنید (مثال: 1405/07/12).";
                return null;
            }

            var previousSet = ResolvePreviousList(excludeDateText, dateYear, dateMonth, dateDay, out error);

            if (error != null)
                return null;

            /* ---------------- کاربر جاری ---------------- */

            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
            {
                error = "دسترسی غیر مجاز.";
                return null;
            }

            var currentUser = db.tbUsers.FirstOrDefault(x => x.usr_ID == currentUserId.Value);

            if (currentUser == null)
            {
                error = "کاربر پیدا نشد.";
                return null;
            }

            /* ---------------- ورودی‌ها ---------------- */

            sourceCity = (sourceCity ?? "").Trim();

            targetCity = string.IsNullOrWhiteSpace(targetCity) ? sourceCity : targetCity.Trim();

            if (string.IsNullOrWhiteSpace(sourceCity))
            {
                error = "شهر مبدأ انتخاب نشده است.";
                return null;
            }

            if (!EnforceJonobiUserAllocationCity(currentUserId.Value, ref sourceCity, out error))
                return null;
            if (IsJonobiContract()) targetCity = sourceCity;

            decimal specialThreshold = 0;

            decimal.TryParse(
                NormalizeOrderExcelDigits(specialThresholdText ?? "")
                    .Replace(",", "")
                    .Replace("٬", "")
                    .Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out specialThreshold);

            long cityValue = GetCityValue(sourceCity);

            if (cityValue <= 0)
            {
                error = "ارزش پایه شهر «" + sourceCity + "» تعریف نشده است.";
                return null;
            }

            /* ---------------- مناطق و روزکارهای انتخاب‌شده ---------------- */

            var allowedRegions =
                new HashSet<int>(GetAllocationRegionsForCity(sourceCity).Select(x => x.Key));

            var countyCityIds = GetCountyCityIds(allowedRegions);

            var selectedRegions =
                new HashSet<int>((regionIds ?? new List<int>()).Where(x => allowedRegions.Contains(x)));

            if (selectedRegions.Count == 0)
            {
                error = "حداقل یک منطقه انتخاب کنید.";
                return null;
            }

            var selectedWorkDays =
                new HashSet<int>((workDays ?? new List<int>()).Where(x => x >= 1 && x <= 99));

            if (selectedWorkDays.Count == 0)
            {
                error = "حداقل یک روزکار انتخاب کنید.";
                return null;
            }

            /* =====================================================
               مأموران (نقش تب ۱ + محدودیت تب ۲)
               ===================================================== */

            // فقط نیروهایی وارد استاندارد و تخصیص شوند که حداقل یکی از مناطق
            // مجازشان با مناطق انتخاب‌شده همین اجرا هم‌پوشانی دارد.
            var agents = LoadAllocationAgents(selectedRegions, cityValue, targetCity, out error);

            if (agents == null)
                return null;

            if (agents.Count == 0)
            {
                error = "هیچ مأمور فعالی با نقش معتبر (تب ۱) برای این شهرستان پیدا نشد.";
                return null;
            }

            // استاندارد روزانه از روی «همه‌ی» مأموران شهرستان (حتی در تخصیص تکی) محاسبه و ظرفیت‌ها جایگزین می‌شود
            var standard = ComputeCityStandard(sourceCity, agents, selectedAllocationDays);

            // Optional daily standard in rials; only the South contract accepts it.
            if (IsJonobiContract()
                && !string.IsNullOrWhiteSpace(manualDailyStandardText))
            {
                var text = NormalizeOrderExcelDigits(manualDailyStandardText)
                    .Replace(",", "")
                    .Replace("٬", "")
                    .Trim();

                decimal manualDailyStandard;
                if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out manualDailyStandard)
                    || manualDailyStandard <= 0)
                {
                    error = "استاندارد روزانه را به‌صورت عدد مثبت و به ریال وارد کنید.";
                    return null;
                }

                decimal days = selectedAllocationDays.Value;
                if (manualDailyStandard > decimal.MaxValue / days / 2m)
                {
                    error = "مبلغ استاندارد روزانه بیش از حد مجاز است.";
                    return null;
                }

                standard.NewDailyRial = manualDailyStandard;
                standard.UsesNew = true;
                standard.Capacity = manualDailyStandard * days;
                standard.OtherCapacity = standard.Capacity / 2m;
            }

            ApplyCityStandard(agents, standard);

            if (singleUserId.HasValue)
            {
                agents = agents.Where(a => a.UserId == singleUserId.Value).ToList();

                if (agents.Count == 0)
                {
                    error = "مأمور انتخاب‌شده شرایط این شهرستان را ندارد (نقش در تب ۱ لازم است).";
                    return null;
                }
            }

            /* =====================================================
               لیست‌های Excel (بالابری = ListType 1 ، سخت وصول = ListType 2)
               ===================================================== */

            var balabariSets = new Dictionary<int, HashSet<string>>();
            var hardSets = new Dictionary<int, HashSet<string>>();

            if (countyCityIds.Count > 0)
            {
                var batches =
                    db.tbOrderExcelImportBatch
                    .Where(x => x.UserID == currentUserId.Value && x.IsActive == true)
                    .ToList()
                    .Where(x => countyCityIds.Contains(Convert.ToInt32(x.CityID)))
                    .Select(x => new { x.ID, x.ComponentType, x.ListType })
                    .ToList();

                var batchIds = batches.Select(x => x.ID).ToList();

                var values =
                    db.tbOrderExcelImportValue
                    .Where(x => batchIds.Contains(x.BatchID))
                    .Select(x => new { x.BatchID, x.ValueText })
                    .ToList();

                foreach (var b in batches)
                {
                    var target = Convert.ToInt32(b.ListType) == 2 ? hardSets : balabariSets;

                    if (!target.ContainsKey(b.ComponentType))
                        target[b.ComponentType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var v in values.Where(x => x.BatchID == b.ID))
                        target[b.ComponentType].Add(NormalizeBalabariValue(b.ComponentType, v.ValueText));
                }
            }

            /* =====================================================
               پرونده‌ها
               ===================================================== */

            var cityAliases = GetAllocationCityQueryAliases(sourceCity);

            var sourceData =
                db.ValuunicPriorityResult
                .AsNoTracking()
                .Where(x => cityAliases.Contains(x.city))
                .ToList();

            if (sourceData.Count == 0)
            {
                error = "برای شهر «" + sourceCity + "» داده‌ای در ValuunicPriorityResult وجود ندارد.";
                return null;
            }

            var dateSample = sourceData[0];

            sourceData =
                sourceData
                .Where(x =>
                {
                    var region = ParseAllocationInt(x.item4);
                    var day = GetAllocationWorkDay(x.eshterak, x.roz);

                    return region.HasValue
                        && selectedRegions.Contains(region.Value)
                        && day.HasValue
                        && selectedWorkDays.Contains(day.Value);
                })
                .ToList();

            if (sourceData.Count == 0)
            {
                error = "در مناطق و روزکارهای انتخاب‌شده هیچ پرونده‌ای وجود ندارد.";
                return null;
            }

            /* ---------- تکمیل تعرفه‌ی خالی ----------
               اگر nametarefeh خالی بود ← valuunic در جدول dbergharelimsertfk1_7_1218 با FK_db2 = 187 ،
               و از رقم اول valueFK_db2 تعرفه تعیین می‌شود (1 خانگی ، 2 عمومی ، 3 کشاورزی ،
               4 صنعتی ، 5 سایر مصارف (تجاری) ، 9 روشنایی معابر).
               این کار قبل از تشخیص «ویژه» ، فیلتر تعرفه‌ی مأموران و ساخت Excel انجام می‌شود. */

            int tariffFilledCount = FillMissingTariffs(sourceData);

            var removedRecords = new List<AllocationRecord>();

            Action<IEnumerable<ValuunicPriorityResult>, string> markRemoved = (items, why) =>
            {
                foreach (var x in items)
                {
                    removedRecords.Add(new AllocationRecord
                    {
                        Id = Convert.ToInt64(x.ID),
                        Amount = x.P5,
                        Region = ParseAllocationInt(x.item4),
                        WorkDay = GetAllocationWorkDay(x.eshterak, x.roz),
                        Area = GetAllocationMahdodeh(x.eshterak, x.item2),
                        Eshterak = NormalizeAllocationKey(x.eshterak),
                        Tariff = NormalizeAllocationKey(x.nametarefeh),
                        IsDemand = IsAllocationDemand(x),
                        HasMobile = !string.IsNullOrWhiteSpace(Convert.ToString(x.shomaremobile)),
                        ExcludeReason = why,
                        Source = x
                    });
                }
            };

            var workData = sourceData.ToList();

            int excludedCount = sourceData.Count - workData.Count;

            /* ---------- لیست تاریخ قبلی: این پرونده‌ها در لیست جدید نمی‌آیند ---------- */

            int previousCount = 0;

            if (previousSet != null && previousSet.Count > 0)
            {
                var beforePrevious = workData.Count;

                markRemoved(
                    workData.Where(x => !IsAllocationDemand(x) && !IsAllocationSpecial(x, specialThreshold) && previousSet.Contains(Convert.ToString(x.valuunic))).ToList(),
                    "تکراری: در لیست تاریخ قبلی (" + excludeDateText + ") بوده و در لیست جدید قرار نمی‌گیرد.");

                workData =
                    workData
                    .Where(x => IsAllocationDemand(x) || IsAllocationSpecial(x, specialThreshold) || !previousSet.Contains(Convert.ToString(x.valuunic)))
                    .ToList();

                previousCount = beforePrevious - workData.Count;
            }

            /* ---------- تخصیص تکی: پرونده‌های قبلاً ثبت‌شده در همین تاریخ و شهر دوباره داده نمی‌شوند ---------- */

            if (singleUserId.HasValue)
            {
                var y = DatePart(dateSample.yaer, dateYear);
                var m = DatePart(dateSample.month, dateMonth);
                var dd = DatePart(dateSample.day, dateDay);

                var existingRows =
                    db.ValuunicPriorityResult3
                    .AsNoTracking()
                    .Where(s =>
                        s.month == m
                        && s.day == dd
                        && s.yaer == y
                        && s.city == targetCity)
                    .ToList();

                var alreadyAllocated =
                    new HashSet<string>(existingRows.Select(s => Convert.ToString(s.valuunic)));

                workData =
                    workData
                    .Where(x => !alreadyAllocated.Contains(Convert.ToString(x.valuunic)))
                    .ToList();
            }

            /* ---------- لیست سخت وصول: اصلاً وارد تخصیص نمی‌شود ---------- */

            int hardCount = 0;

            if (hardSets.Count > 0)
            {
                var beforeHard = workData.Count;

                markRemoved(
                    workData.Where(x => IsListRecord(x, GetAllocationWorkDay(x.eshterak, x.roz), hardSets)).ToList(),
                    "در لیست سخت وصول است و وارد تخصیص نمی‌شود.");

                workData =
                    workData
                    .Where(x => !IsListRecord(x, GetAllocationWorkDay(x.eshterak, x.roz), hardSets))
                    .ToList();

                hardCount = beforeHard - workData.Count;
            }

            var specialRecords =
                workData
                .Where(x => IsAllocationSpecial(x, specialThreshold))
                .OrderByDescending(x => x.P5)
                .ToList();

            var specialIds = new HashSet<ValuunicPriorityResult>(specialRecords);

            var records = new List<AllocationRecord>();

            int balabariCount = 0;

            foreach (var x in workData)
            {
                if (specialIds.Contains(x) || x.P5 <= 0)
                    continue;

                var workDay = GetAllocationWorkDay(x.eshterak, x.roz);

                bool isBalabari = IsBalabariRecord(x, workDay, balabariSets);

                if (isBalabari)
                    balabariCount++;

                records.Add(
                    new AllocationRecord
                    {
                        Id = Convert.ToInt64(x.ID),
                        Amount = x.P5,
                        Region = ParseAllocationInt(x.item4),
                        WorkDay = workDay,
                        Area = GetAllocationMahdodeh(x.eshterak, x.item2), // جنوبی: item2؛ سیستان: رقم ۶ و ۷ اشتراک
                        Eshterak = NormalizeAllocationKey(x.eshterak),
                        Tariff = NormalizeAllocationKey(x.nametarefeh),
                        IsDemand = IsAllocationDemand(x), // جنوبی: noeshtrak؛ سیستان: رقم اول اشتراک
                        HasMobile = !string.IsNullOrWhiteSpace(Convert.ToString(x.shomaremobile)),
                        IsBalabari = isBalabari,
                        Source = x
                    });
            }

            /* =====================================================
               اجرای موتور
               ===================================================== */

            var allocationSettings = GetSmartAllocationSettings(cutPercent, completePercent);

            var engine = new SmartAllocationEngine(allocationSettings, IsJonobiContract());

            var result = engine.Run(agents, records);

            return new SmartAllocationContext
            {
                SourceCity = sourceCity,
                TargetCity = targetCity,
                CityValue = cityValue,
                SpecialThreshold = specialThreshold,
                SelectedRegions = new HashSet<int>(selectedRegions),
                Result = result,
                Standard = standard,
                Settings = allocationSettings,
                SpecialRecords = singleUserId.HasValue ? new List<ValuunicPriorityResult>() : specialRecords,
                SourceCount = sourceData.Count,
                RemovedRecords = singleUserId.HasValue ? new List<AllocationRecord>() : removedRecords,
                ExcludedCount = excludedCount,
                BalabariCount = balabariCount,
                HardCount = hardCount,
                PreviousCount = previousCount,
                TariffFilledCount = tariffFilledCount,
                DateYear = dateYear,
                DateMonth = dateMonth,
                DateDay = dateDay
            };
        }


        private string NormalizeBalabariValue(int componentType, string value)
        {
            switch (componentType)
            {
                case 1:     // شناسه قبض
                case 2:     // پرونده
                    return NormalizeAllocationIdKey(value);

                case 3:     // روزکار
                case 4:     // محدوده
                    {
                        var number = ParseAllocationInt(value);
                        return number.HasValue ? number.Value.ToString() : NormalizeAllocationKey(value);
                    }

                default:
                    return NormalizeAllocationKey(value);
            }
        }


        private bool IsBalabariRecord(ValuunicPriorityResult x, int? workDay, Dictionary<int, HashSet<string>> sets)
        {
            return IsListRecord(x, workDay, sets, BALABARI_MATCH_ALL_COMPONENTS);
        }


        /// <summary>
        /// 1 = شناسه قبض ← valuunic ، 2 = پرونده ← parvandeh ، 3 = روزکار ← رقم ۴ و ۵ اشتراک ، 4 = محدوده ← رقم ۶ و ۷ اشتراک
        /// </summary>
        private bool IsListRecord(
            ValuunicPriorityResult x,
            int? workDay,
            Dictionary<int, HashSet<string>> sets,
            bool matchAll = false)
        {
            if (sets == null || sets.Count == 0)
                return false;

            var checks = new List<bool>();

            HashSet<string> set;

            if (sets.TryGetValue(1, out set) && set.Count > 0)
                checks.Add(set.Contains(NormalizeAllocationIdKey(x.valuunic)));

            if (sets.TryGetValue(2, out set) && set.Count > 0)
                checks.Add(set.Contains(NormalizeAllocationIdKey(x.parvandeh)));

            if (sets.TryGetValue(3, out set) && set.Count > 0)
                checks.Add(workDay.HasValue && set.Contains(workDay.Value.ToString()));

            if (sets.TryGetValue(4, out set) && set.Count > 0)
            {
                var mahdodeh = GetAllocationMahdodeh(x.eshterak, x.item2);
                checks.Add(mahdodeh.HasValue && set.Contains(mahdodeh.Value.ToString()));
            }

            if (checks.Count == 0)
                return false;

            return matchAll ? checks.All(c => c) : checks.Any(c => c);
        }


        /// <summary>تاریخ ثبت = تاریخ واردشده توسط کاربر (نه تاریخ پرونده‌ی منبع)</summary>
        private ValuunicPriorityResult3 ToAllocationResult3(
            ValuunicPriorityResult x,
            string targetCity,
            AllocationAgent agent,
            bool isSpecial,
            DateTime now,
            SmartAllocationContext ctx)
        {
            return new ValuunicPriorityResult3
            {
                valuunic = x.valuunic,
                city = targetCity,
                makaan = x.makaan,
                nametarefeh = x.nametarefeh,
                noeshtrak = x.noeshtrak,
                mahdodeh = x.mahdodeh,
                shomaremobile = x.shomaremobile,
                parvandeh = x.parvandeh,
                eshterak = x.eshterak,

                roz = agent != null ? agent.FullName : x.roz,

                shenasehpardaght = x.shenasehpardaght,

                P1 = agent != null ? agent.PersonalId : x.P1,
                P2 = x.P2,
                P3 = x.P3,
                P4 = x.P4,
                P5 = x.P5,

                SortRank = x.SortRank,
                day = DatePart(x.day, ctx.DateDay),
                month = DatePart(x.month, ctx.DateMonth),
                yaer = DatePart(x.yaer, ctx.DateYear),

                CreatedAt = now,

                item1 = x.item1,
                item2 = x.item2,

                item3 = isSpecial ? "ویژه" : (agent != null ? agent.NationalCode : x.item3),

                item4 = x.item4
            };
        }


        private object BuildSmartAllocationSummary(SmartAllocationContext ctx)
        {
            var r = ctx.Result;

            var agents =
                r.Agents
                .OrderBy(a => a.Freedom)
                .ThenBy(a => a.FullName)
                .Select(a => new
                {
                    a.UserId,
                    a.FullName,
                    a.NationalCode,
                    a.JobTitle,
                    Roles = a.Roles.Select(GetAllocationRoleTitle).ToList(),
                    Regions = a.Regions == null ? "همه" : string.Join("، ", a.Regions.OrderBy(x => x)),
                    WorkDays = a.ScopeDescription(CompressDayList),
                    a.Freedom,
                    Capacity = Math.Round(a.Capacity, 0),
                    Load = Math.Round(a.Load, 0),
                    a.FillPercent,
                    RecordCount = a.Records.Count,
                    a.RecordLimit,
                    a.CountLimitReached,
                    DemandCount = a.Records.Count(x => x.IsDemand),
                    MaxRound = a.Records.Any() ? a.Records.Max(x => x.Round) : 0
                })
                .ToList();

            decimal totalCapacity = r.Agents.Sum(a => a.Capacity);
            decimal totalLoad = r.Agents.Sum(a => a.Load);

            return new
            {
                sourceCity = ctx.SourceCity,
                targetCity = ctx.TargetCity,
                cityValue = ctx.CityValue,

                standard = BuildCityStandardSummary(ctx.Standard),

                sourceCount = ctx.SourceCount,
                excludedCount = ctx.ExcludedCount,
                specialCount = ctx.SpecialRecords.Count,
                balabariCount = ctx.BalabariCount,
                hardCount = ctx.HardCount,
                previousCount = ctx.PreviousCount,
                tariffFilledCount = ctx.TariffFilledCount,

                allocatedCount = r.Allocated.Count,
                allocatedAmount = Math.Round(r.Allocated.Sum(x => x.Amount), 0),
                unallocatedCount = r.Unallocated.Count,
                noPermissionCount = r.NoPermission.Count,

                agentCount = r.Agents.Count,
                completeAgents = r.Agents.Count(a => a.Capacity > 0 && (a.CountLimitReached || a.Load >= a.Capacity * ctx.Settings.CompleteThreshold)),

                settings = new
                {
                    cutPercent = Math.Round(ctx.Settings.CutoffFactor * 100m, 0),
                    completePercent = Math.Round(ctx.Settings.CompleteThreshold * 100m, 0),
                    overflowPercent = Math.Round(ctx.Settings.MaxOverflow * 100m, 0)
                },
                totalCapacity = Math.Round(totalCapacity, 0),
                totalLoad = Math.Round(totalLoad, 0),
                totalFill = totalCapacity <= 0 ? 0 : Math.Round(totalLoad * 100m / totalCapacity, 1),

                agents = agents,

                logs = r.Logs.Select(l => new
                {
                    l.PhaseTitle,
                    l.Round,
                    l.TargetAgents,
                    l.Candidates,
                    l.Gap,
                    l.CutCount,
                    l.CutWeight,
                    l.MinCutAmount,
                    l.MaxCutAmount,
                    l.MaxNotCutCandidateAmount,
                    l.AssignedCount,
                    l.AssignedWeight,
                    l.DeficitAgentsAfter
                })
            };
        }


        /* =========================================================
           TAB 4 - PREVIEW (بدون ذخیره)
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PreviewSmartAllocation(
            string sourceCity,
            string targetCity,
            string specialThreshold,
            List<int> RegionIds,
            List<int> WorkDays,
            string sourceDate,
            string excludeDate,
            decimal? cutPercent = null,
            decimal? completePercent = null,
            string allocationDays = null,
            string manualDailyStandard = null)
        {
            try
            {
                string error;

                var ctx =
                    BuildSmartAllocation(
                        sourceCity, targetCity, specialThreshold,
                        RegionIds, WorkDays, sourceDate, excludeDate, cutPercent, completePercent,
                        null, out error, allocationDays, manualDailyStandard);

                if (ctx == null)
                    return Json(new { success = false, message = error });

                return Json(new { success = true, data = BuildSmartAllocationSummary(ctx) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در اجرای پیش‌نمایش: " + ex.Message });
            }
        }


        /* =========================================================
           TAB 4 - RUN (ذخیره در Result3 + فایل Excel)
           ========================================================= */

        [HttpGet]
        public ActionResult SouthAgentMappings()
        {
            Response.ContentEncoding = System.Text.Encoding.UTF8;
            Response.Charset = "utf-8";
            if (!IsJonobiContract())
                return new HttpNotFoundResult("این صفحه فقط برای خراسان جنوبی فعال است.");

            try
            {
                ViewBag.AgentMappings = ReadSouthAgentMappings();
                ViewBag.MappingError = null;
            }
            catch (Exception ex)
            {
                ViewBag.AgentMappings = new List<SouthAgentMapRow>();
                ViewBag.MappingError = SouthMappingErrorText(ex);
            }

            return View("SouthAgentMappings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ImportSouthAgentMappings(HttpPostedFileBase file)
        {
            if (!IsJonobiContract())
                return new HttpNotFoundResult("این صفحه فقط برای خراسان جنوبی فعال است.");
            if (file == null || file.ContentLength == 0)
                return RedirectToAction("SouthAgentMappings");

            try
            {
                var incoming = new List<SouthAgentMapRow>();
                using (var workbook = new XLWorkbook(file.InputStream))
                {
                    var ws = workbook.Worksheets.FirstOrDefault();
                    if (ws == null) throw new InvalidOperationException("شیت اول فایل اکسل پیدا نشد.");
                    var used = ws.RangeUsed();
                    if (used == null) throw new InvalidOperationException("فایل اکسل خالی است.");

                    int headerRow = used.RangeAddress.FirstAddress.RowNumber;
                    int firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
                    int lastCol = used.RangeAddress.LastAddress.ColumnNumber;
                    int lastRow = used.RangeAddress.LastAddress.RowNumber;
                    int idCol = 0, personnelCol = 0, nameCol = 0, cityCol = 0;

                    for (int col = firstCol; col <= lastCol; col++)
                    {
                        string header = NormalizeSouthHeader(ws.Cell(headerRow, col).GetString());
                        if (header == "id") idCol = col;
                        else if (header == "کدپرسنلی") personnelCol = col;
                        else if (header == "name" || header == "نام") nameCol = col;
                        else if (header == "city" || header == "شهر") cityCol = col;
                    }
                    if (idCol == 0 || personnelCol == 0)
                        throw new InvalidOperationException("ستون‌های id و کد پرسنلی در ردیف اول فایل لازم است.");

                    for (int row = headerRow + 1; row <= lastRow; row++)
                    {
                        string code = NormalizeSouthPersonnelCode(ws.Cell(row, personnelCol).GetFormattedString());
                        string idText = NormalizeSouthPersonnelCode(ws.Cell(row, idCol).GetFormattedString());
                        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(idText)) continue;
                        int officerId;
                        if (string.IsNullOrWhiteSpace(code) || !int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out officerId))
                            throw new InvalidOperationException("ردیف " + row + " باید هم کد پرسنلی و هم id عددی داشته باشد.");

                        incoming.Add(new SouthAgentMapRow
                        {
                            PersonnelCode = code,
                            OfficerId = officerId,
                            Name = nameCol == 0 ? "" : ws.Cell(row, nameCol).GetString().Trim(),
                            City = cityCol == 0 ? "" : ws.Cell(row, cityCol).GetString().Trim()
                        });
                    }
                }

                if (incoming.Count == 0) throw new InvalidOperationException("هیچ ردیف مأموری با کد پرسنلی معتبر پیدا نشد.");
                if (incoming.GroupBy(x => x.PersonnelCode, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("کد پرسنلی تکراری در فایل وجود دارد؛ قبل از بارگذاری آن را یکتا کنید.");
                if (incoming.GroupBy(x => x.OfficerId).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("id تکراری در فایل وجود دارد؛ قبل از بارگذاری آن را یکتا کنید.");
                // فقط کاربران مرتبط با کدهای موجود در فایل بررسی شوند.
                var incomingCodes = new HashSet<string>(
                    incoming.Select(x => x.PersonnelCode),
                    StringComparer.OrdinalIgnoreCase
                );

                var usersByCode = db.tbUsers.AsNoTracking()
                    .Where(x => x.usr_Personal_ID.HasValue)
                    .Select(x => new
                    {
                        x.usr_ID,
                        x.usr_Personal_ID
                    })
                    .ToList()
                    .GroupBy(
                        x => NormalizeSouthPersonnelCode(
                            x.usr_Personal_ID.Value.ToString(
                                CultureInfo.InvariantCulture
                            )
                        ),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Where(g => incomingCodes.Contains(g.Key))
                    .ToList();

                // تکرار فقط برای کدهایی مهم است که داخل همین اکسل هستند.
                var duplicateUserCodes = usersByCode
                    .Where(g => g.Count() > 1)
                    .Select(g =>
                        g.Key + " (شناسه کاربران: " +
                        string.Join(", ", g.Select(u => u.usr_ID)) + ")"
                    )
                    .ToList();

                if (duplicateUserCodes.Count > 0)
                {
                    throw new InvalidOperationException(
                        "برای این کدهای پرسنلیِ فایل، چند کاربر در سامانه وجود دارد: " +
                        string.Join("، ", duplicateUserCodes.Take(10))
                    );
                }

                var userIdByCode = usersByCode.ToDictionary(
                    g => g.Key,
                    g => g.Single().usr_ID,
                    StringComparer.OrdinalIgnoreCase
                );

                var unmatchedCodes = incoming
                    .Where(x => !userIdByCode.ContainsKey(x.PersonnelCode))
                    .Select(x => x.PersonnelCode)
                    .ToList();

                if (unmatchedCodes.Count > 0)
                {
                    throw new InvalidOperationException(
                        "این کدهای پرسنلی در کاربران سامانه پیدا نشدند: " +
                        string.Join("، ", unmatchedCodes.Take(15))
                    );
                }

                foreach (var item in incoming)
                {
                    item.UserId = userIdByCode[item.PersonnelCode];
                }
                //var usersByCode = db.tbUsers.AsNoTracking()
                //    .Where(x => x.usr_Personal_ID.HasValue)
                //    .Select(x => new { x.usr_ID, x.usr_Personal_ID })
                //    .ToList()
                //    .GroupBy(x => NormalizeSouthPersonnelCode(x.usr_Personal_ID.Value.ToString(CultureInfo.InvariantCulture)), StringComparer.OrdinalIgnoreCase)
                //    .ToList();
                //var duplicateUserCodes = usersByCode.Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                //if (duplicateUserCodes.Count > 0)
                //    throw new InvalidOperationException("کد پرسنلی در جدول کاربران تکراری است: " + string.Join("، ", duplicateUserCodes.Take(10)));
                //var userIdByCode = usersByCode.ToDictionary(x => x.Key, x => x.Single().usr_ID, StringComparer.OrdinalIgnoreCase);
                //var unmatchedCodes = incoming.Where(x => !userIdByCode.ContainsKey(x.PersonnelCode)).Select(x => x.PersonnelCode).ToList();
                //if (unmatchedCodes.Count > 0)
                //    throw new InvalidOperationException("این کدهای پرسنلی در کاربران سامانه پیدا نشدند: " + string.Join("، ", unmatchedCodes.Take(15)));
                //foreach (var item in incoming) item.UserId = userIdByCode[item.PersonnelCode];

                var connection = CreateSouthAgentMapConnection();
                bool closeConnection = connection.State != ConnectionState.Open;
                try
                {
                    if (closeConnection) connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var deactivate = new SqlCommand("UPDATE dbo.SouthOfficerUserMap SET IsActive = 0;", connection, transaction))
                            deactivate.ExecuteNonQuery();

                        foreach (var item in incoming)
                        {
                            const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.SouthOfficerUserMap WHERE codeusrsystem = @code)
    UPDATE dbo.SouthOfficerUserMap SET FKusr=@usr, officer_id=@id, AgentName=@name, CityName=@city, IsActive=1, UpdatedAt=SYSUTCDATETIME() WHERE codeusrsystem=@code;
ELSE
    INSERT dbo.SouthOfficerUserMap(FKusr, codeusrsystem, officer_id, AgentName, CityName, IsActive, UpdatedAt)
    VALUES(@usr, @code, @id, @name, @city, 1, SYSUTCDATETIME());";
                            using (var command = new SqlCommand(sql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@code", item.PersonnelCode);
                                command.Parameters.AddWithValue("@usr", item.UserId);
                                command.Parameters.AddWithValue("@id", item.OfficerId);
                                command.Parameters.AddWithValue("@name", (object)item.Name ?? "");
                                command.Parameters.AddWithValue("@city", (object)item.City ?? "");
                                command.ExecuteNonQuery();
                            }
                        }
                        transaction.Commit();
                    }
                }
                finally
                {
                    if (closeConnection && connection.State == ConnectionState.Open) connection.Close();
                }
                TempData["SouthMappingMessage"] = "بارگذاری انجام شد؛ " + incoming.Count + " مأمور فعال شد.";
            }
            catch (Exception ex)
            {
                TempData["SouthMappingError"] = "بارگذاری انجام نشد: " + SouthMappingErrorText(ex);
            }
            return RedirectToAction("SouthAgentMappings");
        }

        private SqlConnection CreateSouthAgentMapConnection()
        {
            // از همان اتصال DbContext استفاده می‌شود، نه یک اتصال SQL مستقل.
            var connection = db.Database.Connection as SqlConnection;
            if (connection == null) throw new InvalidOperationException("اتصال دیتابیس برنامه از نوع SQL Server نیست.");
            return connection;
        }

        private List<SouthAgentMapRow> ReadSouthAgentMappings()
        {
            var rows = new List<SouthAgentMapRow>();
            var connection = CreateSouthAgentMapConnection();
            bool closeConnection = connection.State != ConnectionState.Open;
            try
            {
                if (closeConnection) connection.Open();
                using (var command = new SqlCommand("SELECT ID, codeusrsystem, officer_id, AgentName, CityName, FKusr FROM dbo.SouthOfficerUserMap WHERE IsActive=1 ORDER BY CityName, AgentName;", connection))
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) rows.Add(new SouthAgentMapRow { ID = reader.GetInt64(0), PersonnelCode = reader.GetString(1), OfficerId = reader.GetInt32(2), Name = reader.IsDBNull(3) ? "" : reader.GetString(3), City = reader.IsDBNull(4) ? "" : reader.GetString(4), UserId = reader.GetInt32(5) });
            }
            finally { if (closeConnection && connection.State == ConnectionState.Open) connection.Close(); }
            return rows;
        }

        private static string SouthMappingErrorText(Exception ex)
        {
            var sqlError = ex as SqlException;
            if (sqlError != null && sqlError.Number == 18456)
                return "ورود SQL Server رد شد (Login failed). اعتبارنامه یا دسترسی اتصال دیتابیس برنامه در Web.config را بررسی کنید.";
            if (sqlError != null && (sqlError.Number == 208 || sqlError.Number == 229))
                return "جدول dbo.SouthOfficerUserMap وجود ندارد یا دسترسی آن کافی نیست. اسکریپت ساخت جدول را روی دیتابیس jonobi10aban اجرا کنید.";
            return ex.Message;
        }

        private static string NormalizeSouthHeader(string value)
        {
            return NormalizeSouthPersonnelCode(value).Replace(" ", "").Replace("‌", "").Replace("_", "").ToLowerInvariant();
        }

        private static string NormalizeSouthPersonnelCode(string value)
        {
            if (value == null) return "";
            const string persian = "۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩";
            const string latin = "01234567890123456789";
            for (int i = 0; i < persian.Length; i++) value = value.Replace(persian[i], latin[i]);
            return value.Trim().Replace(",", "").Replace("٬", "").Replace("٫", ".");
        }

        public class SouthAgentMapRow
        {
            public long ID { get; set; }
            public string PersonnelCode { get; set; }
            public int OfficerId { get; set; }
            public int UserId { get; set; }
            public string Name { get; set; }
            public string City { get; set; }
        }

        private class SouthSampleExportRow
        {
            public List<string> Values { get; set; }
            public int OfficerId { get; set; }
            public AllocationRecord Record { get; set; }
        }

        private List<SouthSampleExportRow> BuildSouthSampleRows(List<AllocationRecord> records)
        {
            if (records == null || !records.Any(x => x.Agent != null))
                return new List<SouthSampleExportRow>();

            var mapping = ReadSouthAgentMappings().ToDictionary(x => NormalizeSouthPersonnelCode(x.PersonnelCode), x => x.OfficerId, StringComparer.OrdinalIgnoreCase);
            var personnelByValuunic = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records.Where(x => x.Agent != null))
            {
                string code = NormalizeSouthPersonnelCode(record.Agent.PersonalId.ToString(CultureInfo.InvariantCulture));
                int officerId;
                if (!mapping.TryGetValue(code, out officerId))
                    throw new InvalidOperationException("برای کد پرسنلی " + code + " (" + record.Agent.FullName + ") در فایل مأموران جنوبی id ثبت نشده است. ابتدا فایل مأموران را بارگذاری کنید.");
                var source = (ValuunicPriorityResult)record.Source;
                personnelByValuunic[Convert.ToString(source.valuunic).Trim()] = officerId;
            }

            int batchId;
            using (var command = db.Database.Connection.CreateCommand())
            {
                command.CommandText = @"SELECT TOP (1) d.ID FROM dbo.dbergharelinsert_6 d
INNER JOIN dbo.dbergharel_4 g ON g.ID=d.FK_table4
WHERE g.FK_namegozarwsh=2 ORDER BY d.ID DESC;";
                if (command.Connection.State != ConnectionState.Open) command.Connection.Open();
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) throw new InvalidOperationException("بارگذاری منبع جنوبی (گروه ۲) پیدا نشد.");
                batchId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            var valuesByKey = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            var keys = personnelByValuunic.Keys.ToList();
            for (int offset = 0; offset < keys.Count; offset += 350)
            {
                var chunk = keys.Skip(offset).Take(350).ToList();
                var parameters = chunk.Select((x, i) => "@v" + i).ToList();
                using (var command = db.Database.Connection.CreateCommand())
                {
                    command.CommandText = "SELECT CONVERT(NVARCHAR(500), valuunic) AS U, FK_db2, MAX(CONVERT(NVARCHAR(MAX), valueFK_db2)) AS V FROM dbo.dbergharelimsertfk1_7 WHERE FK_dbergharelinsert_6=@batch AND FK_db2 IN (1,2,3,4,5,6,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,30,31,35) AND CONVERT(NVARCHAR(500), valuunic) IN (" + string.Join(",", parameters) + ") GROUP BY CONVERT(NVARCHAR(500), valuunic), FK_db2;";
                    var batchParameter = command.CreateParameter(); batchParameter.ParameterName = "@batch"; batchParameter.Value = batchId; command.Parameters.Add(batchParameter);
                    for (int i = 0; i < chunk.Count; i++) { var p = command.CreateParameter(); p.ParameterName = parameters[i]; p.Value = chunk[i]; command.Parameters.Add(p); }
                    if (command.Connection.State != ConnectionState.Open) command.Connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string key = reader.GetString(0).Trim();
                            int fieldId = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                            Dictionary<int, string> fields;
                            if (!valuesByKey.TryGetValue(key, out fields)) valuesByKey[key] = fields = new Dictionary<int, string>();
                            fields[fieldId] = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        }
                    }
                }
            }

            int[] sourceFields = { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 30, 31, 35 };
            return records.Where(x => x.Agent != null).Select(x =>
            {
                var source = (ValuunicPriorityResult)x.Source;
                string key = Convert.ToString(source.valuunic).Trim();
                Dictionary<int, string> fields;
                valuesByKey.TryGetValue(key, out fields);
                return new SouthSampleExportRow
                {
                    Values = sourceFields.Select(id =>
                    {
                        string value = fields != null && fields.ContainsKey(id) ? fields[id] : "";
                        // FK_db2=30 maps to the shenaseghabz column in the South export.
                        if (id == 30 && string.IsNullOrWhiteSpace(value))
                            return key;
                        return value;
                    }).ToList(),
                    OfficerId = personnelByValuunic[key],
                    Record = x
                };
            }).ToList();
        }

        private void WriteSouthSampleSheet(IXLWorksheet ws, List<SouthSampleExportRow> rows)
        {
            string[] headers = { "row", "ramz", "khoshhesabi", "last_read", "taneh", "phone", "mobile", "shenasayi", "day", "branch", "area", "serial", "eshterak", "name", "address", "organ_code", "faz", "amper", "dimand", "tariff", "mantaghe_code", "doreh", "all_dept", "current_dept", "last_dept", "last_pay", "average_masraf", "due_date", "shenaseghabz", "shenasepardakht", "cut_date", "officer_id", "list_name" };
            rows = rows ?? new List<SouthSampleExportRow>();
            var referralDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Iran Standard Time");
            var calendar = new PersianCalendar();
            string dateText = string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}",
                calendar.GetYear(referralDate), calendar.GetMonth(referralDate), calendar.GetDayOfMonth(referralDate));
            var titles = rows.GroupBy(r => r.OfficerId).ToDictionary(g => g.Key, g =>
                BuildSouthListName(g.Select(r => r.Record).ToList(), dateText));
            var writtenOfficers = new HashSet<int>();
            ws.RightToLeft = false;
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
            ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");
            for (int i = 0; i < rows.Count; i++)
            {
                ws.Cell(i + 2, 1).Value = i + 1;
                for (int j = 0; j < rows[i].Values.Count; j++)
                {
                    ws.Cell(i + 2, j + 2).SetValue(rows[i].Values[j] ?? "");
                    ws.Cell(i + 2, j + 2).Style.NumberFormat.Format = "@";
                }
                ws.Cell(i + 2, headers.Length - 1).Value = rows[i].OfficerId;
                ws.Cell(i + 2, headers.Length).SetValue(writtenOfficers.Add(rows[i].OfficerId)
                    ? titles[rows[i].OfficerId] : "");
                ws.Cell(i + 2, headers.Length).Style.NumberFormat.Format = "@";
            }
            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, Math.Max(1, rows.Count + 1), headers.Length).SetAutoFilter();
            ws.Columns().AdjustToContents();
            ws.Column(headers.Length).Width = 80;
            ws.Column(headers.Length).Style.Alignment.WrapText = true;
        }

        private string BuildSouthListName(List<AllocationRecord> records, string dateText)
        {
            Func<string, int, string> shorten = (value, limit) =>
            {
                value = NormalizeOrderExcelText(value);
                return value.Length <= limit ? value : value.Substring(0, limit - 1) + "…";
            };
            Func<IEnumerable<int?>, string> range = values =>
            {
                var numbers = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
                return numbers.Count == 0 ? "نامشخص تا نامشخص"
                    : numbers.Min().ToString(CultureInfo.InvariantCulture) + " تا "
                      + numbers.Max().ToString(CultureInfo.InvariantCulture);
            };
            string title = "نام مامور: " + shorten(records.First().Agent.FullName, 80)
                + "&منطقه از " + range(records.Select(r => r.Region))
                + "&روزکار از " + range(records.Select(r => r.WorkDay))
                + "&تعرفه ها: " + shorten(string.Join("، ", records.Select(r => r.Tariff)
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t)), 120)
                + "&نوع اشتراک: " + string.Join("، ", records.Select(r => r.IsDemand ? "دیماند" : "عادی").Distinct())
                + "&تعداد لیست: " + records.Count.ToString(CultureInfo.InvariantCulture)
                + "&مبلغ از " + records.Min(r => r.Amount).ToString("0.##", CultureInfo.InvariantCulture)
                + " تا " + records.Max(r => r.Amount).ToString("0.##", CultureInfo.InvariantCulture)
                + "&تاریخ ارجاع: " + dateText;
            return shorten(title, 500);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RunSmartAllocation(
            string sourceCity,
            string targetCity,
            string specialThreshold,
            List<int> RegionIds,
            List<int> WorkDays,
            string sourceDate,
            string excludeDate,
            decimal? cutPercent = null,
            decimal? completePercent = null,
            string allocationDays = null,
            string manualDailyStandard = null,
            bool singleSheetOnly = false)
        {
            string error;

            SmartAllocationContext ctx;

            try
            {
                ctx =
                    BuildSmartAllocation(
                        sourceCity, targetCity, specialThreshold,
                        RegionIds, WorkDays, sourceDate, excludeDate, cutPercent, completePercent,
                        null, out error, allocationDays, manualDailyStandard);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در اجرای تخصیص: " + ex.Message });
            }

            if (ctx == null)
                return Json(new { success = false, message = error });

            var now = DateTime.Now;
            var result = ctx.Result;

            var allocatedRows =
                result.Allocated
                .OrderBy(x => x.Agent.FullName)
                .ThenBy(x => x.Eshterak, new NaturalKeyComparer())
                .Select(x => new
                {
                    Rec = x,
                    Row = ToAllocationResult3((ValuunicPriorityResult)x.Source, ctx.TargetCity, x.Agent, false, now, ctx)
                })
                .ToList();

            var specialRows =
                ctx.SpecialRecords
                .Select(x => ToAllocationResult3(x, ctx.TargetCity, null, true, now, ctx))
                .ToList();

            var insertList =
                allocatedRows.Select(x => x.Row)
                .Concat(specialRows)
                .ToList();

            if (insertList.Count == 0)
                return Json(new { success = false, message = "هیچ پرونده‌ای تخصیص داده نشد." });

            List<SouthSampleExportRow> southSampleRows = null;
            if (IsJonobiContract())
            {
                try { southSampleRows = BuildSouthSampleRows(allocatedRows.Select(x => x.Rec).ToList()); }
                catch (Exception ex) { return Json(new { success = false, message = "خروجی قالب جنوبی آماده نشد و تخصیص ذخیره نشد: " + ex.Message }); }
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var dates =
                        insertList
                        .Select(x => new { x.yaer, x.month, x.day })
                        .Distinct()
                        .ToList();

                    foreach (var d in dates)
                    {
                        var y = d.yaer;
                        var m = d.month;
                        var dd = d.day;

                        var old =
                            db.ValuunicPriorityResult3
                            .Where(s =>
                                s.month == m
                                && s.day == dd
                                && s.yaer == y
                                && s.city == ctx.TargetCity)
                            .ToList();

                        if (old.Count > 0)
                            db.ValuunicPriorityResult3.RemoveRange(old);
                    }

                    db.SaveChanges();

                    db.ValuunicPriorityResult3.AddRange(insertList);
                    db.SaveChanges();

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    return Json(new { success = false, message = "خطا در ذخیره تخصیص: " + ex.Message });
                }
            }

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    if (southSampleRows != null)
                    {
                        WriteSouthSampleSheet(workbook.Worksheets.Add("sheet1"), southSampleRows);
                        if (singleSheetOnly)
                        {
                            using (var stream = new MemoryStream())
                            {
                                workbook.SaveAs(stream);
                                return File(stream.ToArray(),
                                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                    "South_Sheet1_" + now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                            }
                        }
                    }
                    WriteSmartAllocationSummarySheet(workbook.Worksheets.Add("خلاصه مامورین"), ctx);

                    Func<string, bool?, List<AllocationRecord>> byPhase =
                        (phase, demand) =>
                            allocatedRows
                            .Where(x =>
                                x.Rec.PhaseKey == phase
                                && (!demand.HasValue || x.Rec.IsDemand == demand.Value))
                            .Select(x => x.Rec)
                            .ToList();

                    WriteSmartAllocationSheet(workbook.Worksheets.Add("عادی"), byPhase(SmartAllocationEngine.PHASE_FIELD, false));
                    WriteSmartAllocationSheet(workbook.Worksheets.Add("دیماند"), byPhase(SmartAllocationEngine.PHASE_FIELD, true));
                    WriteSmartAllocationSheet(workbook.Worksheets.Add("بالابری"), byPhase(SmartAllocationEngine.PHASE_BALABARI, null));
                    WriteSmartAllocationSheet(workbook.Worksheets.Add("سایر"), byPhase(SmartAllocationEngine.PHASE_OTHER, null));
                    WriteSmartAllocationSheet(workbook.Worksheets.Add(GetSpecialCollectionTitle()), byPhase(SmartAllocationEngine.PHASE_TELEPHONE, null));

                    WriteSmartAllocationSheet(
                        workbook.Worksheets.Add("ویژه"),
                        ctx.SpecialRecords
                            .Select(x => new AllocationRecord { Amount = x.P5, Source = x, IsDemand = IsAllocationDemand(x) })
                            .ToList());

                    var unallocatedAll =
                        result.Unallocated
                            .Concat(result.NoPermission)
                            .Concat(ctx.RemovedRecords)
                            .OrderByDescending(x => x.Amount)
                            .ToList();

                    var auditAgents = LoadAllocationAuditAgents(ctx);

                    WriteSmartAllocationSheet(
                        workbook.Worksheets.Add("تخصیص نیافته"),
                        unallocatedAll,
                        auditAgents);

                    WriteUnallocatedReasonSheet(
                        workbook.Worksheets.Add("علت تخصیص نیافته (خلاصه)"),
                        unallocatedAll,
                        auditAgents);

                    WriteCutAuditSheet(workbook.Worksheets.Add("گزارش کات"), ctx);

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);

                        string fileName = ctx.TargetCity + "_" + now.ToString("yyyyMMdd_HHmmss") + ".xlsx";

                        return File(
                            stream.ToArray(),
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                string errorId = Guid.NewGuid().ToString("N");

                System.Diagnostics.Trace.TraceError(
                    "RunSmartAllocation Excel error {0}: {1}",
                    errorId, ex.ToString());

                Response.StatusCode = 500;
                Response.TrySkipIisCustomErrors = true;

                return Json(new
                {
                    success = false,
                    allocationSaved = true,
                    message = "تخصیص ذخیره شد، اما ساخت فایل اکسل ناموفق بود. شناسه خطا: " + errorId,
                    errorId = errorId
                });
            }
        }


        /* =========================================================
           TAB 6 - تخصیص تکی (یک مأمور)

           همان الگوریتم تب ۴ ، فقط با یک مأمور:
             - هیچ چیزِ ثبت‌شده‌ی قبلی پاک نمی‌شود
             - فقط از پرونده‌های باقیمانده (که برای همان تاریخ و شهر در Result3 نیامده‌اند) می‌دهد
             - تاریخ لیست قبلی (اختیاری) مثل تخصیص گروهی اعمال می‌شود
             - مناطق = همه مناطق شهرستان ، روزکار = ۱ تا ۹۹
               (محدودیت واقعی منطقه / روزکار / تعرفه / محدوده را تنظیم تب ۲ مأمور اعمال می‌کند)
           ========================================================= */

        private static readonly List<int> SINGLE_ALLOCATION_WORKDAYS =
            Enumerable.Range(1, 99).ToList();


        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GetSingleAgentList(string sourceCity)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (!currentUserId.HasValue)
                    return Json(new { success = false, message = "دسترسی غیر مجاز." });

                sourceCity = (sourceCity ?? "").Trim();

                if (string.IsNullOrWhiteSpace(sourceCity))
                    return Json(new { success = false, message = "شهرستان انتخاب نشده است." });

                string cityAccessError;
                if (!EnforceJonobiUserAllocationCity(currentUserId.Value, ref sourceCity, out cityAccessError))
                    return Json(new { success = false, message = cityAccessError });

                var regionSet =
                    new HashSet<int>(GetAllocationRegionsForCity(sourceCity).Select(x => x.Key));

                if (regionSet.Count == 0)
                    return Json(new { success = false, message = "برای این شهرستان منطقه‌ای تعریف نشده است." });

                string error;

                var agents =
                    LoadAllocationAgents(
                        regionSet,
                        GetCityValue(sourceCity),
                        sourceCity,
                        out error);

                if (agents == null)
                    return Json(new { success = false, message = error });

                string hint = null;

                if (agents.Count == 0)
                {
                    var assignmentId = GetActiveRoleAssignmentId();

                    int roleUsers =
                        assignmentId.HasValue
                        ? db.tbOrderAssignmentActivity
                            .Where(x => x.AssignmentID == assignmentId.Value)
                            .Select(x => x.UserID)
                            .Distinct()
                            .Count()
                        : 0;

                    hint =
                        "مأمور واجد شرایطی پیدا نشد. مأموران دارای نقش (تب ۱): " + roleUsers
                        + " | منطقه‌های این شهرستان: " + string.Join("، ", regionSet.OrderBy(x => x));
                }

                return Json(new
                {
                    success = true,
                    hint = hint,
                    data = agents
                        .OrderBy(a => a.FullName)
                        .Select(a => new
                        {
                            a.UserId,
                            a.FullName,
                            a.NationalCode,
                            Roles = string.Join("، ", a.Roles.Select(GetAllocationRoleTitle)),
                            Regions = a.Regions == null ? "همه" : string.Join("، ", a.Regions.OrderBy(x => x))
                        })
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در دریافت مأموران: " + ex.Message });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PreviewSingleAllocation(
            string sourceCity,
            string targetCity,
            string specialThreshold,
            string sourceDate,
            string excludeDate,
            int? userId,
            decimal? cutPercent = null,
            decimal? completePercent = null,
            string allocationDays = null,
            string manualDailyStandard = null)
        {
            try
            {
                if (!userId.HasValue)
                    return Json(new { success = false, message = "مأمور را انتخاب کنید." });

                string error;

                var regionIds =
                    GetAllocationRegionsForCity((sourceCity ?? "").Trim())
                    .Select(x => x.Key)
                    .ToList();

                var ctx =
                    BuildSmartAllocation(
                        sourceCity, targetCity, specialThreshold,
                        regionIds, SINGLE_ALLOCATION_WORKDAYS, sourceDate, excludeDate, cutPercent, completePercent,
                        userId, out error, allocationDays, manualDailyStandard);

                if (ctx == null)
                    return Json(new { success = false, message = error });

                return Json(new { success = true, data = BuildSmartAllocationSummary(ctx) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در اجرای پیش‌نمایش تکی: " + ex.Message });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RunSingleAllocation(
            string sourceCity,
            string targetCity,
            string specialThreshold,
            string sourceDate,
            string excludeDate,
            int? userId,
            decimal? cutPercent = null,
            decimal? completePercent = null,
            string allocationDays = null,
            string manualDailyStandard = null,
            bool singleSheetOnly = false)
        {
            if (!userId.HasValue)
                return Json(new { success = false, message = "مأمور را انتخاب کنید." });

            string error;

            SmartAllocationContext ctx;

            try
            {
                var regionIds =
                    GetAllocationRegionsForCity((sourceCity ?? "").Trim())
                    .Select(x => x.Key)
                    .ToList();

                ctx =
                    BuildSmartAllocation(
                        sourceCity, targetCity, specialThreshold,
                        regionIds, SINGLE_ALLOCATION_WORKDAYS, sourceDate, excludeDate, cutPercent, completePercent,
                        userId, out error, allocationDays, manualDailyStandard);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در اجرای تخصیص تکی: " + ex.Message });
            }

            if (ctx == null)
                return Json(new { success = false, message = error });

            var now = DateTime.Now;
            var result = ctx.Result;

            var allocatedRows =
                result.Allocated
                .OrderBy(x => x.Eshterak, new NaturalKeyComparer())
                .Select(x => new
                {
                    Rec = x,
                    Row = ToAllocationResult3((ValuunicPriorityResult)x.Source, ctx.TargetCity, x.Agent, false, now, ctx)
                })
                .ToList();

            if (allocatedRows.Count == 0)
                return Json(new { success = false, message = "از پرونده‌های باقیمانده، چیزی برای این مأمور تخصیص داده نشد." });

            List<SouthSampleExportRow> southSampleRows = null;
            if (IsJonobiContract())
            {
                try { southSampleRows = BuildSouthSampleRows(allocatedRows.Select(x => x.Rec).ToList()); }
                catch (Exception ex) { return Json(new { success = false, message = "خروجی قالب جنوبی آماده نشد و تخصیص ذخیره نشد: " + ex.Message }); }
            }

            // فقط افزودن — هیچ ردیف قبلی پاک نمی‌شود
            try
            {
                db.ValuunicPriorityResult3.AddRange(allocatedRows.Select(x => x.Row));
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "خطا در ذخیره تخصیص تکی: " + ex.Message });
            }

            using (var workbook = new XLWorkbook())
            {
                if (southSampleRows != null)
                {
                    WriteSouthSampleSheet(workbook.Worksheets.Add("sheet1"), southSampleRows);
                    if (singleSheetOnly)
                    {
                        using (var stream = new MemoryStream())
                        {
                            workbook.SaveAs(stream);
                            return File(stream.ToArray(),
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                "South_Sheet1_" + now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                        }
                    }
                }
                WriteSmartAllocationSummarySheet(workbook.Worksheets.Add("خلاصه مامور"), ctx);

                Func<string, bool?, List<AllocationRecord>> byPhase =
                    (phase, demand) =>
                        allocatedRows
                        .Where(x =>
                            x.Rec.PhaseKey == phase
                            && (!demand.HasValue || x.Rec.IsDemand == demand.Value))
                        .Select(x => x.Rec)
                        .ToList();

                WriteSmartAllocationSheet(workbook.Worksheets.Add("عادی"), byPhase(SmartAllocationEngine.PHASE_FIELD, false));
                WriteSmartAllocationSheet(workbook.Worksheets.Add("دیماند"), byPhase(SmartAllocationEngine.PHASE_FIELD, true));
                WriteSmartAllocationSheet(workbook.Worksheets.Add("بالابری"), byPhase(SmartAllocationEngine.PHASE_BALABARI, null));
                WriteSmartAllocationSheet(workbook.Worksheets.Add("سایر"), byPhase(SmartAllocationEngine.PHASE_OTHER, null));
                WriteSmartAllocationSheet(workbook.Worksheets.Add(GetSpecialCollectionTitle()), byPhase(SmartAllocationEngine.PHASE_TELEPHONE, null));

                WriteCutAuditSheet(workbook.Worksheets.Add("گزارش کات"), ctx);

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);

                    var agent = result.Agents[0];

                    string fileName = "Single_" + agent.NationalCode + "_" + now.ToString("yyyyMMdd_HHmmss") + ".xlsx";

                    return File(
                        stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
        }


        /* =========================================================
           TAB 7 - خروجی یکجای یک روز (همه شهرستان‌ها)
           خلاصه شامل همه مأموران، تنظیمات و علت تخصیص صفر است.
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportDayAllocation(string sourceDate, string excludeDate)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (!currentUserId.HasValue)
                    return Json(new { success = false, message = "Access denied. Please sign in again." });

                int year, month, day;

                if (!TryParseAllocationDate(sourceDate, out year, out month, out day))
                    return Json(new { success = false, message = "Invalid date. Use a Jalali date such as 1405/08/12." });

                var sample = db.ValuunicPriorityResult3.AsNoTracking().FirstOrDefault();

                if (sample == null)
                    return Json(new { success = false, message = "No saved allocation exists in the system." });

                var y = DatePart(sample.yaer, year);
                var m = DatePart(sample.month, month);
                var dd = DatePart(sample.day, day);

                var rows =
                    db.ValuunicPriorityResult3
                    .AsNoTracking()
                    .Where(s => s.yaer == y && s.month == m && s.day == dd)
                    .ToList();

                if (rows.Count == 0)
                    return Json(new { success = false, message = "No saved allocation was found for this date." });

                /* ---------- اطلاعات مأموران: شغل و نقش‌ها ---------- */

                var nationalCodes =
                    rows
                    .Select(r => Convert.ToString(r.item3))
                    .Where(c => !string.IsNullOrWhiteSpace(c) && c != "ویژه")
                    .Distinct()
                    .ToList();

                var jobTitles = LoadJobTitles();

                var users =
                    db.tbUsers
                    .AsNoTracking()
                    .Where(u => nationalCodes.Contains(u.usr_NationalCode))
                    .Select(u => new
                    {
                        u.usr_ID,
                        u.usr_Name,
                        u.usr_Family,
                        u.usr_NationalCode,
                        u.usr_Personal_ID,
                        u.usr_typeshoghl
                    })
                    .ToList()
                    .GroupBy(u => u.usr_NationalCode)
                    .ToDictionary(g => g.Key, g => g.First());

                var dayUserIds = users.Values.Select(u => u.usr_ID).Distinct().ToList();

                var activeAssignmentId = GetActiveRoleAssignmentId();

                var roleRows =
                    activeAssignmentId.HasValue
                    ? db.tbOrderAssignmentActivity
                        .Where(x =>
                            x.AssignmentID == activeAssignmentId.Value
                            && dayUserIds.Contains(x.UserID))
                        .Select(x => new { x.UserID, x.ActivityTypeID })
                        .ToList()
                    : null;

                // نقش‌ها یک بار index می‌شوند؛ قبلاً برای هر گروه خروجی کل لیست پیمایش می‌شد.
                var roleIdsByUser = roleRows == null
                    ? new Dictionary<int, List<int>>()
                    : roleRows
                        .GroupBy(x => x.UserID)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.ActivityTypeID).Distinct().ToList());

                var activityKinds =
                    db.tbOrderActivityType
                    .ToList()
                    .ToDictionary(x => x.ID, x => ResolveAllocationRole(x.Title));

                Func<string, HashSet<AllocationRoleKind>> rolesOf = code =>
                {
                    var result = new HashSet<AllocationRoleKind>();

                    if (roleRows == null || !users.ContainsKey(code))
                        return result;

                    var userId = users[code].usr_ID;

                    List<int> userRoleIds;

                    if (!roleIdsByUser.TryGetValue(userId, out userRoleIds))
                        return result;

                    foreach (var activityTypeId in userRoleIds)
                    {
                        AllocationRoleKind kind;

                        if (activityKinds.TryGetValue(activityTypeId, out kind) && kind != AllocationRoleKind.None)
                            result.Add(kind);
                    }

                    return result;
                };

                Func<string, string> jobOf = code =>
                    users.ContainsKey(code)
                    ? GetAllocationJobTitle(users[code].usr_typeshoghl, jobTitles)
                    : "";

                var rolesCache = new Dictionary<string, HashSet<AllocationRoleKind>>();

                Func<string, HashSet<AllocationRoleKind>> cachedRoles = code =>
                {
                    HashSet<AllocationRoleKind> found;

                    if (!rolesCache.TryGetValue(code ?? "", out found))
                    {
                        found = rolesOf(code ?? "");
                        rolesCache[code ?? ""] = found;
                    }

                    return found;
                };

                Func<string, string> rolesTextOf = code =>
                    string.Join("، ", cachedRoles(code).Select(GetAllocationRoleTitle));

                Func<string, bool> isTelephoneOf = code =>
                    cachedRoles(code).Contains(AllocationRoleKind.Telephone);

                var settings = GetSmartAllocationSettings();

                Func<ValuunicPriorityResult3, bool> isSpecial = r => Convert.ToString(r.item3) == "ویژه";

                // جنوبی: نوع اشتراک از noeshtrak؛ سیستان: همان رقم اول شماره اشتراک.
                Func<ValuunicPriorityResult3, bool> isDemand = r =>
                    IsAllocationDemand(r.eshterak, r.noeshtrak);

                var ordered =
                    rows
                    .OrderBy(r => Convert.ToString(r.city))
                    .ThenBy(r => Convert.ToString(r.roz))
                    .ThenBy(r => NormalizeAllocationKey(r.eshterak), new NaturalKeyComparer())
                    .ToList();

                using (var workbook = new XLWorkbook())
                {
                    /* ---------- خلاصه مأموران (همه شهرستان‌ها) ---------- */

                    var ws = workbook.Worksheets.Add("خلاصه مامورین");
                    ws.RightToLeft = true;

                    var headers = new[]
                    {
                        "ردیف", "شهر", "نام مامور", "کد ملی", "کد پرسنلی", "شغل", "نقش‌ها",
                        "ظرفیت استاندارد", "بار تخصیص‌یافته", "درصد تکمیل", "تعداد پرونده", "تعداد دیماند",
                        "کمترین مبلغ پرونده", "بیشترین مبلغ پرونده", "میانگین مبلغ پرونده",
                        "مناطق مجاز", "روزکار ، تعرفه و محدوده‌ی مجاز هر منطقه", "علت عدم تخصیص"
                    };

                    for (int i = 0; i < headers.Length; i++)
                        ws.Cell(1, i + 1).Value = headers[i];

                    var header = ws.Range(1, 1, 1, headers.Length);
                    header.Style.Font.Bold = true;
                    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

                    // همه مأموران واجد شرایط شهرستان‌های موجود در خروجی همان روز خوانده می‌شوند؛
                    // حتی مأموری که در این روز هیچ پرونده‌ای نگرفته، باید با مقادیر صفر در خلاصه دیده شود.
                    var summaryCities = ordered
                        .Select(r => Convert.ToString(r.city))
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .GroupBy(c => NormalizeOrderExcelText(c))
                        .Select(g => g.First())
                        .OrderBy(c => c)
                        .ToList();

                    var allCityAgents = new List<KeyValuePair<string, AllocationAgent>>();
                    var cityAgentsByCity = new Dictionary<string, List<AllocationAgent>>();

                    // مناطق واقعی موجود در خروجی ذخیره‌شده هر شهر؛ برای اینکه نیروی منطقه‌ای
                    // خارج از اجرای جاری صرفاً با مقدار صفر وارد شیت خلاصه نشود.
                    var executedRegionsByCity = ordered
                        .Where(r => !isSpecial(r))
                        .Select(r => new
                        {
                            CityKey = NormalizeOrderExcelText(Convert.ToString(r.city)),
                            Region = ParseAllocationInt(r.item4)
                        })
                        .Where(x => x.Region.HasValue)
                        .GroupBy(x => x.CityKey)
                        .ToDictionary(
                            g => g.Key,
                            g => new HashSet<int>(g.Select(x => x.Region.Value)));

                    foreach (var cityName in summaryCities)
                    {
                        var cityKey = NormalizeOrderExcelText(cityName);
                        HashSet<int> executedRegions;

                        var cityRegions =
                            executedRegionsByCity.TryGetValue(cityKey, out executedRegions)
                            && executedRegions.Count > 0
                                ? new HashSet<int>(executedRegions)
                                : new HashSet<int>(GetAllocationRegionsForCity(cityName).Select(x => x.Key));

                        string ignoredAgentError;

                        var cityAgents = LoadAllocationAgents(
                            cityRegions,
                            GetCityValue(cityName),
                            cityName,
                            out ignoredAgentError) ?? new List<AllocationAgent>();

                        cityAgentsByCity[cityKey] = cityAgents;

                        foreach (var agent in cityAgents)
                            allCityAgents.Add(new KeyValuePair<string, AllocationAgent>(cityName, agent));
                    }

                    allCityAgents = allCityAgents
                        .GroupBy(x => NormalizeOrderExcelText(x.Key) + "|" + x.Value.UserId)
                        .Select(g => g.First())
                        .ToList();

                    var allocatedDayUsers = users.Values
                        .Select(u => new UnregisteredUserRow
                        {
                            UserId = u.usr_ID,
                            Name = u.usr_Name ?? "",
                            Family = u.usr_Family ?? "",
                            NationalCode = u.usr_NationalCode ?? "",
                            PersonalId = u.usr_Personal_ID.HasValue ? u.usr_Personal_ID.Value.ToString() : "",
                            JobTitle = GetAllocationJobTitle(u.usr_typeshoghl, jobTitles),
                            IsRegistered = true,
                            Roles = rolesTextOf(u.usr_NationalCode)
                        })
                        .ToList();

                    var eligibleDayUsers = allCityAgents
                        .Select(x => x.Value)
                        .GroupBy(a => a.UserId)
                        .Select(g => g.First())
                        .Select(a => new UnregisteredUserRow
                        {
                            UserId = a.UserId,
                            Name = a.FullName ?? "",
                            Family = "",
                            NationalCode = a.NationalCode ?? "",
                            PersonalId = a.PersonalId > 0 ? a.PersonalId.ToString() : "",
                            JobTitle = a.JobTitle ?? "",
                            IsRegistered = true,
                            Roles = string.Join("، ", a.Roles.Select(GetAllocationRoleTitle))
                        })
                        .ToList();

                    var dayUsers = allocatedDayUsers
                        .Concat(eligibleDayUsers)
                        .GroupBy(u => u.UserId)
                        .Select(g => g.Last())
                        .ToList();

                    var scopeRowsByCode =
                        GetRegisteredUserScopeRows(dayUsers)
                        .GroupBy(s => NormalizeAllocationIdKey(s.NationalCode))
                        .ToDictionary(g => g.Key, g => g.ToList());

                    // استاندارد هر شهرستان (یک بار برای هر شهر محاسبه می‌شود)
                    var standardCache = new Dictionary<string, CityStandard>();
                    var regionCache = new Dictionary<string, HashSet<int>>();

                    Func<string, HashSet<int>> regionsOfCity = cityName =>
                    {
                        var key = NormalizeOrderExcelText(cityName);
                        HashSet<int> found;

                        if (!regionCache.TryGetValue(key, out found))
                        {
                            found = new HashSet<int>(GetAllocationRegionsForCity(cityName).Select(x => x.Key));
                            regionCache[key] = found;
                        }

                        return found;
                    };

                    Func<string, CityStandard> standardOf = cityName =>
                    {
                        CityStandard found;

                        if (!standardCache.TryGetValue(cityName ?? "", out found))
                        {
                            var regionSetOfCity = regionsOfCity(cityName);

                            string ignored;

                            List<AllocationAgent> cityAgents;

                            if (!cityAgentsByCity.TryGetValue(NormalizeOrderExcelText(cityName), out cityAgents))
                            {
                                cityAgents = LoadAllocationAgents(
                                    regionSetOfCity,
                                    GetCityValue(cityName),
                                    cityName,
                                    out ignored) ?? new List<AllocationAgent>();

                                cityAgentsByCity[NormalizeOrderExcelText(cityName)] = cityAgents;
                            }

                            found = ComputeCityStandard(cityName, cityAgents);
                            standardCache[cityName ?? ""] = found;
                        }

                        return found;
                    };

                    var groups =
                        ordered
                        .Where(r => !isSpecial(r))
                        .GroupBy(r => new { City = Convert.ToString(r.city), Code = Convert.ToString(r.item3) })
                        .OrderBy(g => g.Key.City)
                        .ThenBy(g => g.First().roz)
                        .ToList();

                    int rowIndex = 2;

                    foreach (var g in groups)
                    {
                        var roles = rolesOf(g.Key.Code);
                        var cityStandard = standardOf(g.Key.City);

                        decimal capacity =
                            roles.Count == 0
                            ? 0
                            : roles.Max(role => cityStandard.CapacityFor(role));

                        decimal load =
                            g.Sum(r =>
                            {
                                decimal amount = r.P5 ?? 0m;

                                return isDemand(r) ? amount * settings.DemandWeight : amount;
                            });

                        ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                        ws.Cell(rowIndex, 2).Value = g.Key.City;
                        ws.Cell(rowIndex, 3).Value = Convert.ToString(g.First().roz);
                        ws.Cell(rowIndex, 4).Value = g.Key.Code;
                        ws.Cell(rowIndex, 5).Value = Convert.ToString(g.First().P1);
                        ws.Cell(rowIndex, 6).Value = jobOf(g.Key.Code);
                        ws.Cell(rowIndex, 7).Value = string.Join("، ", roles.Select(GetAllocationRoleTitle));
                        ws.Cell(rowIndex, 8).Value = Math.Round(capacity, 0);
                        ws.Cell(rowIndex, 9).Value = Math.Round(load, 0);
                        ws.Cell(rowIndex, 10).Value = capacity <= 0 ? 0 : Math.Round(load * 100m / capacity, 1);
                        ws.Cell(rowIndex, 11).Value = g.Count();
                        ws.Cell(rowIndex, 12).Value = g.Count(isDemand);

                        var amounts = g.Select(r => r.P5 ?? 0m).ToList();

                        ws.Cell(rowIndex, 13).Value = Math.Round(amounts.Min(), 0);
                        ws.Cell(rowIndex, 14).Value = Math.Round(amounts.Max(), 0);
                        ws.Cell(rowIndex, 15).Value = Math.Round(amounts.Average(), 0);

                        List<UserScopeSheetRow> scopeRows;

                        if (scopeRowsByCode.TryGetValue(NormalizeAllocationIdKey(g.Key.Code), out scopeRows) && scopeRows.Count > 0)
                        {
                            ws.Cell(rowIndex, 16).Value =
                                string.Join("، ", scopeRows.Select(s => s.Region).Distinct());

                            ws.Cell(rowIndex, 17).Value =
                                string.Join("\n", scopeRows.Select(ScopeRowText));
                        }
                        else
                        {
                            ws.Cell(rowIndex, 16).Value = "-";
                            ws.Cell(rowIndex, 17).Value = "-";
                        }

                        ws.Cell(rowIndex, 18).Value = "تخصیص با موفقیت انجام شده است.";

                        rowIndex++;
                    }

                    // مأموران واجد شرایطی که در این روز هیچ پرونده‌ای نگرفته‌اند.
                    Func<string, string, string> summaryAgentKey = (cityName, nationalCode) =>
                        NormalizeOrderExcelText(cityName) + "|" + NormalizeAllocationIdKey(nationalCode);

                    var assignedAgentKeys = new HashSet<string>(
                        groups.Select(g => summaryAgentKey(g.Key.City, g.Key.Code)),
                        StringComparer.OrdinalIgnoreCase);

                    var zeroAgents = allCityAgents
                        .Where(x => !assignedAgentKeys.Contains(summaryAgentKey(x.Key, x.Value.NationalCode)))
                        .OrderBy(x => x.Key)
                        .ThenBy(x => x.Value.FullName)
                        .ThenBy(x => x.Value.UserId)
                        .ToList();

                    // تشخیص علت فقط روی همان پرونده‌های ذخیره‌شده‌ی روز انجام می‌شود.
                    // بنابراین هیچ Query سنگینی به جدول منبع اضافه نمی‌شود و دانلود سریع می‌ماند.
                    var diagnosticRecordsByCity = ordered
                        .Where(x => !isSpecial(x))
                        .GroupBy(x => NormalizeOrderExcelText(Convert.ToString(x.city)))
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => new AllocationRecord
                            {
                                Amount = x.P5 ?? 0m,
                                Region = ParseAllocationInt(x.item4),
                                // در Result3 ستون roz نام مأمور است؛ روزکار اصلی در آن نگهداری نمی‌شود.
                                WorkDay = GetAllocationWorkDay(x.eshterak),
                                Area = GetAllocationMahdodeh(x.eshterak, x.item2),
                                Eshterak = NormalizeAllocationKey(x.eshterak),
                                Tariff = NormalizeAllocationKey(x.nametarefeh),
                                IsDemand = IsAllocationDemand(x.eshterak, x.noeshtrak),
                                HasMobile = !string.IsNullOrWhiteSpace(Convert.ToString(x.shomaremobile)),
                                Source = x
                            }).ToList());

                    Func<AllocationAgent, AllocationRecord, bool> roleAllowsForSummary = (agent, record) =>
                        MeetsJonobiDebtFloor(agent, record)
                        && (agent.Roles.Contains(AllocationRoleKind.Other)
                            || (record.IsDemand && agent.ServesDemand)
                            || (!record.IsDemand && agent.ServesNormal)
                            || (!record.IsDemand
                                && (IsJonobiContract() || record.HasMobile)
                                && agent.Roles.Contains(AllocationRoleKind.Telephone)));

                    Func<AllocationAgent, AllocationRecord, bool, bool, bool, bool> scopeAllowsForSummary =
                        (agent, record, checkDay, checkTariff, checkArea) =>
                        {
                            List<AgentRegionRule> rules;

                            return agent.TryGetRulesForRecord(record, out rules)
                                && rules.Any(rule => AllocationAgent.RuleAllows(
                                    rule,
                                    record,
                                    checkDay,
                                    checkTariff,
                                    checkArea));
                        };

                    Func<string, AllocationAgent, decimal, string> zeroAgentReason =
                        (cityName, agent, capacity) =>
                        {
                            if (capacity <= 0)
                                return "ظرفیت محاسبه‌شده برای این مأمور صفر است.";

                            List<AllocationRecord> cityRecords;

                            if (!diagnosticRecordsByCity.TryGetValue(
                                    NormalizeOrderExcelText(cityName),
                                    out cityRecords)
                                || cityRecords.Count == 0)
                                return "برای این شهر پرونده واجد شرایطی در منبع وجود ندارد.";

                            var roleMatches = cityRecords
                                .Where(record => roleAllowsForSummary(agent, record))
                                .ToList();

                            if (roleMatches.Count == 0)
                                return "هیچ پرونده‌ای با نقش‌های تعیین‌شده برای مأمور مطابقت ندارد.";

                            var regionMatches = roleMatches
                                .Where(record => record.Region.HasValue && agent.AllowsRegion(record.Region.Value))
                                .ToList();

                            if (regionMatches.Count == 0)
                                return "هیچ پرونده‌ای با مناطق مجاز مأمور مطابقت ندارد.";

                            var dayMatches = regionMatches
                                .Where(record => scopeAllowsForSummary(agent, record, true, false, false))
                                .ToList();

                            if (dayMatches.Count == 0)
                                return "هیچ پرونده‌ای با روزکارهای تنظیم‌شده مأمور مطابقت ندارد.";

                            var tariffMatches = dayMatches
                                .Where(record => scopeAllowsForSummary(agent, record, true, true, false))
                                .ToList();

                            if (tariffMatches.Count == 0)
                                return "هیچ پرونده‌ای با تعرفه‌های تنظیم‌شده مأمور مطابقت ندارد.";

                            var areaMatches = tariffMatches
                                .Where(record => scopeAllowsForSummary(agent, record, true, true, true))
                                .ToList();

                            if (areaMatches.Count == 0)
                                return "هیچ پرونده‌ای با محدوده‌های تنظیم‌شده مأمور مطابقت ندارد.";

                            return "پرونده منطبق وجود داشته، اما به علت اولویت، ظرفیت، ترتیب مراحل یا توازن بار به مأمور دیگری رسیده است.";
                        };

                    foreach (var item in zeroAgents)
                    {
                        var cityName = item.Key;
                        var agent = item.Value;
                        var cityStandard = standardOf(cityName);

                        decimal capacity = agent.Roles.Count == 0
                            ? 0
                            : agent.Roles.Max(role => cityStandard.CapacityFor(role));

                        ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                        ws.Cell(rowIndex, 2).Value = cityName;
                        ws.Cell(rowIndex, 3).Value = agent.FullName ?? "";
                        ws.Cell(rowIndex, 4).Value = agent.NationalCode ?? "";
                        ws.Cell(rowIndex, 5).Value = agent.PersonalId > 0 ? agent.PersonalId.ToString() : "";
                        ws.Cell(rowIndex, 6).Value = agent.JobTitle ?? "";
                        ws.Cell(rowIndex, 7).Value = string.Join("، ", agent.Roles.Select(GetAllocationRoleTitle));
                        ws.Cell(rowIndex, 8).Value = Math.Round(capacity, 0);
                        ws.Cell(rowIndex, 9).Value = 0;
                        ws.Cell(rowIndex, 10).Value = 0;
                        ws.Cell(rowIndex, 11).Value = 0;
                        ws.Cell(rowIndex, 12).Value = 0;
                        ws.Cell(rowIndex, 13).Value = 0;
                        ws.Cell(rowIndex, 14).Value = 0;
                        ws.Cell(rowIndex, 15).Value = 0;

                        List<UserScopeSheetRow> scopeRows;

                        if (scopeRowsByCode.TryGetValue(NormalizeAllocationIdKey(agent.NationalCode), out scopeRows) && scopeRows.Count > 0)
                        {
                            ws.Cell(rowIndex, 16).Value =
                                string.Join("، ", scopeRows.Select(s => s.Region).Distinct());

                            ws.Cell(rowIndex, 17).Value =
                                string.Join("\n", scopeRows.Select(ScopeRowText));
                        }
                        else
                        {
                            ws.Cell(rowIndex, 16).Value = "-";
                            ws.Cell(rowIndex, 17).Value = "-";
                        }

                        ws.Cell(rowIndex, 18).Value = zeroAgentReason(cityName, agent, capacity);

                        rowIndex++;
                    }

                    ws.Range(2, 4, Math.Max(2, rowIndex), 5).Style.NumberFormat.Format = "@";
                    ws.Column(8).Style.NumberFormat.Format = "#,##0";
                    ws.Column(9).Style.NumberFormat.Format = "#,##0";
                    ws.Column(13).Style.NumberFormat.Format = "#,##0";
                    ws.Column(14).Style.NumberFormat.Format = "#,##0";
                    ws.Column(15).Style.NumberFormat.Format = "#,##0";
                    ws.Columns().AdjustToContents();

                    ws.Column(17).Width = 80;
                    ws.Column(17).Style.Alignment.WrapText = true;
                    ws.Column(18).Width = 70;
                    ws.Column(18).Style.Alignment.WrapText = true;

                    /* ---------- شیت‌های پرونده (همه شهرستان‌ها سرهم) ---------- */

                    WriteDayRowsSheet(workbook.Worksheets.Add("عادی"),
                        ordered.Where(r => !isSpecial(r) && !isDemand(r)).ToList(), jobOf, rolesTextOf, isTelephoneOf);

                    WriteDayRowsSheet(workbook.Worksheets.Add("دیماند"),
                        ordered.Where(r => !isSpecial(r) && isDemand(r)).ToList(), jobOf, rolesTextOf, isTelephoneOf);

                    WriteDayRowsSheet(workbook.Worksheets.Add("ویژه"),
                        ordered.Where(isSpecial).ToList(), jobOf, rolesTextOf, isTelephoneOf);

                    // FileStreamResult خود stream را dispose می‌کند؛ ToArray در خروجی‌های بزرگ
                    // یک کپی کامل و غیرضروری از فایل در حافظه ایجاد می‌کرد.
                    var stream = new MemoryStream();
                    workbook.SaveAs(stream);
                    stream.Position = 0;

                    return File(
                        stream,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "Allocation_" + year + month.ToString("00") + day.ToString("00") + ".xlsx");
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to build the daily Excel file. Server error: " + ex.GetBaseException().Message
                });
            }
        }


        /* =========================================================
           TAB 8 - خروجی جداگانه‌ی پرونده‌های تخصیص‌نیافته
           ========================================================= */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportDayUnallocated(string sourceDate)
        {
            try
            {
                if (!GetCurrentUserId().HasValue)
                    return Json(new { success = false, message = "Access denied. Please sign in again." });

                int year, month, day;

                if (!TryParseAllocationDate(sourceDate, out year, out month, out day))
                    return Json(new { success = false, message = "Invalid date. Use a Jalali date such as 1405/08/12." });

                var sample = db.ValuunicPriorityResult3.AsNoTracking().FirstOrDefault();

                if (sample == null)
                    return Json(new { success = false, message = "No saved allocation exists in the system." });

                var y = DatePart(sample.yaer, year);
                var m = DatePart(sample.month, month);
                var dd = DatePart(sample.day, day);

                // از Result3 فقط دو کلید موردنیاز خوانده می‌شود.
                var allocatedRows = db.ValuunicPriorityResult3
                    .AsNoTracking()
                    .Where(x => x.yaer == y && x.month == m && x.day == dd)
                    .Select(x => new { x.city, x.valuunic })
                    .ToList();

                if (allocatedRows.Count == 0)
                    return Json(new { success = false, message = "No saved allocation was found for this date." });

                Func<string, object, string> recordKey = (cityName, billId) =>
                    NormalizeOrderExcelText(cityName)
                    + "|"
                    + NormalizeAllocationIdKey(billId);

                var allocatedKeys = new HashSet<string>(
                    allocatedRows.Select(x => recordKey(Convert.ToString(x.city), x.valuunic)),
                    StringComparer.OrdinalIgnoreCase);

                var cities = allocatedRows
                    .Select(x => Convert.ToString(x.city))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .GroupBy(x => NormalizeOrderExcelText(x))
                    .Select(g => g.First())
                    .ToList();

                var unallocatedRows = new List<ValuunicPriorityResult>();

                // Query به‌صورت ستونی و مرتب‌شده در SQL اجرا می‌شود؛ ردیف‌های تخصیص‌یافته
                // همان هنگام پیمایش حذف می‌شوند تا یک کپی کامل دیگر در حافظه ساخته نشود.
                var sourceQuery = db.ValuunicPriorityResult
                    .AsNoTracking()
                    .Where(x => cities.Contains(x.city) && x.P5 > 0)
                    .OrderBy(x => x.city)
                    .ThenBy(x => x.item4)
                    .ThenByDescending(x => x.P5)
                    .Select(x => new
                    {
                        x.city,
                        x.item1,
                        x.item4,
                        x.eshterak,
                        x.mahdodeh,
                        x.nametarefeh,
                        x.P5,
                        x.valuunic,
                        x.shenasehpardaght,
                        x.parvandeh,
                        x.shomaremobile
                    });

                foreach (var x in sourceQuery)
                {
                    if (allocatedKeys.Contains(recordKey(Convert.ToString(x.city), x.valuunic)))
                        continue;

                    unallocatedRows.Add(new ValuunicPriorityResult
                    {
                        city = x.city,
                        item1 = x.item1,
                        item4 = x.item4,
                        eshterak = x.eshterak,
                        mahdodeh = x.mahdodeh,
                        nametarefeh = x.nametarefeh,
                        P5 = x.P5,
                        valuunic = x.valuunic,
                        shenasehpardaght = x.shenasehpardaght,
                        parvandeh = x.parvandeh,
                        shomaremobile = x.shomaremobile
                    });
                }

                // فقط تعرفه‌های خالی، آن هم به‌صورت batch، از جدول مرجع تکمیل می‌شوند.
                FillMissingTariffs(unallocatedRows);

                using (var workbook = new XLWorkbook())
                {
                    WriteUnallocatedSheet(
                        workbook.Worksheets.Add("تخصیص نیافته"),
                        unallocatedRows);

                    var stream = new MemoryStream();
                    workbook.SaveAs(stream);
                    stream.Position = 0;

                    return File(
                        stream,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "Unallocated_" + year + month.ToString("00") + day.ToString("00") + ".xlsx");
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to build the unallocated Excel file. Server error: "
                        + ex.GetBaseException().Message
                });
            }
        }


        private Dictionary<int, HashSet<string>> LoadListValueSets(
            int userId,
            ICollection<int> countyCityIds,
            int listType)
        {
            var sets = new Dictionary<int, HashSet<string>>();

            if (countyCityIds == null || countyCityIds.Count == 0)
                return sets;

            var batches =
                db.tbOrderExcelImportBatch
                .Where(x => x.UserID == userId && x.IsActive == true)
                .ToList()
                .Where(x =>
                    countyCityIds.Contains(Convert.ToInt32(x.CityID))
                    && Convert.ToInt32(x.ListType) == listType)
                .Select(x => new { x.ID, x.ComponentType })
                .ToList();

            if (batches.Count == 0)
                return sets;

            var batchIds = batches.Select(x => x.ID).ToList();

            var values =
                db.tbOrderExcelImportValue
                .Where(x => batchIds.Contains(x.BatchID))
                .Select(x => new { x.BatchID, x.ValueText })
                .ToList();

            foreach (var b in batches)
            {
                if (!sets.ContainsKey(b.ComponentType))
                    sets[b.ComponentType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var v in values.Where(x => x.BatchID == b.ID))
                    sets[b.ComponentType].Add(NormalizeBalabariValue(b.ComponentType, v.ValueText));
            }

            return sets;
        }


        /* =========================================================
           EXCEL: شیت‌ها
           ========================================================= */

        private void WriteUnallocatedSheet(IXLWorksheet ws, List<ValuunicPriorityResult> rows)
        {
            ws.RightToLeft = true;

            var headers = new[]
            {
                "ردیف", "شهر", "شماره اشتراک", "نام مشترک", "شناسه قبض",
                "شناسه پرداخت", "شماره پرونده", "نوع اشتراک", "تعرفه",
                "منطقه", "روزکار", "ناحیه (محدوده)", "شماره موبایل", "مبلغ بدهی"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            // درج گروهی داده‌ها در ClosedXML بسیار سریع‌تر از Cell-by-Cell برای شیت‌های بزرگ است.
            var data = rows
                .Select((x, index) =>
                {
                    var workDay = GetAllocationWorkDay(x.eshterak, x.roz);
                    var mahdodeh = GetAllocationMahdodeh(x.eshterak, x.item2);

                    return new object[]
                    {
                        index + 1,
                        Convert.ToString(x.city),
                        Convert.ToString(x.eshterak),
                        Convert.ToString(x.item1),
                        Convert.ToString(x.valuunic),
                        Convert.ToString(x.shenasehpardaght),
                        Convert.ToString(x.parvandeh),
                        AllocationKindTitle(x.eshterak, x.noeshtrak),
                        Convert.ToString(x.nametarefeh),
                        Convert.ToString(x.item4),
                        workDay.HasValue ? workDay.Value.ToString() : "",
                        mahdodeh.HasValue
                            ? mahdodeh.Value.ToString("00")
                            : (IsJonobiContract() ? "" : Convert.ToString(x.mahdodeh)),
                        Convert.ToString(x.shomaremobile),
                        x.P5
                    };
                });

            if (rows.Count > 0)
                ws.Cell(2, 1).InsertData(data);

            int lastRow = Math.Max(2, rows.Count + 1);

            ws.Range(2, 2, lastRow, 13).Style.NumberFormat.Format = "@";
            ws.Column(14).Style.NumberFormat.Format = "#,##0";

            ws.SheetView.FreezeRows(1);

            FitColumns(ws, rows.Count);
        }


        /// <summary>ستون «نام مشترک» (item1) را قبل از ستون beforeColumn درج می‌کند</summary>
        private void InsertSubscriberNameColumn(
            IXLWorksheet ws,
            int beforeColumn,
            List<string> names,
            List<bool> highlight)
        {
            ws.Column(beforeColumn).InsertColumnsBefore(1);

            var head = ws.Cell(1, beforeColumn);

            head.Value = "نام مشترک";
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            for (int i = 0; i < names.Count; i++)
            {
                var cell = ws.Cell(i + 2, beforeColumn);

                cell.Value = names[i] ?? "";

                if (highlight != null && highlight[i])
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            }
        }


        private void WriteDayRowsSheet(
            IXLWorksheet ws,
            List<ValuunicPriorityResult3> rows,
            Func<string, string> jobOf,
            Func<string, string> rolesTextOf,
            Func<string, bool> isTelephoneOf)
        {
            ws.RightToLeft = true;

            var headers = new[]
            {
                "ردیف", "شهر", "اشتراک", "شناسه قبض", "شناسه پرداخت", "پرونده", "نوع اشتراک", "تعرفه",
                "منطقه", "روزکار", "محدوده", "موبایل", "مبلغ بدهی",
                "نام مامور", "کد ملی مامور", "کد پرسنلی", "شغل مامور",
                "نقش‌های مامور", IsJonobiContract() ? "مامور وصول مذاکره‌ای" : "مامور تلفنی"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            int rowIndex = 2;

            var names = new List<string>();
            var phoneRows = new List<bool>();

            foreach (var x in rows)
            {
                var code = Convert.ToString(x.item3);
                bool special = code == "ویژه";

                names.Add(Convert.ToString(x.item1));
                phoneRows.Add(!special && isTelephoneOf(code));

                ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                ws.Cell(rowIndex, 2).Value = Convert.ToString(x.city);
                ws.Cell(rowIndex, 3).Value = Convert.ToString(x.eshterak);
                ws.Cell(rowIndex, 4).Value = Convert.ToString(x.valuunic);
                ws.Cell(rowIndex, 5).Value = Convert.ToString(x.shenasehpardaght);
                ws.Cell(rowIndex, 6).Value = Convert.ToString(x.parvandeh);
                ws.Cell(rowIndex, 7).Value = AllocationKindTitle(x.eshterak, x.noeshtrak);
                ws.Cell(rowIndex, 8).Value = Convert.ToString(x.nametarefeh);
                ws.Cell(rowIndex, 9).Value = Convert.ToString(x.item4);

                // در Result3 ستون roz نام مأمور است؛ برای سازگاری خروجی روزانه از اشتراک خوانده می‌شود.
                var workDay = GetAllocationWorkDay(x.eshterak);
                ws.Cell(rowIndex, 10).Value = workDay.HasValue ? workDay.Value.ToString() : "";

                var mahdodeh = GetAllocationMahdodeh(x.eshterak, x.item2);
                ws.Cell(rowIndex, 11).Value = mahdodeh.HasValue
                    ? mahdodeh.Value.ToString("00")
                    : (IsJonobiContract() ? "" : Convert.ToString(x.mahdodeh));

                ws.Cell(rowIndex, 12).Value = Convert.ToString(x.shomaremobile);
                ws.Cell(rowIndex, 13).Value = x.P5 ?? 0m;

                ws.Cell(rowIndex, 14).Value = special ? "" : Convert.ToString(x.roz);
                ws.Cell(rowIndex, 15).Value = special ? "" : code;
                ws.Cell(rowIndex, 16).Value = special ? "" : Convert.ToString(x.P1);
                ws.Cell(rowIndex, 17).Value = special ? "" : jobOf(code);

                if (!special)
                {
                    bool telephone = isTelephoneOf(code);

                    ws.Cell(rowIndex, 18).Value = rolesTextOf(code);
                    ws.Cell(rowIndex, 19).Value = telephone ? "بله" : "";

                    if (telephone)
                        ws.Range(rowIndex, 1, rowIndex, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                }

                rowIndex++;
            }

            ws.Range(2, 3, Math.Max(2, rowIndex), 6).Style.NumberFormat.Format = "@";
            ws.Column(13).Style.NumberFormat.Format = "#,##0";

            InsertSubscriberNameColumn(ws, 4, names, phoneRows);

            FitColumns(ws, rows.Count);
        }


        private class UnallocatedAuditInfo
        {
            public string RoleAgents { get; set; }
            public string RegionAgents { get; set; }
            public string DayAgents { get; set; }
            public string TariffAgents { get; set; }
            public string AreaAgents { get; set; }
            public string ScopeDetails { get; set; }
            public string Reason { get; set; }
        }


        private bool MeetsJonobiDebtFloor(AllocationAgent agent, AllocationRecord record)
        {
            if (!IsJonobiContract())
                return true;

            bool negotiation = agent != null
                && agent.Roles != null
                && agent.Roles.Contains(AllocationRoleKind.Telephone);

            return record != null
                && record.Amount >= (negotiation ? 1000000m : 5000000m);
        }


        private bool CanAgentRoleReceive(AllocationAgent agent, AllocationRecord record)
        {
            if (agent == null || record == null || agent.Roles == null)
                return false;

            if (!MeetsJonobiDebtFloor(agent, record))
                return false;

            if (record.IsBalabari && agent.Roles.Contains(AllocationRoleKind.Balabari))
                return true;

            if (record.IsDemand && agent.ServesDemand)
                return true;

            if (!record.IsDemand && agent.ServesNormal)
                return true;

            if (!record.IsDemand
                && (IsJonobiContract() || record.HasMobile)
                && agent.Roles.Contains(AllocationRoleKind.Telephone))
                return true;

            return agent.Roles.Contains(AllocationRoleKind.Other);
        }


        private string AllocationAgentLabel(AllocationAgent agent)
        {
            var roles = agent.Roles == null
                ? ""
                : string.Join("، ", agent.Roles.Select(GetAllocationRoleTitle));

            return string.IsNullOrWhiteSpace(roles)
                ? agent.FullName
                : agent.FullName + " [" + roles + "]";
        }


        // با گروه‌ها: «روزکار» وقتی منطبق است که حداقل یکی از گروه‌های منطقه روزکار پرونده را بپذیرد
        private bool AgentAllowsRecordDay(AllocationAgent agent, AllocationRecord record)
        {
            List<AgentRegionRule> rules;

            if (agent == null || record == null || !agent.TryGetRulesForRecord(record, out rules))
                return false;

            return rules.Any(r => AllocationAgent.RuleAllows(r, record, true, false, false));
        }


        // «تعرفه» = حداقل یک گروه که روزکار و تعرفه‌ی پرونده را بپذیرد
        private bool AgentAllowsRecordTariff(AllocationAgent agent, AllocationRecord record)
        {
            List<AgentRegionRule> rules;

            if (agent == null || record == null || !agent.TryGetRulesForRecord(record, out rules))
                return false;

            return rules.Any(r => AllocationAgent.RuleAllows(r, record, true, true, false));
        }


        // «محدوده» = حداقل یک گروه که روزکار ، تعرفه و محدوده‌ی پرونده را بپذیرد
        private bool AgentAllowsRecordArea(AllocationAgent agent, AllocationRecord record)
        {
            List<AgentRegionRule> rules;

            if (agent == null || record == null || !agent.TryGetRulesForRecord(record, out rules))
                return false;

            return rules.Any(r => AllocationAgent.RuleAllows(r, record, true, true, true));
        }


        private string AgentScopeForRecord(AllocationAgent agent, AllocationRecord record)
        {
            if (agent == null || record == null)
                return "-";

            List<AgentRegionRule> rules;

            if (!agent.TryGetRulesForRecord(record, out rules))
            {
                if (!record.Region.HasValue)
                    return AllocationAgentLabel(agent) + ": منطقه پرونده قابل تشخیص نیست";

                return AllocationAgentLabel(agent)
                    + ": منطقه " + record.Region.Value + " جزو مناطق مجاز مامور نیست";
            }

            Func<AgentRegionRule, string> describe = rule =>
                "روزکار " + (rule.Days == null ? "همه" : CompressDayList(rule.Days))
                + " | تعرفه " + (rule.Tariffs == null ? "همه" : string.Join("، ", rule.Tariffs.OrderBy(x => x)))
                + " | محدوده " + (rule.Areas == null ? "همه" : CompressDayList(rule.Areas));

            var groupsText = rules.Count == 1
                ? describe(rules[0])
                : string.Join(" یا ", rules.Select(r => "[" + describe(r) + "]"));

            return AllocationAgentLabel(agent)
                + ": " + (agent.Regions == null ? "همه مناطق" : "منطقه " + record.Region)
                + " | " + groupsText;
        }


        /// <summary>مسیر تطابق پرونده تخصیص‌نیافته: نقش (تب ۱) ← منطقه ← روزکار ← تعرفه ← محدوده (تب ۲)</summary>
        private UnallocatedAuditInfo BuildUnallocatedAudit(
            AllocationRecord record,
            IEnumerable<AllocationAgent> sourceAgents)
        {
            var agents = (sourceAgents ?? Enumerable.Empty<AllocationAgent>()).ToList();

            var roleAgents = agents
                .Where(a => CanAgentRoleReceive(a, record))
                .OrderBy(a => a.FullName)
                .ToList();

            var regionAgents = roleAgents
                .Where(a => record.Region.HasValue && a.AllowsRegion(record.Region.Value))
                .ToList();

            var dayAgents = regionAgents.Where(a => AgentAllowsRecordDay(a, record)).ToList();
            var tariffAgents = dayAgents.Where(a => AgentAllowsRecordTariff(a, record)).ToList();
            var areaAgents = tariffAgents.Where(a => AgentAllowsRecordArea(a, record)).ToList();

            Func<IEnumerable<AllocationAgent>, string> names = list =>
            {
                var values = list.Select(AllocationAgentLabel).Distinct().ToList();
                return values.Count == 0 ? "-" : string.Join("\n", values);
            };

            string reason;

            if (roleAgents.Count == 0)
                reason = "در تب ۱ نیرویی با نقش مناسب این پرونده ثبت نشده است.";
            else if (!record.Region.HasValue)
                reason = "منطقه پرونده قابل تشخیص نیست.";
            else if (regionAgents.Count == 0)
                reason = "منطقه پرونده برای هیچ نیروی دارای نقش مناسب در تب ۲ مجاز نیست.";
            else if (!record.WorkDay.HasValue)
                reason = "روزکار پرونده قابل تشخیص نیست.";
            else if (dayAgents.Count == 0)
                reason = "روزکار پرونده با تنظیم تب ۲ نیروهای این منطقه منطبق نیست.";
            else if (tariffAgents.Count == 0)
                reason = "نوع تعرفه پرونده با تنظیم تب ۲ نیروها منطبق نیست.";
            else if (areaAgents.Count == 0)
                reason = "محدوده (رقم ۶ و ۷ اشتراک) پرونده با تنظیم تب ۲ نیروها منطبق نیست.";
            else if (areaAgents.All(a => a.CountLimitReached))
                reason = "سقف تعداد پرونده همه نیروهای منطبق تکمیل شده است (تعداد روز × ۶۰).";
            else if (areaAgents.All(a => a.RemainingCapacity <= 0))
                reason = "نیروی منطبق وجود دارد، ولی ظرفیت همه نیروهای منطبق تکمیل شده است (پرونده‌های با مبلغ بالاتر زودتر تخصیص یافتند).";
            else
                reason = "نیروی منطبق وجود دارد؛ پرونده به علت ترتیب فازها، توازن بار یا ظرفیت باقیمانده تخصیص نیافته است.";

            return new UnallocatedAuditInfo
            {
                RoleAgents = names(roleAgents),
                RegionAgents = names(regionAgents),
                DayAgents = names(dayAgents),
                TariffAgents = names(tariffAgents),
                AreaAgents = names(areaAgents),
                ScopeDetails = roleAgents.Count == 0
                    ? "-"
                    : string.Join("\n", roleAgents.Select(a => AgentScopeForRecord(a, record))),
                Reason = reason
            };
        }


        private List<AllocationAgent> LoadAllocationAuditAgents(SmartAllocationContext ctx)
        {
            var regionCodes = ctx.SelectedRegions != null && ctx.SelectedRegions.Count > 0
                ? new HashSet<int>(ctx.SelectedRegions)
                : new HashSet<int>(GetAllocationRegionsForCity(ctx.SourceCity).Select(x => x.Key));

            string ignoredError;

            var auditAgents = LoadAllocationAgents(
                regionCodes,
                ctx.CityValue,
                ctx.TargetCity,
                out ignoredError) ?? new List<AllocationAgent>();

            ApplyCityStandard(auditAgents, ctx.Standard ?? ComputeCityStandard(ctx.SourceCity, auditAgents));

            // نیروهای گزارش تازه بارگذاری شده‌اند (بار صفر) ؛ بار واقعی بعد از تخصیص را از نتیجه‌ی اجرا برمی‌داریم
            if (ctx.Result != null && ctx.Result.Agents != null)
            {
                var finalLoad = ctx.Result.Agents
                    .GroupBy(x => x.UserId)
                    .ToDictionary(g => g.Key, g => g.First().Load);

                foreach (var a in auditAgents)
                {
                    decimal load;

                    if (finalLoad.TryGetValue(a.UserId, out load))
                        a.Load = load;
                }
            }

            return auditAgents;
        }


        /// <summary>در شیت‌های بزرگ AdjustToContents بسیار کند است و فایل را سنگین می‌کند</summary>
        private void FitColumns(IXLWorksheet ws, int rowCount)
        {
            if (rowCount <= 3000)
            {
                ws.Columns().AdjustToContents();
                return;
            }

            foreach (var col in ws.ColumnsUsed())
                col.Width = 16;
        }


        /// <summary>پرونده‌هایی که منطقه/روزکار/محدوده/تعرفه/نوع یکسان دارند علت یکسان هم دارند</summary>
        private string UnallocatedSignature(AllocationRecord r)
        {
            return string.Join("|", new object[]
            {
                r.Region, r.WorkDay, r.Tariff, r.Area,
                r.IsDemand ? 1 : 0, r.HasMobile ? 1 : 0, r.IsBalabari ? 1 : 0, r.ExcludeReason
            });
        }


        /// <summary>
        /// شیت ممیزی کات:
        /// بخش اول حد واقعی مبلغ کات را برای هر فاز/دور نشان می‌دهد؛
        /// بخش دوم تک‌تک پرونده‌ها را به «داخل کات» و «اصلاً/نهایتاً خارج کات» تفکیک می‌کند.
        /// </summary>
        private void WriteCutAuditSheet(IXLWorksheet ws, SmartAllocationContext ctx)
        {
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value =
                "کات مبلغ ثابت ندارد؛ پرونده‌ها نزولی مرتب می‌شوند و تا سقف ظرفیت خالی وزنی همان فاز/دور وارد کات می‌شوند.";
            ws.Range(1, 1, 1, 12).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");

            var logHeaders = new[]
            {
                "فاز", "دور", "تعداد نیروهای هدف", "تعداد کاندید", "ظرفیت کات (وزنی)",
                "تعداد داخل کات", "مجموع وزن داخل کات", "بیشترین مبلغ داخل کات",
                "کمترین مبلغ داخل کات", "بیشترین مبلغ کاندید خارج کات",
                "تعداد تخصیص", "تعداد نیروهای دارای کسری پس از دور"
            };

            for (int i = 0; i < logHeaders.Length; i++)
                ws.Cell(3, i + 1).Value = logHeaders[i];

            ws.Range(3, 1, 3, logHeaders.Length).Style.Font.Bold = true;
            ws.Range(3, 1, 3, logHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            var logs = ctx.Result.Logs ?? new List<AllocationPhaseLog>();

            if (logs.Count > 0)
            {
                ws.Cell(4, 1).InsertData(logs.Select(l => new object[]
                {
                    l.PhaseTitle,
                    l.Round,
                    l.TargetAgents,
                    l.Candidates,
                    l.Gap,
                    l.CutCount,
                    l.CutWeight,
                    l.MaxCutAmount,
                    l.MinCutAmount,
                    l.MaxNotCutCandidateAmount,
                    l.AssignedCount,
                    l.DeficitAgentsAfter
                }));
            }

            int detailHeaderRow = 5 + logs.Count;

            var detailHeaders = new[]
            {
                "وضعیت کات", "تخصیص شد؟", "اولین فاز ورود به کات", "اولین دور کات",
                "ظرفیت کات اولین دور", "تعداد دفعات کاندید", "تعداد دفعات ورود به کات",
                "مبلغ بدهی", "وزن محاسباتی", "شناسه قبض", "اشتراک", "نوع اشتراک",
                "منطقه", "روزکار", "محدوده", "تعرفه", "مامور تخصیص‌یافته",
                "فاز تخصیص", "دور تخصیص", "آخرین فاز کاندید", "آخرین دور کاندید", "توضیح"
            };

            for (int i = 0; i < detailHeaders.Length; i++)
                ws.Cell(detailHeaderRow, i + 1).Value = detailHeaders[i];

            ws.Range(detailHeaderRow, 1, detailHeaderRow, detailHeaders.Length).Style.Font.Bold = true;
            ws.Range(detailHeaderRow, 1, detailHeaderRow, detailHeaders.Length)
                .Style.Fill.BackgroundColor = XLColor.FromHtml("#E2F0D9");

            var result = ctx.Result;
            var noPermissionSet = new HashSet<AllocationRecord>(result.NoPermission ?? new List<AllocationRecord>());

            var allRecords =
                (result.Allocated ?? new List<AllocationRecord>())
                .Concat(result.Unallocated ?? new List<AllocationRecord>())
                .Concat(result.NoPermission ?? new List<AllocationRecord>())
                .Concat(ctx.RemovedRecords ?? new List<AllocationRecord>())
                .ToList();

            if (ctx.SpecialRecords != null)
            {
                allRecords.AddRange(ctx.SpecialRecords.Select(x => new AllocationRecord
                {
                    Id = Convert.ToInt64(x.ID),
                    Amount = x.P5,
                    Region = ParseAllocationInt(x.item4),
                    WorkDay = GetAllocationWorkDay(x.eshterak, x.roz),
                    Area = GetAllocationMahdodeh(x.eshterak, x.item2),
                    Eshterak = NormalizeAllocationKey(x.eshterak),
                    Tariff = NormalizeAllocationKey(x.nametarefeh),
                    IsDemand = IsAllocationDemand(x),
                    HasMobile = !string.IsNullOrWhiteSpace(Convert.ToString(x.shomaremobile)),
                    ExcludeReason = "پرونده ویژه است و وارد موتور کات نشده است.",
                    Source = x
                }));
            }

            allRecords = allRecords
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .OrderByDescending(r => r.EnteredCut)
                .ThenByDescending(r => r.Amount)
                .ToList();

            Func<AllocationRecord, string> cutStatus = r =>
                r.EnteredCut
                    ? "داخل کات"
                    : (r.WasCutCandidate
                        ? "کاندید بود ولی داخل کات نیامد"
                        : "اصلاً داخل کات نیامد");

            var detailData = allRecords.Select(r =>
            {
                var source = r.Source as ValuunicPriorityResult;
                decimal effectiveWeight = r.Weight > 0
                    ? r.Weight
                    : (r.IsDemand ? r.Amount * ctx.Settings.DemandWeight : r.Amount);

                string explanation = r.ExcludeReason;

                if (string.IsNullOrWhiteSpace(explanation) && noPermissionSet.Contains(r))
                    explanation = "نیروی دارای نقش و محدوده مجاز برای پرونده پیدا نشد.";
                else if (string.IsNullOrWhiteSpace(explanation) && r.WasCutCandidate && !r.EnteredCut)
                    explanation = "کاندید بود، اما با ظرفیت کات یا ظرفیت باقیمانده هیچ مأمور منطبق جا نشد.";
                else if (string.IsNullOrWhiteSpace(explanation) && !r.WasCutCandidate)
                    explanation = "به مرحله کاندیدای کات نرسید.";

                return new object[]
                {
                    cutStatus(r),
                    r.Agent == null ? "خیر" : "بله",
                    r.FirstCutPhaseTitle ?? "",
                    r.FirstCutRound.HasValue ? (object)r.FirstCutRound.Value : "",
                    r.FirstCutGap.HasValue ? (object)r.FirstCutGap.Value : "",
                    r.CutCandidateCount,
                    r.CutEntryCount,
                    r.Amount,
                    effectiveWeight,
                    source == null ? "" : Convert.ToString(source.valuunic),
                    r.Eshterak ?? "",
                    r.IsDemand ? "دیماند" : "عادی",
                    r.Region.HasValue ? r.Region.Value.ToString() : "",
                    r.WorkDay.HasValue ? r.WorkDay.Value.ToString() : "",
                    r.Area.HasValue ? r.Area.Value.ToString("00") : "",
                    r.Tariff ?? "",
                    r.Agent == null ? "" : r.Agent.FullName,
                    r.PhaseKey ?? "",
                    r.Round > 0 ? (object)r.Round : "",
                    r.LastCandidatePhaseTitle ?? "",
                    r.LastCandidateRound.HasValue ? (object)r.LastCandidateRound.Value : "",
                    explanation ?? ""
                };
            });

            if (allRecords.Count > 0)
                ws.Cell(detailHeaderRow + 1, 1).InsertData(detailData);

            int detailLastRow = Math.Max(detailHeaderRow + 1, detailHeaderRow + allRecords.Count);
            ws.Range(detailHeaderRow + 1, 8, detailLastRow, 9).Style.NumberFormat.Format = "#,##0";
            ws.Range(4, 5, Math.Max(4, 3 + logs.Count), 10).Style.NumberFormat.Format = "#,##0";
            ws.SheetView.FreezeRows(detailHeaderRow);
            ws.Range(detailHeaderRow, 1, detailLastRow, detailHeaders.Length).SetAutoFilter();

            foreach (var col in ws.ColumnsUsed())
                col.Width = Math.Min(35, Math.Max(12, col.Width));

            ws.Column(22).Width = 60;
            ws.Column(22).Style.Alignment.WrapText = true;
        }


        private void WriteSmartAllocationSheet(
            IXLWorksheet ws,
            List<AllocationRecord> rows,
            List<AllocationAgent> auditAgents = null)
        {
            ws.RightToLeft = true;

            // ستون «نام مشترک» جای ستون 3 است ؛ بدون InsertColumn (که در شیت بزرگ بسیار کند است) مستقیم نوشته می‌شود
            Func<int, int> c = n => n >= 3 ? n + 1 : n;

            var headers = new List<string>
            {
                "ردیف", "اشتراک", "نام مشترک", "شناسه قبض", "شناسه پرداخت", "پرونده", "نوع اشتراک", "تعرفه",
                "منطقه", "روزکار", "محدوده", "موبایل", "مبلغ بدهی",
                "نام مامور", "کد ملی مامور", "کد پرسنلی", "شغل مامور", "مرحله", "دور تخصیص",
                "نقش‌های مامور", IsJonobiContract() ? "مامور وصول مذاکره‌ای" : "مامور تلفنی"
            };

            bool includeAudit = auditAgents != null;

            // فقط «علت» (کوتاه) ؛ جزئیات در شیت «علت تخصیص نیافته (خلاصه)» به‌صورت گروه‌بندی‌شده است
            if (includeAudit)
                headers.Add("علت تخصیص‌نیافتن");

            for (int i = 0; i < headers.Count; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Count);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            int rowIndex = 2;
            var auditCache = new Dictionary<string, string>();

            foreach (var r in rows)
            {
                if (rowIndex > 1048000)
                    break;

                var x = (ValuunicPriorityResult)r.Source;

                bool telephone = r.Agent != null && r.Agent.Roles.Contains(AllocationRoleKind.Telephone);

                ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                ws.Cell(rowIndex, c(2)).Value = Convert.ToString(x.eshterak);
                ws.Cell(rowIndex, 3).Value = Convert.ToString(x.item1) ?? "";
                ws.Cell(rowIndex, c(3)).Value = Convert.ToString(x.valuunic);
                ws.Cell(rowIndex, c(4)).Value = Convert.ToString(x.shenasehpardaght);
                ws.Cell(rowIndex, c(5)).Value = Convert.ToString(x.parvandeh);
                ws.Cell(rowIndex, c(6)).Value = AllocationKindTitle(x.eshterak, x.noeshtrak);
                ws.Cell(rowIndex, c(7)).Value = x.nametarefeh;
                ws.Cell(rowIndex, c(8)).Value = Convert.ToString(x.item4);

                var workDay = GetAllocationWorkDay(x.eshterak, x.roz);
                ws.Cell(rowIndex, c(9)).Value = workDay.HasValue ? workDay.Value.ToString() : "";

                var mahdodeh = GetAllocationMahdodeh(x.eshterak, x.item2);
                ws.Cell(rowIndex, c(10)).Value = mahdodeh.HasValue
                    ? mahdodeh.Value.ToString("00")
                    : (IsJonobiContract() ? "" : Convert.ToString(x.mahdodeh));

                ws.Cell(rowIndex, c(11)).Value = Convert.ToString(x.shomaremobile);
                ws.Cell(rowIndex, c(12)).Value = x.P5;

                if (r.Agent != null)
                {
                    ws.Cell(rowIndex, c(13)).Value = r.Agent.FullName;
                    ws.Cell(rowIndex, c(14)).Value = r.Agent.NationalCode;
                    ws.Cell(rowIndex, c(15)).Value = r.Agent.PersonalId.ToString();
                    ws.Cell(rowIndex, c(16)).Value = r.Agent.JobTitle ?? "";
                    ws.Cell(rowIndex, c(19)).Value = string.Join("، ", r.Agent.Roles.Select(GetAllocationRoleTitle));
                    ws.Cell(rowIndex, c(20)).Value = telephone ? "بله" : "";
                }

                ws.Cell(rowIndex, c(17)).Value = GetAllocationPhaseTitle(r.PhaseKey);
                ws.Cell(rowIndex, c(18)).Value = r.Round > 0 ? r.Round.ToString() : "";

                if (telephone)
                {
                    ws.Range(rowIndex, 1, rowIndex, headers.Count)
                        .Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                }

                if (includeAudit)
                {
                    var signature = UnallocatedSignature(r);
                    string reason;

                    if (!auditCache.TryGetValue(signature, out reason))
                    {
                        reason = r.ExcludeReason ?? BuildUnallocatedAudit(r, auditAgents).Reason;
                        auditCache[signature] = reason;
                    }

                    ws.Cell(rowIndex, c(21)).Value = reason;
                }

                rowIndex++;
            }

            ws.Range(2, 2, Math.Max(2, rowIndex), 2).Style.NumberFormat.Format = "@";
            ws.Range(2, 4, Math.Max(2, rowIndex), 6).Style.NumberFormat.Format = "@";
            ws.Column(c(12)).Style.NumberFormat.Format = "#,##0";

            FitColumns(ws, rows.Count);

            if (includeAudit)
                ws.Column(c(21)).Width = 80;

            ws.SheetView.FreezeRows(1);

            if (rows.Count <= 100000)
            {
                var usedRange = ws.RangeUsed();
                if (usedRange != null)
                    usedRange.SetAutoFilter();
            }
        }


        /// <summary>
        /// شیت «علت تخصیص نیافته (خلاصه)» : پرونده‌های تخصیص‌نیافته بر اساس
        /// (منطقه ، روزکار ، محدوده ، تعرفه ، نوع) گروه‌بندی می‌شوند ؛ هر گروه یک ردیف با تعداد و جمع مبلغ.
        /// </summary>
        private void WriteUnallocatedReasonSheet(
            IXLWorksheet ws,
            List<AllocationRecord> rows,
            List<AllocationAgent> auditAgents)
        {
            ws.RightToLeft = true;

            var headers = new[]
            {
                "ردیف", "علت", "منطقه", "روزکار", "محدوده", "تعرفه", "نوع اشتراک", "موبایل", "بالابری",
                "تعداد پرونده", "جمع مبلغ بدهی",
                "نیروهای دارای نقش مناسب (تب ۱)", "منطبق با منطقه (تب ۲)", "منطبق با روزکار (تب ۲)",
                "منطبق با تعرفه (تب ۲)", "منطبق با محدوده (تب ۲)", "تنظیمات نیروهای منطبق"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            Func<string, string> shorten = text =>
            {
                text = (text ?? "").Replace("\n", " ، ");

                return text.Length > 400 ? text.Substring(0, 400) + " …" : text;
            };

            var groups =
                rows
                .GroupBy(UnallocatedSignature)
                .Select(g => new { First = g.First(), Count = g.Count(), Amount = g.Sum(r => r.Amount) })
                .OrderByDescending(g => g.Amount)
                .Take(20000)
                .ToList();

            int rowIndex = 2;

            foreach (var g in groups)
            {
                var r = g.First;
                var audit = r.ExcludeReason != null
                    ? new UnallocatedAuditInfo
                    {
                        RoleAgents = "-",
                        RegionAgents = "-",
                        DayAgents = "-",
                        TariffAgents = "-",
                        AreaAgents = "-",
                        ScopeDetails = "-",
                        Reason = r.ExcludeReason
                    }
                    : BuildUnallocatedAudit(r, auditAgents);

                ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                ws.Cell(rowIndex, 2).Value = audit.Reason;
                ws.Cell(rowIndex, 3).Value = r.Region.HasValue ? r.Region.Value.ToString() : "";
                ws.Cell(rowIndex, 4).Value = r.WorkDay.HasValue ? r.WorkDay.Value.ToString() : "";
                ws.Cell(rowIndex, 5).Value = r.Area.HasValue ? r.Area.Value.ToString("00") : "";
                ws.Cell(rowIndex, 6).Value = r.Tariff ?? "";
                ws.Cell(rowIndex, 7).Value = r.IsDemand ? "دیماند" : "عادی";
                ws.Cell(rowIndex, 8).Value = r.HasMobile ? "دارد" : "";
                ws.Cell(rowIndex, 9).Value = r.IsBalabari ? "بله" : "";
                ws.Cell(rowIndex, 10).Value = g.Count;
                ws.Cell(rowIndex, 11).Value = Math.Round(g.Amount, 0);
                ws.Cell(rowIndex, 12).Value = shorten(audit.RoleAgents);
                ws.Cell(rowIndex, 13).Value = shorten(audit.RegionAgents);
                ws.Cell(rowIndex, 14).Value = shorten(audit.DayAgents);
                ws.Cell(rowIndex, 15).Value = shorten(audit.TariffAgents);
                ws.Cell(rowIndex, 16).Value = shorten(audit.AreaAgents);
                ws.Cell(rowIndex, 17).Value = shorten(audit.ScopeDetails);

                rowIndex++;
            }

            ws.Column(11).Style.NumberFormat.Format = "#,##0";

            FitColumns(ws, groups.Count);

            ws.Column(2).Width = 60;

            ws.SheetView.FreezeRows(1);
        }


        private void WriteSmartAllocationSummarySheet(IXLWorksheet ws, SmartAllocationContext ctx)
        {
            ws.RightToLeft = true;

            var headers = new[]
            {
                "ردیف", "نام مامور", "کد ملی", "شغل", "نقش‌ها", "مناطق مجاز", "روزکار ، تعرفه و محدوده مجاز",
                "درجه آزادی", "ظرفیت استاندارد", "بار تخصیص‌یافته", "درصد تکمیل",
                "تعداد پرونده", "تعداد دیماند",
                "کمترین مبلغ پرونده", "بیشترین مبلغ پرونده", "میانگین مبلغ پرونده", "سقف تعداد پرونده"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            int rowIndex = 2;

            foreach (var a in ctx.Result.Agents.OrderBy(a => a.Freedom).ThenBy(a => a.FullName))
            {
                ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                ws.Cell(rowIndex, 2).Value = a.FullName;
                ws.Cell(rowIndex, 3).Value = a.NationalCode;
                ws.Cell(rowIndex, 4).Value = a.JobTitle ?? "";
                ws.Cell(rowIndex, 5).Value = string.Join("، ", a.Roles.Select(GetAllocationRoleTitle));
                ws.Cell(rowIndex, 6).Value = a.Regions == null ? "همه" : string.Join("، ", a.Regions.OrderBy(x => x));
                ws.Cell(rowIndex, 7).Value = a.ScopeDescription(CompressDayList);
                ws.Cell(rowIndex, 8).Value = a.Freedom;
                ws.Cell(rowIndex, 9).Value = Math.Round(a.Capacity, 0);
                ws.Cell(rowIndex, 10).Value = Math.Round(a.Load, 0);
                ws.Cell(rowIndex, 11).Value = a.FillPercent;
                ws.Cell(rowIndex, 12).Value = a.Records.Count;
                ws.Cell(rowIndex, 13).Value = a.Records.Count(x => x.IsDemand);

                ws.Cell(rowIndex, 14).Value = a.Records.Count > 0 ? Math.Round(a.Records.Min(x => x.Amount), 0) : 0m;
                ws.Cell(rowIndex, 15).Value = a.Records.Count > 0 ? Math.Round(a.Records.Max(x => x.Amount), 0) : 0m;
                ws.Cell(rowIndex, 16).Value = a.Records.Count > 0 ? Math.Round(a.Records.Average(x => x.Amount), 0) : 0m;
                if (a.RecordLimit.HasValue) ws.Cell(rowIndex, 17).Value = a.RecordLimit.Value;

                rowIndex++;
            }

            ws.Range(2, 3, Math.Max(2, rowIndex), 3).Style.NumberFormat.Format = "@";
            ws.Column(9).Style.NumberFormat.Format = "#,##0";
            ws.Column(10).Style.NumberFormat.Format = "#,##0";
            ws.Column(14).Style.NumberFormat.Format = "#,##0";
            ws.Column(15).Style.NumberFormat.Format = "#,##0";
            ws.Column(16).Style.NumberFormat.Format = "#,##0";

            ws.Columns().AdjustToContents();
        }


        /* =====================================================================
           بخش ۷ : موتور الگوریتم (کلاس‌های تو در تو)
           ---------------------------------------------------------------------
           مراحل (برای هر فاز نقش):
             ۱. فیلتر مجوزها         : فقط پرونده‌هایی که حداقل یک مأمور مجاز
                                        (نقش + منطقه + یکی از گروه‌های روزکار/تعرفه/محدوده) دارند
             ۲. اولویت‌بندی مالی      : مرتب‌سازی نزولی بر اساس مبلغ بدهی
             ۳. برش سهمیه‌ای (Cutoff) : جدا کردن پرونده‌های برتر به اندازه مجموع ظرفیت خالی
             ۴. خوشه‌بندی مسیر        : مرتب‌سازی مجدد بر اساس شماره اشتراک
             ۵. کمترین درجه آزادی     : هر پرونده به مأمور مجازی با کمترین دامنه می‌رسد
             ۶. حلقه تکمیلی           : تکرار ۱ تا ۵ برای مأموران دارای کسری
           ===================================================================== */

        public enum AllocationRoleKind
        {
            None = 0,
            Normal = 1,             // عادی
            Demand = 2,             // دیماند
            NormalAndDemand = 3,    // دیماند/عادی
            Telephone = 4,          // تلفنی
            Balabari = 5,           // بالابری
            Other = 6               // سایر
        }


        public class AllocationSettings
        {
            public decimal DemandWeight { get; set; } = 0.5m;
            public decimal CompleteThreshold { get; set; } = 0.99m;
            public decimal MaxOverflow { get; set; } = 1.0m;
            public decimal CutoffFactor { get; set; } = 1.0m;
            public int MaxRounds { get; set; } = 10;
            public bool DemandFallbackToNormal { get; set; } = true;
        }


        /// <summary>روزکار ، تعرفه و محدوده مجاز یک «گروه» از یک منطقه (null = همه)</summary>
        public class AgentRegionRule
        {
            public HashSet<int> Days { get; set; }
            public HashSet<string> Tariffs { get; set; }

            /// <summary>محدوده (کد ناحیه ۰ تا ۴۵ = رقم ۶ و ۷ اشتراک)</summary>
            public HashSet<int> Areas { get; set; }
        }


        public class AllocationAgent
        {
            public int UserId { get; set; }
            public string FullName { get; set; }
            public string NationalCode { get; set; }
            public int PersonalId { get; set; }

            public int? JobId { get; set; }
            public string JobTitle { get; set; }

            public HashSet<AllocationRoleKind> Roles { get; set; } = new HashSet<AllocationRoleKind>();

            /// <summary>مناطق مجاز مامور (تب ۲). null = همه مناطق (بدون محدودیت)</summary>
            public HashSet<int> Regions { get; set; }

            /// <summary>قانون عمومی (RegionCode = NULL) — برای مامور بدون منطقه مشخص</summary>
            public AgentRegionRule DefaultRule { get; set; } = new AgentRegionRule();

            /// <summary>
            /// گروه‌های هر منطقه (کلید = کد منطقه). هر گروه یک قانون مستقل است؛
            /// پرونده وقتی مجاز است که حداقل یکی از گروه‌های منطقه‌اش را کامل رعایت کند.
            /// </summary>
            public Dictionary<int, List<AgentRegionRule>> RegionRules { get; set; } = new Dictionary<int, List<AgentRegionRule>>();

            // جنوبی: سقف تعداد مستقل از مبلغ و مشترک بین همه نقش‌ها و دورهاست.
            public long? RecordLimit { get; set; }
            public bool CountLimitReached
            {
                get { return RecordLimit.HasValue && Records.Count >= RecordLimit.Value; }
            }

            public decimal Capacity { get; set; }
            public decimal Load { get; set; }
            public int Freedom { get; set; }

            public List<AllocationRecord> Records { get; } = new List<AllocationRecord>();


            public bool ServesNormal
            {
                get
                {
                    return Roles.Contains(AllocationRoleKind.Normal)
                        || Roles.Contains(AllocationRoleKind.NormalAndDemand);
                }
            }

            public bool ServesDemand
            {
                get
                {
                    return Roles.Contains(AllocationRoleKind.Demand)
                        || Roles.Contains(AllocationRoleKind.NormalAndDemand);
                }
            }

            public int TypeFreedom
            {
                get { return (ServesNormal ? 1 : 0) + (ServesDemand ? 1 : 0); }
            }

            public decimal FillPercent
            {
                get { return Capacity <= 0 ? 0 : Math.Round(Load * 100m / Capacity, 1); }
            }

            public decimal RemainingCapacity
            {
                get { return Math.Max(0, Capacity - Load); }
            }

            public bool AllowsRegion(int region)
            {
                return Regions == null || Regions.Contains(region);
            }

            /// <summary>گروه‌های (قانون‌های) یک منطقه؛ false = این منطقه برای مامور مجاز نیست</summary>
            public bool TryGetRules(int region, out List<AgentRegionRule> rules)
            {
                if (Regions == null)
                {
                    rules = new List<AgentRegionRule> { DefaultRule ?? new AgentRegionRule() };
                    return true;
                }

                rules = null;

                if (!Regions.Contains(region))
                    return false;

                if (!RegionRules.TryGetValue(region, out rules) || rules == null || rules.Count == 0)
                    rules = new List<AgentRegionRule> { new AgentRegionRule() };

                return true;
            }

            /// <summary>گروه‌های مربوط به منطقه‌ی یک پرونده</summary>
            public bool TryGetRulesForRecord(AllocationRecord record, out List<AgentRegionRule> rules)
            {
                if (Regions == null)
                {
                    rules = new List<AgentRegionRule> { DefaultRule ?? new AgentRegionRule() };
                    return true;
                }

                rules = null;

                if (record == null || !record.Region.HasValue)
                    return false;

                return TryGetRules(record.Region.Value, out rules);
            }

            /// <summary>آیا این گروه (قانون) پرونده را می‌پذیرد؟ هر بخش را می‌توان جدا بررسی کرد</summary>
            public static bool RuleAllows(
                AgentRegionRule rule,
                AllocationRecord record,
                bool checkDay,
                bool checkTariff,
                bool checkArea)
            {
                if (rule == null)
                    return true;

                if (checkDay && rule.Days != null)
                {
                    if (!record.WorkDay.HasValue || !rule.Days.Contains(record.WorkDay.Value))
                        return false;
                }

                if (checkTariff && rule.Tariffs != null)
                {
                    if (string.IsNullOrWhiteSpace(record.Tariff) || !rule.Tariffs.Contains(record.Tariff))
                        return false;
                }

                if (checkArea && rule.Areas != null)
                {
                    if (!record.Area.HasValue || !rule.Areas.Contains(record.Area.Value))
                        return false;
                }

                return true;
            }

            public bool AllowsRegionDay(int region, int day)
            {
                List<AgentRegionRule> rules;

                return TryGetRules(region, out rules)
                    && rules.Any(rule => rule.Days == null || rule.Days.Contains(day));
            }

            /// <summary>منطقه + حداقل یکی از گروه‌های منطقه (روزکار و تعرفه و محدوده‌ی همان گروه)</summary>
            public bool IsScopeAllowed(AllocationRecord record)
            {
                List<AgentRegionRule> rules;

                if (!TryGetRulesForRecord(record, out rules))
                    return false;

                return rules.Any(rule => RuleAllows(rule, record, true, true, true));
            }

            /// <summary>مثال: «منطقه 14: [روزکار 1 تا 10 | تعرفه: خانگی | محدوده: 0 تا 5] یا [روزکار 11 تا 20 | ...]»</summary>
            public string ScopeDescription(Func<IEnumerable<int>, string> compressDays)
            {
                Func<AgentRegionRule, string> describe = rule =>
                    "روزکار " + (rule == null || rule.Days == null ? "همه" : compressDays(rule.Days))
                    + " | تعرفه: " + (rule == null || rule.Tariffs == null ? "همه" : string.Join("، ", rule.Tariffs.OrderBy(x => x)))
                    + " | محدوده: " + (rule == null || rule.Areas == null ? "همه" : compressDays(rule.Areas));

                Func<List<AgentRegionRule>, string> describeGroups = list =>
                    list == null || list.Count == 0
                        ? describe(null)
                        : (list.Count == 1
                            ? describe(list[0])
                            : string.Join(" یا ", list.Select(r => "[" + describe(r) + "]")));

                if (Regions == null)
                    return "همه مناطق: " + describe(DefaultRule);

                var lines = new List<string>();

                foreach (var region in Regions.OrderBy(x => x))
                {
                    List<AgentRegionRule> list;

                    RegionRules.TryGetValue(region, out list);

                    lines.Add("منطقه " + region + ": " + describeGroups(list));
                }

                return lines.Count == 0 ? "-" : string.Join(" ؛ ", lines);
            }
        }


        public class AllocationRecord
        {
            public long Id { get; set; }

            /// <summary>مبلغ بدهی (P5)</summary>
            public decimal Amount { get; set; }

            public int? Region { get; set; }
            public int? WorkDay { get; set; }

            /// <summary>محدوده (کد ناحیه) = رقم ۶ و ۷ شماره اشتراک</summary>
            public int? Area { get; set; }

            public string Eshterak { get; set; }
            public string Tariff { get; set; }

            /// <summary>رقم اول اشتراک: 1 = دیماند ، 2 = عادی</summary>
            public bool IsDemand { get; set; }
            public bool HasMobile { get; set; }
            public bool IsBalabari { get; set; }

            /// <summary>اگر پرونده قبل از تخصیص کنار گذاشته شده باشد (تکراری ، سخت وصول ، ...) علت اینجاست</summary>
            public string ExcludeReason { get; set; }

            public object Source { get; set; }

            /* ---- خروجی ---- */
            public AllocationAgent Agent { get; set; }
            public string PhaseKey { get; set; }
            public int Round { get; set; }
            public decimal Weight { get; set; }

            // ردگیری کات برای گزارش اکسل؛ روی نتیجه تخصیص اثری ندارد.
            public bool WasCutCandidate { get; set; }
            public bool EnteredCut { get; set; }
            public int CutCandidateCount { get; set; }
            public int CutEntryCount { get; set; }
            public string FirstCutPhaseTitle { get; set; }
            public int? FirstCutRound { get; set; }
            public decimal? FirstCutGap { get; set; }
            public string LastCandidatePhaseTitle { get; set; }
            public int? LastCandidateRound { get; set; }
        }


        public class AllocationPhaseLog
        {
            public string PhaseKey { get; set; }
            public string PhaseTitle { get; set; }
            public int Round { get; set; }
            public int TargetAgents { get; set; }
            public int Candidates { get; set; }
            public decimal Gap { get; set; }
            public int CutCount { get; set; }
            public decimal CutWeight { get; set; }
            public decimal MinCutAmount { get; set; }
            public decimal MaxCutAmount { get; set; }
            public decimal MaxNotCutCandidateAmount { get; set; }
            public int AssignedCount { get; set; }
            public decimal AssignedWeight { get; set; }
            public int DeficitAgentsAfter { get; set; }
        }


        public class AllocationResult
        {
            public List<AllocationAgent> Agents { get; set; }
            public List<AllocationRecord> Allocated { get; set; }
            public List<AllocationRecord> Unallocated { get; set; }
            public List<AllocationRecord> NoPermission { get; set; }
            public List<AllocationPhaseLog> Logs { get; set; }
        }


        public class SmartAllocationEngine
        {
            public const string PHASE_BALABARI = "balabari";
            public const string PHASE_FIELD = "field";
            public const string PHASE_OTHER = "other";
            public const string PHASE_TELEPHONE = "telephone";

            // کف مبلغ بدهی فقط برای اجرای تخصیص جنوبی (ریال)
            private const decimal JONOBI_MIN_NEGOTIATION_DEBT = 1000000m;
            private const decimal JONOBI_MIN_DISCONNECT_DEBT = 5000000m;

            private readonly AllocationSettings _settings;
            private readonly bool _isJonobi;

            public SmartAllocationEngine(
                AllocationSettings settings = null,
                bool isJonobi = false)
            {
                _settings = settings ?? new AllocationSettings();
                _isJonobi = isJonobi;
            }


            private class AllocationPhase
            {
                public string Key;
                public string Title;
                public Func<AllocationAgent, bool> AgentFilter;
                public Func<AllocationRecord, bool> RecordFilter;
                public Func<AllocationAgent, AllocationRecord, bool> TypeMatch;
            }

            public AllocationResult Run(
    List<AllocationAgent> agents,
    List<AllocationRecord> records)
            {
                agents = agents ?? new List<AllocationAgent>();
                records = records ?? new List<AllocationRecord>();

                var logs = new List<AllocationPhaseLog>();

                foreach (var r in records)
                    r.Weight = GetWeight(r);

                ComputeFreedom(agents, records);

                /*
                   ترتیب مراحل:
                   بالابری ← میدانی مشترک عادی و دیماند ← تلفنی/مذاکره‌ای ← سایر

                   در مرحله میدانی:
                   1. عادی و دیماند در یک مجموعه بررسی می‌شوند.
                   2. مجوز نوع پرونده برای هر مأمور کنترل می‌شود.
                   3. RunPhase پرونده‌ها را براساس مبلغ نزولی مرتب می‌کند.
                   4. مأمور دارای هر دو نقش، ظرفیت مشترک دارد.
                */
                var phases = new List<AllocationPhase>
    {
        new AllocationPhase
        {
            Key = PHASE_BALABARI,
            Title = "بالابری",

            AgentFilter = a =>
                a.Roles.Contains(AllocationRoleKind.Balabari),

            RecordFilter = r =>
                r.IsBalabari
                && (!_isJonobi
                    || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT),

            TypeMatch = (a, r) => true
        },

        // عادی و دیماند در یک مرحله مشترک، براساس مبلغ
        new AllocationPhase
        {
            Key = PHASE_FIELD,
            Title = "میدانی - عادی و دیماند براساس مبلغ",

            AgentFilter = a =>
                a.ServesNormal || a.ServesDemand,

            RecordFilter = r =>
                !_isJonobi
                || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT,

            TypeMatch = (a, r) =>
                (r.IsDemand && a.ServesDemand)
                || (!r.IsDemand && a.ServesNormal)
        },

        new AllocationPhase
        {
            Key = PHASE_TELEPHONE,

            Title = _isJonobi
                ? "وصول مذاکره‌ای (باقیمانده عادی)"
                : "تلفنی (باقیمانده عادی)",

            AgentFilter = a =>
                a.Roles.Contains(AllocationRoleKind.Telephone),

            RecordFilter = r =>
                !r.IsDemand
                && (_isJonobi
                    ? r.Amount >= JONOBI_MIN_NEGOTIATION_DEBT
                    : r.HasMobile),

            TypeMatch = (a, r) => true
        },

        new AllocationPhase
        {
            Key = PHASE_OTHER,
            Title = "سایر",

            AgentFilter = a =>
                a.Roles.Contains(AllocationRoleKind.Other),

            RecordFilter = r =>
                !_isJonobi
                || r.Amount >= JONOBI_MIN_NEGOTIATION_DEBT,

            TypeMatch = (a, r) => true
        }
    };

                foreach (var phase in phases)
                    RunPhase(phase, agents, records, logs);

                var noPermission = records
                    .Where(r =>
                        r.Agent == null
                        && !agents.Any(a =>
                            a.Capacity > 0
                            && CanEverServe(phases, a, r)))
                    .ToList();

                var noPermissionSet =
                    new HashSet<AllocationRecord>(noPermission);

                return new AllocationResult
                {
                    Agents = agents,

                    Allocated = records
                        .Where(r => r.Agent != null)
                        .ToList(),

                    Unallocated = records
                        .Where(r =>
                            r.Agent == null
                            && !noPermissionSet.Contains(r))
                        .ToList(),

                    NoPermission = noPermission,
                    Logs = logs
                };
            }
            //public AllocationResult Run(List<AllocationAgent> agents, List<AllocationRecord> records)
            //{
            //    agents = agents ?? new List<AllocationAgent>();
            //    records = records ?? new List<AllocationRecord>();

            //    var logs = new List<AllocationPhaseLog>();

            //    foreach (var r in records)
            //        r.Weight = GetWeight(r);

            //    ComputeFreedom(agents, records);

            //    /*
            //       ترتیب فازها: بالابری ← میدانی (دیماند سپس عادی) ← تلفنی ← سایر
            //       همه‌ی مراحل میدانی Key = field دارند تا شیت‌های «عادی» و «دیماند» Excel همان بمانند.
            //    */
            //    var phases = new List<AllocationPhase>
            //    {
            //        new AllocationPhase
            //        {
            //            Key = PHASE_BALABARI,
            //            Title = "بالابری",
            //            AgentFilter = a => a.Roles.Contains(AllocationRoleKind.Balabari),
            //            RecordFilter = r => r.IsBalabari
            //                && (!_isJonobi || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT),
            //            TypeMatch = (a, r) => true
            //        },
            //        new AllocationPhase
            //        {
            //            Key = PHASE_FIELD,
            //            Title = "دیماند - مأموران فقط دیماند",
            //            AgentFilter = a => a.ServesDemand && !a.ServesNormal,
            //            RecordFilter = r => r.IsDemand
            //                && (!_isJonobi || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT),
            //            TypeMatch = (a, r) => true
            //        },
            //        new AllocationPhase
            //        {
            //            Key = PHASE_FIELD,
            //            Title = "دیماند - مأموران دیماند/عادی",
            //            AgentFilter = a => a.ServesDemand && a.ServesNormal,
            //            RecordFilter = r => r.IsDemand
            //                && (!_isJonobi || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT),
            //            TypeMatch = (a, r) => true
            //        },
            //        new AllocationPhase
            //        {
            //            Key = PHASE_FIELD,
            //            Title = "عادی - مأموران فقط عادی",
            //            AgentFilter = a => a.ServesNormal && !a.ServesDemand,
            //            RecordFilter = r => !r.IsDemand
            //                && (!_isJonobi || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT),
            //            TypeMatch = (a, r) => true
            //        },
            //        new AllocationPhase
            //        {
            //            Key = PHASE_FIELD,
            //            Title = "عادی - مأموران دیماند/عادی",
            //            AgentFilter = a => a.ServesNormal && a.ServesDemand,
            //            RecordFilter = r => !r.IsDemand
            //                && (!_isJonobi || r.Amount >= JONOBI_MIN_DISCONNECT_DEBT),
            //            TypeMatch = (a, r) => true
            //        },
            //        new AllocationPhase
            //        {
            //            Key = PHASE_TELEPHONE,
            //            Title = _isJonobi
            //                ? "وصول مذاکره‌ای (باقیمانده عادی)"
            //                : "تلفنی (باقیمانده عادی)",
            //            AgentFilter = a => a.Roles.Contains(AllocationRoleKind.Telephone),
            //            // جنوبی: بدون شرط موبایل و با کف یک میلیون ریال.
            //            // سیستان: همان قانون تلفنی قبلی حفظ می‌شود.
            //            RecordFilter = r => !r.IsDemand
            //                && (_isJonobi
            //                    ? r.Amount >= JONOBI_MIN_NEGOTIATION_DEBT
            //                    : r.HasMobile),
            //            TypeMatch = (a, r) => true
            //        },
            //        new AllocationPhase
            //        {
            //            Key = PHASE_OTHER,
            //            Title = "سایر",
            //            AgentFilter = a => a.Roles.Contains(AllocationRoleKind.Other),
            //            RecordFilter = r => !_isJonobi
            //                || r.Amount >= JONOBI_MIN_NEGOTIATION_DEBT,
            //            TypeMatch = (a, r) => true
            //        }
            //    };

            //    foreach (var phase in phases)
            //        RunPhase(phase, agents, records, logs);

            //    var noPermission = records
            //        .Where(r => r.Agent == null
            //            && !agents.Any(a => a.Capacity > 0 && CanEverServe(phases, a, r)))
            //        .ToList();

            //    var noPermissionSet = new HashSet<AllocationRecord>(noPermission);

            //    return new AllocationResult
            //    {
            //        Agents = agents,
            //        Allocated = records.Where(r => r.Agent != null).ToList(),
            //        Unallocated = records.Where(r => r.Agent == null && !noPermissionSet.Contains(r)).ToList(),
            //        NoPermission = noPermission,
            //        Logs = logs
            //    };
            //}


            private static bool CanEverServe(List<AllocationPhase> phases, AllocationAgent a, AllocationRecord r)
            {
                return phases.Any(p =>
                    p.AgentFilter(a) && p.RecordFilter(r) && p.TypeMatch(a, r) && a.IsScopeAllowed(r));
            }


            private void RunPhase(
                AllocationPhase phase,
                List<AllocationAgent> allAgents,
                List<AllocationRecord> records,
                List<AllocationPhaseLog> logs)
            {
                var phaseAgents = allAgents
                    .Where(a => a.Capacity > 0 && phase.AgentFilter(a))
                    .ToList();

                if (phaseAgents.Count == 0)
                    return;

                /* مرحله ۱ (پیش‌محاسبه): مأموران مجاز هر پرونده در این فاز */

                var eligibleMap = new Dictionary<AllocationRecord, List<AllocationAgent>>();

                foreach (var r in records)
                {
                    if (r.Agent != null || !phase.RecordFilter(r))
                        continue;

                    var list = phaseAgents
                        .Where(a => phase.TypeMatch(a, r) && a.IsScopeAllowed(r))
                        .ToList();

                    if (list.Count > 0)
                        eligibleMap[r] = list;
                }

                if (eligibleMap.Count == 0)
                    return;

                var targets = new HashSet<AllocationAgent>(phaseAgents.Where(a => !IsComplete(a)));

                var eshterakComparer = new NaturalKeyComparer();

                for (int round = 1; round <= _settings.MaxRounds; round++)
                {
                    if (targets.Count == 0)
                        break;

                    int targetCountBefore = targets.Count;

                    /* مرحله ۱: فیلتر مجوز */

                    var candidates = eligibleMap
                        .Where(kv => kv.Key.Agent == null && kv.Value.Any(a => targets.Contains(a)))
                        .Select(kv => kv.Key)
                        .ToList();

                    if (candidates.Count == 0)
                        break;

                    /* مرحله ۲: اولویت‌بندی مالی */

                    candidates = candidates
                        .OrderByDescending(r => r.Amount)
                        .ThenBy(r => r.Id)
                        .ToList();

                    // ثبت اینکه پرونده در این فاز/دور به مرحله بررسی کات رسیده است.
                    foreach (var r in candidates)
                    {
                        r.WasCutCandidate = true;
                        r.CutCandidateCount++;
                        r.LastCandidatePhaseTitle = phase.Title;
                        r.LastCandidateRound = round;
                    }

                    /* مرحله ۳: برش سهمیه‌ای */

                    var activeTargets = new HashSet<AllocationAgent>(
                        candidates
                            .SelectMany(r => eligibleMap[r])
                            .Where(a => targets.Contains(a)));

                    if (activeTargets.Count == 0)
                        break;

                    // دور اول: فقط CutoffFactor از ظرفیت خالی جدا می‌شود (مثلاً نصف) ؛
                    // دورهای بعد (حلقه‌ی تکمیلی): کل «مانده ظرفیت خالی» مأموران دارای کسری
                    decimal gap = activeTargets.Sum(a => Math.Max(0, a.Capacity - a.Load))
                                  * (round == 1 ? _settings.CutoffFactor : 1m);

                    var cut = new List<AllocationRecord>();
                    decimal cutWeight = 0;
                    Dictionary<AllocationRecord, AllocationAgent> southernReservations = null;

                    if (_isJonobi)
                    {
                        /*
                           جنوبی:
                           ظرفیت کات هر مأمور جداگانه نگهداری می‌شود. پرونده‌ها هنوز از مبلغ
                           بیشتر به کمتر پیمایش می‌شوند، ولی ظرفیت یک مأمور نمی‌تواند کات مأمور
                           دیگری را پر کند. مأمور رزروشده بعد از کات تغییر نمی‌کند.
                        */
                        decimal factor = round == 1 ? _settings.CutoffFactor : 1m;

                        var cutBudget = activeTargets.ToDictionary(
                            a => a,
                            a => Math.Max(0, a.Capacity - a.Load) * factor);

                        var reservedWeight = activeTargets.ToDictionary(a => a, a => 0m);
                        var temporaryLoad = activeTargets.ToDictionary(a => a, a => a.Load);
                        // ابتدا ظرفیت تعدادی؛ رزروهای همین کات هم جزو تعداد حساب می‌شوند.
                        var temporaryCount = activeTargets.ToDictionary(a => a, a => (long)a.Records.Count);

                        southernReservations = new Dictionary<AllocationRecord, AllocationAgent>();

                        foreach (var r in candidates)
                        {
                            var eligibleOpenAgents = eligibleMap[r]
                                .Where(a => activeTargets.Contains(a)
                                    && (!a.RecordLimit.HasValue || temporaryCount[a] < a.RecordLimit.Value))
                                .ToList();

                            if (eligibleOpenAgents.Count == 0)
                                continue;

                            // جنوبی: پرونده‌ای که به‌تنهایی از سقف مأمور بزرگ‌تر است، یا در
                            // مانده ظرفیت جاری او جا نمی‌شود، فقط برای همان مرحله رد می‌شود.
                            // مأمور نباید بسته شود؛ چون پرونده‌های کوچک‌تر بعدی ممکن است
                            // کاملاً در ظرفیت او جا شوند و باید بررسی آن‌ها ادامه پیدا کند.
                            var serviceableAgents = eligibleOpenAgents
                                .Where(a => r.Weight <= a.Capacity * _settings.MaxOverflow)
                                .ToList();

                            var fittingAgents = serviceableAgents
                                .Where(a =>
                                    reservedWeight[a] + r.Weight <= cutBudget[a]
                                    && temporaryLoad[a] < a.Capacity * _settings.CompleteThreshold
                                    && temporaryLoad[a] + r.Weight <= a.Capacity * _settings.MaxOverflow)
                                .ToList();

                            var best = fittingAgents
                                .OrderBy(a => a.Freedom)
                                .ThenBy(a => a.TypeFreedom)
                                .ThenBy(a => a.Capacity <= 0 ? decimal.MaxValue : temporaryLoad[a] / a.Capacity)
                                .ThenBy(a => a.Roles.Count)
                                .ThenBy(a => a.UserId)
                                .FirstOrDefault();

                            if (best == null)
                                continue;

                            cut.Add(r);
                            cutWeight += r.Weight;
                            southernReservations[r] = best;
                            reservedWeight[best] += r.Weight;
                            temporaryLoad[best] += r.Weight;
                            temporaryCount[best]++;

                            r.EnteredCut = true;
                            r.CutEntryCount++;

                            if (!r.FirstCutRound.HasValue)
                            {
                                r.FirstCutPhaseTitle = phase.Title;
                                r.FirstCutRound = round;
                                r.FirstCutGap = Math.Round(cutBudget[best], 0);
                            }
                        }
                    }
                    else
                    {
                        // سیستان: همان کات تجمیعی قبلی، بدون هیچ تغییر.
                        foreach (var r in candidates)
                        {
                            if (cutWeight + r.Weight > gap)
                                continue;

                            if (!eligibleMap[r].Any(a => activeTargets.Contains(a) && Fits(a, r.Weight, round)))
                                continue;

                            cut.Add(r);
                            cutWeight += r.Weight;

                            r.EnteredCut = true;
                            r.CutEntryCount++;

                            if (!r.FirstCutRound.HasValue)
                            {
                                r.FirstCutPhaseTitle = phase.Title;
                                r.FirstCutRound = round;
                                r.FirstCutGap = Math.Round(gap, 0);
                            }

                            if (gap - cutWeight <= 0)
                                break;
                        }
                    }

                    int assignedCount = 0;
                    decimal assignedWeight = 0;

                    if (_isJonobi)
                    {
                        /*
                           بعد از نهایی‌شدن کات مالی هر مأمور، فقط پرونده‌های رزروشده همان مأمور
                           برای خوشه‌بندی مسیر با شماره اشتراک مرتب می‌شوند.
                        */
                        var routeOrderedReservations = southernReservations
                            .GroupBy(x => x.Value)
                            .OrderBy(g => g.Key.Freedom)
                            .ThenBy(g => g.Key.UserId)
                            .SelectMany(g =>
                                g.OrderBy(x => x.Key.Eshterak ?? "", eshterakComparer)
                                 .ThenByDescending(x => x.Key.Amount))
                            .ToList();

                        foreach (var reservation in routeOrderedReservations)
                        {
                            var r = reservation.Key;
                            var best = reservation.Value;

                            if (r.Agent != null
                                || !targets.Contains(best)
                                || best.CountLimitReached
                                || best.Load + r.Weight > best.Capacity * _settings.MaxOverflow)
                                continue;

                            Assign(best, r, phase.Key, round);
                            assignedCount++;
                            assignedWeight += r.Weight;
                        }
                    }
                    else
                    {
                        /* سیستان: خوشه‌بندی و انتخاب مأمور قبلی بدون تغییر. */
                        cut = cut
                            .OrderBy(r => r.Eshterak ?? "", eshterakComparer)
                            .ThenByDescending(r => r.Amount)
                            .ToList();

                        AllocationAgent current = null;

                        foreach (var r in cut)
                        {
                            if (r.Agent != null)
                                continue;

                            var best = eligibleMap[r]
                                .Where(a => targets.Contains(a) && Fits(a, r.Weight, round))
                                .OrderBy(a => a.Freedom)
                                .ThenBy(a => a.TypeFreedom)
                                .ThenBy(a => a == current ? 0 : 1)
                                .ThenBy(a => a.Roles.Count)
                                .ThenBy(a => a.UserId)
                                .FirstOrDefault();

                            if (best == null)
                                continue;

                            Assign(best, r, phase.Key, round);
                            current = best;
                            assignedCount++;
                            assignedWeight += r.Weight;
                        }
                    }

                    /* مرحله ۶: فقط مأموران دارای کسری برای دور بعد */

                    targets = new HashSet<AllocationAgent>(targets.Where(a => !IsComplete(a)));

                    logs.Add(new AllocationPhaseLog
                    {
                        PhaseKey = phase.Key,
                        PhaseTitle = phase.Title,
                        Round = round,
                        TargetAgents = targetCountBefore,
                        Candidates = candidates.Count,
                        Gap = Math.Round(gap, 0),
                        CutCount = cut.Count,
                        CutWeight = Math.Round(cutWeight, 0),
                        MinCutAmount = cut.Count == 0 ? 0 : Math.Round(cut.Min(r => r.Amount), 0),
                        MaxCutAmount = cut.Count == 0 ? 0 : Math.Round(cut.Max(r => r.Amount), 0),
                        MaxNotCutCandidateAmount = candidates
                            .Where(r => !cut.Contains(r))
                            .Select(r => Math.Round(r.Amount, 0))
                            .DefaultIfEmpty(0)
                            .Max(),
                        AssignedCount = assignedCount,
                        AssignedWeight = Math.Round(assignedWeight, 0),
                        DeficitAgentsAfter = targets.Count
                    });

                    // سقف همه‌ی دورها یکی است؛ اگر هیچ پرونده‌ای جا نشد دور بعد نتیجه‌ی تازه‌ای ندارد
                    if (assignedCount == 0)
                        break;
                }
            }


            private decimal GetWeight(AllocationRecord r)
            {
                return r.IsDemand ? r.Amount * _settings.DemandWeight : r.Amount;
            }

            private bool IsComplete(AllocationAgent a)
            {
                return (_isJonobi && a.CountLimitReached)
                    || a.Load >= a.Capacity * _settings.CompleteThreshold;
            }

            private decimal GetLimit(AllocationAgent a, int round)
            {
                return a.Capacity * _settings.MaxOverflow;
            }

            private bool Fits(AllocationAgent a, decimal weight, int round)
            {
                if (IsComplete(a))
                    return false;

                return a.Load + weight <= GetLimit(a, round);
            }

            private static void Assign(AllocationAgent agent, AllocationRecord r, string phaseKey, int round)
            {
                if (agent.CountLimitReached)
                    throw new InvalidOperationException("سقف تعداد پرونده مأمور تکمیل شده است.");

                r.Agent = agent;
                r.PhaseKey = phaseKey;
                r.Round = round;

                agent.Load += r.Weight;
                agent.Records.Add(r);
            }

            /// <summary>
            /// درجه آزادی = مجموع (روزکار × تعرفه × محدوده) مجاز در همه‌ی گروه‌های هر منطقه‌ی موجود در داده.
            /// مأمور بدون محدودیت = همه مناطق × همه مقادیر موجود.
            /// </summary>
            private static void ComputeFreedom(List<AllocationAgent> agents, List<AllocationRecord> records)
            {
                var dataRegions = new HashSet<int>(records.Where(r => r.Region.HasValue).Select(r => r.Region.Value));
                var dataDays = new HashSet<int>(records.Where(r => r.WorkDay.HasValue).Select(r => r.WorkDay.Value));
                var dataAreas = new HashSet<int>(records.Where(r => r.Area.HasValue).Select(r => r.Area.Value));

                var dataTariffs = new HashSet<string>(
                    records.Where(r => !string.IsNullOrWhiteSpace(r.Tariff)).Select(r => r.Tariff),
                    StringComparer.OrdinalIgnoreCase);

                int totalDays = Math.Max(1, dataDays.Count);
                int totalTariffs = Math.Max(1, dataTariffs.Count);
                int totalAreas = Math.Max(1, dataAreas.Count);

                foreach (var a in agents)
                {
                    int freedom = 0;

                    var regionSet = a.Regions ?? dataRegions;

                    foreach (var region in regionSet.Where(x => dataRegions.Contains(x)))
                    {
                        List<AgentRegionRule> rules;

                        if (!a.TryGetRules(region, out rules))
                            continue;

                        foreach (var rule in rules)
                        {
                            int dayCount =
                                rule.Days == null ? totalDays : rule.Days.Count(x => dataDays.Contains(x));

                            int tariffCount =
                                rule.Tariffs == null ? totalTariffs : rule.Tariffs.Count(x => dataTariffs.Contains(x));

                            int areaCount =
                                rule.Areas == null ? totalAreas : rule.Areas.Count(x => dataAreas.Contains(x));

                            freedom += dayCount * tariffCount * areaCount;
                        }
                    }

                    a.Freedom = freedom;
                }
            }
        }


        /// <summary>
        /// مرتب‌سازی طبیعی شماره اشتراک؛ هر قسمت بین / به‌صورت عددی مقایسه می‌شود.
        /// مثال: 1/68/4/3490 قبل از 1/68/10/300 قرار می‌گیرد.
        /// </summary>
        public class NaturalKeyComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                x = x ?? "";
                y = y ?? "";

                if (ReferenceEquals(x, y))
                    return 0;

                var xParts = x.Split('/');
                var yParts = y.Split('/');
                int commonLength = Math.Min(xParts.Length, yParts.Length);

                for (int i = 0; i < commonLength; i++)
                {
                    decimal nx, ny;
                    bool bx = TryParsePart(xParts[i], out nx);
                    bool by = TryParsePart(yParts[i], out ny);

                    if (bx && by)
                    {
                        int numberCompare = nx.CompareTo(ny);
                        if (numberCompare != 0)
                            return numberCompare;

                        continue;
                    }

                    // در یک بخش یکسان، مقدار عددی قبل از مقدار متنی قرار می‌گیرد.
                    if (bx != by)
                        return bx ? -1 : 1;

                    int textCompare = string.CompareOrdinal(xParts[i], yParts[i]);
                    if (textCompare != 0)
                        return textCompare;
                }

                int partCountCompare = xParts.Length.CompareTo(yParts.Length);
                if (partCountCompare != 0)
                    return partCountCompare;

                // برای مقادیر عددی برابر مثل 04 و 4، خروجی قطعی و پایدار می‌ماند.
                return string.CompareOrdinal(x, y);
            }

            private static bool TryParsePart(string value, out decimal number)
            {
                return decimal.TryParse(
                    NormalizeDigits((value ?? "").Trim()),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out number);
            }

            private static string NormalizeDigits(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return "";

                var chars = value.ToCharArray();

                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] >= '\u06F0' && chars[i] <= '\u06F9')
                        chars[i] = (char)('0' + (chars[i] - '\u06F0'));
                    else if (chars[i] >= '\u0660' && chars[i] <= '\u0669')
                        chars[i] = (char)('0' + (chars[i] - '\u0660'));
                }

                return new string(chars);
            }
        }


        /* =====================================================================
           بخش ۸ : تب ۵ - گزارش نیروهای ثبت‌شده / ثبت‌نشده در تب ۱
           ===================================================================== */

        public class UnregisteredUserRow
        {
            public int UserId { get; set; }
            public string Name { get; set; }
            public string Family { get; set; }
            public string NationalCode { get; set; }
            public string PersonalId { get; set; }
            public string JobTitle { get; set; }
            public string CityName { get; set; }

            /// <summary>آیا در تب ۱ حداقل یک نقش دارد؟</summary>
            public bool IsRegistered { get; set; }

            /// <summary>نقش‌های ثبت‌شده در تب ۱</summary>
            public string Roles { get; set; }

            /// <summary>تنظیمات ارجاع (تب ۲): منطقه / گروه / روزکار / تعرفه / محدوده — هر گروه یک خط</summary>
            public string Scope { get; set; }
        }


        private List<UnregisteredUserRow> GetUnregisteredUsers()
        {
            return GetUsersByRegistration(false);
        }


        private List<UnregisteredUserRow> GetRegisteredUsers()
        {
            return GetUsersByRegistration(true);
        }


        /// <summary>ثبت‌نشده‌ها و ثبت‌شده‌ها (با نقش‌ها و تنظیمات ارجاع شامل محدوده) برای نمایش در تب ۵</summary>
        private List<UnregisteredUserRow> GetReportUsers()
        {
            var unregistered = GetUnregisteredUsers();
            var registered = GetRegisteredUsers();

            var scopeByUser =
                GetRegisteredUserScopeRows(registered)
                .GroupBy(s => s.UserId)
                .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(ScopeRowText)));

            foreach (var u in registered)
            {
                string text;

                u.Scope = scopeByUser.TryGetValue(u.UserId, out text) ? text : "";
            }

            return unregistered.Concat(registered).ToList();
        }


        private List<UnregisteredUserRow> GetUsersByRegistration(bool registered)
        {
            var assignmentId = GetActiveRoleAssignmentId();

            var roleRows = assignmentId.HasValue
                ? db.tbOrderAssignmentActivity
                    .Where(x => x.AssignmentID == assignmentId.Value)
                    .Select(x => new { x.UserID, x.ActivityTypeID })
                    .ToList()
                : null;

            var registeredIds = roleRows != null
                ? new HashSet<int>(roleRows.Select(x => x.UserID))
                : new HashSet<int>();

            var activityTitles = db.tbOrderActivityType
                .ToList()
                .ToDictionary(x => x.ID, x => x.Title ?? "");

            var rolesByUser = roleRows == null
                ? new Dictionary<int, string>()
                : roleRows
                    .GroupBy(x => x.UserID)
                    .ToDictionary(
                        g => g.Key,
                        g => string.Join("، ",
                            g.Select(x => activityTitles.ContainsKey(x.ActivityTypeID) ? activityTitles[x.ActivityTypeID] : "")
                             .Where(t => t != "")
                             .Distinct()));

            var users = db.tbUsers
                .AsNoTracking()
                .Where(x => x.usr_IsActive == true)
                .Select(x => new
                {
                    x.usr_ID,
                    x.usr_Name,
                    x.usr_Family,
                    x.usr_NationalCode,
                    x.usr_Personal_ID,
                    x.usr_typeshoghl,
                    x.usr_City_Dutysystem
                })
                .ToList()
                .Where(x => registeredIds.Contains(x.usr_ID) == registered)
                .ToList();

            var jobTitles = LoadJobTitles();

            var cities = db.tbCities
                .AsNoTracking()
                .Select(x => new { x.ID, x.Name })
                .ToList()
                .ToDictionary(x => Convert.ToInt32(x.ID), x => NormalizeOrderExcelText(x.Name));

            return users
                .OrderBy(x => x.usr_Family)
                .ThenBy(x => x.usr_Name)
                .Select(x =>
                {
                    string cityName = "";

                    if (x.usr_City_Dutysystem.HasValue)
                        cities.TryGetValue(x.usr_City_Dutysystem.Value, out cityName);

                    return new UnregisteredUserRow
                    {
                        UserId = x.usr_ID,
                        Name = x.usr_Name ?? "",
                        Family = x.usr_Family ?? "",
                        NationalCode = x.usr_NationalCode ?? "",
                        PersonalId = x.usr_Personal_ID.HasValue ? x.usr_Personal_ID.Value.ToString() : "",
                        JobTitle = GetAllocationJobTitle(x.usr_typeshoghl, jobTitles),
                        CityName = cityName ?? "",
                        IsRegistered = registered,
                        Roles = rolesByUser.ContainsKey(x.usr_ID) ? rolesByUser[x.usr_ID] : "",
                        Scope = ""
                    };
                })
                .ToList();
        }


        private void WriteUserListSheet(IXLWorksheet ws, List<UnregisteredUserRow> rows, bool withRoles)
        {
            ws.RightToLeft = true;

            var headers = withRoles
                ? new[] { "ردیف", "نام", "نام خانوادگی", "کد ملی", "کد پرسنلی", "شغل", "شهرستان", "نقش‌ها" }
                : new[] { "ردیف", "نام", "نام خانوادگی", "کد ملی", "کد پرسنلی", "شغل", "شهرستان" };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            int rowIndex = 2;

            foreach (var r in rows)
            {
                ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                ws.Cell(rowIndex, 2).Value = r.Name;
                ws.Cell(rowIndex, 3).Value = r.Family;
                ws.Cell(rowIndex, 4).Value = r.NationalCode;
                ws.Cell(rowIndex, 5).Value = r.PersonalId;
                ws.Cell(rowIndex, 6).Value = r.JobTitle;
                ws.Cell(rowIndex, 7).Value = r.CityName;

                if (withRoles)
                    ws.Cell(rowIndex, 8).Value = r.Roles;

                rowIndex++;
            }

            ws.Range(2, 4, Math.Max(2, rowIndex), 5).Style.NumberFormat.Format = "@";

            ws.Columns().AdjustToContents();
        }


        /* ---------------------------------------------------------------------
           تنظیمات ارجاع (تب ۲) برای هر نیروی ثبت‌شده
           هر ردیف = یک نیرو + یک منطقه (+ شماره گروه) + روزکارها + تعرفه‌ها + محدوده‌های همان گروه
           نبودن تنظیم = «همه» (بدون محدودیت)
           --------------------------------------------------------------------- */

        public class UserScopeSheetRow
        {
            public int UserId { get; set; }
            public string Name { get; set; }
            public string Family { get; set; }
            public string NationalCode { get; set; }
            public string PersonalId { get; set; }
            public string Roles { get; set; }
            public string Region { get; set; }
            public string WorkDays { get; set; }
            public string Tariffs { get; set; }
            public string Areas { get; set; }
        }


        /// <summary>[22,23,24,25,27] → "22 تا 25، 27"</summary>
        private string CompressDayList(IEnumerable<int> list)
        {
            var s = list.Distinct().OrderBy(x => x).ToList();
            var parts = new List<string>();

            int i = 0;

            while (i < s.Count)
            {
                int j = i;

                while (j + 1 < s.Count && s[j + 1] == s[j] + 1)
                    j++;

                parts.Add(j > i ? s[i] + " تا " + s[j] : s[i].ToString());

                i = j + 1;
            }

            return string.Join("، ", parts);
        }


        private string ScopeRowText(UserScopeSheetRow s)
        {
            return (s.Region == "همه" ? "همه مناطق" : "منطقه " + s.Region)
                + ": روزکار " + s.WorkDays
                + " | تعرفه: " + s.Tariffs
                + " | محدوده: " + s.Areas;
        }


        private List<UserScopeSheetRow> GetRegisteredUserScopeRows(List<UnregisteredUserRow> registeredUsers = null)
        {
            var users = registeredUsers ?? GetRegisteredUsers();

            var userIds = users.Select(x => x.UserId).ToList();

            var scopes = db.tbOrderAgentScope
                .Where(x => x.IsActive == true && userIds.Contains(x.UserID))
                .ToList()
                .GroupBy(x => x.UserID)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ID).First());

            var scopeIds = scopes.Values.Select(x => x.ID).ToList();

            var regionRows = db.tbOrderAgentScopeRegion
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode })
                .ToList();

            var dayRows = db.tbOrderAgentScopeWorkDay
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode, x.GroupNo, x.WorkDay })
                .ToList();

            var tariffRows = db.tbOrderAgentScopeTariff
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode, x.GroupNo, x.TariffTypeID })
                .ToList();

            var areaRows = db.tbOrderAgentScopeArea
                .Where(x => scopeIds.Contains(x.ScopeID))
                .Select(x => new { x.ScopeID, x.RegionCode, x.GroupNo, x.AreaCode })
                .ToList();

            var tariffTitles = db.tbOrderTariffType
                .ToList()
                .ToDictionary(x => x.ID, x => NormalizeOrderExcelText(x.Title));

            Func<IEnumerable<int>, string> tariffText = ids =>
            {
                var titles = ids
                    .Distinct()
                    .Select(id => tariffTitles.ContainsKey(id) ? tariffTitles[id] : id.ToString())
                    .Where(t => t != "")
                    .ToList();

                return titles.Count == 0 ? "همه" : string.Join("، ", titles);
            };

            // انتخاب کامل بازه مجاز یا خالی = همه
            Func<IEnumerable<int>, string> areaText = list =>
            {
                var l = list.Distinct().ToList();

                return (l.Count == 0 || l.Count >= GetAreaTotal()) ? "همه" : CompressDayList(l);
            };

            var result = new List<UserScopeSheetRow>();

            foreach (var u in users)
            {
                Func<string, string, string, string, UserScopeSheetRow> newRow = (region, days, tariffs, areas) =>
                    new UserScopeSheetRow
                    {
                        UserId = u.UserId,
                        Name = u.Name,
                        Family = u.Family,
                        NationalCode = u.NationalCode,
                        PersonalId = u.PersonalId,
                        Roles = u.Roles,
                        Region = region,
                        WorkDays = days,
                        Tariffs = tariffs,
                        Areas = areas
                    };

                tbOrderAgentScope scope;

                // مامور بدون هیچ تنظیمی در تب ۲ = بدون محدودیت
                if (!scopes.TryGetValue(u.UserId, out scope))
                {
                    result.Add(newRow("همه", "همه", "همه", "همه"));
                    continue;
                }

                var regions = regionRows
                    .Where(x => x.ScopeID == scope.ID)
                    .Select(x => x.RegionCode)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                var myDays = dayRows.Where(x => x.ScopeID == scope.ID).ToList();
                var myTariffs = tariffRows.Where(x => x.ScopeID == scope.ID).ToList();
                var myAreas = areaRows.Where(x => x.ScopeID == scope.ID).ToList();

                var generalDays = myDays.Where(x => !x.RegionCode.HasValue).Select(x => x.WorkDay).ToList();
                var generalTariffs = myTariffs.Where(x => !x.RegionCode.HasValue).Select(x => x.TariffTypeID).ToList();
                var generalAreas = myAreas.Where(x => !x.RegionCode.HasValue).Select(x => x.AreaCode).ToList();

                if (regions.Count == 0)
                {
                    result.Add(newRow(
                        "همه",
                        generalDays.Count > 0 ? CompressDayList(generalDays) : "همه",
                        tariffText(generalTariffs),
                        areaText(generalAreas)));

                    continue;
                }

                foreach (var r in regions)
                {
                    var groupNos = myDays.Where(x => x.RegionCode == r).Select(x => x.GroupNo)
                        .Concat(myTariffs.Where(x => x.RegionCode == r).Select(x => x.GroupNo))
                        .Concat(myAreas.Where(x => x.RegionCode == r).Select(x => x.GroupNo))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    if (groupNos.Count == 0)
                        groupNos.Add(1);

                    foreach (var g in groupNos)
                    {
                        var days = myDays.Where(x => x.RegionCode == r && x.GroupNo == g).Select(x => x.WorkDay).ToList();
                        if (days.Count == 0) days = generalDays;

                        var tariffs = myTariffs.Where(x => x.RegionCode == r && x.GroupNo == g).Select(x => x.TariffTypeID).ToList();
                        if (tariffs.Count == 0) tariffs = generalTariffs;

                        var areas = myAreas.Where(x => x.RegionCode == r && x.GroupNo == g).Select(x => x.AreaCode).ToList();
                        if (areas.Count == 0) areas = generalAreas;

                        result.Add(newRow(
                            r.ToString() + (groupNos.Count > 1 ? " (گروه " + g + ")" : ""),
                            days.Count > 0 ? CompressDayList(days) : "همه",
                            tariffText(tariffs),
                            areaText(areas)));
                    }
                }
            }

            return result;
        }


        private void WriteUserScopeSheet(IXLWorksheet ws, List<UserScopeSheetRow> rows)
        {
            ws.RightToLeft = true;

            var headers = new[]
            {
                "ردیف", "نام", "نام خانوادگی", "کد ملی", "کد پرسنلی", "نقش‌ها",
                "منطقه", "روزکارهای منطقه", "تعرفه‌های منطقه", "محدوده‌های منطقه (کد ناحیه)"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            var header = ws.Range(1, 1, 1, headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEBFA");

            int rowIndex = 2;

            foreach (var r in rows)
            {
                ws.Cell(rowIndex, 1).Value = rowIndex - 1;
                ws.Cell(rowIndex, 2).Value = r.Name;
                ws.Cell(rowIndex, 3).Value = r.Family;
                ws.Cell(rowIndex, 4).Value = r.NationalCode;
                ws.Cell(rowIndex, 5).Value = r.PersonalId;
                ws.Cell(rowIndex, 6).Value = r.Roles;
                ws.Cell(rowIndex, 7).Value = r.Region;
                ws.Cell(rowIndex, 8).Value = r.WorkDays;
                ws.Cell(rowIndex, 9).Value = r.Tariffs;
                ws.Cell(rowIndex, 10).Value = r.Areas;

                rowIndex++;
            }

            ws.Range(2, 4, Math.Max(2, rowIndex), 5).Style.NumberFormat.Format = "@";

            ws.Columns().AdjustToContents();
        }


        /// <summary>خروجی Excel: شیت ۱ ثبت‌نشده‌ها ، شیت ۲ ثبت‌شده‌ها ، شیت ۳ تنظیمات ارجاع هر نیرو (با محدوده)</summary>
        [HttpGet]
        public ActionResult ExportUnregisteredUsers()
        {
            var currentUserId = GetCurrentUserId();

            if (!currentUserId.HasValue)
                return Content("دسترسی غیر مجاز");

            var unregistered = GetUnregisteredUsers();
            var registered = GetRegisteredUsers();

            using (var workbook = new XLWorkbook())
            {
                WriteUserListSheet(workbook.Worksheets.Add("ثبت نشده ها"), unregistered, false);
                WriteUserListSheet(workbook.Worksheets.Add("ثبت شده ها"), registered, true);
                WriteUserScopeSheet(workbook.Worksheets.Add("تنظیمات ارجاع"), GetRegisteredUserScopeRows(registered));

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);

                    return File(
                        stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "UsersReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                }
            }
        }

        #endregion
    }
}
