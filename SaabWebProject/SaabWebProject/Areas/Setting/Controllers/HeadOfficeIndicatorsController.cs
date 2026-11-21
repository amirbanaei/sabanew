using SaabWebProject.Models.DomainModels;
using SaabWebProject.Models.ViewModels.Setting;
using SaabWebProject.Utility;
using System;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace SaabWebProject.Areas.Setting.Controllers
{
    /// <summary>
    /// تنظیمات ستادی - تعریف شاخص اصلی.
    /// این کنترلر عمداً از مدل EDMX مستقل است تا بروزرسانی مدل Database-First
    /// باعث تغییر یا بازتولید صفحات و موجودیت‌های قبلی نشود.
    /// </summary>
    public class HeadOfficeIndicatorsController : Controller
    {
        private readonly SaabEntities db = new SaabEntities();

        [AuthorizeAAA]
        public ActionResult Index()
        {
            EnsureStorage();
            return View(BuildPageModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult Save(HeadOfficeIndicatorInputModel model)
        {
            EnsureStorage();
            Normalize(model);

            if (model != null && !string.IsNullOrWhiteSpace(model.Title))
            {
                var duplicateCount = db.Database.SqlQuery<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.tbHeadOfficeMainIndicator
                      WHERE IsDeleted = 0
                        AND LTRIM(RTRIM(Title)) = @Title
                        AND IndicatorId <> @IndicatorId",
                    new SqlParameter("@Title", model.Title),
                    new SqlParameter("@IndicatorId", model.IndicatorId)).Single();

                if (duplicateCount > 0)
                {
                    ModelState.AddModelError("Title", "شاخصی با این عنوان قبلاً ثبت شده است.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View("Index", BuildPageModel(model));
            }

            var userId = GetCurrentUserId();

            if (model.IndicatorId == 0)
            {
                db.Database.ExecuteSqlCommand(
                    @"INSERT INTO dbo.tbHeadOfficeMainIndicator
                        (Title, Unit, IsDeleted, CreatedAt, CreatedBy)
                      VALUES
                        (@Title, @Unit, 0, GETDATE(), @UserId)",
                    new SqlParameter("@Title", model.Title),
                    new SqlParameter("@Unit", model.Unit),
                    NullableIntParameter("@UserId", userId));

                TempData["HeadOfficeIndicatorSuccess"] = "شاخص اصلی با موفقیت ثبت شد.";
            }
            else
            {
                var affected = db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.tbHeadOfficeMainIndicator
                      SET Title = @Title,
                          Unit = @Unit,
                          UpdatedAt = GETDATE(),
                          UpdatedBy = @UserId
                      WHERE IndicatorId = @IndicatorId
                        AND IsDeleted = 0",
                    new SqlParameter("@Title", model.Title),
                    new SqlParameter("@Unit", model.Unit),
                    NullableIntParameter("@UserId", userId),
                    new SqlParameter("@IndicatorId", model.IndicatorId));

                TempData[affected > 0 ? "HeadOfficeIndicatorSuccess" : "HeadOfficeIndicatorError"] =
                    affected > 0 ? "اطلاعات شاخص با موفقیت ویرایش شد." : "شاخص موردنظر پیدا نشد.";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAAA]
        public ActionResult Delete(int id)
        {
            EnsureStorage();
            var affected = db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.tbHeadOfficeMainIndicator
                  SET IsDeleted = 1,
                      DeletedAt = GETDATE(),
                      DeletedBy = @UserId
                  WHERE IndicatorId = @IndicatorId
                    AND IsDeleted = 0",
                NullableIntParameter("@UserId", GetCurrentUserId()),
                new SqlParameter("@IndicatorId", id));

            TempData[affected > 0 ? "HeadOfficeIndicatorSuccess" : "HeadOfficeIndicatorError"] =
                affected > 0 ? "شاخص اصلی حذف شد." : "شاخص موردنظر پیدا نشد یا قبلاً حذف شده است.";

            return RedirectToAction("Index");
        }

        private HeadOfficeIndicatorPageViewModel BuildPageModel(HeadOfficeIndicatorInputModel form = null)
        {
            return new HeadOfficeIndicatorPageViewModel
            {
                Form = form ?? new HeadOfficeIndicatorInputModel(),
                Indicators = db.Database.SqlQuery<HeadOfficeIndicatorRowModel>(
                    @"SELECT IndicatorId, Title, Unit, CreatedAt, UpdatedAt
                      FROM dbo.tbHeadOfficeMainIndicator
                      WHERE IsDeleted = 0
                      ORDER BY IndicatorId DESC").ToList()
            };
        }

        private static void Normalize(HeadOfficeIndicatorInputModel model)
        {
            if (model == null) return;
            model.Title = (model.Title ?? string.Empty).Trim();
            model.Unit = (model.Unit ?? string.Empty).Trim();
        }

        private int? GetCurrentUserId()
        {
            try
            {
                var cookie = Request.Cookies[Base64.Base64Encode("CodeMeli")];
                if (cookie == null) return null;

                var nationalCode = Base64.Base64Decode(cookie.Value);
                return db.tbUsers
                    .Where(x => x.usr_NationalCode == nationalCode)
                    .Select(x => (int?)x.usr_ID)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static SqlParameter NullableIntParameter(string name, int? value)
        {
            return new SqlParameter(name, (object)value ?? DBNull.Value);
        }

        /// <summary>
        /// ایجاد idempotent جدول جدید. تنها همین جدول تازه را مدیریت می‌کند و هیچ
        /// تغییری در جداول یا صفحات قبلی نمی‌دهد.
        /// </summary>
        private void EnsureStorage()
        {
            db.Database.ExecuteSqlCommand(@"
IF OBJECT_ID(N'dbo.tbHeadOfficeMainIndicator', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbHeadOfficeMainIndicator
    (
        IndicatorId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_tbHeadOfficeMainIndicator PRIMARY KEY,
        Title NVARCHAR(150) NOT NULL,
        Unit NVARCHAR(50) NOT NULL,
        IsDeleted BIT NOT NULL
            CONSTRAINT DF_tbHeadOfficeMainIndicator_IsDeleted DEFAULT (0),
        CreatedAt DATETIME2(0) NOT NULL
            CONSTRAINT DF_tbHeadOfficeMainIndicator_CreatedAt DEFAULT (SYSDATETIME()),
        CreatedBy INT NULL,
        UpdatedAt DATETIME2(0) NULL,
        UpdatedBy INT NULL,
        DeletedAt DATETIME2(0) NULL,
        DeletedBy INT NULL
    );

    CREATE UNIQUE INDEX UX_tbHeadOfficeMainIndicator_ActiveTitle
        ON dbo.tbHeadOfficeMainIndicator(Title)
        WHERE IsDeleted = 0;
END");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
