using SaabWebProject.Areas.Mdr.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SaabWebProject.Areas.Mdr.Core
{
    //public static class PermissionHelper
    //{
    //    private const string PermissionSessionKey =
    //        "CurrentUserPagePermissions";


    //    // =========================================================
    //    // بررسی دسترسی بر اساس UserID
    //    // برای PageAuthorize
    //    // =========================================================

    //    public static bool HasAccess(
    //        int userId,
    //        string pageCode)
    //    {
    //        using (var db = new MdrContext())
    //        {
    //            return db.tbUserPageAccess.Any(x =>
    //                x.UserID == userId &&
    //                x.CanView &&
    //                x.tbPages.PageCode == pageCode &&
    //                x.tbPages.IsActive);
    //        }
    //    }


    //    // =========================================================
    //    // دریافت لیست دسترسی‌های کاربر فعلی
    //    // =========================================================

    //    private static HashSet<string>
    //        GetCurrentUserPermissions()
    //    {
    //        var session =
    //            HttpContext.Current.Session;

    //        if (session == null)
    //        {
    //            return new HashSet<string>();
    //        }


    //        var cached =
    //            session[PermissionSessionKey]
    //            as HashSet<string>;

    //        if (cached != null)
    //        {
    //            return cached;
    //        }


    //        var userId =
    //            CurrentUserHelper.UserID;


    //        if (!userId.HasValue)
    //        {
    //            return new HashSet<string>();
    //        }


    //        using (var db = new MdrContext())
    //        {
    //            var pages = db.tbUserPageAccess
    //                .Where(x =>
    //                    x.UserID == userId.Value &&
    //                    x.CanView &&
    //                    x.tbPages.IsActive)
    //                .Select(x => x.tbPages.PageCode)
    //                .ToList();


    //            var permissions =
    //                new HashSet<string>(pages);


    //            session[PermissionSessionKey] =
    //                permissions;


    //            return permissions;
    //        }
    //    }


    //    // =========================================================
    //    // بررسی دسترسی کاربر فعلی
    //    // =========================================================

    //    public static bool HasAccess(
    //        string pageCode)
    //    {
    //        if (string.IsNullOrWhiteSpace(pageCode))
    //        {
    //            return false;
    //        }


    //        var permissions =
    //            GetCurrentUserPermissions();


    //        return permissions.Contains(pageCode);
    //    }


    //    // =========================================================
    //    // بررسی چند صفحه
    //    // =========================================================

    //    public static bool HasAnyAccess(
    //        params string[] pageCodes)
    //    {
    //        if (pageCodes == null ||
    //            pageCodes.Length == 0)
    //        {
    //            return false;
    //        }


    //        var permissions =
    //            GetCurrentUserPermissions();


    //        foreach (var pageCode in pageCodes)
    //        {
    //            if (permissions.Contains(pageCode))
    //            {
    //                return true;
    //            }
    //        }


    //        return false;
    //    }


    //    // =========================================================
    //    // پاک کردن دسترسی‌های Cache شده
    //    // =========================================================

    //    public static void ClearCache()
    //    {
    //        var session =
    //            HttpContext.Current.Session;

    //        if (session != null)
    //        {
    //            session.Remove(
    //                PermissionSessionKey);
    //        }
    //    }
    //}




    public static class PermissionHelper
    {
        private const string PermissionSessionKey =
        "CurrentUserPagePermissions";

        // =========================================================
        // بررسی دسترسی مشاهده بر اساس UserID
        // برای PageAuthorize
        // =========================================================

        public static bool HasAccess(
            int userId,
            string pageCode)
        {
            using (var db = new MdrContext())
            {
                return db.tbUserPageAccess.Any(x =>
                    x.UserID == userId &&
                    x.CanView &&
                    x.tbPages.PageCode == pageCode &&
                    x.tbPages.IsActive);
            }
        }


        // =========================================================
        // مشاهده
        // =========================================================

        public static bool CanView(
            int userId,
            string pageCode)
        {
            if (userId <= 0 ||
                string.IsNullOrWhiteSpace(pageCode))
            {
                return false;
            }

            using (var db = new MdrContext())
            {
                return db.tbUserPageAccess.Any(x =>
                    x.UserID == userId &&
                    x.CanView &&
                    x.tbPages.PageCode == pageCode &&
                    x.tbPages.IsActive);
            }
        }


        // =========================================================
        // ایجاد
        // =========================================================

        public static bool CanInsert(
            int userId,
            string pageCode)
        {
            if (userId <= 0 ||
                string.IsNullOrWhiteSpace(pageCode))
            {
                return false;
            }

            using (var db = new MdrContext())
            {
                return db.tbUserPageAccess.Any(x =>
                    x.UserID == userId &&
                    x.CanInsert &&
                    x.tbPages.PageCode == pageCode &&
                    x.tbPages.IsActive);
            }
        }


        // =========================================================
        // ویرایش
        // =========================================================

        public static bool CanEdit(
            int userId,
            string pageCode)
        {
            if (userId <= 0 ||
                string.IsNullOrWhiteSpace(pageCode))
            {
                return false;
            }

            using (var db = new MdrContext())
            {
                return db.tbUserPageAccess.Any(x =>
                    x.UserID == userId &&
                    x.CanEdit &&
                    x.tbPages.PageCode == pageCode &&
                    x.tbPages.IsActive);
            }
        }


        // =========================================================
        // حذف
        // =========================================================

        public static bool CanDelete(
            int userId,
            string pageCode)
        {
            if (userId <= 0 ||
                string.IsNullOrWhiteSpace(pageCode))
            {
                return false;
            }

            using (var db = new MdrContext())
            {
                return db.tbUserPageAccess.Any(x =>
                    x.UserID == userId &&
                    x.CanDelete &&
                    x.tbPages.PageCode == pageCode &&
                    x.tbPages.IsActive);
            }
        }


        // =========================================================
        // متدهای دسترسی برای کاربر فعلی
        // =========================================================

        public static bool CanView(
            string pageCode)
        {
            if (!CurrentUserHelper.UserID.HasValue)
            {
                return false;
            }

            return CanView(
                CurrentUserHelper.UserID.Value,
                pageCode);
        }


        public static bool CanInsert(
            string pageCode)
        {
            if (!CurrentUserHelper.UserID.HasValue)
            {
                return false;
            }

            return CanInsert(
                CurrentUserHelper.UserID.Value,
                pageCode);
        }


        public static bool CanEdit(
            string pageCode)
        {
            if (!CurrentUserHelper.UserID.HasValue)
            {
                return false;
            }

            return CanEdit(
                CurrentUserHelper.UserID.Value,
                pageCode);
        }


        public static bool CanDelete(
            string pageCode)
        {
            if (!CurrentUserHelper.UserID.HasValue)
            {
                return false;
            }

            return CanDelete(
                CurrentUserHelper.UserID.Value,
                pageCode);
        }


        // =========================================================
        // دریافت لیست دسترسی‌های مشاهده کاربر فعلی
        // برای Sidebar و PageAuthorize
        // =========================================================

        private static HashSet<string>
            GetCurrentUserPermissions()
        {
            var session =
                HttpContext.Current.Session;

            if (session == null)
            {
                return new HashSet<string>();
            }


            var cached =
                session[PermissionSessionKey]
                as HashSet<string>;

            if (cached != null)
            {
                return cached;
            }


            var userId =
                CurrentUserHelper.UserID;


            if (!userId.HasValue)
            {
                return new HashSet<string>();
            }


            using (var db = new MdrContext())
            {
                var pages =
                    db.tbUserPageAccess
                        .Where(x =>
                            x.UserID == userId.Value &&
                            x.CanView &&
                            x.tbPages.IsActive)
                        .Select(x =>
                            x.tbPages.PageCode)
                        .ToList();


                var permissions =
                    new HashSet<string>(pages);


                session[PermissionSessionKey] =
                    permissions;


                return permissions;
            }
        }


        // =========================================================
        // بررسی دسترسی مشاهده کاربر فعلی
        // =========================================================

        public static bool HasAccess(
            string pageCode)
        {
            if (string.IsNullOrWhiteSpace(pageCode))
            {
                return false;
            }


            var permissions =
                GetCurrentUserPermissions();


            return permissions.Contains(pageCode);
        }


        // =========================================================
        // بررسی چند صفحه
        // =========================================================

        public static bool HasAnyAccess(
            params string[] pageCodes)
        {
            if (pageCodes == null ||
                pageCodes.Length == 0)
            {
                return false;
            }


            var permissions =
                GetCurrentUserPermissions();


            foreach (var pageCode in pageCodes)
            {
                if (permissions.Contains(pageCode))
                {
                    return true;
                }
            }


            return false;
        }


        // =========================================================
        // پاک کردن Cache شده
        // =========================================================

        public static void ClearCache()
        {
            var session =
                HttpContext.Current.Session;

            if (session != null)
            {
                session.Remove(
                    PermissionSessionKey);
            }
        }
    }





}