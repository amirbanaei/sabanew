using System.Collections.Generic;

namespace SaabWebProject.Areas.Mdr.Infrastructure
{
    // =====================================================
    // MDR | فهرست پنل‌های سند جامع (۱ تا ۱۶)
    // هر ماژول وقتی ساخته شد: Available = true و Url پر می‌شود.
    // صفحه‌ی ورودی /Mdr فقط همین فهرست را نمایش می‌دهد.
    // =====================================================
    public class MdrModuleInfo
    {
        public int Number { get; set; }
        public string Code { get; set; }
        public string TitleKey { get; set; }
        public bool Available { get; set; }
        public string Url { get; set; }
    }

    public static class MdrModuleCatalog
    {
        public static readonly IReadOnlyList<MdrModuleInfo> All = new List<MdrModuleInfo>
        {
            new MdrModuleInfo { Number = 1,  Code = "Core",          TitleKey = "mod.core", Available = true, Url = "~/Mdr/AG/OrganizationStructure" },
            new MdrModuleInfo { Number = 2,  Code = "Workflow",      TitleKey = "mod.workflow", Available = true, Url = "~/Mdr/Workflow" },
            new MdrModuleInfo { Number = 3,  Code = "Notification",  TitleKey = "mod.notification", Available = true, Url = "~/Mdr/AG/Notifications" },
            new MdrModuleInfo { Number = 4,  Code = "Workbench",     TitleKey = "mod.workbench", Available = true, Url = "~/Mdr/Workbench" },
            new MdrModuleInfo { Number = 5,  Code = "Correspondence",TitleKey = "mod.correspondence", Available = true, Url = "~/Mdr/Letters" },
            new MdrModuleInfo { Number = 6,  Code = "Requests",      TitleKey = "mod.requests", Available = true, Url = "~/Mdr/Requests" },
            new MdrModuleInfo { Number = 7,  Code = "Tasks",         TitleKey = "mod.tasks", Available = true, Url = "~/Mdr/Tasks" },
            new MdrModuleInfo { Number = 8,  Code = "Documents",     TitleKey = "mod.documents", Available = true, Url = "~/Mdr/Documents" },
            new MdrModuleInfo { Number = 9,  Code = "Cases",         TitleKey = "mod.cases", Available = true, Url = "~/Mdr/Cases" },
            new MdrModuleInfo { Number = 10, Code = "Search",        TitleKey = "mod.search", Available = true, Url = "~/Mdr/Search" },
            new MdrModuleInfo { Number = 11, Code = "Analytics",     TitleKey = "mod.analytics", Available = true, Url = "~/Mdr/Reports" },
            new MdrModuleInfo { Number = 12, Code = "Chat",          TitleKey = "mod.chat", Available = true, Url = "~/Mdr/Chat" },
            new MdrModuleInfo { Number = 13, Code = "Finance",       TitleKey = "mod.finance", Available = true, Url = "~/Mdr/Finance" },
            new MdrModuleInfo { Number = 14, Code = "Inventory",     TitleKey = "mod.inventory", Available = true, Url = "~/Mdr/Warehouse" },
            new MdrModuleInfo { Number = 15, Code = "Training",      TitleKey = "mod.training", Available = true, Url = "~/Mdr/Training" },
            new MdrModuleInfo { Number = 16, Code = "AI",            TitleKey = "mod.ai", Available = true, Url = "~/Mdr/Ai" }
        };
    }
}
