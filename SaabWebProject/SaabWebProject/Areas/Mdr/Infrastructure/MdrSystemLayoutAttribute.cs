using System.Web.Mvc;
using System;
using System.Reflection;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // =====================================================
    // MDR | صفحه‌هایی که از لایه‌ی فعلی سامانه (Views/Shared/_Layout) استفاده می‌کنند
    // منوی کناری آن لایه با UserStuf.PosID (سمت کاربر از کوکی) ساخته می‌شود و این مقدار فقط
    // در AuthorizeAAA تنظیم می‌شد. این فیلتر همان کار را null-safe انجام می‌دهد تا منو درست دیده شود.
    // (UserStuf.PosID از قبل static است؛ رفتار همان سامانه‌ی فعلی.)
    // =====================================================
    public class MdrSystemLayoutAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            try
            {
                var cookie = filterContext.HttpContext.Request.Cookies[MdrBase64.Base64Encode("positionID")];
                if (cookie != null && !string.IsNullOrWhiteSpace(cookie.Value))
                {
                    // لایه‌ی سامانه منوی قدیمی را با فیلد static «UserStuf.PosID» می‌سازد؛ برای خودکفا بودن Area از Reflection استفاده می‌شود
                    // (اگر آن کلاس در پروژه نبود، بی‌اثر است).
                    var t = Type.GetType("SaabWebProject.Models.Classes.UserStuf");
                    var f = t == null ? null : t.GetField("PosID", BindingFlags.Public | BindingFlags.Static);
                    if (f != null) f.SetValue(null, MdrBase64.Base64Decode(cookie.Value));
                }
            }
            catch
            {
                // کوکی خراب: منو پیش‌فرض می‌ماند؛ نمایش صفحه نباید خراب شود
            }
        }
    }
}
