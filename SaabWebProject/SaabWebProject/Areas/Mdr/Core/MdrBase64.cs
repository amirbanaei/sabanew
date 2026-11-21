using System;
using System.Linq;

namespace SaabWebProject.Areas.Mdr.Core
{
    // کدگذاری Cookie سامانه (همان الگوریتم Base64 قدیمی: «=» پایانی به «:» تبدیل می‌شود)؛ برای خودکفا بودن Area کپی شده است.
    public static class MdrBase64
    {
        public static string Base64Encode(string text)
        {
            var res = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
            var u = res.Count(p => p == '=');
            for (var i = 0; i < u; i++) res = res.Replace('=', ' ').Trim() + ":";
            return res;
        }

        public static string Base64Decode(string text)
        {
            var res = text;
            var u = res.Count(p => p == ':');
            for (var i = 0; i < u; i++) res = res.Replace(':', ' ').Trim() + "=";
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(res));
        }
    }
}
