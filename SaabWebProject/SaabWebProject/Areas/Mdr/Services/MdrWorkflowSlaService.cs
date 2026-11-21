using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 2 | SLA گردش کار
    // CheckOverdue: برای Instance های Running که DueAt گذشته،
    //   یک‌بار برای هر ورود به گام: اعلان Warning به مسئول گام و
    //   شروع‌کننده + ردیف History با کلید SlaBreach + Audit.
    // - ردیف Instance را تغییر نمی‌دهد (RowVer کاربران بی‌دلیل باطل نشود)؛
    //   «قبلاً اعلام شده» از History تشخیص داده می‌شود (SlaBreach بعد از آخرین Enter).
    // - Instance های Paused نادیده گرفته می‌شوند.
    // MdrSlaScheduler: زمان‌سنج درون‌برنامه‌ای (از MdrAreaRegistration شروع می‌شود).
    //   فقط تا زمانی کار می‌کند که برنامه در IIS زنده است؛ برای اطمینان،
    //   مدیر می‌تواند از مانیتور «بررسی SLA» را دستی بزند.
    // =====================================================
    public static class MdrWorkflowSlaService
    {
        private static int _running;

        public static int CheckOverdue()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return 0;

            try
            {
                var notified = 0;
                var now = DateTime.Now;

                using (var db = new MdrContext())
                {
                    var overdue = db.WorkflowInstances
                        .Where(i => i.Status == MdrWorkflowService.Statuses.Running && i.DueAt != null && i.DueAt < now && i.CurrentStepID != null)
                        .ToList();

                    foreach (var inst in overdue)
                    {
                        var lastEnter = db.WorkflowHistory
                            .Where(h => h.InstanceID == inst.InstanceID && h.ActionKey == "Enter")
                            .Select(h => (DateTime?)h.CreatedAt).Max();

                        var already = db.WorkflowHistory.Any(h => h.InstanceID == inst.InstanceID && h.ActionKey == "SlaBreach"
                                                                  && (lastEnter == null || h.CreatedAt >= lastEnter));
                        if (already) continue;

                        var step = db.WorkflowSteps.FirstOrDefault(s => s.StepID == inst.CurrentStepID.Value);
                        if (step == null) continue;

                        db.WorkflowHistory.Add(new MdrWorkflowHistory
                        {
                            InstanceID = inst.InstanceID, StepID = step.StepID, ActionKey = "SlaBreach",
                            ActorUserID = null, Reason = null, CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now
                        });
                        db.SaveChanges();

                        var recipients = new HashSet<int>();
                        if (step.AssigneeType == "User" && step.AssigneeRef.HasValue) recipients.Add(step.AssigneeRef.Value);
                        if (inst.StartedBy.HasValue) recipients.Add(inst.StartedBy.Value);

                        foreach (var uid in recipients)
                            NotificationService.Send(
                                uid,
                                "تأخیر در مهلت (SLA): " + step.Name,
                                inst.Subject,
                                severity: NotificationService.Severities.Warning,
                                moduleName: "Workflow",
                                entityName: "MDR_WorkflowInstances",
                                entityId: inst.InstanceID,
                                linkUrl: "/Mdr/Workflow/Monitor?id=" + inst.InstanceID);

                        AuditLogger.Log(AuditLogger.Actions.Update, "Workflow", "نقض SLA",
                            entityName: "MDR_WorkflowInstances", entityId: inst.InstanceID, entityTitle: inst.Subject,
                            newValues: new { step = step.StepKey, dueAt = inst.DueAt, recipients = recipients.Count });
                        notified++;
                    }
                }

                return notified;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }

    public static class MdrSlaScheduler
    {
        private static Timer _timer;
        private static readonly object Sync = new object();

        public static void Start()
        {
            lock (Sync)
            {
                if (_timer != null) return;

                var minutes = 10;
                int configured;
                if (int.TryParse(ConfigurationManager.AppSettings["Mdr.SlaIntervalMinutes"], out configured) && configured >= 1)
                    minutes = configured;
                if (ConfigurationManager.AppSettings["Mdr.SlaEnabled"] == "false") return;

                _timer = new Timer(Tick, null, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(minutes));
            }
        }

        private static void Tick(object state)
        {
            try
            {
                MdrWorkflowSlaService.CheckOverdue();
            }
            catch (Exception ex)
            {
                // جدول‌ها هنوز ساخته نشده یا DB در دسترس نیست؛ زمان‌سنج باید زنده بماند
                System.Diagnostics.Debug.WriteLine("MDR SLA scheduler: " + ex.Message);
            }

            try
            {
                MdrReportScheduleService.RunDue(DateTime.Now);   // گزارش‌های دوره‌ای (پنل ۱۱)
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MDR report scheduler: " + ex.Message);
            }

            try
            {
                MdrAiService.PurgeExpired(DateTime.Now);          // Retention تاریخچه‌ی دستیار (پنل ۱۶)
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MDR AI retention: " + ex.Message);
            }
        }
    }
}
