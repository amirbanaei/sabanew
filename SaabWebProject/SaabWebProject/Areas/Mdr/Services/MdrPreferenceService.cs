using System;
using System.Linq;
using SaabWebProject.Areas.Mdr.Data;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 1 | ترجیحات کاربر (تم و زبان)
    // مثل AuditLogger / NotificationService: هیچ‌وقت Exception
    // پرتاب نمی‌کند تا صفحه به خاطر ترجیحات خراب نشود.
    // =====================================================
    public static class MdrPreferenceService
    {
        public const string Dark = "dark";
        public const string Light = "light";

        public static string GetTheme(int? userId)
        {
            if (!userId.HasValue)
                return Dark;

            try
            {
                using (var db = new MdrContext())
                {
                    var theme = db.UserPreferences
                        .Where(p => p.UserID == userId.Value)
                        .Select(p => p.ThemeMode)
                        .FirstOrDefault();

                    return theme == Light ? Light : Dark;
                }
            }
            catch
            {
                return Dark;
            }
        }

        public static bool SaveTheme(int userId, string theme)
        {
            if (theme != Light && theme != Dark)
                return false;

            try
            {
                using (var db = new MdrContext())
                {
                    var pref = db.UserPreferences.FirstOrDefault(p => p.UserID == userId);

                    if (pref == null)
                    {
                        db.UserPreferences.Add(new MdrUserPreference
                        {
                            UserID = userId,
                            ThemeMode = theme,
                            CultureCode = "fa-IR",
                            UpdatedAt = DateTime.Now
                        });
                    }
                    else
                    {
                        pref.ThemeMode = theme;
                        pref.UpdatedAt = DateTime.Now;
                    }

                    db.SaveChanges();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
