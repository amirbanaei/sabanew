using System;

namespace SaabWebProject.Areas.Mdr.Core
{
    public static class PermissionMenuHelper
    {
        public static bool CanView(
            string pageCode)
        {
            return PermissionHelper.HasAccess(pageCode);
        }


        public static bool CanViewAny(
            params string[] pageCodes)
        {
            return PermissionHelper.HasAnyAccess(
                pageCodes);
        }
    }
}