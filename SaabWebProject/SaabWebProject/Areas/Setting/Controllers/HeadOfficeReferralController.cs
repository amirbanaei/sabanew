using ClosedXML.Excel;
using SaabWebProject.Models.DomainModels;
using SaabWebProject.Models.ViewModels.Setting;
using SaabWebProject.Utility;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace SaabWebProject.Areas.Setting.Controllers
{
    /// <summary>
    /// تنظیمات ستاد جهت ارجاع متمرکز (بندهای ۲ تا ۵ سند نیازمندی).
    /// جداول این بخش مستقل از EDMX هستند تا مدل Database-First موجود بازتولید نشود.
    /// </summary>
    public class HeadOfficeReferralController : Controller
    {
        private readonly SaabEntities db = new SaabEntities();

        [AuthorizeAAA]
        public ActionResult Index(string tab = "base", string scope = "all", int? cityId = null, long? reportId = null)
        {
            EnsureStorage();
            var validTabs = new[] { "base", "priority", "lists", "conditions" };
            if (!validTabs.Contains(tab)) tab = "base";
            if (scope != "city") { scope = "all"; cityId = null; }
            ViewBag.BaseReportId=reportId;
            return View(BuildPageModel(tab, scope, cityId));
        }

        [HttpGet]
        [AuthorizeAAA]
        public JsonResult Fields(long reportId)
        {
            var fields = db.Database.SqlQuery<ReferralOptionModel>(@"
SELECT DISTINCT f.ID AS Id, f.name AS Title
FROM dbo.dbergha2 f
WHERE ISNULL(f.del, 0) = 0
  AND (
      NOT EXISTS (
          SELECT 1
          FROM dbo.dbergharel_4 r
          INNER JOIN dbo.dbergharel4_5 rf ON rf.FK_dbergharel4 = r.ID AND ISNULL(rf.del, 0) = 0
          WHERE r.FK_namegozarwsh = @ReportId AND ISNULL(r.del, 0) = 0
      )
      OR EXISTS (
          SELECT 1
          FROM dbo.dbergharel_4 r
          INNER JOIN dbo.dbergharel4_5 rf ON rf.FK_dbergharel4 = r.ID AND ISNULL(rf.del, 0) = 0
          WHERE r.FK_namegozarwsh = @ReportId
            AND rf.FK_dbergharel2 = f.ID
            AND ISNULL(r.del, 0) = 0
      )
  )
ORDER BY f.name", new SqlParameter("@ReportId", reportId)).ToList();
            return Json(fields, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [AuthorizeAAA]
        public JsonResult SavedBaseFields(long reportId)
        {
            try
            {
                EnsureStorage();
                var available=Fields(reportId).Data as IEnumerable<ReferralOptionModel>;
                var saved=db.Database.SqlQuery<ReferralOptionModel>(@"
SELECT b.FieldId AS Id,COALESCE(f.name,CONVERT(NVARCHAR(30),b.FieldId)) AS Title
FROM dbo.tbHeadOfficeBaseField b LEFT JOIN dbo.dbergha2 f ON f.ID=b.FieldId
WHERE b.ReportId=@ReportId",new SqlParameter("@ReportId",reportId)).ToList();
                var selected=new HashSet<string>(saved.Select(x=>Convert.ToString(x.Id)));
                var all=(available??Enumerable.Empty<ReferralOptionModel>()).Concat(saved)
                    .GroupBy(x=>x.Id).Select(g=>g.First()).OrderBy(x=>x.Title)
                    .Select(x=>new { x.Id,x.Title,Selected=selected.Contains(Convert.ToString(x.Id)) }).ToList();
                return new JsonResult { Data=new { success=true,items=all },JsonRequestBehavior=JsonRequestBehavior.AllowGet,MaxJsonLength=Int32.MaxValue };
            }
            catch(Exception ex)
            {
                var reference=Guid.NewGuid().ToString("N");
                System.Diagnostics.Trace.TraceError("Base fields load {0}: {1}",reference,ex);
                Response.StatusCode=500;Response.TrySkipIisCustomErrors=true;
                return Json(new {success=false,message="خواندن فیلدها از دیتابیس ناموفق بود. کد پیگیری: "+reference},JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [AuthorizeAAA]
        public JsonResult UniqueFields(long reportId)
        {
            var fields = db.Database.SqlQuery<ReferralOptionModel>(@"
SELECT DISTINCT f.ID AS Id, f.name AS Title
FROM dbo.dberganametanble2_2 t
INNER JOIN dbo.dbergha2 f ON f.ID = t.FK_unic AND ISNULL(f.del, 0) = 0
WHERE ISNULL(t.del, 0) = 0
  AND t.FK_unic IS NOT NULL
ORDER BY f.name").ToList();
            return Json(fields, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [AuthorizeAAA]
        public JsonResult Subsets(long fieldId)
        {
            var items = db.dbergharel2_3
                .Where(x => x.FK_dbergha2 == fieldId && x.del != true)
                .OrderBy(x => x.name)
                .Select(x => new { id = x.ID, title = x.name })
                .ToList();
            return Json(items, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [AuthorizeAAA]
        public JsonResult ListDetails(int id)
        {
            EnsureStorage();
            var item = db.Database.SqlQuery<ListDetailRow>(@"
SELECT ListId, Title, ReportId, KeyFieldId, DailyAmount, DurationDays, RoutingType, AllowDuplicate, CityId
FROM dbo.tbHeadOfficeListDefinition WHERE ListId=@Id", new SqlParameter("@Id", id)).FirstOrDefault();
            if (item == null) return Json(new { success = false, message = "عنوان لیست پیدا نشد." }, JsonRequestBehavior.AllowGet);
            var exceptions = db.Database.SqlQuery<long>("SELECT SubsetId FROM dbo.tbHeadOfficeListException WHERE ListId=@Id",
                new SqlParameter("@Id", id)).ToList();
            return Json(new { success = true, item, exceptionSubsetIds = exceptions }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult SaveBaseFields(SaveBaseFieldsModel model)
        {
            EnsureStorage();
            if (model == null || model.ReportId <= 0 || model.FieldIds == null || model.FieldIds.Count == 0)
            {
                SetError("حداقل یک جدول و یک فیلد پایه انتخاب کنید.");
                return RedirectToAction("Index", new { tab = "base", reportId = model == null ? (long?)null : model.ReportId });
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                db.Database.ExecuteSqlCommand(@"DECLARE @result INT;
EXEC @result=sys.sp_getapplock @Resource=N'HeadOffice.BaseFields.Save',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
IF @result<0 THROW 50010,N'Could not lock base fields. Retry saving.',1;");
                foreach (var fieldId in model.FieldIds.Distinct())
                {
                    db.Database.ExecuteSqlCommand(@"
IF NOT EXISTS(SELECT 1 FROM dbo.tbHeadOfficeBaseField WITH(UPDLOCK,HOLDLOCK) WHERE ReportId=@ReportId AND FieldId=@FieldId)
INSERT INTO dbo.tbHeadOfficeBaseField (ReportId, FieldId, CreatedAt, CreatedBy)
VALUES (@ReportId, @FieldId, SYSDATETIME(), @UserId)",
                        new SqlParameter("@ReportId", model.ReportId),
                        new SqlParameter("@FieldId", fieldId),
                        NullableIntParameter("@UserId", GetCurrentUserId()));
                }
                transaction.Commit();
            }
            SetSuccess("فیلدهای جدید اضافه شدند؛ فیلدهای قبلی حفظ شدند.");
            return RedirectToAction("Index", new { tab = "base", reportId = model == null ? (long?)null : model.ReportId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult DeleteBaseField(int id)
        {
            EnsureStorage();
            db.Database.ExecuteSqlCommand("DELETE FROM dbo.tbHeadOfficeBaseField WHERE BaseFieldId = @Id",
                new SqlParameter("@Id", id));
            SetSuccess("فیلد پایه حذف شد.");
            return RedirectToAction("Index", new { tab = "base" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public JsonResult SavePriorities(SavePriorityRulesModel model)
        {
            EnsureStorage();
            if (model == null) return Json(new { success = false, message = "اطلاعات ارسالی معتبر نیست." });
            var isCity = string.Equals(model.Scope, "city", StringComparison.OrdinalIgnoreCase);
            if (isCity && !model.CityId.HasValue)
                return Json(new { success = false, message = "مدیریت برق را انتخاب کنید." });

            if (model.CopyFromCityId.HasValue && isCity)
            {
                try
                {
                    CopyPriorityProfile(model.CopyFromCityId.Value, model.CityId.Value);
                    return Json(new { success = true, message = "تنظیمات مدیریت مبنا کپی شد." });
                }
                catch (InvalidOperationException ex)
                {
                    return Json(new { success = false, message = ex.Message });
                }
            }

            var rules = model.Rules ?? new List<ReferralPriorityRuleInputModel>();
            foreach (var rule in rules)
            {
                if (rule.MinimumAmount.HasValue && rule.MaximumAmount.HasValue && rule.MinimumAmount > rule.MaximumAmount)
                    return Json(new { success = false, message = "کمترین مبلغ نمی‌تواند از بیشترین مبلغ بزرگ‌تر باشد." });
                var sum = (rule.Weights ?? new List<ReferralPriorityWeightInputModel>()).Sum(x => x.Weight);
                if (Math.Abs(sum - 100m) > 0.001m)
                    return Json(new { success = false, message = "مجموع اولویت شاخص‌ها در هر ردیف باید دقیقاً ۱۰۰ باشد." });
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                var profileId = GetOrCreatePriorityProfile(isCity ? model.CityId : null);
                db.Database.ExecuteSqlCommand(@"
DELETE w FROM dbo.tbHeadOfficePriorityWeight w
INNER JOIN dbo.tbHeadOfficePriorityRule r ON r.RuleId = w.RuleId
WHERE r.ProfileId = @ProfileId;
DELETE FROM dbo.tbHeadOfficePriorityRule WHERE ProfileId = @ProfileId;",
                    new SqlParameter("@ProfileId", profileId));

                foreach (var rule in rules)
                {
                    var ruleId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.tbHeadOfficePriorityRule
    (ProfileId, FieldId, SubsetId, MinimumAmount, MaximumAmount, CreatedAt)
OUTPUT INSERTED.RuleId
VALUES
    (@ProfileId, @FieldId, @SubsetId, @MinimumAmount, @MaximumAmount, SYSDATETIME())",
                        new SqlParameter("@ProfileId", profileId),
                        new SqlParameter("@FieldId", rule.FieldId),
                        NullableLongParameter("@SubsetId", rule.SubsetId),
                        NullableDecimalParameter("@MinimumAmount", rule.MinimumAmount),
                        NullableDecimalParameter("@MaximumAmount", rule.MaximumAmount)).Single();

                    foreach (var weight in rule.Weights)
                    {
                        db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.tbHeadOfficePriorityWeight (RuleId, IndicatorId, Weight)
VALUES (@RuleId, @IndicatorId, @Weight)",
                            new SqlParameter("@RuleId", ruleId),
                            new SqlParameter("@IndicatorId", weight.IndicatorId),
                            new SqlParameter("@Weight", weight.Weight));
                    }
                }
                transaction.Commit();
            }
            return Json(new { success = true, message = "محدودیت‌ها و اولویت‌ها ذخیره شدند." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult SaveList(SaveReferralListModel model)
        {
            EnsureStorage();
            NormalizeList(model);
            if (model == null || string.IsNullOrWhiteSpace(model.Title) || model.ReportId <= 0 || model.KeyFieldId <= 0 ||
                model.DailyAmount <= 0 || model.DurationDays <= 0)
            {
                SetError("عنوان، جدول، فیلد کلیدی، مقدار روزانه و بازه زمانی الزامی هستند.");
                return RedirectToAction("Index", new { tab = "lists" });
            }
            if (model.Scope == "city" && !model.CityId.HasValue)
            {
                SetError("برای تنظیمات تفکیکی، مدیریت برق را انتخاب کنید.");
                return RedirectToAction("Index", new { tab = "lists" });
            }
            var validUniqueKey = db.Database.SqlQuery<int>(@"
SELECT COUNT(1)
FROM dbo.dberganametanble2_2 t
WHERE ISNULL(t.del, 0) = 0
  AND t.FK_unic = @KeyFieldId
  AND t.FK_unic IS NOT NULL",
                new SqlParameter("@KeyFieldId", model.KeyFieldId)).Single() > 0;
            if (!validUniqueKey)
            {
                SetError("کلید یونیک انتخاب‌شده متعلق به جدول گزارش نیست.");
                return RedirectToAction("Index", new { tab = "lists" });
            }

            using (var transaction = db.Database.BeginTransaction())
            {
                int listId;
                if (model.ListId == 0)
                {
                    listId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.tbHeadOfficeListDefinition
    (Title, ReportId, KeyFieldId, DailyAmount, DurationDays, RoutingType, AllowDuplicate, CityId, CreatedAt, CreatedBy)
OUTPUT INSERTED.ListId
VALUES
    (@Title, @ReportId, @KeyFieldId, @DailyAmount, @DurationDays, @RoutingType, @AllowDuplicate, @CityId, SYSDATETIME(), @UserId)",
                        new SqlParameter("@Title", model.Title), new SqlParameter("@ReportId", model.ReportId),
                        new SqlParameter("@KeyFieldId", model.KeyFieldId), new SqlParameter("@DailyAmount", model.DailyAmount),
                        new SqlParameter("@DurationDays", model.DurationDays), new SqlParameter("@RoutingType", model.RoutingType),
                        new SqlParameter("@AllowDuplicate", model.AllowDuplicate), NullableIntParameter("@CityId", model.Scope == "city" ? model.CityId : null),
                        NullableIntParameter("@UserId", GetCurrentUserId())).Single();
                }
                else
                {
                    listId = model.ListId;
                    db.Database.ExecuteSqlCommand(@"
UPDATE dbo.tbHeadOfficeListDefinition
SET Title=@Title, ReportId=@ReportId, KeyFieldId=@KeyFieldId, DailyAmount=@DailyAmount,
    DurationDays=@DurationDays, RoutingType=@RoutingType, AllowDuplicate=@AllowDuplicate,
    CityId=@CityId, UpdatedAt=SYSDATETIME(), UpdatedBy=@UserId
WHERE ListId=@ListId",
                        new SqlParameter("@Title", model.Title), new SqlParameter("@ReportId", model.ReportId),
                        new SqlParameter("@KeyFieldId", model.KeyFieldId), new SqlParameter("@DailyAmount", model.DailyAmount),
                        new SqlParameter("@DurationDays", model.DurationDays), new SqlParameter("@RoutingType", model.RoutingType),
                        new SqlParameter("@AllowDuplicate", model.AllowDuplicate), NullableIntParameter("@CityId", model.Scope == "city" ? model.CityId : null),
                        NullableIntParameter("@UserId", GetCurrentUserId()), new SqlParameter("@ListId", listId));
                    db.Database.ExecuteSqlCommand("DELETE FROM dbo.tbHeadOfficeListException WHERE ListId=@ListId", new SqlParameter("@ListId", listId));
                }

                foreach (var subsetId in (model.ExceptionSubsetIds ?? new List<long>()).Distinct())
                {
                    db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.tbHeadOfficeListException (ListId, FieldId, SubsetId)
SELECT @ListId, FK_dbergha2, ID FROM dbo.dbergharel2_3 WHERE ID=@SubsetId",
                        new SqlParameter("@ListId", listId), new SqlParameter("@SubsetId", subsetId));
                }
                transaction.Commit();
            }
            SetSuccess(model.ListId == 0 ? "عنوان لیست ثبت شد." : "عنوان لیست ویرایش شد.");
            return RedirectToAction("Index", new { tab = "lists" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult DeleteList(int id)
        {
            EnsureStorage();
            db.Database.ExecuteSqlCommand(@"
DELETE FROM dbo.tbHeadOfficeListException WHERE ListId=@Id;
DELETE FROM dbo.tbHeadOfficeListDefinition WHERE ListId=@Id;", new SqlParameter("@Id", id));
            SetSuccess("عنوان لیست حذف شد.");
            return RedirectToAction("Index", new { tab = "lists" });
        }

        [HttpGet]
        [AuthorizeAAA]
        public ActionResult ExceptionSample()
        {
            EnsureStorage();
            using (var workbook = new XLWorkbook())
            {
                var source = workbook.Worksheets.Add("فیلدها و زیرمجموعه‌ها");
                source.RightToLeft = true;
                source.Cell(1, 1).Value = "شناسه فیلد";
                source.Cell(1, 2).Value = "نام فیلد";
                source.Cell(1, 3).Value = "شناسه زیرمجموعه";
                source.Cell(1, 4).Value = "نام زیرمجموعه";
                var items = LoadSubsets();
                var row = 2;
                foreach (var item in items)
                {
                    source.Cell(row, 1).Value = item.ParentId ?? 0;
                    source.Cell(row, 2).Value = item.ParentTitle;
                    source.Cell(row, 3).Value = item.Id;
                    source.Cell(row, 4).Value = item.Title;
                    row++;
                }
                source.Range(1, 1, 1, 4).Style.Font.Bold = true;
                source.Columns().AdjustToContents();

                var selected = workbook.Worksheets.Add("استثنائات");
                selected.RightToLeft = true;
                selected.Cell(1, 1).Value = "شناسه فیلد";
                selected.Cell(1, 2).Value = "شناسه زیرمجموعه";
                selected.Cell(1, 3).Value = "نام فیلد (جهت کنترل)";
                selected.Cell(1, 4).Value = "نام زیرمجموعه (جهت کنترل)";
                selected.Range(1, 1, 1, 4).Style.Font.Bold = true;
                selected.Columns().AdjustToContents();
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ReferralExceptions.xlsx");
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public JsonResult ReadExceptionFile(HttpPostedFileBase file)
        {
            if (file == null || file.ContentLength == 0)
                return Json(new { success = false, message = "فایل اکسل را انتخاب کنید." });
            try
            {
                using (var workbook = new XLWorkbook(file.InputStream))
                {
                    var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name == "استثنائات") ?? workbook.Worksheet(2);
                    var ids = new List<long>();
                    foreach (var row in sheet.RowsUsed().Skip(1))
                    {
                        long id;
                        if (long.TryParse(row.Cell(2).GetString(), out id) && id > 0) ids.Add(id);
                    }
                    return Json(new { success = true, subsetIds = ids.Distinct().ToList(), message = ids.Count + " استثناء از فایل خوانده شد." });
                }
            }
            catch (Exception)
            {
                return Json(new { success = false, message = "ساختار فایل معتبر نیست؛ از فایل نمونه استفاده کنید." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult SaveCondition(SaveReferralConditionModel model)
        {
            EnsureStorage();
            if (model != null)
            {
                model.Title = (model.Title ?? string.Empty).Trim();
                model.Description = (model.Description ?? string.Empty).Trim();
                model.ExpressionText = (model.ExpressionText ?? string.Empty).Trim();
            }
            if (model == null || string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.ExpressionText))
            {
                SetError("عنوان و عبارت شرط الزامی است.");
                return RedirectToAction("Index", new { tab = "conditions" });
            }
            if (model.ConditionId == 0)
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.tbHeadOfficeCondition (Title, Description, ExpressionText, CreatedAt, CreatedBy)
VALUES (@Title, @Description, @ExpressionText, SYSDATETIME(), @UserId)",
                    new SqlParameter("@Title", model.Title), new SqlParameter("@Description", model.Description),
                    new SqlParameter("@ExpressionText", model.ExpressionText), NullableIntParameter("@UserId", GetCurrentUserId()));
            }
            else
            {
                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.tbHeadOfficeCondition SET Title=@Title, Description=@Description,
ExpressionText=@ExpressionText, UpdatedAt=SYSDATETIME(), UpdatedBy=@UserId WHERE ConditionId=@Id",
                    new SqlParameter("@Title", model.Title), new SqlParameter("@Description", model.Description),
                    new SqlParameter("@ExpressionText", model.ExpressionText), NullableIntParameter("@UserId", GetCurrentUserId()),
                    new SqlParameter("@Id", model.ConditionId));
            }
            SetSuccess(model.ConditionId == 0 ? "شرط ثبت شد." : "شرط ویرایش شد.");
            return RedirectToAction("Index", new { tab = "conditions" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult DeleteCondition(int id)
        {
            EnsureStorage();
            db.Database.ExecuteSqlCommand("DELETE FROM dbo.tbHeadOfficeCondition WHERE ConditionId=@Id", new SqlParameter("@Id", id));
            SetSuccess("شرط حذف شد.");
            return RedirectToAction("Index", new { tab = "conditions" });
        }

        private HeadOfficeReferralPageViewModel BuildPageModel(string tab, string scope, int? cityId)
        {
            var model = new HeadOfficeReferralPageViewModel
            {
                ActiveTab = tab,
                Scope = scope,
                CityId = cityId,
                Reports = db.dbergharel_4_4.Where(x => x.del != true).OrderBy(x => x.name)
                    .Select(x => new ReferralOptionModel { Id = x.ID, Title = x.name }).ToList(),
                Fields = db.dbergha2.Where(x => x.del != true).OrderBy(x => x.name)
                    .Select(x => new ReferralFieldOptionModel { Id = x.ID, Title = x.name }).ToList(),
                ReportUniqueFields = db.Database.SqlQuery<ReferralReportUniqueFieldModel>(@"
SELECT DISTINCT CAST(0 AS BIGINT) AS ReportId, f.ID AS Id, f.name AS Title
FROM dbo.dberganametanble2_2 t
INNER JOIN dbo.dbergha2 f ON f.ID = t.FK_unic AND ISNULL(f.del, 0) = 0
WHERE ISNULL(t.del, 0) = 0
  AND t.FK_unic IS NOT NULL
ORDER BY f.name").ToList(),
                Cities = db.tbCities.OrderBy(x => x.Name)
                    .Select(x => new ReferralOptionModel { Id = x.ID, Title = x.Name }).ToList(),
                ConfiguredCityIds = db.Database.SqlQuery<int>(@"
SELECT DISTINCT p.CityId FROM dbo.tbHeadOfficePriorityProfile p
WHERE p.CityId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tbHeadOfficePriorityRule r WHERE r.ProfileId=p.ProfileId)").ToList(),
                Indicators = db.Database.SqlQuery<ReferralIndicatorOptionModel>(@"
SELECT IndicatorId, Title, Unit FROM dbo.tbHeadOfficeMainIndicator WHERE IsDeleted=0 ORDER BY IndicatorId").ToList(),
                BaseFields = db.Database.SqlQuery<ReferralBaseFieldRowModel>(@"
SELECT b.BaseFieldId, b.ReportId, r.name AS ReportTitle, b.FieldId, f.name AS FieldTitle
FROM dbo.tbHeadOfficeBaseField b
INNER JOIN dbo.dbergharel_4_4 r ON r.ID=b.ReportId
INNER JOIN dbo.dbergha2 f ON f.ID=b.FieldId
ORDER BY r.name, f.name").ToList(),
                Lists = db.Database.SqlQuery<ReferralListRowModel>(@"
SELECT l.ListId, l.Title, l.ReportId, r.name AS ReportTitle, l.KeyFieldId, f.name AS KeyFieldTitle,
       l.DailyAmount, l.DurationDays, l.RoutingType, l.AllowDuplicate, l.CityId,
       c.Name AS CityTitle, (SELECT COUNT(1) FROM dbo.tbHeadOfficeListException e WHERE e.ListId=l.ListId) AS ExceptionCount
FROM dbo.tbHeadOfficeListDefinition l
INNER JOIN dbo.dbergharel_4_4 r ON r.ID=l.ReportId
INNER JOIN dbo.dbergha2 f ON f.ID=l.KeyFieldId
LEFT JOIN dbo.tbCities c ON c.ID=l.CityId
ORDER BY l.ListId DESC").ToList(),
                Conditions = db.Database.SqlQuery<ReferralConditionRowModel>(@"
SELECT ConditionId, Title, Description, ExpressionText FROM dbo.tbHeadOfficeCondition ORDER BY ConditionId DESC").ToList()
            };
            model.Subsets = LoadSubsets();
            model.PriorityRows = BuildPriorityRows(scope == "city" ? cityId : null, model.BaseFields, model.Subsets);
            return model;
        }

        private IList<ReferralFieldOptionModel> LoadSubsets()
        {
            return db.Database.SqlQuery<ReferralFieldOptionModel>(@"
SELECT s.ID AS Id, s.name AS Title, s.FK_dbergha2 AS ParentId, f.name AS ParentTitle
FROM dbo.dbergharel2_3 s
INNER JOIN dbo.dbergha2 f ON f.ID=s.FK_dbergha2
WHERE ISNULL(s.del,0)=0 AND ISNULL(f.del,0)=0
ORDER BY f.name, s.name").ToList();
        }

        private IList<ReferralPriorityRowModel> BuildPriorityRows(int? cityId, IList<ReferralBaseFieldRowModel> baseFields, IList<ReferralFieldOptionModel> subsets)
        {
            var rows = new List<ReferralPriorityRowModel>();
            foreach (var field in baseFields.GroupBy(x => x.FieldId).Select(x => x.First()))
            {
                var children = subsets.Where(x => x.ParentId == field.FieldId).ToList();
                if (children.Count == 0)
                    rows.Add(new ReferralPriorityRowModel { FieldId = field.FieldId, FieldTitle = field.FieldTitle });
                else
                    rows.AddRange(children.Select(x => new ReferralPriorityRowModel
                    {
                        FieldId = field.FieldId, FieldTitle = field.FieldTitle, SubsetId = x.Id, SubsetTitle = x.Title
                    }));
            }

            var saved = db.Database.SqlQuery<PriorityFlatRow>(@"
SELECT r.FieldId, r.SubsetId, r.MinimumAmount, r.MaximumAmount, w.IndicatorId, w.Weight
FROM dbo.tbHeadOfficePriorityProfile p
INNER JOIN dbo.tbHeadOfficePriorityRule r ON r.ProfileId=p.ProfileId
LEFT JOIN dbo.tbHeadOfficePriorityWeight w ON w.RuleId=r.RuleId
WHERE ((@CityId IS NULL AND p.CityId IS NULL) OR p.CityId=@CityId)", NullableIntParameter("@CityId", cityId)).ToList();
            foreach (var row in rows)
            {
                var matches = saved.Where(x => x.FieldId == row.FieldId && x.SubsetId == row.SubsetId).ToList();
                var first = matches.FirstOrDefault();
                if (first == null) continue;
                row.MinimumAmount = first.MinimumAmount;
                row.MaximumAmount = first.MaximumAmount;
                foreach (var item in matches.Where(x => x.IndicatorId.HasValue))
                    row.Weights[item.IndicatorId.Value] = item.Weight ?? 0;
            }
            return rows;
        }

        private int GetOrCreatePriorityProfile(int? cityId)
        {
            var existing = db.Database.SqlQuery<int?>(@"
SELECT TOP 1 ProfileId FROM dbo.tbHeadOfficePriorityProfile
WHERE ((@CityId IS NULL AND CityId IS NULL) OR CityId=@CityId)", NullableIntParameter("@CityId", cityId)).FirstOrDefault();
            if (existing.HasValue) return existing.Value;
            return db.Database.SqlQuery<int>(@"
INSERT INTO dbo.tbHeadOfficePriorityProfile (CityId, CreatedAt, CreatedBy)
OUTPUT INSERTED.ProfileId VALUES (@CityId, SYSDATETIME(), @UserId)",
                NullableIntParameter("@CityId", cityId), NullableIntParameter("@UserId", GetCurrentUserId())).Single();
        }

        private void CopyPriorityProfile(int sourceCityId, int targetCityId)
        {
            using (var transaction = db.Database.BeginTransaction())
            {
                var source = db.Database.SqlQuery<int?>("SELECT TOP 1 ProfileId FROM dbo.tbHeadOfficePriorityProfile WHERE CityId=@CityId",
                    new SqlParameter("@CityId", sourceCityId)).FirstOrDefault();
                if (!source.HasValue) throw new InvalidOperationException("برای مدیریت مبنا تنظیماتی ثبت نشده است.");
                var target = GetOrCreatePriorityProfile(targetCityId);
                db.Database.ExecuteSqlCommand(@"
DELETE w FROM dbo.tbHeadOfficePriorityWeight w INNER JOIN dbo.tbHeadOfficePriorityRule r ON r.RuleId=w.RuleId WHERE r.ProfileId=@Target;
DELETE FROM dbo.tbHeadOfficePriorityRule WHERE ProfileId=@Target;", new SqlParameter("@Target", target));
                var sourceRules = db.Database.SqlQuery<PriorityCopyRow>(@"
SELECT RuleId, FieldId, SubsetId, MinimumAmount, MaximumAmount FROM dbo.tbHeadOfficePriorityRule WHERE ProfileId=@Source",
                    new SqlParameter("@Source", source.Value)).ToList();
                foreach (var rule in sourceRules)
                {
                    var newId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.tbHeadOfficePriorityRule (ProfileId,FieldId,SubsetId,MinimumAmount,MaximumAmount,CreatedAt)
OUTPUT INSERTED.RuleId VALUES (@ProfileId,@FieldId,@SubsetId,@Min,@Max,SYSDATETIME())",
                        new SqlParameter("@ProfileId", target), new SqlParameter("@FieldId", rule.FieldId),
                        NullableLongParameter("@SubsetId", rule.SubsetId), NullableDecimalParameter("@Min", rule.MinimumAmount),
                        NullableDecimalParameter("@Max", rule.MaximumAmount)).Single();
                    db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.tbHeadOfficePriorityWeight (RuleId,IndicatorId,Weight)
SELECT @NewRuleId, IndicatorId, Weight FROM dbo.tbHeadOfficePriorityWeight WHERE RuleId=@OldRuleId",
                        new SqlParameter("@NewRuleId", newId), new SqlParameter("@OldRuleId", rule.RuleId));
                }
                transaction.Commit();
            }
        }

        private static void NormalizeList(SaveReferralListModel model)
        {
            if (model == null) return;
            model.Title = (model.Title ?? string.Empty).Trim();
            model.RoutingType = model.RoutingType == "gis" ? "gis" : "address";
            model.Scope = model.Scope == "city" ? "city" : "all";
        }

        private int? GetCurrentUserId()
        {
            try
            {
                var cookie = Request.Cookies[Base64.Base64Encode("CodeMeli")];
                if (cookie == null) return null;
                var nationalCode = Base64.Base64Decode(cookie.Value);
                return db.tbUsers.Where(x => x.usr_NationalCode == nationalCode).Select(x => (int?)x.usr_ID).FirstOrDefault();
            }
            catch { return null; }
        }

        private void SetSuccess(string message) { TempData["ReferralSuccess"] = message; }
        private void SetError(string message) { TempData["ReferralError"] = message; }
        private static SqlParameter NullableIntParameter(string name, int? value) { return new SqlParameter(name, (object)value ?? DBNull.Value); }
        private static SqlParameter NullableLongParameter(string name, long? value) { return new SqlParameter(name, (object)value ?? DBNull.Value); }
        private static SqlParameter NullableDecimalParameter(string name, decimal? value) { return new SqlParameter(name, (object)value ?? DBNull.Value); }

        private class PriorityFlatRow
        {
            public long FieldId { get; set; }
            public long? SubsetId { get; set; }
            public decimal? MinimumAmount { get; set; }
            public decimal? MaximumAmount { get; set; }
            public int? IndicatorId { get; set; }
            public decimal? Weight { get; set; }
        }

        private class PriorityCopyRow
        {
            public int RuleId { get; set; }
            public long FieldId { get; set; }
            public long? SubsetId { get; set; }
            public decimal? MinimumAmount { get; set; }
            public decimal? MaximumAmount { get; set; }
        }

        private class ListDetailRow
        {
            public int ListId { get; set; }
            public string Title { get; set; }
            public long ReportId { get; set; }
            public long KeyFieldId { get; set; }
            public int DailyAmount { get; set; }
            public int DurationDays { get; set; }
            public string RoutingType { get; set; }
            public bool AllowDuplicate { get; set; }
            public int? CityId { get; set; }
        }

        private void EnsureStorage()
        {
            db.Database.ExecuteSqlCommand(@"
IF OBJECT_ID(N'dbo.tbHeadOfficeMainIndicator', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.tbHeadOfficeMainIndicator(IndicatorId INT IDENTITY PRIMARY KEY, Title NVARCHAR(150) NOT NULL, Unit NVARCHAR(50) NOT NULL, IsDeleted BIT NOT NULL DEFAULT(0), CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), CreatedBy INT NULL, UpdatedAt DATETIME2 NULL, UpdatedBy INT NULL, DeletedAt DATETIME2 NULL, DeletedBy INT NULL);
 CREATE UNIQUE INDEX UX_tbHeadOfficeMainIndicator_ActiveTitle ON dbo.tbHeadOfficeMainIndicator(Title) WHERE IsDeleted=0;
END;
IF OBJECT_ID(N'dbo.tbHeadOfficeBaseField', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.tbHeadOfficeBaseField(BaseFieldId INT IDENTITY PRIMARY KEY, ReportId BIGINT NOT NULL, FieldId BIGINT NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), CreatedBy INT NULL);
 CREATE UNIQUE INDEX UX_tbHeadOfficeBaseField ON dbo.tbHeadOfficeBaseField(ReportId,FieldId);
END;
IF OBJECT_ID(N'dbo.tbHeadOfficePriorityProfile', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.tbHeadOfficePriorityProfile(ProfileId INT IDENTITY PRIMARY KEY, CityId INT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), CreatedBy INT NULL);
 CREATE UNIQUE INDEX UX_tbHeadOfficePriorityProfile_Global ON dbo.tbHeadOfficePriorityProfile(CityId) WHERE CityId IS NOT NULL;
END;
IF OBJECT_ID(N'dbo.tbHeadOfficePriorityRule', N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficePriorityRule(RuleId INT IDENTITY PRIMARY KEY, ProfileId INT NOT NULL, FieldId BIGINT NOT NULL, SubsetId BIGINT NULL, MinimumAmount DECIMAL(18,2) NULL, MaximumAmount DECIMAL(18,2) NULL, CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME());
IF OBJECT_ID(N'dbo.tbHeadOfficePriorityWeight', N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficePriorityWeight(WeightId INT IDENTITY PRIMARY KEY, RuleId INT NOT NULL, IndicatorId INT NOT NULL, Weight DECIMAL(6,2) NOT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeListDefinition', N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeListDefinition(ListId INT IDENTITY PRIMARY KEY, Title NVARCHAR(200) NOT NULL, ReportId BIGINT NOT NULL, KeyFieldId BIGINT NOT NULL, DailyAmount INT NOT NULL, DurationDays INT NOT NULL, RoutingType VARCHAR(20) NOT NULL, AllowDuplicate BIT NOT NULL, CityId INT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), CreatedBy INT NULL, UpdatedAt DATETIME2 NULL, UpdatedBy INT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeListException', N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeListException(ExceptionId INT IDENTITY PRIMARY KEY, ListId INT NOT NULL, FieldId BIGINT NOT NULL, SubsetId BIGINT NOT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeCondition', N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeCondition(ConditionId INT IDENTITY PRIMARY KEY, Title NVARCHAR(200) NOT NULL, Description NVARCHAR(1000) NULL, ExpressionText NVARCHAR(MAX) NOT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(), CreatedBy INT NULL, UpdatedAt DATETIME2 NULL, UpdatedBy INT NULL);");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
