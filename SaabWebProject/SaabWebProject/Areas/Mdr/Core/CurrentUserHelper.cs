using System;
using System.Linq;
using System.Web;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Core
{
    public static class CurrentUserHelper
    {
        /// <summary>
        /// شناسه کاربر فعلی
        /// کاربر از Cookie کد ملی شناسایی می‌شود.
        /// </summary>
        public static int? UserID
        {
            get
            {
                try
                {
                    var context = HttpContext.Current;

                    if (context == null)
                        return null;

                    // نام Cookie
                    string cookieName = MdrBase64.Base64Encode("CodeMeli");

                    var cookie = context.Request.Cookies[cookieName];

                    if (cookie == null ||
                        string.IsNullOrWhiteSpace(cookie.Value))
                    {
                        return null;
                    }

                    // Decode کد ملی
                    string nationalCode =
                        MdrBase64.Base64Decode(cookie.Value);

                    if (string.IsNullOrWhiteSpace(nationalCode))
                        return null;

                    // پیدا کردن کاربر
                    using (var db = new MdrContext())
                    {
                        var user = db.tbUsers
                            .FirstOrDefault(x =>
                                x.usr_NationalCode == nationalCode);

                        if (user == null)
                            return null;

                        return user.usr_ID;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }


        /// <summary>
        /// اطلاعات کاربر فعلی
        /// </summary>
        public static MdrUser User
        {
            get
            {
                try
                {
                    var context = HttpContext.Current;

                    if (context == null)
                        return null;

                    string cookieName =
                        MdrBase64.Base64Encode("CodeMeli");

                    var cookie =
                        context.Request.Cookies[cookieName];

                    if (cookie == null ||
                        string.IsNullOrWhiteSpace(cookie.Value))
                    {
                        return null;
                    }

                    string nationalCode =
                        MdrBase64.Base64Decode(cookie.Value);

                    if (string.IsNullOrWhiteSpace(nationalCode))
                        return null;

                    using (var db = new MdrContext())
                    {
                        return db.tbUsers
                            .FirstOrDefault(x =>
                                x.usr_NationalCode == nationalCode);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }


        /// <summary>
        /// آیا کاربر فعلی شناسایی شده است؟
        /// </summary>
        public static bool IsAuthenticated
        {
            get
            {
                return UserID.HasValue;
            }
        }
    }
}