using System.Web;
using System.Web.Mvc;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // =====================================================
    // MDR | Phase 1 | مجوز صفحه‌ای ماژول‌های جدید
    // از جدول‌های موجود tbPages / tbUserPageAccess و
    // PermissionHelper فعلی استفاده می‌کند (بدون تغییر در آن‌ها).
    // فیلتر قدیمی PageAuthorize به Area ای ارجاع می‌دهد که وجود
    // ندارد؛ این نسخه به صفحه‌ی AccessDenied خودِ Mdr می‌رود.
    //
    // نمونه:  [MdrPageAuthorize("MDR.Home")]
    // =====================================================
    public class MdrPageAuthorizeAttribute : AuthorizeAttribute
    {
        private readonly string _pageCode;

        public MdrPageAuthorizeAttribute(string pageCode)
        {
            _pageCode = pageCode;
        }

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            var userId = CurrentUserHelper.UserID;

            if (!userId.HasValue)
                return false;

            return PermissionHelper.HasAccess(userId.Value, _pageCode);
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.HttpContext.Response.StatusCode = 403;
                filterContext.Result = new JsonResult
                {
                    Data = new { success = false, message = "AccessDenied" },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet
                };
                return;
            }

            filterContext.Result = new RedirectToRouteResult(
                new System.Web.Routing.RouteValueDictionary
                {
                    { "area", "Mdr" },
                    { "controller", "Home" },
                    { "action", "AccessDenied" }
                });
        }
    }
}

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // فقط «ورود به سامانه» (برای اعلان‌ها که هر کاربر فقط داده‌ی خودش را می‌بیند)
    public class MdrLoginAuthorizeAttribute : MdrPageAuthorizeAttribute
    {
        public MdrLoginAuthorizeAttribute() : base(null) { }
        protected override bool AuthorizeCore(HttpContextBase httpContext) { return SaabWebProject.Areas.Mdr.Core.CurrentUserHelper.UserID.HasValue; }
    }

    // صفحه‌های مدیریتی زیرساخت (ساختار سازمانی، رویدادها، مدیریت فایل‌ها): همان قاعده‌ی منو — دسترسی ویرایش (CanEdit) روی دست‌کم یک صفحه‌ی MDR
    public class MdrManagerAuthorizeAttribute : MdrPageAuthorizeAttribute
    {
        public MdrManagerAuthorizeAttribute() : base(null) { }
        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            var uid = SaabWebProject.Areas.Mdr.Core.CurrentUserHelper.UserID;
            if (!uid.HasValue) return false;
            return MdrMenu.ForUser(uid.Value, true).IsManager;
        }
    }

    // دفاع در برابر CSRF برای POSTهایی که توکن ضدجعل ندارند: Origin/Referer باید همان میزبان باشد (نبودن هر دو = رد)
    public class MdrSameOriginAttribute : AuthorizeAttribute
    {
        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            var req = httpContext.Request;
            if (!string.Equals(req.HttpMethod, "POST", System.StringComparison.OrdinalIgnoreCase)) return true;
            System.Uri src = null;
            var origin = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin)) System.Uri.TryCreate(origin, System.UriKind.Absolute, out src);
            else src = req.UrlReferrer;
            return src != null && string.Equals(src.Host, req.Url.Host, System.StringComparison.OrdinalIgnoreCase);
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            filterContext.Result = new HttpStatusCodeResult(403, "Cross-site request blocked");
        }
    }
}
