using System.Web.Mvc;

namespace SaabWebProject.Areas.DocumentControl
{
    public class DocumentControlAreaRegistration : AreaRegistration
    {
        public override string AreaName
        {
            get { return "DocumentControl"; }
        }

        public override void RegisterArea(AreaRegistrationContext context)
        {
            // Explicit route precedes the legacy short route to avoid action/controller ambiguity.
            context.MapRoute(
                "DocumentFlow_explicit",
                "DocumentControl/DocumentFlow/{action}/{id}",
                new { controller = "DocumentFlow", action = "Index", id = UrlParameter.Optional },
                new[] { "SaabWebProject.Areas.DocumentControl.Controllers" }
            );

            // مسیر کوتاه ماژول: /DocumentControl/Index و /DocumentControl/GetDashboard
            context.MapRoute(
                "DocumentControl_short",
                "DocumentControl/{action}/{id}",
                new
                {
                    controller = "DocumentControl",
                    action = "Index",
                    id = UrlParameter.Optional
                },
                new[] { "SaabWebProject.Areas.DocumentControl.Controllers" }
            );

            context.MapRoute(
                "DocumentControl_default",
                "DocumentControl/{controller}/{action}/{id}",
                new { action = "Index", id = UrlParameter.Optional },
                new[] { "SaabWebProject.Areas.DocumentControl.Controllers" }
            );
        }
    }
}
