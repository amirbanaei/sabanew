using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // =====================================================
    // MDR | منوی کناری سامانه (بخش «MDR»)
    // صفحه‌هایی که کاربر فعلی می‌تواند ببیند (Security.tbUserPageAccess، CanView=1، صفحه‌ی فعال) را
    // با یک کوئری برمی‌گرداند و ۶۰ ثانیه در Cache نگه می‌دارد (تغییر دسترسی تا یک دقیقه بعد در منو دیده می‌شود).
    // مهم: این کد داخل Layout کل سامانه اجرا می‌شود، پس هرگز نباید Exception بدهد؛ در هر خطا منو خالی است
    // (یعنی بخش MDR نمایش داده نمی‌شود) و بقیه‌ی منو سالم می‌ماند.
    // PermissionHelper.HasAccess(pageCode) عمداً استفاده نشد چون به Session وابسته است.
    // =====================================================
    public class MdrMenuModel
    {
        public HashSet<string> Pages { get; private set; }
        public bool IsManager { get; set; }          // دسترسی ویرایش (CanEdit) روی دست‌کم یک صفحه‌ی MDR

        public MdrMenuModel() { Pages = new HashSet<string>(StringComparer.Ordinal); }

        public bool Any { get { return Pages.Count > 0; } }
        public bool Has(string pageCode) { return Pages.Contains(pageCode); }
    }

    public static class MdrMenu
    {
        private class Row { public string PageCode { get; set; } public bool CanEdit { get; set; } }

        private const int CacheSeconds = 60;

        public static MdrMenuModel ForCurrentUser()
        {
            try
            {
                var uid = CurrentUserHelper.UserID;
                return uid.HasValue ? ForUser(uid.Value, true) : new MdrMenuModel();
            }
            catch
            {
                return new MdrMenuModel();
            }
        }

        public static MdrMenuModel ForUser(int userId, bool useCache)
        {
            var key = "mdr.menu." + userId;

            try
            {
                if (useCache)
                {
                    var cached = HttpRuntime.Cache == null ? null : HttpRuntime.Cache[key] as MdrMenuModel;
                    if (cached != null) return cached;
                }

                var model = new MdrMenuModel();
                using (var db = new MdrContext())
                {
                    var rows = db.Database.SqlQuery<Row>(
                        "SELECT p.PageCode AS PageCode, a.CanEdit AS CanEdit FROM Security.tbUserPageAccess a " +
                        "JOIN Security.tbPages p ON p.PageID = a.PageID " +
                        "WHERE a.UserID = @p0 AND a.CanView = 1 AND p.IsActive = 1 AND p.PageCode LIKE N'MDR.%'", userId).ToList();

                    foreach (var r in rows)
                    {
                        model.Pages.Add(r.PageCode);
                        if (r.CanEdit) model.IsManager = true;
                    }
                }

                if (useCache && HttpRuntime.Cache != null)
                    HttpRuntime.Cache.Insert(key, model, null, DateTime.UtcNow.AddSeconds(CacheSeconds), System.Web.Caching.Cache.NoSlidingExpiration);

                return model;
            }
            catch
            {
                return new MdrMenuModel();
            }
        }
    }
}
