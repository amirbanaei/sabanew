using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using SaabWebProject.Areas.Mdr.Data;
using SaabWebProject.Areas.Mdr.Core;

namespace SaabWebProject.Areas.Mdr.Services
{
    // =====================================================
    // MDR | Phase 4 | سرویس درخواست‌های سازمانی (پنل ۶)
    // نوع درخواست = فیلدهای فرم (JSON) + کد گردش کار. ارسال درخواست
    // یک Instance گردش کار می‌سازد؛ نتیجه‌ی نهایی Instance (رویداد
    // InstanceClosed) وضعیت درخواست را به Approved/Rejected/Cancelled می‌برد.
    // =====================================================

    public class MdrFieldDef
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }          // text | textarea | number | date | select
        public bool Required { get; set; }
        public List<string> Options { get; set; }
    }

    public static class MdrRequestService
    {
        private const string Module = "Requests";
        private static readonly string[] FieldTypes = { "text", "textarea", "number", "date", "select" };
        private static bool _registered;
        private static readonly object Sync = new object();

        public static void Register()
        {
            lock (Sync)
            {
                if (_registered) return;
                _registered = true;
                MdrWorkflowService.InstanceClosed += OnInstanceClosed;
            }
        }

        // ---------------- فیلدها ----------------
        public static List<MdrFieldDef> ParseFields(string json)
        {
            try { return new JavaScriptSerializer().Deserialize<List<MdrFieldDef>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? new List<MdrFieldDef>(); }
            catch { return null; }
        }

        // null = معتبر؛ در غیر این صورت کد خطا
        public static string ValidateFieldDefs(List<MdrFieldDef> fields)
        {
            if (fields == null || fields.Count > 40) return "req.err.fields";
            foreach (var f in fields)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Key) || f.Key.Length > 50 || !System.Text.RegularExpressions.Regex.IsMatch(f.Key, @"^[A-Za-z][A-Za-z0-9_]*$")
                    || string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 200 || !FieldTypes.Contains(f.Type)) return "req.err.fields";
                if (f.Type == "select" && (f.Options == null || f.Options.Count == 0 || f.Options.Count > 50)) return "req.err.fields";
            }
            return fields.Select(f => f.Key).Distinct().Count() != fields.Count ? "req.err.fields" : null;
        }

        // داده‌ی فرم را با تعریف فیلدها اعتبارسنجی و فقط کلیدهای مجاز را نگه می‌دارد
        public static string CleanData(List<MdrFieldDef> fields, IDictionary<string, string> data, bool requireAll, out string error)
        {
            error = null;
            var clean = new Dictionary<string, string>();
            data = data ?? new Dictionary<string, string>();

            foreach (var f in fields)
            {
                string v; data.TryGetValue(f.Key, out v);
                v = (v ?? "").Trim();
                if (v.Length > 2000) { error = "req.err.toolong"; return null; }

                if (v.Length == 0) { if (requireAll && f.Required) { error = "req.err.required"; return null; } clean[f.Key] = ""; continue; }

                decimal n; DateTime d;
                if (f.Type == "number" && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out n)) { error = "req.err.number"; return null; }
                if (f.Type == "date" && !DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) { error = "req.err.date"; return null; }
                if (f.Type == "select" && !(f.Options ?? new List<string>()).Contains(v)) { error = "req.err.option"; return null; }
                clean[f.Key] = v;
            }
            return new JavaScriptSerializer().Serialize(clean);
        }

                // ---------------- نوع درخواست (مدیر) ----------------
        public static MdrResult SaveType(int id, string code, string name, string description, string workflowCode, string fieldsJson, bool isActive, int actorId)
        {
            code = (code ?? "").Trim(); name = (name ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Za-z0-9_\-]{2,50}$")) return MdrResult.Fail("wf.err.code");
            if (name.Length == 0 || name.Length > 200) return MdrResult.Fail("wf.err.name");

            var fields = ParseFields(fieldsJson);
            var err = ValidateFieldDefs(fields);
            if (err != null) return MdrResult.Fail(err);

            try
            {
                using (var db = new MdrContext())
                {
                    MdrRequestType t;
                    if (id == 0)
                    {
                        if (db.RequestTypes.Any(x => x.Code == code)) return MdrResult.Fail("wf.err.codeexists");
                        t = new MdrRequestType { Code = code, CreatedAt = DateTime.Now };
                        db.RequestTypes.Add(t);
                    }
                    else
                    {
                        t = db.RequestTypes.FirstOrDefault(x => x.TypeID == id);
                        if (t == null) return MdrResult.Fail("wf.err.notfound");
                        if (t.Code != code && db.RequestTypes.Any(x => x.Code == code)) return MdrResult.Fail("wf.err.codeexists");
                        t.Code = code;
                    }

                    t.Name = name;
                    t.Description = string.IsNullOrWhiteSpace(description) ? null : (description.Length > 1000 ? description.Substring(0, 1000) : description.Trim());
                    t.WorkflowCode = string.IsNullOrWhiteSpace(workflowCode) ? null : workflowCode.Trim();
                    t.FieldsJson = new JavaScriptSerializer().Serialize(fields);
                    t.IsActive = isActive;
                    db.SaveChanges();

                    AuditLogger.Log(id == 0 ? AuditLogger.Actions.Create : AuditLogger.Actions.Update, Module, "ذخیره‌ی نوع درخواست",
                        entityName: "MDR_RequestTypes", entityId: t.TypeID, entityTitle: t.Name, newValues: new { t.Code, t.WorkflowCode, t.IsActive });
                    return MdrResult.Success(t.TypeID);
                }
            }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- درخواست ----------------
        public static MdrResult Save(long id, int typeId, string title, IDictionary<string, string> data, int actorId, byte[] rowVer)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 300) return MdrResult.Fail("tsk.err.title");

            try
            {
                using (var db = new MdrContext())
                {
                    MdrRequest r = null;
                    if (id != 0)
                    {
                        r = db.Requests.FirstOrDefault(x => x.RequestID == id);
                        if (r == null) return MdrResult.Fail("wf.err.notfound");
                        if (r.RequesterUserID != actorId) return MdrResult.Fail("tsk.err.notallowed");
                        if (r.Status != "Draft") return MdrResult.Fail("req.err.notdraft");
                        if (rowVer != null && !r.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                        typeId = r.TypeID;
                    }

                    var type = db.RequestTypes.FirstOrDefault(x => x.TypeID == typeId);
                    if (type == null) return MdrResult.Fail("wf.err.notfound");
                    if (!type.IsActive && id == 0) return MdrResult.Fail("req.err.inactive");

                    string err;
                    var json = CleanData(ParseFields(type.FieldsJson) ?? new List<MdrFieldDef>(), data, false, out err);
                    if (err != null) return MdrResult.Fail(err);

                    if (r == null)
                    {
                        r = new MdrRequest { TypeID = typeId, Title = title, DataJson = json, Status = "Draft", RequesterUserID = actorId, CreatedAt = DateTime.Now };
                        db.Requests.Add(r);
                        db.SaveChanges();
                        r.Number = "REQ-" + new PersianCalendar().GetYear(DateTime.Now) + "-" + r.RequestID.ToString("D6");
                        Log(db, r.RequestID, "Create", actorId, null);
                    }
                    else
                    {
                        r.Title = title; r.DataJson = json;
                        Log(db, r.RequestID, "Edit", actorId, null);
                    }
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ذخیره‌ی پیش‌نویس درخواست",
                        entityName: "MDR_Requests", entityId: r.RequestID, entityTitle: r.Title);
                    return MdrResult.Success(r.RequestID);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Submit(long id, int actorId, byte[] rowVer)
        {
            try
            {
                using (var db = new MdrContext())
                {
                    var r = db.Requests.FirstOrDefault(x => x.RequestID == id);
                    if (r == null) return MdrResult.Fail("wf.err.notfound");
                    if (r.RequesterUserID != actorId) return MdrResult.Fail("tsk.err.notallowed");
                    if (r.Status != "Draft") return MdrResult.Fail("req.err.notdraft");
                    if (rowVer != null && !r.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");

                    var type = db.RequestTypes.First(x => x.TypeID == r.TypeID);
                    if (!type.IsActive) return MdrResult.Fail("req.err.inactive");

                    // اعتبارسنجی کامل (فیلدهای اجباری) فقط هنگام ارسال
                    var stored = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(r.DataJson);
                    string err;
                    CleanData(ParseFields(type.FieldsJson) ?? new List<MdrFieldDef>(), stored, true, out err);
                    if (err != null) return MdrResult.Fail(err);

                    var wf = db.WorkflowDefinitions.FirstOrDefault(d => d.Code == type.WorkflowCode && d.Status == MdrWorkflowService.Statuses.Published);
                    if (type.WorkflowCode == null || wf == null) return MdrResult.Fail("req.err.noworkflow");

                    var started = MdrWorkflowService.StartInstance(wf.WorkflowID, r.Number + " — " + r.Title, actorId, "MDR_Requests", r.RequestID.ToString());
                    if (!started.Ok) return started;

                    r.Status = "Submitted"; r.WorkflowInstanceID = started.Id; r.SubmittedAt = DateTime.Now;
                    Log(db, r.RequestID, "Submit", actorId, "instance=" + started.Id);
                    db.SaveChanges();

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "ارسال درخواست", entityName: "MDR_Requests",
                        entityId: r.RequestID, entityTitle: r.Title, newValues: new { instance = started.Id });
                    return MdrResult.Success(r.RequestID);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        public static MdrResult Cancel(long id, int actorId, bool isManager, string reason, byte[] rowVer)
        {
            reason = (reason ?? "").Trim();
            try
            {
                using (var db = new MdrContext())
                {
                    var r = db.Requests.FirstOrDefault(x => x.RequestID == id);
                    if (r == null) return MdrResult.Fail("wf.err.notfound");
                    if (r.RequesterUserID != actorId && !isManager) return MdrResult.Fail("tsk.err.notallowed");
                    if (r.Status != "Draft" && r.Status != "Submitted") return MdrResult.Fail("tsk.err.state");
                    if (rowVer != null && !r.RowVer.SequenceEqual(rowVer)) return MdrResult.Fail("wf.err.concurrent");
                    if (r.Status == "Submitted" && reason.Length == 0) return MdrResult.Fail("wf.err.reason");

                    var instanceId = r.WorkflowInstanceID;
                    r.Status = "Cancelled"; r.ClosedAt = DateTime.Now;
                    Log(db, r.RequestID, "Cancel", actorId, reason.Length == 0 ? null : reason);
                    db.SaveChanges();

                    if (instanceId.HasValue) MdrWorkflowService.Intervene(instanceId.Value, "Terminate", actorId, reason, null);

                    AuditLogger.Log(AuditLogger.Actions.Update, Module, "لغو درخواست", entityName: "MDR_Requests",
                        entityId: r.RequestID, entityTitle: r.Title, newValues: new { reason });
                    return MdrResult.Success(r.RequestID);
                }
            }
            catch (DbUpdateConcurrencyException) { return MdrResult.Fail("wf.err.concurrent"); }
            catch (Exception ex) { return Failed(ex); }
        }

        // ---------------- رویداد گردش کار ----------------
        private static void OnInstanceClosed(MdrWorkflowService.ClosedEventArgs e)
        {
            if (e.EntityName != "MDR_Requests") return;

            using (var db = new MdrContext())
            {
                var r = db.Requests.FirstOrDefault(x => x.WorkflowInstanceID == e.InstanceID);
                if (r == null || r.Status != "Submitted") return;   // لغو دستی قبلاً ثبت شده

                r.Status = e.Outcome == "Approved" ? "Approved" : e.Outcome == "Rejected" ? "Rejected" : "Cancelled";
                r.ClosedAt = DateTime.Now;
                Log(db, r.RequestID, r.Status, e.ActorId, e.Reason);
                db.SaveChanges();

                NotificationService.Send(r.RequesterUserID, "نتیجه‌ی درخواست " + r.Number,
                    r.Title, severity: r.Status == "Approved" ? NotificationService.Severities.Success : NotificationService.Severities.Warning,
                    moduleName: Module, entityName: "MDR_Requests", entityId: r.RequestID, linkUrl: "/Mdr/Requests#id=" + r.RequestID);
                AuditLogger.Log(AuditLogger.Actions.Update, Module, "نتیجه‌ی درخواست: " + r.Status, entityName: "MDR_Requests",
                    entityId: r.RequestID, entityTitle: r.Title);
            }
        }

        private static void Log(MdrContext db, long requestId, string action, int? actorId, string detail)
        {
            db.RequestHistory.Add(new MdrRequestHistory
            {
                RequestID = requestId, ActionKey = action, ActorUserID = actorId,
                Detail = string.IsNullOrWhiteSpace(detail) ? null : (detail.Length > 500 ? detail.Substring(0, 500) : detail),
                CorrelationID = Guid.NewGuid(), CreatedAt = DateTime.Now
            });
        }

        private static MdrResult Failed(Exception ex)
        {
            AuditLogger.Log(AuditLogger.Actions.Error, Module, "خطای درخواست‌ها", newValues: new { ex.Message }, isSuccess: false);
            return MdrResult.Fail("wf.err.unexpected");
        }
    }
}
