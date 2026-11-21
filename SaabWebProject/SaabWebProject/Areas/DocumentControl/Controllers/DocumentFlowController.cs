using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;
using SaabWebProject.Models.DomainModels;

namespace SaabWebProject.Areas.DocumentControl.Controllers
{
    [Authorize]
    public class DocumentFlowController : Controller
    {
        private readonly SaabEntities db = new SaabEntities();
        private DbTransaction tx;
        private int actor;
        private bool manager;
        private const int MaxFile = 1024 * 1024;
        protected override void Dispose(bool disposing) { if (disposing) db.Dispose(); base.Dispose(disposing); }
        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            // Identity.Name is the national code from the signed FormsAuthentication ticket.
            var nationalCode = User.Identity.Name;
            actor = db.tbUsers.Where(u => u.usr_NationalCode == nationalCode && u.usr_IsActive == true)
                .Select(u => u.usr_ID).FirstOrDefault();
            if (actor == 0) { context.Result = new HttpStatusCodeResult(401); return; }
            manager = Number("SELECT COUNT(*) FROM [Usser].tbUser_link_BusinessSide WHERE FK_u_ID=@p0 AND Status=1 AND FK_up_ID IN(9,2190,1130,2175,2200,2162)", actor) > 0;
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            base.OnActionExecuting(context);
        }
        private DbCommand Command(string sql, params object[] args)
        {
            if (db.Database.Connection.State != ConnectionState.Open) db.Database.Connection.Open();
            var c = db.Database.Connection.CreateCommand(); c.CommandText = sql; c.Transaction = tx;
            for (int i = 0; i < args.Length; i++) { var p = c.CreateParameter(); p.ParameterName = "@p" + i; p.Value = args[i] ?? DBNull.Value; c.Parameters.Add(p); }
            return c;
        }
        private List<Dictionary<string, object>> Rows(string sql, params object[] args)
        {
            using (var c = Command(sql, args)) using (var r = c.ExecuteReader())
            {
                var rows = new List<Dictionary<string, object>>();
                while (r.Read()) { var row = new Dictionary<string, object>(); for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : (r.GetFieldType(i) == typeof(DateTime) ? (object)DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc) : (r.GetFieldType(i) == typeof(decimal) ? (object)r.GetDecimal(i).ToString(CultureInfo.InvariantCulture) : r.GetValue(i))); rows.Add(row); }
                return rows;
            }
        }
        private int Number(string sql, params object[] args) { using (var c = Command(sql, args)) return Convert.ToInt32(c.ExecuteScalar()); }
        private void Run(string sql, params object[] args) { using (var c = Command(sql, args)) c.ExecuteNonQuery(); }
        private static int I(Dictionary<string, object> r, string key) { return Convert.ToInt32(r[key]); }
        private static void Need(bool ok, string message) { if (!ok) throw new FlowException(message); }
        private static string Clean(string text, int max, bool required = true) { text = (text ?? "").Trim(); Need((!required || text.Length > 0) && text.Length <= max, "متن الزامی است یا طول آن بیش از حد مجاز است."); return text; }
        private ActionResult Write(Func<object> action)
        {
            if (!ModelState.IsValid) return Json(new { success = false, message = "مقادیر ورودی معتبر نیستند." });
            try
            {
                if (db.Database.Connection.State != ConnectionState.Open) db.Database.Connection.Open();
                using (tx = db.Database.Connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try { var result = action(); tx.Commit(); return Json(new { success = true, data = result }); }
                    catch { tx.Rollback(); throw; }
                    finally { tx = null; }
                }
            }
            catch (FlowException ex) { return Json(new { success = false, message = ex.Message }); }
            catch (DbException ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); return Json(new { success = false, message = "ذخیره انجام نشد؛ ممکن است رکورد تکراری باشد یا هم‌زمان تغییر کرده باشد. صفحه را تازه کنید." }); }
        }
        private ActionResult Read(Func<object> action)
        {
            try { return Json(new { success = true, data = action() }, JsonRequestBehavior.AllowGet); }
            catch (FlowException ex) { Response.StatusCode = 403; return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet); }
        }
        private void Admin() { Need(manager, "دسترسی تنظیمات ندارید."); }
        private bool ContractAccess(int id) { return Number(@"SELECT COUNT(*) FROM [Contract].tbPeymanContracts p WHERE p.pec_ID=@p0 AND ISNULL(p.Inactive,0)=0 AND (@p1=1 OR EXISTS(SELECT 1 FROM [Contract].Link_User_And_Peyman l WHERE l.FK_Peyman_ID=p.pec_ID AND l.FK_User_ID=@p2 AND l.Status=1))", id, manager, actor) > 0; }
        private Dictionary<string, object> RequestRow(int id)
        {
            var list = Rows("SELECT * FROM dbo.DF_Request WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id);
            Need(list.Count == 1, "پرونده پیدا نشد."); return list[0];
        }
        private bool CanSee(int id) { return Number(@"SELECT COUNT(*) FROM dbo.DF_Request r WHERE r.Id=@p0 AND (@p1=1 OR (r.OwnerId=@p2 AND EXISTS(SELECT 1 FROM [Contract].Link_User_And_Peyman l WHERE l.FK_User_ID=@p2 AND l.FK_Peyman_ID=r.ContractId AND l.Status=1)) OR EXISTS(SELECT 1 FROM dbo.DF_RequestReviewer a WHERE a.RequestId=r.Id AND a.UserId=@p2))", id, manager, actor) > 0; }
        [HttpGet] public ActionResult Index() { ViewBag.IsManager = manager; return View(); }
        [HttpGet]
        public ActionResult Bootstrap()
        {
            return Read(() => new {
                manager = manager,
                userId = actor,
                types = Rows("SELECT Id,Title,Description,ComponentId,MonthOffset,IsActive,SampleName,Version FROM dbo.DF_Type WHERE IsActive=1 OR @p0=1 ORDER BY Title", manager),
                contracts = Rows(@"SELECT p.pec_ID AS Id,p.pec_Title AS Title,p.pec_ContractNumber AS Number FROM [Contract].tbPeymanContracts p WHERE ISNULL(p.Inactive,0)=0 AND (@p0=1 OR EXISTS(SELECT 1 FROM [Contract].Link_User_And_Peyman l WHERE l.FK_Peyman_ID=p.pec_ID AND l.FK_User_ID=@p1 AND l.Status=1)) ORDER BY p.pec_Title", manager, actor),
                users = manager ? Rows("SELECT usr_ID AS Id,CONCAT(usr_Name,N' ',usr_Family) AS Title FROM [Usser].tbUsers WHERE usr_IsActive=1 ORDER BY usr_Family") : null,
                components = manager ? Rows("SELECT md_ID AS Id,md_Title AS Title FROM [Contract].tbContractMoalefeDastmozdi ORDER BY md_Title") : null,
                period = CurrentPeriod(0)
            });
        }
        private static int[] CurrentPeriod(int offset)
        {
            var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Iran Standard Time");
            var pc = new PersianCalendar(); var date = pc.AddMonths(now, offset);
            return new[] { pc.GetYear(date), pc.GetMonth(date) };
        }
        [HttpGet]
        public ActionResult List()
        {
            return Read(() => Rows(@"SELECT r.Id,r.TypeTitle,r.ContractId,p.pec_Title AS ContractTitle,r.OwnerId,
                CONCAT(u.usr_Name,N' ',u.usr_Family) AS OwnerName,r.PeriodYear,r.PeriodMonth,r.Status,r.CurrentStep,r.Revision,r.ApprovedAmount,r.CreatedUtc,r.UpdatedUtc,
                s.Title AS StepTitle,
                CASE WHEN r.Status=1 AND EXISTS(SELECT 1 FROM dbo.DF_RequestReviewer a WHERE a.RequestId=r.Id AND a.Position=r.CurrentStep AND a.UserId=@p1)
                AND NOT EXISTS(SELECT 1 FROM dbo.DF_Review v WHERE v.RequestId=r.Id AND v.Revision=r.Revision AND v.Step=r.CurrentStep AND v.ReviewerId=@p1) THEN 1 ELSE 0 END AS CanReview
                FROM dbo.DF_Request r JOIN [Contract].tbPeymanContracts p ON p.pec_ID=r.ContractId JOIN [Usser].tbUsers u ON u.usr_ID=r.OwnerId
                LEFT JOIN dbo.DF_RequestStep s ON s.RequestId=r.Id AND s.Position=r.CurrentStep
                WHERE @p0=1 OR (r.OwnerId=@p1 AND EXISTS(SELECT 1 FROM [Contract].Link_User_And_Peyman l WHERE l.FK_User_ID=@p1 AND l.FK_Peyman_ID=r.ContractId AND l.Status=1))
                OR EXISTS(SELECT 1 FROM dbo.DF_RequestReviewer a WHERE a.RequestId=r.Id AND a.UserId=@p1) ORDER BY r.UpdatedUtc DESC", manager, actor));
        }
        [HttpGet]
        public ActionResult Workflow(int id)
        {
            return Read(() => {
                Admin(); return new
                {
                    stages = Rows("SELECT Id,Position,Title,RequireAll FROM dbo.DF_Stage WHERE TypeId=@p0 ORDER BY Position", id),
                    users = Rows("SELECT a.StageId,a.UserId FROM dbo.DF_StageUser a JOIN dbo.DF_Stage s ON s.Id=a.StageId WHERE s.TypeId=@p0", id),
                    version = Rows("SELECT Version FROM dbo.DF_Type WHERE Id=@p0", id)
                };
            });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SaveType(int id, string title, string description, int? componentId, int monthOffset, bool isActive, string version, HttpPostedFileBase sample)
        {
            return Write(() => {
                Admin(); title = Clean(title, 200); description = Clean(description, 2000, false); Need(monthOffset == 0 || monthOffset == -1, "دوره نامعتبر است.");
                if (componentId.HasValue) Need(Number("SELECT COUNT(*) FROM [Contract].tbContractMoalefeDastmozdi WHERE md_ID=@p0", componentId) > 0, "مؤلفه نامعتبر است.");
                var file = Upload(sample, false);
                if (id == 0) id = Number("INSERT dbo.DF_Type(Title,Description,ComponentId,MonthOffset,IsActive) OUTPUT INSERTED.Id VALUES(@p0,@p1,@p2,@p3,@p4)", title, description, componentId, monthOffset, isActive);
                else
                {
                    var old = Rows("SELECT Version FROM dbo.DF_Type WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", id);
                    Need(old.Count == 1 && Convert.ToBase64String((byte[])old[0]["Version"]) == version, "تعریف مدرک تغییر کرده؛ صفحه را تازه کنید.");
                    Run("UPDATE dbo.DF_Type SET Title=@p1,Description=@p2,ComponentId=@p3,MonthOffset=@p4,IsActive=@p5,UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", id, title, description, componentId, monthOffset, isActive);
                }
                if (file != null) Run("UPDATE dbo.DF_Type SET SampleName=@p1,SampleData=@p2 WHERE Id=@p0", id, file.Name, file.Data);
                return new { id = id };
            });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SaveWorkflow(int typeId, string stagesJson, string version)
        {
            return Write(() => {
                Admin(); var old = Rows("SELECT Version FROM dbo.DF_Type WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", typeId);
                Need(old.Count == 1 && Convert.ToBase64String((byte[])old[0]["Version"]) == version, "تعریف مدرک تغییر کرده؛ دوباره آن را انتخاب کنید.");
                StageInput[] stages;
                try { stages = new JavaScriptSerializer().Deserialize<StageInput[]>(stagesJson ?? ""); } catch { throw new FlowException("ساختار مراحل نامعتبر است."); }
                Need(stages != null && stages.Length > 0 && stages.Length <= 20, "بین ۱ تا ۲۰ مرحله تعریف کنید.");
                Run("DELETE a FROM dbo.DF_StageUser a JOIN dbo.DF_Stage s ON s.Id=a.StageId WHERE s.TypeId=@p0", typeId);
                Run("DELETE dbo.DF_Stage WHERE TypeId=@p0", typeId);
                for (int i = 0; i < stages.Length; i++)
                {
                    var s = stages[i]; Need(s != null && s.Users != null && s.Users.Length > 0 && s.Users.Length <= 100, "هر مرحله باید تأییدکننده داشته باشد.");
                    var sid = Number("INSERT dbo.DF_Stage(TypeId,Position,Title,RequireAll) OUTPUT INSERTED.Id VALUES(@p0,@p1,@p2,@p3)", typeId, i + 1, Clean(s.Title, 100), s.RequireAll);
                    foreach (var uid in s.Users.Distinct())
                    {
                        Need(Number("SELECT COUNT(*) FROM [Usser].tbUsers WHERE usr_ID=@p0 AND usr_IsActive=1", uid) > 0, "تأییدکننده فعال نیست.");
                        Run("INSERT dbo.DF_StageUser(StageId,UserId) VALUES(@p0,@p1)", sid, uid);
                    }
                }
                Run("UPDATE dbo.DF_Type SET UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", typeId); return new { id = typeId };
            });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Submit(int typeId, int contractId, string description, HttpPostedFileBase excel, HttpPostedFileBase attachment)
        {
            return Write(() => {
                Need(ContractAccess(contractId), "به پیمان فعال دسترسی ندارید.");
                var list = Rows("SELECT * FROM dbo.DF_Type WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0 AND IsActive=1", typeId);
                Need(list.Count == 1, "نوع مدرک غیرفعال یا نامعتبر است."); var type = list[0];
                Need(Number("SELECT COUNT(*) FROM dbo.DF_Stage WHERE TypeId=@p0", typeId) > 0, "ابتدا مراحل تأیید این مدرک تعریف شود.");
                Need(Number(@"SELECT COUNT(*) FROM dbo.DF_Stage s WHERE s.TypeId=@p0 AND NOT EXISTS(SELECT 1 FROM dbo.DF_StageUser a JOIN [Usser].tbUsers u ON u.usr_ID=a.UserId AND u.usr_IsActive=1 WHERE a.StageId=s.Id)", typeId) == 0, "یک مرحله تأییدکننده فعال ندارد؛ تنظیمات را اصلاح کنید.");
                var ex = Upload(excel, true); var att = Upload(attachment, false);
                ValidateExcel(ex);
                Need(att != null, "بارگذاری پیوست الزامی است.");
                var period = CurrentPeriod(I(type, "MonthOffset"));
                var id = Number(@"INSERT dbo.DF_Request(TypeId,TypeTitle,ComponentId,ContractId,OwnerId,PeriodYear,PeriodMonth,Status,CurrentStep) OUTPUT INSERTED.Id VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,1,1)", typeId, type["Title"], type["ComponentId"], contractId, actor, period[0], period[1]);
                Run("INSERT dbo.DF_RequestStep(RequestId,Position,Title,RequireAll) SELECT @p0,Position,Title,RequireAll FROM dbo.DF_Stage WHERE TypeId=@p1", id, typeId);
                Run(@"INSERT dbo.DF_RequestReviewer(RequestId,Position,UserId) SELECT @p0,s.Position,a.UserId FROM dbo.DF_Stage s JOIN dbo.DF_StageUser a ON a.StageId=s.Id JOIN [Usser].tbUsers u ON u.usr_ID=a.UserId AND u.usr_IsActive=1 WHERE s.TypeId=@p1", id, typeId);
                Revision(id, 1, description, ex, att); return new { id = id };
            });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Resubmit(int id, int revision, string description, HttpPostedFileBase excel, HttpPostedFileBase attachment)
        {
            return Write(() => {
                var r = RequestRow(id); Need(I(r, "OwnerId") == actor && ContractAccess(I(r, "ContractId")), "فقط ثبت‌کننده دارای دسترسی پیمان می‌تواند اصلاح کند.");
                Need(I(r, "Status") == 0 && I(r, "Revision") == revision, "پرونده در وضعیت اصلاح نیست یا نسخه تغییر کرده است.");
                var ex = Upload(excel, true); var att = Upload(attachment, false);
                ValidateExcel(ex);
                Need(att != null, "بارگذاری پیوست الزامی است.");
                Revision(id, revision + 1, description, ex, att);
                Run("UPDATE dbo.DF_Request SET Revision=Revision+1,Status=1,CurrentStep=1,ApprovedAmount=NULL,UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", id);
                return new { id = id };
            });
        }
        private void Revision(int id, int revision, string description, UploadData excel, UploadData attachment)
        {
            Run("INSERT dbo.DF_Revision(RequestId,Revision,Description,ActorId) VALUES(@p0,@p1,@p2,@p3)", id, revision, Clean(description, 4000), actor);
            SaveFile(id, revision, null, "excel", excel); SaveFile(id, revision, null, "attachment", attachment);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Review(int id, int revision, int step, int decision, string description, decimal? amount, HttpPostedFileBase file)
        {
            return Write(() => {
                var r = RequestRow(id);
                Need(I(r, "Status") == 1 && I(r, "Revision") == revision && I(r, "CurrentStep") == step, "مرحله یا نسخه پرونده تغییر کرده است.");
                Need(Number("SELECT COUNT(*) FROM dbo.DF_RequestReviewer WHERE RequestId=@p0 AND Position=@p1 AND UserId=@p2", id, step, actor) > 0, "تأییدکننده این مرحله نیستید.");
                Need(Number("SELECT COUNT(*) FROM dbo.DF_Review WHERE RequestId=@p0 AND Revision=@p1 AND Step=@p2 AND ReviewerId=@p3", id, revision, step, actor) == 0, "نظر شما قبلاً ثبت شده است.");
                Need(decision >= 0 && decision <= 2, "نتیجه نامعتبر است.");
                Need(!amount.HasValue || (amount.Value >= 0 && amount.Value <= 9999999999999999.99m && decimal.Round(amount.Value, 2) == amount.Value), "مبلغ نامعتبر است؛ حداکثر دو رقم اعشار وارد کنید.");
                var attachment = Upload(file, false);
                var reviewId = Number("INSERT dbo.DF_Review(RequestId,Revision,Step,ReviewerId,Decision,Description,Amount) OUTPUT INSERTED.Id VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)", id, revision, step, actor, decision, Clean(description, 4000), amount);
                SaveFile(id, revision, reviewId, "review", attachment);
                if (decision == 0) Run("UPDATE dbo.DF_Request SET Status=0,ApprovedAmount=NULL,UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", id);
                else
                {
                    var required = Number("SELECT CASE WHEN RequireAll=1 THEN (SELECT COUNT(*) FROM dbo.DF_RequestReviewer a WHERE a.RequestId=s.RequestId AND a.Position=s.Position) ELSE 1 END FROM dbo.DF_RequestStep s WHERE RequestId=@p0 AND Position=@p1", id, step);
                    var votes = Number("SELECT COUNT(*) FROM dbo.DF_Review WHERE RequestId=@p0 AND Revision=@p1 AND Step=@p2 AND Decision IN(1,2)", id, revision, step);
                    if (votes >= required)
                    {
                        bool last = Number("SELECT COUNT(*) FROM dbo.DF_RequestStep WHERE RequestId=@p0 AND Position>@p1", id, step) == 0;
                        var conditional = Number("SELECT COUNT(*) FROM dbo.DF_Review WHERE RequestId=@p0 AND Revision=@p1 AND Decision=2", id, revision) > 0;
                        Run(@"UPDATE dbo.DF_Request SET Status=@p1,CurrentStep=@p2,ApprovedAmount=CASE WHEN @p3=1 THEN (SELECT MIN(Amount) FROM dbo.DF_Review WHERE RequestId=@p0 AND Revision=@p4 AND Step=@p5 AND Decision IN(1,2)) ELSE NULL END,UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", id, last ? (conditional ? 3 : 2) : 1, last ? step : step + 1, last, revision, step);
                    }
                    else Run("UPDATE dbo.DF_Request SET UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", id);
                }
                return new { id = id };
            });
        }
        [HttpGet]
        public ActionResult Detail(int id)
        {
            return Read(() => {
                Need(CanSee(id), "به این پرونده دسترسی ندارید.");
                return new
                {
                    request = Rows("SELECT r.*,p.pec_Title AS ContractTitle FROM dbo.DF_Request r JOIN [Contract].tbPeymanContracts p ON p.pec_ID=r.ContractId WHERE r.Id=@p0", id)[0],
                    steps = Rows("SELECT * FROM dbo.DF_RequestStep WHERE RequestId=@p0 ORDER BY Position", id),
                    reviewers = Rows("SELECT a.Position,a.UserId,CONCAT(u.usr_Name,N' ',u.usr_Family) AS Name FROM dbo.DF_RequestReviewer a JOIN [Usser].tbUsers u ON u.usr_ID=a.UserId WHERE RequestId=@p0", id),
                    revisions = Rows("SELECT v.*,CONCAT(u.usr_Name,N' ',u.usr_Family) AS ActorName FROM dbo.DF_Revision v JOIN [Usser].tbUsers u ON u.usr_ID=v.ActorId WHERE RequestId=@p0 ORDER BY Revision DESC", id),
                    reviews = Rows("SELECT v.*,CONCAT(u.usr_Name,N' ',u.usr_Family) AS ReviewerName FROM dbo.DF_Review v JOIN [Usser].tbUsers u ON u.usr_ID=v.ReviewerId WHERE RequestId=@p0 ORDER BY CreatedUtc", id),
                    files = Rows("SELECT Id,Revision,ReviewId,Kind,FileName,CreatedUtc FROM dbo.DF_File WHERE RequestId=@p0 ORDER BY Id", id),
                    canEdit = Number("SELECT COUNT(*) FROM dbo.DF_Request WHERE Id=@p0 AND OwnerId=@p1 AND Status=0", id, actor) > 0 && ContractAccess(Number("SELECT ContractId FROM dbo.DF_Request WHERE Id=@p0", id)),
                    canReview = Number(@"SELECT COUNT(*) FROM dbo.DF_Request r JOIN dbo.DF_RequestReviewer a ON a.RequestId=r.Id AND a.Position=r.CurrentStep WHERE r.Id=@p0 AND r.Status=1 AND a.UserId=@p1 AND NOT EXISTS(SELECT 1 FROM dbo.DF_Review v WHERE v.RequestId=r.Id AND v.Revision=r.Revision AND v.Step=r.CurrentStep AND v.ReviewerId=@p1)", id, actor) > 0
                };
            });
        }
        [HttpGet]
        public ActionResult Download(int id)
        {
            var metadata = Rows("SELECT RequestId FROM dbo.DF_File WHERE Id=@p0", id);
            if (metadata.Count == 0 || !CanSee(I(metadata[0], "RequestId"))) return new HttpStatusCodeResult(404);
            var f = Rows("SELECT FileName,ContentType,Data FROM dbo.DF_File WHERE Id=@p0", id)[0];
            Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Cache.SetCacheability(HttpCacheability.NoCache);
            return File((byte[])f["Data"], (string)f["ContentType"], (string)f["FileName"]);
        }
        [HttpGet]
        public ActionResult Sample(int id, int? contractId)
        {
            if (!manager && (!contractId.HasValue || !ContractAccess(contractId.Value))) return new HttpStatusCodeResult(403);
            var rows = Rows("SELECT SampleName,SampleData FROM dbo.DF_Type WHERE Id=@p0 AND (IsActive=1 OR @p1=1)", id, manager);
            if (rows.Count == 0 || rows[0]["SampleData"] == null) return HttpNotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File((byte[])rows[0]["SampleData"], "application/octet-stream", (string)rows[0]["SampleName"]);
        }

        // ===================== پرسنل لینک‌شده و اکسل =====================

        // tbAdamAdam.Name = usr_ID کاربر لاگین‌شده | tbAdamAdam.nam = usr_ID فرد لینک‌شده | Value=1 یعنی فعال
        // کد پرسنلی = usr_Personal_ID
        private List<Dictionary<string, object>> LinkedPeople()
        {
            return Rows(@"SELECT u.usr_Personal_ID AS Code, MIN(CONCAT(u.usr_Name,N' ',u.usr_Family)) AS FullName
                FROM dbo.tbAdamAdam a
                JOIN [Usser].tbUsers u ON u.usr_ID = TRY_CONVERT(int, a.nam)
                WHERE TRY_CONVERT(int, a.Name) = @p0 AND a.Value = 1 AND u.usr_IsActive = 1 AND u.usr_Personal_ID IS NOT NULL
                GROUP BY u.usr_Personal_ID
                ORDER BY u.usr_Personal_ID", actor);
        }
        private static string Digits(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in (s ?? "").Trim())
            {
                if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
                else if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
                else if (ch == '\u066C' || ch == ',') continue;
                else if (ch == '\u066B') sb.Append('.');
                else sb.Append(ch);
            }
            return sb.ToString();
        }
        private static XDocument LoadXml(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var r = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(r);
        }
        private void ValidateExcel(UploadData ex)
        {
            if (ex == null) return;
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var allowed = new HashSet<int>(LinkedPeople().Select(p => I(p, "Code")));
            Need(allowed.Count > 0, "هیچ پرسنلی برای شما تعریف نشده است.");
            var errors = new List<string>(); var seen = new HashSet<int>(); int count = 0;
            try
            {
                using (var ms = new MemoryStream(ex.Data)) using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    var shared = new List<string>();
                    var ss = zip.GetEntry("xl/sharedStrings.xml");
                    if (ss != null) foreach (var si in LoadXml(ss).Root.Elements(ns + "si")) shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
                    var sheet = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")).OrderBy(e => e.FullName).FirstOrDefault();
                    Need(sheet != null, "شیت اکسل پیدا نشد.");
                    int rowIndex = 0;
                    foreach (var row in LoadXml(sheet).Descendants(ns + "row"))
                    {
                        rowIndex++;
                        int rn = (int?)row.Attribute("r") ?? rowIndex;
                        if (rn == 1) continue; // سطر عنوان
                        var cells = new Dictionary<string, string>();
                        foreach (var c in row.Elements(ns + "c"))
                        {
                            var col = new string(((string)c.Attribute("r") ?? "").TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
                            var t = (string)c.Attribute("t"); string val;
                            if (t == "inlineStr") val = string.Concat(c.Descendants(ns + "t").Select(x => x.Value));
                            else
                            {
                                val = (string)c.Element(ns + "v") ?? "";
                                int si; if (t == "s" && int.TryParse(val, out si) && si >= 0 && si < shared.Count) val = shared[si];
                            }
                            cells[col] = (val ?? "").Trim();
                        }
                        string codeText, amountText;
                        cells.TryGetValue("A", out codeText); cells.TryGetValue("C", out amountText);
                        if (string.IsNullOrWhiteSpace(codeText) && string.IsNullOrWhiteSpace(amountText)) continue;
                        if (++count > 5000) { errors.Add("تعداد ردیف‌ها بیش از ۵۰۰۰ است."); break; }
                        decimal cd; int code;
                        if (!decimal.TryParse(Digits(codeText), NumberStyles.Float, CultureInfo.InvariantCulture, out cd) || cd != Math.Truncate(cd) || cd < 1 || cd > int.MaxValue)
                        { errors.Add("ردیف " + rn + ": کد پرسنلی نامعتبر است."); continue; }
                        code = (int)cd;
                        if (!allowed.Contains(code)) { errors.Add("ردیف " + rn + ": کد پرسنلی " + code + " جزو پرسنل شما نیست."); continue; }
                        if (!seen.Add(code)) { errors.Add("ردیف " + rn + ": کد پرسنلی " + code + " تکراری است."); continue; }
                        decimal amount;
                        if (!decimal.TryParse(Digits(amountText), NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || amount < 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
                            errors.Add("ردیف " + rn + ": مبلغ نامعتبر است.");
                    }
                }
            }
            catch (InvalidDataException) { throw new FlowException("فایل اکسل معتبر نیست."); }
            catch (XmlException) { throw new FlowException("محتوای فایل اکسل معتبر نیست."); }
            Need(count > 0, "فایل اکسل هیچ ردیف داده‌ای ندارد.");
            Need(errors.Count == 0, string.Join(" | ", errors.Take(5)) + (errors.Count > 5 ? " ... (" + errors.Count + " خطا)" : ""));
        }
        [HttpGet]
        public ActionResult ExcelTemplate()
        {
            var people = LinkedPeople();
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetViews><sheetView workbookViewId=""0"" rightToLeft=""1""/></sheetViews><cols><col min=""1"" max=""1"" width=""22"" customWidth=""1""/><col min=""2"" max=""2"" width=""32"" customWidth=""1""/><col min=""3"" max=""3"" width=""30"" customWidth=""1""/></cols><sheetData>");
            sb.Append(@"<row r=""1""><c r=""A1"" t=""inlineStr""><is><t>کد پرسنلی</t></is></c><c r=""B1"" t=""inlineStr""><is><t>نام و نام خانوادگی</t></is></c><c r=""C1"" t=""inlineStr""><is><t>مبلغ را وارد نمایید</t></is></c></row>");
            for (int i = 0; i < people.Count; i++)
            {
                int r = i + 2;
                sb.Append("<row r=\"" + r + "\"><c r=\"A" + r + "\"><v>" + I(people[i], "Code") + "</v></c><c r=\"B" + r + "\" t=\"inlineStr\"><is><t>" + System.Security.SecurityElement.Escape((string)people[i]["FullName"]) + "</t></is></c></row>");
            }
            sb.Append("</sheetData></worksheet>");

            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    WriteExcelPart(zip, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8""?><Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/><Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/></Types>");
                    WriteExcelPart(zip, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/></Relationships>");
                    WriteExcelPart(zip, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8""?><workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets><sheet name=""اطلاعات پرسنل"" sheetId=""1"" r:id=""rId1""/></sheets></workbook>");
                    WriteExcelPart(zip, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/></Relationships>");
                    WriteExcelPart(zip, "xl/worksheets/sheet1.xml", sb.ToString());
                }
                bytes = stream.ToArray();
            }
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PersonnelTemplate.xlsx");
        }
        private static void WriteExcelPart(ZipArchive zip, string path, string xml)
        {
            var entry = zip.CreateEntry(path);
            using (var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)))
                writer.Write(xml);
        }
        private UploadData Upload(HttpPostedFileBase file, bool excelOnly)
        {
            if (file == null || file.ContentLength == 0) return null;
            Need(file.ContentLength > 0 && file.ContentLength <= MaxFile, "حداکثر اندازه هر فایل ۱ مگابایت است.");
            var name = Path.GetFileName(file.FileName); Need(!String.IsNullOrWhiteSpace(name) && name.Length <= 255, "نام فایل نامعتبر است.");
            var ext = Path.GetExtension(name).ToLowerInvariant();
            Need(!excelOnly || ext == ".xlsx", "در بخش فایل اکسل فقط فرمت XLSX مجاز است.");
            byte[] data;
            using (var ms = new MemoryStream()) { var buffer = new byte[8192]; int count; while ((count = file.InputStream.Read(buffer, 0, buffer.Length)) > 0) { Need(ms.Length + count <= MaxFile, "فایل بیش از حد مجاز است."); ms.Write(buffer, 0, count); } data = ms.ToArray(); }
            var mime = "application/octet-stream";
            if (ext == ".xlsx")
            {
                try
                {
                    using (var ms = new MemoryStream(data)) using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                    {
                        Need(zip.GetEntry("[Content_Types].xml") != null && zip.GetEntry("xl/workbook.xml") != null && zip.Entries.Count <= 2000 && zip.Entries.Sum(e => e.Length) <= 20L * 1024 * 1024 && !zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)), "ساختار فایل XLSX معتبر نیست.");
                    }
                }
                catch (InvalidDataException) { throw new FlowException("فایل اکسل معتبر نیست."); }
                mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            }
            else if (ext == ".pdf") { Need(data.Length >= 5 && System.Text.Encoding.ASCII.GetString(data, 0, 5) == "%PDF-", "فایل PDF معتبر نیست."); mime = "application/pdf"; }
            else if (ext == ".png") { Need(data.Length >= 8 && data.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "فایل PNG معتبر نیست."); mime = "image/png"; }
            else if (ext == ".jpg" || ext == ".jpeg") { Need(data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255, "فایل JPG معتبر نیست."); mime = "image/jpeg"; }
            return new UploadData { Name = name, Data = data, Mime = mime };
        }
        private void SaveFile(int requestId, int revision, int? reviewId, string kind, UploadData file)
        {
            if (file != null) Run("INSERT dbo.DF_File(RequestId,Revision,ReviewId,Kind,FileName,ContentType,Data,ActorId) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)", requestId, revision, reviewId, kind, file.Name, file.Mime, file.Data, actor);
        }

        // Simple correspondence: independent of the existing approval workflow.
        private bool MessageWriter()
        {
            return Number("SELECT COUNT(*) FROM [Usser].tbUser_link_BusinessSide WHERE FK_u_ID=@p0 AND Status=1 AND FK_up_ID IN(9,2190,2175,1130)", actor) > 0;
        }
        private bool MessageAccess(int id)
        {
            return Number(@"SELECT COUNT(*) FROM dbo.DF_MessageThread t WHERE t.Id=@p0 AND
                (t.CreatorId=@p1 OR EXISTS(SELECT 1 FROM dbo.DF_MessageRecipient r WHERE r.ThreadId=t.Id AND r.UserId=@p1))", id, actor) > 0;
        }
        [HttpGet]
        public ActionResult Messages() { return View(); }
        [HttpGet]
        public ActionResult MessageBootstrap()
        {
            return Read(() => new {
                userId = actor,
                canWrite = MessageWriter(),
                users = MessageWriter() ? Rows(@"SELECT usr_ID AS Id, CONCAT(usr_Name,N' ',usr_Family,N' (',usr_ID,N')') AS Title
                    FROM [Usser].tbUsers WHERE usr_IsActive=1 AND usr_ID<>@p0 ORDER BY usr_Family,usr_Name,usr_ID", actor) : null
            });
        }
        [HttpGet]
        public ActionResult MessageList(int page = 1)
        {
            return Read(() => {
                Need(page >= 1 && page <= 100000, "شماره صفحه نامعتبر است.");
                return Rows(@"SELECT t.Id,t.Subject,t.CreatorId,t.CreatedUtc,t.UpdatedUtc,
                    CONCAT(u.usr_Name,N' ',u.usr_Family) AS CreatorName,
                    (SELECT COUNT(*) FROM dbo.DF_MessagePost p WHERE p.ThreadId=t.Id) AS PostCount
                    FROM dbo.DF_MessageThread t JOIN [Usser].tbUsers u ON u.usr_ID=t.CreatorId
                    WHERE t.CreatorId=@p0 OR EXISTS(SELECT 1 FROM dbo.DF_MessageRecipient r WHERE r.ThreadId=t.Id AND r.UserId=@p0)
                    ORDER BY t.UpdatedUtc DESC,t.Id DESC OFFSET @p1 ROWS FETCH NEXT 30 ROWS ONLY", actor, (page - 1) * 30);
            });
        }
        [HttpGet]
        public ActionResult MessageDetail(int id, int page = 1)
        {
            return Read(() => {
                Need(MessageAccess(id), "به این گفتگو دسترسی ندارید.");
                Need(page >= 1 && page <= 100000, "شماره صفحه نامعتبر است.");
                return new
                {
                    thread = Rows(@"SELECT t.Id,t.Subject,t.CreatorId,t.CreatedUtc,CONCAT(u.usr_Name,N' ',u.usr_Family) AS CreatorName
                        FROM dbo.DF_MessageThread t JOIN [Usser].tbUsers u ON u.usr_ID=t.CreatorId WHERE t.Id=@p0", id).Single(),
                    recipients = Rows(@"SELECT r.UserId,CONCAT(u.usr_Name,N' ',u.usr_Family) AS FullName
                        FROM dbo.DF_MessageRecipient r JOIN [Usser].tbUsers u ON u.usr_ID=r.UserId WHERE r.ThreadId=@p0 ORDER BY r.UserId", id),
                    posts = Rows(@"SELECT p.Id,p.SenderId,p.Body,p.CreatedUtc,p.FileName,p.FileSize,p.ParentPostId,
                        CONCAT(u.usr_Name,N' ',u.usr_Family) AS SenderName
                        FROM dbo.DF_MessagePost p JOIN [Usser].tbUsers u ON u.usr_ID=p.SenderId WHERE p.ThreadId=@p0
                        ORDER BY p.Id OFFSET @p1 ROWS FETCH NEXT 50 ROWS ONLY", id, (page - 1) * 50),
                    canReply = true // MessageAccess(id) was verified above.
                };
            });
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult MessageCreate(string subject, string body, int[] recipientIds, HttpPostedFileBase attachment)
        {
            return Write(() => {
                Need(MessageWriter(), "فقط سمت‌های ۹، ۲۱۹۰، ۲۱۷۵ و ۱۱۳۰ اجازه شروع گفتگو دارند.");
                subject = Clean(subject, 200); body = Clean(body, 4000);
                Need(recipientIds != null && recipientIds.Length > 0 && recipientIds.Length <= 100, "بین ۱ تا ۱۰۰ گیرنده انتخاب کنید.");
                var ids = recipientIds.Distinct().ToArray();
                foreach (var uid in ids) Need(uid != actor && Number("SELECT COUNT(*) FROM [Usser].tbUsers WHERE usr_ID=@p0 AND usr_IsActive=1", uid) > 0, "گیرنده نامعتبر یا غیرفعال است.");
                var file = Upload(attachment, false);
                Need(file != null, "برای پیام اولیه پیوست الزامی است.");
                int id = Number("INSERT dbo.DF_MessageThread(Subject,CreatorId) OUTPUT INSERTED.Id VALUES(@p0,@p1)", subject, actor);
                foreach (var uid in ids) Run("INSERT dbo.DF_MessageRecipient(ThreadId,UserId) VALUES(@p0,@p1)", id, uid);
                MessageInsertPost(id, body, null, file);
                return new { id = id };
            });
        }
        private void MessageInsertPost(int id, string body, int? parentPostId, UploadData file)
        {
            Run(@"INSERT dbo.DF_MessagePost(ThreadId,SenderId,Body,ParentPostId,FileName,FileSize,FileData)
                VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)", id, actor, body, parentPostId,
                file == null ? null : file.Name, file == null ? (object)null : file.Data.Length, file == null ? null : file.Data);
            Run("UPDATE dbo.DF_MessageThread SET UpdatedUtc=SYSUTCDATETIME() WHERE Id=@p0", id);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult MessageReply(int id, string body, int? parentPostId, HttpPostedFileBase attachment)
        {
            return Write(() => {
                Need(MessageAccess(id), "به این گفتگو دسترسی ندارید.");
                body = Clean(body, 4000);
                if (parentPostId.HasValue) Need(Number("SELECT COUNT(*) FROM dbo.DF_MessagePost WHERE Id=@p0 AND ThreadId=@p1", parentPostId, id) > 0, "پیام مرجع متعلق به این گفتگو نیست.");
                MessageInsertPost(id, body, parentPostId, Upload(attachment, false));
                return new { id = id };
            });
        }
        [HttpGet]
        public ActionResult MessageDownload(int id)
        {
            var rows = Rows(@"SELECT p.FileName,p.FileData FROM dbo.DF_MessagePost p
                JOIN dbo.DF_MessageThread t ON t.Id=p.ThreadId WHERE p.Id=@p0 AND p.FileData IS NOT NULL
                AND (t.CreatorId=@p1 OR EXISTS(SELECT 1 FROM dbo.DF_MessageRecipient r WHERE r.ThreadId=t.Id AND r.UserId=@p1))", id, actor);
            if (rows.Count != 1) return HttpNotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File((byte[])rows[0]["FileData"], "application/octet-stream", (string)rows[0]["FileName"]);
        }

        public class StageInput { public string Title { get; set; } public bool RequireAll { get; set; } public int[] Users { get; set; } }
        private class UploadData { public string Name; public byte[] Data; public string Mime; }
        private class FlowException : Exception { public FlowException(string message) : base(message) { } }
    }
}