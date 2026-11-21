using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using SaabWebProject.Models.DomainModels;
using SaabWebProject.Utility;

namespace SaabWebProject.Areas.Setting.Controllers
{
    [AuthorizeAAA]
    public class ProcedureDesignerController : Controller
    {
        readonly SaabEntities db = new SaabEntities();
        protected override void Dispose(bool disposing) { if (disposing) db.Dispose(); base.Dispose(disposing); }
        SqlConnection NewConnection()
        {
            // گرفتن ConnectionString از اتصال بازشده EF قابل اعتماد نیست؛ اگر
            // Persist Security Info=false باشد، Password پس از اولین Open حذف می‌شود.
            var configured = System.Configuration.ConfigurationManager.ConnectionStrings["SaabEntities"];
            if (configured != null && !String.IsNullOrWhiteSpace(configured.ConnectionString))
            {
                var entityBuilder = new System.Data.Entity.Core.EntityClient.EntityConnectionStringBuilder(configured.ConnectionString);
                return new SqlConnection(entityBuilder.ProviderConnectionString);
            }

            return new SqlConnection(db.Database.Connection.ConnectionString);
        }
        static object V(object x) { return x ?? DBNull.Value; }
        int? UserId()
        {
            try
            {
                var c = Request.Cookies[Utility.Base64.Base64Encode("CodeMeli")]; if (c == null) return null;
                var n = Utility.Base64.Base64Decode(c.Value); return db.tbUsers.Where(x => x.usr_NationalCode == n).Select(x => (int?)x.usr_ID).FirstOrDefault();
            }
            catch { return null; }
        }
        void Token()
        {
            var c = Request.Cookies[AntiForgeryConfig.CookieName]; var t = Request.Headers["RequestVerificationToken"];
            if (c == null || String.IsNullOrWhiteSpace(t)) throw new HttpException(403, "توکن امنیتی نامعتبر است.");
            AntiForgery.Validate(c.Value, t);
        }
        List<Dictionary<string, object>> Query(string sql, params SqlParameter[] ps)
        {
            var z = new List<Dictionary<string, object>>(); using (var cn = NewConnection()) using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 180 })
            { if (ps != null) cmd.Parameters.AddRange(ps); cn.Open(); using (var r = cmd.ExecuteReader()) while (r.Read()) { var d = new Dictionary<string, object>(); for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); z.Add(d); } }
            return z;
        }
        List<List<Dictionary<string, object>>> QuerySets(string sql, params SqlParameter[] ps)
        {
            var all = new List<List<Dictionary<string, object>>>(); using (var cn = NewConnection()) using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 })
            { cmd.CommandTimeout = 900; if (ps != null) cmd.Parameters.AddRange(ps); cn.Open(); using (var r = cmd.ExecuteReader()) do { var set = new List<Dictionary<string, object>>(); while (r.Read()) { var d = new Dictionary<string, object>(); for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); set.Add(d); } all.Add(set); } while (r.NextResult()); }
            return all;
        }

        void EnsureReferralExecutionIndex()
        {
            using (var cn = NewConnection())
            using (var cmd = new SqlCommand(@"
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.dbergharelimsertfk1_7')
      AND name=N'IX_Referral_Batch_Valuunic_Field'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Referral_Batch_Valuunic_Field
    ON dbo.dbergharelimsertfk1_7
       (FK_dbergharelinsert_6, valuunic, FK_db2)
    INCLUDE (valueFK_db2);
END", cn) { CommandTimeout = 900 })
            {
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        [HttpGet] public ActionResult Index(long? id) { ViewBag.DesignId = id; return View("~/Areas/Setting/Views/ProcedureDesigner/Index.cshtml"); }
        [HttpGet]
        public JsonResult Bootstrap(long? id)
        {
            try
            {
                EnsureUpstreamStorage(); EnsureAdvancedClauses();
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        designs = Query(@"SELECT d.ID,d.Title,d.ProcedureSchema,d.ProcedureName,d.PublishedDate,
                    TRY_CONVERT(BIGINT,REPLACE(s.ClientKey,'list_','')) AS ListId,l.Title AS ListTitle
                    FROM Setting.tbProcedureDesign d
                    OUTER APPLY(SELECT TOP(1) ClientKey FROM Setting.tbProcedureSource WHERE DesignID=d.ID AND IsRoot=1 ORDER BY ID)s
                    LEFT JOIN dbo.tbHeadOfficeListDefinition l ON l.ListId=TRY_CONVERT(BIGINT,REPLACE(s.ClientKey,'list_',''))
                    ORDER BY d.ID DESC"),
                        lists = Query(@"SELECT l.ListId AS ID,l.Title,l.ReportId,r.name AS ReportTitle,l.KeyFieldId,k.name AS KeyFieldTitle,
                    l.DailyAmount,l.DurationDays,l.RoutingType,l.AllowDuplicate,l.CityId,c.Name AS CityTitle
                    FROM dbo.tbHeadOfficeListDefinition l
                    INNER JOIN dbo.dbergharel_4_4 r ON r.ID=l.ReportId AND ISNULL(r.del,0)=0
                    INNER JOIN dbo.dbergha2 k ON k.ID=l.KeyFieldId AND ISNULL(k.del,0)=0
                    LEFT JOIN dbo.tbCities c ON c.ID=l.CityId
                    WHERE EXISTS(SELECT 1 FROM dbo.tbHeadOfficeBaseField b WHERE b.ReportId=l.ReportId)
                    ORDER BY l.Title"),
                        indicators = Query("SELECT IndicatorId,Title,Unit FROM dbo.tbHeadOfficeMainIndicator WHERE IsDeleted=0 ORDER BY IndicatorId"),
                        snapshot = id.HasValue ? Snapshot(id.Value) : null
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }, JsonRequestBehavior.AllowGet); }
        }
        object Snapshot(long id)
        {
            return new
            {
                design = Query(@"SELECT d.*,TRY_CONVERT(BIGINT,REPLACE(s.ClientKey,'list_','')) AS ListId
                 FROM Setting.tbProcedureDesign d
                 OUTER APPLY(SELECT TOP(1) ClientKey FROM Setting.tbProcedureSource WHERE DesignID=d.ID AND IsRoot=1 ORDER BY ID)s
                 WHERE d.ID=@id", new SqlParameter("@id", id)).FirstOrDefault(),
                sources = Query("SELECT * FROM Setting.tbProcedureSource WHERE DesignID=@id ORDER BY SortOrder,ID", new SqlParameter("@id", id)),
                fields = Query(@"SELECT f.* FROM Setting.tbProcedureField f JOIN Setting.tbProcedureSource s ON s.ID=f.SourceID
                 WHERE s.DesignID=@id ORDER BY f.SortOrder,f.ID", new SqlParameter("@id", id)),
                rules = Query("SELECT * FROM Setting.tbProcedureRule WHERE DesignID=@id ORDER BY SortOrder,ID", new SqlParameter("@id", id)),
                clauses = Query(@"SELECT c.* FROM Setting.tbProcedureRuleClause c JOIN Setting.tbProcedureRule r ON r.ID=c.RuleID
                 WHERE r.DesignID=@id ORDER BY c.RuleID,c.GroupNo,c.SortOrder", new SqlParameter("@id", id))
            };
        }
        [HttpGet]
        public JsonResult Columns(string schemaName, string tableName)
        { try { return Json(new { success = true, data = Query("EXEC Setting.sp_ProcedureDesigner_Columns @s,@t", new SqlParameter("@s", schemaName), new SqlParameter("@t", tableName)) }, JsonRequestBehavior.AllowGet); } catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }, JsonRequestBehavior.AllowGet); } }

        [HttpGet]
        public JsonResult Context(long listId)
        {
            try
            {
                EnsureUpstreamStorage(); EnsureAdvancedClauses();
                var list = Query(@"SELECT l.ListId,l.Title,l.ReportId,r.name AS ReportTitle,l.KeyFieldId,k.name AS KeyFieldTitle,
                    l.DailyAmount,l.DurationDays,l.RoutingType,l.AllowDuplicate,l.CityId,c.Name AS CityTitle
                    FROM dbo.tbHeadOfficeListDefinition l
                    INNER JOIN dbo.dbergharel_4_4 r ON r.ID=l.ReportId
                    INNER JOIN dbo.dbergha2 k ON k.ID=l.KeyFieldId
                    LEFT JOIN dbo.tbCities c ON c.ID=l.CityId WHERE l.ListId=@id", new SqlParameter("@id", listId)).FirstOrDefault();
                if (list == null) throw new Exception("عنوان لیست انتخاب‌شده پیدا نشد.");
                var fields = Query(@"SELECT DISTINCT f.ID,f.name AS Title,f.type AS FieldType,
                    CASE WHEN f.type=1 THEN 'DATE' WHEN f.type IN(2,3) THEN 'NUMBER' ELSE 'TEXT' END AS ValueType
                    FROM dbo.tbHeadOfficeListDefinition l
                    INNER JOIN dbo.tbHeadOfficeBaseField b ON b.ReportId=l.ReportId
                    INNER JOIN dbo.dbergha2 f ON f.ID=b.FieldId AND ISNULL(f.del,0)=0
                    WHERE l.ListId=@id ORDER BY f.name", new SqlParameter("@id", listId));
                var subsets = Query(@"SELECT s.ID,s.FK_dbergha2 AS FieldId,s.name AS Title
                    FROM dbo.tbHeadOfficeListDefinition l
                    INNER JOIN dbo.tbHeadOfficeBaseField b ON b.ReportId=l.ReportId
                    INNER JOIN dbo.dbergharel2_3 s ON s.FK_dbergha2=b.FieldId AND ISNULL(s.del,0)=0
                    WHERE l.ListId=@id ORDER BY s.FK_dbergha2,s.name", new SqlParameter("@id", listId));
                var priorities = Query(@"DECLARE @CityId INT=(SELECT CityId FROM dbo.tbHeadOfficeListDefinition WHERE ListId=@id);
                    DECLARE @ProfileId INT=COALESCE(
                      (SELECT TOP(1) ProfileId FROM dbo.tbHeadOfficePriorityProfile WHERE CityId=@CityId AND @CityId IS NOT NULL ORDER BY ProfileId DESC),
                      (SELECT TOP(1) ProfileId FROM dbo.tbHeadOfficePriorityProfile WHERE CityId IS NULL ORDER BY ProfileId DESC));
                    SELECT r.FieldId,f.name AS FieldTitle,r.SubsetId,s.name AS SubsetTitle,r.MinimumAmount,r.MaximumAmount,
                           i.IndicatorId,i.Title AS IndicatorTitle,i.Unit,w.Weight
                    FROM dbo.tbHeadOfficePriorityRule r
                    INNER JOIN dbo.dbergha2 f ON f.ID=r.FieldId
                    LEFT JOIN dbo.dbergharel2_3 s ON s.ID=r.SubsetId
                    LEFT JOIN dbo.tbHeadOfficePriorityWeight w ON w.RuleId=r.RuleId
                    LEFT JOIN dbo.tbHeadOfficeMainIndicator i ON i.IndicatorId=w.IndicatorId AND i.IsDeleted=0
                    WHERE r.ProfileId=@ProfileId ORDER BY f.name,s.name,i.IndicatorId", new SqlParameter("@id", listId));
                return Json(new { success = true, data = new { list, fields, subsets, priorities } }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }, JsonRequestBehavior.AllowGet); }
        }

        void EnsureUpstreamStorage()
        {
            using (var cn = NewConnection()) using (var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.tbHeadOfficeMainIndicator',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeMainIndicator(IndicatorId INT IDENTITY PRIMARY KEY,Title NVARCHAR(150) NOT NULL,Unit NVARCHAR(50) NOT NULL,IsDeleted BIT NOT NULL DEFAULT(0),CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),CreatedBy INT NULL,UpdatedAt DATETIME2 NULL,UpdatedBy INT NULL,DeletedAt DATETIME2 NULL,DeletedBy INT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeBaseField',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeBaseField(BaseFieldId INT IDENTITY PRIMARY KEY,ReportId BIGINT NOT NULL,FieldId BIGINT NOT NULL,CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),CreatedBy INT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficePriorityProfile',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficePriorityProfile(ProfileId INT IDENTITY PRIMARY KEY,CityId INT NULL,CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),CreatedBy INT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficePriorityRule',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficePriorityRule(RuleId INT IDENTITY PRIMARY KEY,ProfileId INT NOT NULL,FieldId BIGINT NOT NULL,SubsetId BIGINT NULL,MinimumAmount DECIMAL(18,2) NULL,MaximumAmount DECIMAL(18,2) NULL,CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME());
IF OBJECT_ID(N'dbo.tbHeadOfficePriorityWeight',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficePriorityWeight(WeightId INT IDENTITY PRIMARY KEY,RuleId INT NOT NULL,IndicatorId INT NOT NULL,Weight DECIMAL(6,2) NOT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeListDefinition',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeListDefinition(ListId INT IDENTITY PRIMARY KEY,Title NVARCHAR(200) NOT NULL,ReportId BIGINT NOT NULL,KeyFieldId BIGINT NOT NULL,DailyAmount INT NOT NULL,DurationDays INT NOT NULL,RoutingType VARCHAR(20) NOT NULL,AllowDuplicate BIT NOT NULL,CityId INT NULL,CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),CreatedBy INT NULL,UpdatedAt DATETIME2 NULL,UpdatedBy INT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeListException',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeListException(ExceptionId INT IDENTITY PRIMARY KEY,ListId INT NOT NULL,FieldId BIGINT NOT NULL,SubsetId BIGINT NOT NULL);
IF OBJECT_ID(N'dbo.tbHeadOfficeCondition',N'U') IS NULL
 CREATE TABLE dbo.tbHeadOfficeCondition(ConditionId INT IDENTITY PRIMARY KEY,Title NVARCHAR(200) NOT NULL,Description NVARCHAR(1000) NULL,ExpressionText NVARCHAR(MAX) NOT NULL,CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),CreatedBy INT NULL,UpdatedAt DATETIME2 NULL,UpdatedBy INT NULL);", cn))
            { cn.Open(); cmd.ExecuteNonQuery(); }
        }

        [HttpPost]
        public JsonResult Save(ProcedureDesignDto x)
        {
            try
            {
                Token(); EnsureAdvancedClauses(); ApplyUpstreamConfiguration(x); Validate(x);
                using (var cn = NewConnection())
                {
                    cn.Open(); using (var tx = cn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        // Serialize saves across tabs before resolving the list's existing design.
                        using (var gate = new SqlCommand(@"DECLARE @result INT;
EXEC @result=sys.sp_getapplock @Resource=N'ProcedureDesigner.Save', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
IF @result<0 THROW 50001, 'Could not lock procedure design. Retry saving.', 1;", cn, tx)) gate.ExecuteNonQuery();
                        using (var existing = new SqlCommand(@"SELECT d.ID,d.ProcedureSchema,d.ProcedureName
FROM Setting.tbProcedureDesign d WITH(UPDLOCK,HOLDLOCK)
WHERE EXISTS(SELECT 1 FROM Setting.tbProcedureSource s WHERE s.DesignID=d.ID AND s.IsRoot=1 AND s.ClientKey=@key)
ORDER BY d.ID", cn, tx))
                        {
                            existing.Parameters.AddWithValue("@key", "list_" + x.ListId.ToString(CultureInfo.InvariantCulture));
                            using (var reader = existing.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    var existingId = reader.GetInt64(0);
                                    if (x.Id > 0 && x.Id != existingId) throw new Exception("این لیست طراحی دیگری دارد؛ صفحه را دوباره باز کنید.");
                                    x.Id = existingId;
                                    x.ProcedureSchema = reader.GetString(1);
                                    x.ProcedureName = reader.GetString(2);
                                    if (reader.Read()) throw new Exception("برای این لیست چند طراحی قدیمی وجود دارد؛ ابتدا طراحی‌های تکراری را بررسی و یکپارچه کنید.");
                                }
                                else if (x.Id > 0) throw new Exception("تغییر لیست طراحی موجود مجاز نیست؛ لیست مقصد را انتخاب کنید.");
                            }
                        }
                        ValidateCatalog(cn, tx, x);
                        long id;
                        if (x.Id > 0)
                        {
                            using (var cmd = new SqlCommand(@"UPDATE Setting.tbProcedureDesign SET Title=@t,ProcedureSchema=@s,ProcedureName=@p,
                         IsActive=@a,ModifiedDate=SYSDATETIME(),PublishedDate=NULL WHERE ID=@id;SELECT @id", cn, tx))
                            { cmd.Parameters.AddWithValue("@t", x.Title.Trim()); cmd.Parameters.AddWithValue("@s", x.ProcedureSchema.Trim()); cmd.Parameters.AddWithValue("@p", x.ProcedureName.Trim()); cmd.Parameters.AddWithValue("@a", x.IsActive); cmd.Parameters.AddWithValue("@id", x.Id); id = Convert.ToInt64(cmd.ExecuteScalar()); }
                            using (var cmd = new SqlCommand(@"DELETE c FROM Setting.tbProcedureRuleClause c JOIN Setting.tbProcedureRule r ON r.ID=c.RuleID WHERE r.DesignID=@id;
                         DELETE FROM Setting.tbProcedureRule WHERE DesignID=@id;DELETE f FROM Setting.tbProcedureField f JOIN Setting.tbProcedureSource s ON s.ID=f.SourceID WHERE s.DesignID=@id;
                         DELETE FROM Setting.tbProcedureSource WHERE DesignID=@id;", cn, tx)) { cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); }
                        }
                        else using (var cmd = new SqlCommand(@"INSERT Setting.tbProcedureDesign(Title,ProcedureSchema,ProcedureName,IsActive,CreatedByUserID)
                         VALUES(@t,@s,@p,@a,@u);SELECT CONVERT(BIGINT,SCOPE_IDENTITY())", cn, tx))
                            { cmd.Parameters.AddWithValue("@t", x.Title.Trim()); cmd.Parameters.AddWithValue("@s", x.ProcedureSchema.Trim()); cmd.Parameters.AddWithValue("@p", x.ProcedureName.Trim()); cmd.Parameters.AddWithValue("@a", x.IsActive); cmd.Parameters.AddWithValue("@u", V(UserId())); id = Convert.ToInt64(cmd.ExecuteScalar()); }

                        var sourceMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                        foreach (var s in x.Sources.OrderBy(a => a.SortOrder)) using (var cmd = new SqlCommand(@"INSERT Setting.tbProcedureSource
                      (DesignID,ClientKey,Title,SchemaName,TableName,KeyColumn,BatchColumn,IsRoot,SortOrder)
                      VALUES(@d,@c,@t,@s,@tb,@k,@b,@r,@o);SELECT CONVERT(BIGINT,SCOPE_IDENTITY())", cn, tx))
                            { cmd.Parameters.AddWithValue("@d", id); cmd.Parameters.AddWithValue("@c", s.ClientKey); cmd.Parameters.AddWithValue("@t", s.Title.Trim()); cmd.Parameters.AddWithValue("@s", s.SchemaName); cmd.Parameters.AddWithValue("@tb", s.TableName); cmd.Parameters.AddWithValue("@k", s.KeyColumn); cmd.Parameters.AddWithValue("@b", V(s.BatchColumn)); cmd.Parameters.AddWithValue("@r", s.IsRoot); cmd.Parameters.AddWithValue("@o", s.SortOrder); sourceMap[s.ClientKey] = Convert.ToInt64(cmd.ExecuteScalar()); }

                        var fieldMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in x.Fields.OrderBy(a => a.SortOrder)) using (var cmd = new SqlCommand(@"INSERT Setting.tbProcedureField
                      (SourceID,ClientKey,Title,FieldMode,ColumnName,ValueType,EavFieldID,EavFieldIdColumn,EavValueColumn,SortOrder)
                      VALUES(@s,@c,@t,@m,@col,@v,@eid,@ec,@ev,@o);SELECT CONVERT(BIGINT,SCOPE_IDENTITY())", cn, tx))
                            { cmd.Parameters.AddWithValue("@s", sourceMap[f.SourceClientKey]); cmd.Parameters.AddWithValue("@c", f.ClientKey); cmd.Parameters.AddWithValue("@t", f.Title.Trim()); cmd.Parameters.AddWithValue("@m", f.FieldMode); cmd.Parameters.AddWithValue("@col", f.FieldMode == "EAV" ? f.EavValueColumn : f.ColumnName); cmd.Parameters.AddWithValue("@v", f.ValueType); cmd.Parameters.AddWithValue("@eid", V(f.EavFieldId)); cmd.Parameters.AddWithValue("@ec", V(f.EavFieldIdColumn)); cmd.Parameters.AddWithValue("@ev", V(f.EavValueColumn)); cmd.Parameters.AddWithValue("@o", f.SortOrder); fieldMap[f.ClientKey] = Convert.ToInt64(cmd.ExecuteScalar()); }

                        foreach (var r in x.Rules.OrderBy(a => a.SortOrder))
                        {
                            long rid; using (var cmd = new SqlCommand(@"INSERT Setting.tbProcedureRule(DesignID,Title,RejectReason,IsActive,SortOrder)
                          VALUES(@d,@t,@rr,1,@o);SELECT CONVERT(BIGINT,SCOPE_IDENTITY())", cn, tx))
                            { cmd.Parameters.AddWithValue("@d", id); cmd.Parameters.AddWithValue("@t", r.Title.Trim()); cmd.Parameters.AddWithValue("@rr", r.RejectReason.Trim()); cmd.Parameters.AddWithValue("@o", r.SortOrder); rid = Convert.ToInt64(cmd.ExecuteScalar()); }
                            foreach (var c in r.Clauses.OrderBy(a => a.GroupNo).ThenBy(a => a.SortOrder)) using (var cmd = new SqlCommand(@"INSERT Setting.tbProcedureRuleClause
                         (RuleID,GroupNo,SortOrder,LeftFieldID,ArithmeticOperator,RightFieldID,RightConstant,CompareOperator,CompareValue,MatchMode)
                         VALUES(@r,@g,@o,@l,@a,@rf,@rc,@co,@cv,@mode)", cn, tx))
                                { cmd.Parameters.AddWithValue("@r", rid); cmd.Parameters.AddWithValue("@g", c.GroupNo); cmd.Parameters.AddWithValue("@o", c.SortOrder); cmd.Parameters.AddWithValue("@l", fieldMap[c.LeftFieldClientKey]); cmd.Parameters.AddWithValue("@a", c.ArithmeticOperator); cmd.Parameters.AddWithValue("@rf", c.RightFieldClientKey == null ? DBNull.Value : (object)fieldMap[c.RightFieldClientKey]); cmd.Parameters.AddWithValue("@rc", V(c.RightConstant)); cmd.Parameters.AddWithValue("@co", c.CompareOperator); cmd.Parameters.AddWithValue("@mode", c.MatchMode ?? "LEGACY"); cmd.Parameters.AddWithValue("@cv", c.CompareValue ?? ""); cmd.ExecuteNonQuery(); }
                        }
                        tx.Commit(); return Json(new { success = true, id, message = "طراحی ذخیره شد؛ هنوز هیچ پروسیجری منتشر نشده است." });
                    }
                }
            }
            catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }); }
        }

        static readonly string[] MatchModes = { "LEGACY", "ANY_EQ", "ANY_IN", "NONE_IN", "ANY_RANGE", "ANY_OPEN_RANGE", "NONE_RANGE", "ANY_STARTS", "ANY_AFTER_TODAY" };
        void EnsureAdvancedClauses()
        {
            using (var cn = NewConnection())
            {
                cn.Open(); using (var tx = cn.BeginTransaction())
                {
                    using (var cmd = new SqlCommand(@"DECLARE @r INT; EXEC @r=sys.sp_getapplock @Resource=N'ProcedureDesigner.AdvancedSchema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
IF @r<0 THROW 50010,'Schema lock failed',1;
IF COL_LENGTH(N'Setting.tbProcedureRuleClause',N'MatchMode') IS NULL
ALTER TABLE Setting.tbProcedureRuleClause ADD MatchMode VARCHAR(32) NOT NULL CONSTRAINT DF_ProcedureClause_MatchMode DEFAULT('LEGACY');", cn, tx)) cmd.ExecuteNonQuery(); tx.Commit();
                }
            }
        }
        void ValidateAdvanced(ProcedureClauseDto c)
        {
            var mode = c.MatchMode ?? "LEGACY";
            if (!MatchModes.Contains(mode)) throw new Exception("حالت بررسی رکوردها نامعتبر است.");
            if (mode == "LEGACY") return;
            if (c.ArithmeticOperator != "NONE" || c.RightFieldClientKey != null) throw new Exception("در حالت بررسی تمام رکوردها محاسبه و فیلد دوم را خالی بگذارید.");
            if (mode.EndsWith("IN") || mode.EndsWith("RANGE"))
            {
                var parts = (c.CompareValue ?? "").Split('|'); decimal value;
                if (parts.Length == 0 || parts.Length > 100 || parts.Any(v => !Decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))) throw new Exception("اعداد را با رقم انگلیسی و جداکننده | وارد کنید.");
                if (mode.EndsWith("RANGE") && (parts.Length != 2 || Decimal.Parse(parts[0], CultureInfo.InvariantCulture) > Decimal.Parse(parts[1], CultureInfo.InvariantCulture))) throw new Exception("بازه باید به صورت حداقل|حداکثر باشد.");
            }
            if (mode != "ANY_AFTER_TODAY" && String.IsNullOrWhiteSpace(c.CompareValue)) throw new Exception("مقدار شرط را وارد کنید.");
        }
        static readonly string[] Arithmetic = { "NONE", "+", "-", "*", "/" };
        static readonly string[] Compare = { "=", "<>", ">", ">=", "<", "<=", "CONTAINS", "STARTS", "ISNULL", "NOTNULL" };
        void ApplyUpstreamConfiguration(ProcedureDesignDto x)
        {
            if (x == null || x.ListId <= 0) throw new Exception("ابتدا عنوان لیست تعریف‌شده در مرحله قبل را انتخاب کنید.");
            var list = Query(@"SELECT l.ListId,l.Title,l.ReportId,r.name AS ReportTitle,l.KeyFieldId,k.name AS KeyFieldTitle
                FROM dbo.tbHeadOfficeListDefinition l
                INNER JOIN dbo.dbergharel_4_4 r ON r.ID=l.ReportId AND ISNULL(r.del,0)=0
                INNER JOIN dbo.dbergha2 k ON k.ID=l.KeyFieldId AND ISNULL(k.del,0)=0
                WHERE l.ListId=@id", new SqlParameter("@id", x.ListId)).FirstOrDefault();
            if (list == null) throw new Exception("عنوان لیست یا یونیک تعریف‌شده برای آن معتبر نیست.");
            var allowed = Query(@"SELECT DISTINCT f.ID,f.name AS Title,f.type AS FieldType
                FROM dbo.tbHeadOfficeBaseField b
                INNER JOIN dbo.tbHeadOfficeListDefinition l ON l.ReportId=b.ReportId
                INNER JOIN dbo.dbergha2 f ON f.ID=b.FieldId AND ISNULL(f.del,0)=0
                WHERE l.ListId=@id", new SqlParameter("@id", x.ListId));
            if (allowed.Count == 0) throw new Exception("برای گزارش این لیست هنوز فیلد پایه تعریف نشده است.");
            x.ProcedureSchema = "Setting";
            x.ProcedureName = "usp_ReferralList_" + x.ListId.ToString(CultureInfo.InvariantCulture);
            var sourceKey = "list_" + x.ListId.ToString(CultureInfo.InvariantCulture);
            x.Sources = new List<ProcedureSourceDto>{new ProcedureSourceDto{
                ClientKey=sourceKey,Title=Convert.ToString(list["ReportTitle"])+" / "+Convert.ToString(list["Title"]),
                SchemaName="dbo",TableName="dbergharelimsertfk1_7",KeyColumn="valuunic",
                BatchColumn="FK_dbergharelinsert_6",IsRoot=true,SortOrder=0}};
            x.Fields = allowed.Select((f, index) => new ProcedureFieldDto
            {
                ClientKey = "field_" + Convert.ToString(f["ID"], CultureInfo.InvariantCulture),
                SourceClientKey = sourceKey,
                Title = Convert.ToString(f["Title"]),
                FieldMode = "EAV",
                ColumnName = "valueFK_db2",
                ValueType = Convert.ToInt32(f["FieldType"] ?? 4) == 1 ? "DATE" : (new[] { 2, 3 }.Contains(Convert.ToInt32(f["FieldType"] ?? 4)) ? "NUMBER" : "TEXT"),
                EavFieldId = Convert.ToInt64(f["ID"]),
                EavFieldIdColumn = "FK_db2",
                EavValueColumn = "valueFK_db2",
                SortOrder = index
            }).ToList();
        }
        void Validate(ProcedureDesignDto x)
        {
            if (x == null || String.IsNullOrWhiteSpace(x.Title)) throw new Exception("عنوان طراحی الزامی است.");
            if (!Regex.IsMatch(x.ProcedureSchema ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$") || !Regex.IsMatch(x.ProcedureName ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new Exception("نام Schema یا پروسیجر معتبر نیست.");
            if (x.Sources == null || x.Sources.Count == 0 || x.Sources.Count(s => s.IsRoot) != 1) throw new Exception("دقیقاً یک جدول ریشه مشخص کنید.");
            if (x.Sources.Select(s => s.ClientKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != x.Sources.Count) throw new Exception("کلید جدول‌ها تکراری است.");
            if (x.Fields == null || x.Fields.Count == 0) throw new Exception("حداقل یک فیلد تعریف کنید.");
            if (x.Fields.Any(f => !x.Sources.Any(s => s.ClientKey.Equals(f.SourceClientKey, StringComparison.OrdinalIgnoreCase)))) throw new Exception("جدول یکی از فیلدها پیدا نشد.");
            if (x.Fields.Select(f => f.ClientKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != x.Fields.Count) throw new Exception("کلید فیلدها تکراری است.");
            if (x.Fields.Any(f => !new[] { "NORMAL", "EAV" }.Contains(f.FieldMode) || !new[] { "TEXT", "NUMBER", "DATE" }.Contains(f.ValueType))) throw new Exception("نوع یکی از فیلدها نامعتبر است.");
            if (x.Fields.Any(f => f.FieldMode == "NORMAL" && String.IsNullOrWhiteSpace(f.ColumnName))) throw new Exception("ستون فیلد عادی مشخص نشده است.");
            if (x.Fields.Any(f => f.FieldMode == "EAV" && (!f.EavFieldId.HasValue || String.IsNullOrWhiteSpace(f.EavFieldIdColumn) || String.IsNullOrWhiteSpace(f.EavValueColumn)))) throw new Exception("اطلاعات فیلد EAV کامل نیست.");
            foreach (var r in x.Rules ?? new List<ProcedureRuleDto>())
            {
                if (String.IsNullOrWhiteSpace(r.Title) || String.IsNullOrWhiteSpace(r.RejectReason) || r.Clauses == null || r.Clauses.Count == 0) throw new Exception("عنوان، دلیل رد و بندهای شرط کامل نیست.");
                foreach (var c in r.Clauses) { ValidateAdvanced(c); if (c.GroupNo < 1 || !Arithmetic.Contains(c.ArithmeticOperator) || !Compare.Contains(c.CompareOperator)) throw new Exception("عملگر شرط نامعتبر است."); if (!x.Fields.Any(f => f.ClientKey == c.LeftFieldClientKey)) throw new Exception("فیلد سمت چپ نامعتبر است."); if (c.RightFieldClientKey != null && !x.Fields.Any(f => f.ClientKey == c.RightFieldClientKey)) throw new Exception("فیلد سمت راست نامعتبر است."); }
            }
        }
        void ValidateCatalog(SqlConnection cn, SqlTransaction tx, ProcedureDesignDto x)
        {
            foreach (var s in x.Sources)
            {
                var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase); using (var cmd = new SqlCommand(@"SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas sc ON sc.schema_id=t.schema_id WHERE sc.name=@s AND t.name=@t", cn, tx))
                { cmd.Parameters.AddWithValue("@s", s.SchemaName); cmd.Parameters.AddWithValue("@t", s.TableName); using (var r = cmd.ExecuteReader()) while (r.Read()) cols.Add(r.GetString(0)); }
                if (cols.Count == 0) throw new Exception("جدول " + s.SchemaName + "." + s.TableName + " وجود ندارد.");
                foreach (var c in new[] { s.KeyColumn, s.BatchColumn }.Where(a => !String.IsNullOrWhiteSpace(a))) if (!cols.Contains(c)) throw new Exception("ستون " + c + " در جدول " + s.TableName + " وجود ندارد.");
                foreach (var f in x.Fields.Where(f => f.SourceClientKey == s.ClientKey))
                { var main = f.FieldMode == "EAV" ? f.EavValueColumn : f.ColumnName; if (!cols.Contains(main)) throw new Exception("ستون " + main + " وجود ندارد."); if (f.FieldMode == "EAV" && (!cols.Contains(f.EavFieldIdColumn) || !cols.Contains(f.EavValueColumn))) throw new Exception("ستون‌های EAV معتبر نیستند."); }
            }
        }

        [HttpPost]
        public JsonResult Preview(long id) { try { Token(); var sql = Compile(id); StoreBuild(id, sql, false); return Json(new { success = true, data = sql }); } catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }); } }
        [HttpPost]
        public JsonResult Publish(long id)
        {
            try { Token(); var sql = Compile(id); using (var cn = NewConnection()) { cn.Open(); using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 }) cmd.ExecuteNonQuery(); using (var cmd = new SqlCommand("UPDATE Setting.tbProcedureDesign SET PublishedDate=SYSDATETIME() WHERE ID=@id", cn)) { cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); } } StoreBuild(id, sql, true); return Json(new { success = true, message = "پروسیجر با موفقیت ساخته و منتشر شد." }); }
            catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }); }
        }

        [HttpPost]
        public JsonResult PublishAndExecute(long id)
        {
            try
            {
                Server.ScriptTimeout = 900;
                Token();

                // طراحی ذخیره‌شده را به پروسیجر واقعی تبدیل و منتشر می‌کند.
                var sql = Compile(id);
                using (var cn = NewConnection())
                {
                    cn.Open();
                    using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 })
                        cmd.ExecuteNonQuery();
                    using (var cmd = new SqlCommand("UPDATE Setting.tbProcedureDesign SET PublishedDate=SYSDATETIME() WHERE ID=@id", cn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
                StoreBuild(id, sql, true);

                // آخرین Batch فقط برای همان گزارشی انتخاب می‌شود که عنوان لیست به آن متصل است.
                var batch = Query(@"
SELECT TOP (1) b.ID AS BatchId
FROM Setting.tbProcedureDesign d
INNER JOIN Setting.tbProcedureSource src
    ON src.DesignID = d.ID AND src.IsRoot = 1
INNER JOIN dbo.tbHeadOfficeListDefinition l
    ON l.ListId = TRY_CONVERT(BIGINT, REPLACE(src.ClientKey, 'list_', ''))
INNER JOIN dbo.dbergharelinsert_6 b
    ON 1 = 1
INNER JOIN dbo.dbergharel_4 report
    ON report.ID = b.FK_table4
   AND report.FK_namegozarwsh = l.ReportId
   AND ISNULL(report.del, 0) = 0
WHERE d.ID = @id
ORDER BY b.ID DESC", new SqlParameter("@id", id)).FirstOrDefault();

                if (batch == null || batch["BatchId"] == null)
                    throw new Exception("برای گزارش انتخاب‌شده هیچ Batch قابل اجرایی پیدا نشد.");

                var batchId = Convert.ToInt64(batch["BatchId"]);
                EnsureReferralExecutionIndex();
                var sets = RunSavedProcedure(id, batchId);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        message = "طراحی ذخیره، پروسیجر منتشر و آخرین Batch با موفقیت اجرا شد.",
                        batchId,
                        summary = sets.Count > 0 ? sets[0].FirstOrDefault() : null
                    }
                });
            }
            catch (Exception e)
            {
                return Json(new { success = false, message = e.GetBaseException().Message });
            }
        }
        void StoreBuild(long id, string sql, bool pub)
        {
            using (var cn = NewConnection()) using (var cmd = new SqlCommand(@"DECLARE @v INT=ISNULL((SELECT MAX(VersionNo) FROM Setting.tbProcedureBuild WHERE DesignID=@d),0)+1;
          INSERT Setting.tbProcedureBuild(DesignID,VersionNo,GeneratedSql,SqlHash,IsPublished,BuiltByUserID) VALUES(@d,@v,@s,HASHBYTES('SHA2_256',@s),@p,@u)", cn))
            { cmd.Parameters.AddWithValue("@d", id); cmd.Parameters.AddWithValue("@s", sql); cmd.Parameters.AddWithValue("@p", pub); cmd.Parameters.AddWithValue("@u", V(UserId())); cn.Open(); cmd.ExecuteNonQuery(); }
        }

        class SourceM { public long ID; public string SchemaName, TableName, KeyColumn, BatchColumn; public bool IsRoot; }
        class FieldM { public long ID, SourceID; public string FieldMode, ColumnName, ValueType, EavFieldIdColumn, EavValueColumn; public long? EavFieldID; }
        class RuleM { public long ID; public string Title, RejectReason; public int SortOrder; }
        class ClauseM { public long RuleID, LeftFieldID; public long? RightFieldID; public int GroupNo, SortOrder; public string ArithmeticOperator, RightConstant, CompareOperator, CompareValue, MatchMode; }
        string Compile(long id)
        {
            EnsureAdvancedClauses();
            ProcedureMeta d; var sources = new List<SourceM>(); var fields = new List<FieldM>(); var rules = new List<RuleM>(); var clauses = new List<ClauseM>();
            using (var cn = NewConnection())
            {
                cn.Open();
                d = ReadOne(cn, "SELECT ProcedureSchema,ProcedureName FROM Setting.tbProcedureDesign WHERE ID=@id", id, r => new ProcedureMeta { Schema = r.GetString(0), Name = r.GetString(1) }); if (d == null) throw new Exception("طراحی پیدا نشد.");
                sources = Read(cn, "SELECT ID,SchemaName,TableName,KeyColumn,BatchColumn,IsRoot FROM Setting.tbProcedureSource WHERE DesignID=@id", id, r => new SourceM { ID = r.GetInt64(0), SchemaName = r.GetString(1), TableName = r.GetString(2), KeyColumn = r.GetString(3), BatchColumn = r.IsDBNull(4) ? null : r.GetString(4), IsRoot = r.GetBoolean(5) });
                fields = Read(cn, @"SELECT f.ID,f.SourceID,f.FieldMode,f.ColumnName,f.ValueType,f.EavFieldID,f.EavFieldIdColumn,f.EavValueColumn FROM Setting.tbProcedureField f JOIN Setting.tbProcedureSource s ON s.ID=f.SourceID WHERE s.DesignID=@id", id, r => new FieldM { ID = r.GetInt64(0), SourceID = r.GetInt64(1), FieldMode = r.GetString(2), ColumnName = r.GetString(3), ValueType = r.GetString(4), EavFieldID = r.IsDBNull(5) ? (long?)null : r.GetInt64(5), EavFieldIdColumn = r.IsDBNull(6) ? null : r.GetString(6), EavValueColumn = r.IsDBNull(7) ? null : r.GetString(7) });
                rules = Read(cn, "SELECT ID,Title,RejectReason,SortOrder FROM Setting.tbProcedureRule WHERE DesignID=@id AND IsActive=1 ORDER BY SortOrder,ID", id, r => new RuleM { ID = r.GetInt64(0), Title = r.GetString(1), RejectReason = r.GetString(2), SortOrder = r.GetInt32(3) });
                clauses = Read(cn, @"SELECT c.RuleID,c.GroupNo,c.SortOrder,c.LeftFieldID,c.ArithmeticOperator,c.RightFieldID,c.RightConstant,c.CompareOperator,c.CompareValue,c.MatchMode FROM Setting.tbProcedureRuleClause c JOIN Setting.tbProcedureRule r ON r.ID=c.RuleID WHERE r.DesignID=@id AND r.IsActive=1 ORDER BY c.RuleID,c.GroupNo,c.SortOrder", id, r => new ClauseM { RuleID = r.GetInt64(0), GroupNo = r.GetInt32(1), SortOrder = r.GetInt32(2), LeftFieldID = r.GetInt64(3), ArithmeticOperator = r.GetString(4), RightFieldID = r.IsDBNull(5) ? (long?)null : r.GetInt64(5), RightConstant = r.IsDBNull(6) ? null : r.GetString(6), CompareOperator = r.GetString(7), CompareValue = r.GetString(8), MatchMode = r.GetString(9) });
            }
            var listRow = Query(@"SELECT TRY_CONVERT(BIGINT,REPLACE(ClientKey,'list_','')) AS ListId FROM Setting.tbProcedureSource WHERE DesignID=@id AND IsRoot=1", new SqlParameter("@id", id)).Single();
            var listId = Convert.ToInt64(listRow["ListId"]);
            EnsureResultTables(listId);
            var root = sources.Single(s => s.IsRoot); string Q(string x) => "[" + x.Replace("]", "]]") + "]"; string Lit(string x) => "N'" + (x ?? "").Replace("'", "''") + "'";
            string valueAlias(FieldM f) => "fv" + f.ID.ToString(CultureInfo.InvariantCulture);
            string raw(FieldM f) => valueAlias(f) + ".[FieldValue]";
            string fieldApply(FieldM f)
            {
                var s = sources.Single(source => source.ID == f.SourceID);
                var alias = "s" + s.ID.ToString(CultureInfo.InvariantCulture) + "_" + f.ID.ToString(CultureInfo.InvariantCulture);
                var where = "CONVERT(NVARCHAR(500)," + alias + "." + Q(s.KeyColumn) + ")=CONVERT(NVARCHAR(500),r." + Q(root.KeyColumn) + ")";
                if (!String.IsNullOrWhiteSpace(s.BatchColumn)) where += " AND (@BatchID IS NULL OR " + alias + "." + Q(s.BatchColumn) + "=@BatchID)";
                if (f.FieldMode == "EAV") where += " AND " + alias + "." + Q(f.EavFieldIdColumn) + "=" + f.EavFieldID.Value.ToString(CultureInfo.InvariantCulture);
                return "\r\n OUTER APPLY (SELECT MAX(CONVERT(NVARCHAR(MAX)," + alias + "." + Q(f.FieldMode == "EAV" ? f.EavValueColumn : f.ColumnName) + ")) AS [FieldValue] FROM " + Q(s.SchemaName) + "." + Q(s.TableName) + " " + alias + " WHERE " + where + ") " + valueAlias(f);
            }
            // همه مقادیر ورودی رشته هستند؛ فقط در زمان عملیات عددی پاک‌سازی و تبدیل می‌شوند.
            string num(string e) => "TRY_CONVERT(DECIMAL(38,6),NULLIF(REPLACE(REPLACE(REPLACE(TRANSLATE(LTRIM(RTRIM(" + e + ")),N'۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩',N'01234567890123456789'),N',',N''),N'٬',N''),N'٫',N'.'),N''))";
            string normalizeNumberLiteral(string value)
            {
                var result = (value ?? String.Empty).Trim().Replace(",", String.Empty).Replace("٬", String.Empty).Replace("٫", ".");
                const string source = "۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩";
                const string target = "01234567890123456789";
                for (var i = 0; i < source.Length; i++) result = result.Replace(source[i], target[i]);
                return result;
            }
            bool isNumericLiteral(string value)
            {
                decimal parsed;
                return Decimal.TryParse(normalizeNumberLiteral(value), NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out parsed);
            }
            string advancedClause(ClauseM c)
            {
                var f = fields.Single(x => x.ID == c.LeftFieldID);
                var source = sources.Single(x => x.ID == f.SourceID);
                var value = "a." + Q(f.FieldMode == "EAV" ? f.EavValueColumn : f.ColumnName);
                var where = "CONVERT(NVARCHAR(500),a." + Q(source.KeyColumn) + ")=CONVERT(NVARCHAR(500),r." + Q(root.KeyColumn) + ")";
                if (!String.IsNullOrWhiteSpace(source.BatchColumn)) where += " AND a." + Q(source.BatchColumn) + "=@BatchID";
                if (f.FieldMode == "EAV") where += " AND a." + Q(f.EavFieldIdColumn) + "=" + f.EavFieldID.Value.ToString(CultureInfo.InvariantCulture);
                string condition;
                var mode = c.MatchMode;
                if (mode == "ANY_AFTER_TODAY")
                {
                    var dateValue = "TRY_CONVERT(BIGINT,REPLACE(REPLACE(TRANSLATE(LTRIM(RTRIM(" + value + ")),N'۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩',N'01234567890123456789'),N'/',N''),N'-',N''))";
                    condition = dateValue + ">CONVERT(BIGINT,@RunYear)*10000+@RunMonth*100+@RunDay";
                }
                else if (mode == "ANY_STARTS") condition = "LTRIM(RTRIM(" + value + ")) LIKE " + Lit((c.CompareValue ?? "").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")) + "+N'%'";
                else if (mode.EndsWith("IN")) condition = num(value) + " IN (" + String.Join(",", (c.CompareValue ?? "").Split('|').Select(v => num(Lit(v)))) + ")";
                else if (mode.EndsWith("RANGE"))
                {
                    var parts = c.CompareValue.Split('|'); condition = mode == "ANY_OPEN_RANGE" ? "(" + num(value) + ">" + num(Lit(parts[0])) + " AND " + num(value) + "<" + num(Lit(parts[1])) + ")" : num(value) + " BETWEEN " + num(Lit(parts[0])) + " AND " + num(Lit(parts[1]));
                }
                else condition = "LTRIM(RTRIM(" + value + "))=" + Lit(c.CompareValue);
                return (mode.StartsWith("NONE_") ? "NOT EXISTS" : "EXISTS") + "(SELECT 1 FROM " + Q(source.SchemaName) + "." + Q(source.TableName) + " a WHERE " + where + " AND (" + condition + "))";
            }
            string clause(ClauseM c)
            {
                if (!String.IsNullOrEmpty(c.MatchMode) && c.MatchMode != "LEGACY") return advancedClause(c); var lf = fields.Single(f => f.ID == c.LeftFieldID); var le = raw(lf); if (c.CompareOperator == "ISNULL") return "NULLIF(LTRIM(RTRIM(" + le + ")),N'') IS NULL"; if (c.CompareOperator == "NOTNULL") return "NULLIF(LTRIM(RTRIM(" + le + ")),N'') IS NOT NULL";
                string left = le; if (c.ArithmeticOperator != "NONE") { var right = c.RightFieldID.HasValue ? num(raw(fields.Single(f => f.ID == c.RightFieldID.Value))) : num(Lit(c.RightConstant)); left = "(" + num(le) + " " + c.ArithmeticOperator + " " + (c.ArithmeticOperator == "/" ? "NULLIF(" + right + ",0)" : right) + ")"; }
                if (c.CompareOperator == "CONTAINS") return left + " LIKE N'%'+" + Lit(c.CompareValue) + "+N'%'"; if (c.CompareOperator == "STARTS") return left + " LIKE " + Lit(c.CompareValue) + "+N'%'";
                // به type دیتابیس وابسته نیستیم: عملیات ریاضی یا مقدار مقایسه عددی، تبدیل عددی را فعال می‌کند.
                bool numeric = c.ArithmeticOperator != "NONE" || isNumericLiteral(c.CompareValue); bool date = !numeric && lf.ValueType == "DATE";
                string l = numeric ? num(left) : (date ? "TRY_CONVERT(DATE," + left + ")" : "ISNULL(CONVERT(NVARCHAR(MAX)," + left + "),N'')");
                string rv = numeric ? num(Lit(c.CompareValue)) : (date ? "TRY_CONVERT(DATE," + Lit(c.CompareValue) + ")" : Lit(c.CompareValue)); return l + " " + c.CompareOperator + " " + rv;
            }
            string predicate(RuleM r0) => "(" + String.Join(" OR ", clauses.Where(c => c.RuleID == r0.ID).GroupBy(c => c.GroupNo).OrderBy(g => g.Key).Select(g => "(" + String.Join(" AND ", g.OrderBy(c => c.SortOrder).Select(clause)) + ")")) + ")";
            var cases = rules.Select(r0 => "CASE WHEN " + predicate(r0) + " THEN N'؛ " + r0.RejectReason.Replace("'", "''") + "' ELSE N'' END").ToList();
            var rejected = rules.Count == 0 ? "0" : "CASE WHEN " + String.Join(" OR ", rules.Select(predicate)) + " THEN 1 ELSE 0 END";
            var reason = cases.Count == 0 ? "CAST(N'' AS NVARCHAR(MAX))" : "STUFF(" + String.Join(" + ", cases) + ",1,2,N'')";
            var usedFieldIds = new HashSet<long>(clauses.Where(c => String.IsNullOrEmpty(c.MatchMode) || c.MatchMode == "LEGACY").Select(c => c.LeftFieldID)
                .Concat(clauses.Where(c => c.RightFieldID.HasValue).Select(c => c.RightFieldID.Value)));
            var usedFields = fields.Where(f => usedFieldIds.Contains(f.ID)).ToList();
            var applies = String.Join(String.Empty, usedFields.Select(fieldApply));
            var rootAlias = "rootSource";
            var rootBatch = String.IsNullOrWhiteSpace(root.BatchColumn) ? String.Empty :
                " WHERE (@BatchID IS NULL OR " + rootAlias + "." + Q(root.BatchColumn) + "=@BatchID)";
            var rootSet = "(SELECT DISTINCT " + rootAlias + "." + Q(root.KeyColumn) + " FROM " +
                Q(root.SchemaName) + "." + Q(root.TableName) + " " + rootAlias + rootBatch + ")";
            return "CREATE OR ALTER PROCEDURE " + Q(d.Schema) + "." + Q(d.Name) + "\r\n @BatchID BIGINT=NULL, @RunDate DATE, @RunYear INT, @RunMonth INT, @RunDay INT\r\nAS\r\nBEGIN\r\n SET NOCOUNT ON; SET XACT_ABORT ON;\r\n" + ResolveBatchSql(listId) + "\r\n SELECT CONVERT(NVARCHAR(500),r." + Q(root.KeyColumn) + ") AS UniqueKey,\r\n  CONVERT(BIT," + rejected + ") AS IsRejected,\r\n  " + reason + " AS RejectReason\r\n INTO #Evaluation\r\n FROM " + rootSet + " r" + applies + ";" + PersistResultsSql(listId) + "\r\nEND;";
        }
        void EnsureResultTables(long listId)
        {
            var sql = @"IF OBJECT_ID(N'dbo.Result1218',N'U') IS NULL
CREATE TABLE dbo.Result1218(ID BIGINT IDENTITY PRIMARY KEY,valuunic NVARCHAR(500),Input_Year INT,Input_Month INT,Input_Day INT,Input_Day_ASL INT,CreatedAt DATETIME DEFAULT GETDATE());
IF OBJECT_ID(N'dbo.Result1218_Rejected',N'U') IS NULL
CREATE TABLE dbo.Result1218_Rejected(ID BIGINT IDENTITY PRIMARY KEY,valuunic NVARCHAR(500),Reason NVARCHAR(500),Input_Day_ASL INT NULL,Month INT NULL,Year INT NULL,TotalVosol NVARCHAR(500),CheckDate DATETIME DEFAULT GETDATE());
IF COL_LENGTH(N'dbo.Result1218',N'ListId') IS NULL ALTER TABLE dbo.Result1218 ADD ListId BIGINT NULL;
IF COL_LENGTH(N'dbo.Result1218',N'BatchID') IS NULL ALTER TABLE dbo.Result1218 ADD BatchID BIGINT NULL;
IF COL_LENGTH(N'dbo.Result1218',N'RunDate') IS NULL ALTER TABLE dbo.Result1218 ADD RunDate DATE NULL;
IF COL_LENGTH(N'dbo.Result1218_Rejected',N'ListId') IS NULL ALTER TABLE dbo.Result1218_Rejected ADD ListId BIGINT NULL;
IF COL_LENGTH(N'dbo.Result1218_Rejected',N'BatchID') IS NULL ALTER TABLE dbo.Result1218_Rejected ADD BatchID BIGINT NULL;
IF COL_LENGTH(N'dbo.Result1218_Rejected',N'RunDate') IS NULL ALTER TABLE dbo.Result1218_Rejected ADD RunDate DATE NULL;
ALTER TABLE dbo.Result1218_Rejected ALTER COLUMN Reason NVARCHAR(MAX) NULL;";
            using (var cn = NewConnection())
            {
                cn.Open(); using (var tx = cn.BeginTransaction())
                {
                    using (var cmd = new SqlCommand(@"DECLARE @r INT; EXEC @r=sys.sp_getapplock @Resource=N'ProcedureDesigner.ResultTables',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 50010,'Result table lock failed',1;", cn, tx)) cmd.ExecuteNonQuery();
                    using (var cmd = new SqlCommand(sql, cn, tx)) cmd.ExecuteNonQuery(); tx.Commit();
                }
            }
        }
        string ResolveBatchSql(long listId)
        {
            return @"IF @BatchID IS NULL
SELECT TOP(1) @BatchID=b.ID FROM dbo.dbergharelinsert_6 b
JOIN dbo.dbergharel_4 r ON r.ID=b.FK_table4
JOIN dbo.tbHeadOfficeListDefinition l ON l.ReportId=r.FK_namegozarwsh
WHERE l.ListId=" + listId + @" AND ISNULL(r.del,0)=0 ORDER BY b.ID DESC;
IF @BatchID IS NULL THROW 50001,N'No batch found for this list',1;
IF NOT EXISTS(SELECT 1 FROM dbo.dbergharelinsert_6 b
JOIN dbo.dbergharel_4 r ON r.ID=b.FK_table4
JOIN dbo.tbHeadOfficeListDefinition l ON l.ReportId=r.FK_namegozarwsh
WHERE l.ListId=" + listId + @" AND b.ID=@BatchID AND ISNULL(r.del,0)=0)
THROW 50002,N'Batch does not belong to this list',1;";
        }
        string PersistResultsSql(long listId)
        {
            return @"
IF @RunDate IS NULL OR @RunYear IS NULL OR @RunMonth IS NULL OR @RunDay IS NULL OR @RunMonth NOT BETWEEN 1 AND 12 OR @RunDay NOT BETWEEN 1 AND 31
THROW 50004,'Execution date is required',1;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @lockResult INT;
EXEC @lockResult=sys.sp_getapplock @Resource=N'Result1218_Daily',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
IF @lockResult<0 THROW 50003,'Result lock failed',1;
DELETE FROM dbo.Result1218 WHERE ListId=" + listId + @" AND RunDate=@RunDate;
DELETE FROM dbo.Result1218_Rejected WHERE ListId=" + listId + @" AND RunDate=@RunDate;
INSERT dbo.Result1218(valuunic,Input_Year,Input_Month,Input_Day,Input_Day_ASL,CreatedAt,ListId,BatchID,RunDate)
SELECT UniqueKey,@RunYear,@RunMonth,@RunDay,@RunDay,DATEADD(MINUTE,210,GETUTCDATE())," + listId + @",@BatchID,@RunDate FROM #Evaluation WHERE IsRejected=0 AND UniqueKey IS NOT NULL;
DECLARE @accepted BIGINT=@@ROWCOUNT;
INSERT dbo.Result1218_Rejected(valuunic,Reason,Input_Day_ASL,Month,Year,TotalVosol,CheckDate,ListId,BatchID,RunDate)
SELECT UniqueKey,RejectReason,@RunDay,@RunMonth,@RunYear,NULL,DATEADD(MINUTE,210,GETUTCDATE())," + listId + @",@BatchID,@RunDate FROM #Evaluation WHERE IsRejected=1 AND UniqueKey IS NOT NULL;
DECLARE @rejected BIGINT=@@ROWCOUNT;
COMMIT;
SELECT @BatchID AS BatchId,@accepted AS AcceptedCount,@rejected AS RejectedCount;
END TRY
BEGIN CATCH
IF XACT_STATE()<>0 ROLLBACK;
THROW;
END CATCH;";
        }
        List<List<Dictionary<string, object>>> RunSavedProcedure(long id, long? batchId)
        {
            var d = Query("SELECT ProcedureSchema,ProcedureName FROM Setting.tbProcedureDesign WHERE ID=@id AND PublishedDate IS NOT NULL", new SqlParameter("@id", id)).Single();
            string quote(string v) => "[" + v.Replace("]", "]]") + "]";
            var localNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Iran Standard Time");
            var calendar = new PersianCalendar();
            return QuerySets("EXEC " + quote(Convert.ToString(d["ProcedureSchema"])) + "." + quote(Convert.ToString(d["ProcedureName"])) + " @BatchID=@b,@RunDate=@rd,@RunYear=@ry,@RunMonth=@rm,@RunDay=@rday", new SqlParameter("@b", V(batchId)), new SqlParameter("@rd", localNow.Date), new SqlParameter("@ry", calendar.GetYear(localNow)), new SqlParameter("@rm", calendar.GetMonth(localNow)), new SqlParameter("@rday", calendar.GetDayOfMonth(localNow)));
        }
        class ProcedureMeta { public string Schema, Name; }
        T ReadOne<T>(SqlConnection c, string q, long id, Func<SqlDataReader, T> f) where T : class { using (var x = new SqlCommand(q, c)) { x.Parameters.AddWithValue("@id", id); using (var r = x.ExecuteReader()) return r.Read() ? f(r) : null; } }
        List<T> Read<T>(SqlConnection c, string q, long id, Func<SqlDataReader, T> f) { var z = new List<T>(); using (var x = new SqlCommand(q, c)) { x.Parameters.AddWithValue("@id", id); using (var r = x.ExecuteReader()) while (r.Read()) z.Add(f(r)); } return z; }

        void RequireBirjandProcessing()
        {
            if (!UserId().HasValue || !db.tbPeymanContracts.Any(x => x.pec_Title == "کارگزاری خدمات توزیع نیروی برق بیرجند"))
                throw new HttpException(403, "این بخش فقط برای بیرجند در دسترس است.");
        }
        [HttpGet]
        public JsonResult ProcessingDesigns(long periodId)
        {
            try
            {
                RequireBirjandProcessing();
                var rows = Query(@"SELECT d.ID,d.Title,l.Title AS ListTitle
FROM Setting.tbProcedureDesign d
JOIN Setting.tbProcedureSource s ON s.DesignID=d.ID AND s.IsRoot=1
JOIN dbo.tbHeadOfficeListDefinition l ON l.ListId=TRY_CONVERT(BIGINT,REPLACE(s.ClientKey,'list_',''))
JOIN dbo.dbergharel_4 p ON p.FK_namegozarwsh=l.ReportId AND p.ID=@period AND ISNULL(p.del,0)=0
WHERE d.IsActive=1 ORDER BY l.Title,d.ID", new SqlParameter("@period", periodId));
                return Json(new { success = true, items = rows }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }, JsonRequestBehavior.AllowGet); }
        }
        [HttpPost]
        public JsonResult ProcessUploadedData(long id, long periodId)
        {
            try
            {
                Token(); RequireBirjandProcessing(); Server.ScriptTimeout = 900;
                var batch = Query(@"SELECT TOP(1) b.ID AS BatchId
FROM Setting.tbProcedureDesign d
JOIN Setting.tbProcedureSource s ON s.DesignID=d.ID AND s.IsRoot=1
JOIN dbo.tbHeadOfficeListDefinition l ON l.ListId=TRY_CONVERT(BIGINT,REPLACE(s.ClientKey,'list_',''))
JOIN dbo.dbergharel_4 p ON p.FK_namegozarwsh=l.ReportId AND p.ID=@period AND ISNULL(p.del,0)=0
JOIN dbo.dbergharelinsert_6 b ON b.FK_table4=p.ID
WHERE d.ID=@id AND d.IsActive=1 ORDER BY b.ID DESC", new SqlParameter("@id", id), new SqlParameter("@period", periodId)).FirstOrDefault();
                if (batch == null) throw new Exception("برای دوره و لیست انتخاب‌شده بارگذاری ثبت‌شده‌ای پیدا نشد؛ ابتدا ثبت اکسل را کامل کنید.");
                var batchId = Convert.ToInt64(batch["BatchId"]);
                var sql = Compile(id);
                using (var cn = NewConnection())
                {
                    cn.Open(); using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 }) cmd.ExecuteNonQuery();
                    using (var cmd = new SqlCommand("UPDATE Setting.tbProcedureDesign SET PublishedDate=SYSDATETIME() WHERE ID=@id", cn)) { cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); }
                }
                StoreBuild(id, sql, true);
                var sets = RunSavedProcedure(id, batchId);
                return Json(new { success = true, message = "پردازش کامل شد؛ نتایج در جدول‌های ۱۲۱۸ ذخیره شدند.", batchId, summary = sets.Count > 0 ? sets[0].FirstOrDefault() : null });
            }
            catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }); }
        }

        [HttpPost]
        public JsonResult Execute(long id, long? batchId)
        { try { Token(); var sets = RunSavedProcedure(id, batchId); return Json(new { success = true, data = new { summary = sets.Count > 0 ? sets[0].FirstOrDefault() : null } }); } catch (Exception e) { return Json(new { success = false, message = e.GetBaseException().Message }); } }
    }

    public class ProcedureDesignDto
    {
        public long Id { get; set; }
        public long ListId { get; set; }
        public string Title { get; set; }
        public string ProcedureSchema { get; set; }
        public string ProcedureName { get; set; }
        public bool IsActive { get; set; }
        public List<ProcedureSourceDto> Sources { get; set; } = new List<ProcedureSourceDto>(); public List<ProcedureFieldDto> Fields { get; set; } = new List<ProcedureFieldDto>(); public List<ProcedureRuleDto> Rules { get; set; } = new List<ProcedureRuleDto>();
    }
    public class ProcedureSourceDto { public string ClientKey { get; set; } public string Title { get; set; } public string SchemaName { get; set; } public string TableName { get; set; } public string KeyColumn { get; set; } public string BatchColumn { get; set; } public bool IsRoot { get; set; } public int SortOrder { get; set; } }
    public class ProcedureFieldDto { public string ClientKey { get; set; } public string SourceClientKey { get; set; } public string Title { get; set; } public string FieldMode { get; set; } public string ColumnName { get; set; } public string ValueType { get; set; } public long? EavFieldId { get; set; } public string EavFieldIdColumn { get; set; } public string EavValueColumn { get; set; } public int SortOrder { get; set; } }
    public class ProcedureRuleDto { public string Title { get; set; } public string RejectReason { get; set; } public int SortOrder { get; set; } public List<ProcedureClauseDto> Clauses { get; set; } = new List<ProcedureClauseDto>(); }
    public class ProcedureClauseDto { public string MatchMode { get; set; } = "LEGACY"; public int GroupNo { get; set; } public int SortOrder { get; set; } public string LeftFieldClientKey { get; set; } public string ArithmeticOperator { get; set; } public string RightFieldClientKey { get; set; } public string RightConstant { get; set; } public string CompareOperator { get; set; } public string CompareValue { get; set; } }
}
