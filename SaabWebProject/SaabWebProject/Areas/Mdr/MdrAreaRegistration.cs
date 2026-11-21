using System.Web.Mvc;

namespace SaabWebProject.Areas.Mdr
{
    // =====================================================
    // MDR | Phase 0 | Area مشترک ماژول‌های میز مدیریت
    // همه‌ی ماژول‌های جدید سند جامع زیر مسیر /Mdr قرار می‌گیرند
    // و به AGController یا Areaهای قدیمی دست نمی‌زنند.
    // =====================================================
    public class MdrAreaRegistration : AreaRegistration
    {
        public override string AreaName
        {
            get { return "Mdr"; }
        }

        public override void RegisterArea(AreaRegistrationContext context)
        {
            // تنها مسیر Area (کنترلر یکپارچه‌ی MdrController؛ جزئیات در MdrRoute)
            context.Routes.Add(new SaabWebProject.Areas.Mdr.Infrastructure.MdrRoute());

            // زمان‌سنج بررسی SLA گردش کار (غیرفعال‌سازی: appSettings «Mdr.SlaEnabled» = false)
            SaabWebProject.Areas.Mdr.Services.MdrSlaScheduler.Start();

            // اتصال نتیجه‌ی گردش کار به وضعیت درخواست‌ها
            SaabWebProject.Areas.Mdr.Services.MdrRequestService.Register();
        }
    }
}
