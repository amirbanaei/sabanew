using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // =====================================================
    // MDR | مسیر یکپارچه‌ی Area
    // Area فقط یک کنترلر دارد (MdrController) با اکشن‌هایی به شکل «بخش_عملیات» (مثلاً Tasks_GetList).
    // این مسیر آدرس‌های همیشگی را حفظ می‌کند:
    //   ورودی : /Mdr/{بخش}/{عملیات}/{id}   →  controller=Mdr ، action={بخش}_{عملیات}
    //   خروجی : Url.Action("GetList", "Tasks", new { area = "Mdr" })  →  /Mdr/Tasks/GetList
    // پس هیچ View/JS لازم نیست «نام کنترلر» را بداند.
    // =====================================================
    public class MdrRoute : Route
    {
        public static readonly string[] Sections =
        {
            "Home", "Workbench", "Letters", "Requests", "Tasks", "Documents", "Cases", "Workflow", "Search", "Reports",
            "Chat", "Finance", "Warehouse", "Assets", "Training", "Ai", "Files", "AG"
        };

        public MdrRoute()
            : base("Mdr/{section}/{op}/{id}",
                   new RouteValueDictionary(new { section = "Home", op = "Index", id = UrlParameter.Optional }),
                   new RouteValueDictionary(new { section = "(?i)^(" + string.Join("|", Sections) + ")$" }),
                   new RouteValueDictionary(new { area = "Mdr", UseNamespaceFallback = false, Namespaces = new[] { "SaabWebProject.Areas.Mdr.Controllers" } }),
                   new MvcRouteHandler())
        {
        }

        private static string Canonical(string section)
        {
            return Sections.FirstOrDefault(s => string.Equals(s, section, StringComparison.OrdinalIgnoreCase));
        }

        public override RouteData GetRouteData(HttpContextBase httpContext)
        {
            var rd = base.GetRouteData(httpContext);
            if (rd == null) return null;
            var section = Canonical(Convert.ToString(rd.Values["section"]));
            var op = Convert.ToString(rd.Values["op"]);
            if (section == null || string.IsNullOrEmpty(op)) return null;
            rd.Values["controller"] = "Mdr";
            rd.Values["action"] = section + "_" + op;
            rd.Values.Remove("section");
            rd.Values.Remove("op");
            return rd;
        }

        public override VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values)
        {
            object c, a;
            values.TryGetValue("controller", out c);
            values.TryGetValue("action", out a);
            var section = Canonical(Convert.ToString(c));
            var op = Convert.ToString(a);
            if (section == null && string.Equals(Convert.ToString(c), "Mdr", StringComparison.OrdinalIgnoreCase) && op.IndexOf('_') > 0)
            {
                section = Canonical(op.Substring(0, op.IndexOf('_')));
                op = op.Substring(op.IndexOf('_') + 1);
            }
            if (section == null) return null;

            var v = new RouteValueDictionary(values);
            v.Remove("controller"); v.Remove("action");
            v["section"] = section; v["op"] = string.IsNullOrEmpty(op) ? "Index" : op;
            var vp = base.GetVirtualPath(requestContext, v);
            if (vp == null) return null;
            foreach (var kv in DataTokens) vp.DataTokens[kv.Key] = kv.Value;
            return vp;
        }
    }
}
