using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // MDR | بررسی سطح دسترسی (View/Insert/Edit/Delete) داخل اکشن‌ها
    // روی همان جدول Security.tbUserPageAccess و PermissionHelper فعلی.
    public static class MdrPermission
    {
        public static bool CanInsert(string pageCode)
        {
            var uid = CurrentUserHelper.UserID;
            return uid.HasValue && PermissionHelper.CanInsert(uid.Value, pageCode);
        }

        public static bool CanEdit(string pageCode)
        {
            var uid = CurrentUserHelper.UserID;
            return uid.HasValue && PermissionHelper.CanEdit(uid.Value, pageCode);
        }
    }
}
