using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using SaabWebProject.Areas.DocumentControl.Models;
using SaabWebProject.Models.DomainModels;

namespace SaabWebProject.Areas.DocumentControl.Controllers
{
    /// <summary>
    /// ماژول مستقل کنترل مدارک و گردش پیگیری.
    /// تمام عملیات تنظیمی و اجرایی از کنترلرهای فعلی پروژه جدا است.
    /// </summary>
    public class DocumentControlController : Controller
    {
        private readonly SaabEntities db = new SaabEntities();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        private static readonly string[] ManageableModuleRoles = { "Admin", "Manager" };

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        private int? GetCurrentUserId()
        {
            try
            {
                var cookie = Request.Cookies[Utility.Base64.Base64Encode("CodeMeli")];
                if (cookie == null) return null;

                var nationalCode = Utility.Base64.Base64Decode(cookie.Value);
                return db.tbUsers
                    .Where(x => x.usr_NationalCode == nationalCode && x.usr_IsActive == true)
                    .Select(x => (int?)x.usr_ID)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private JsonResult Fail(string message)
        {
            return Json(new { success = false, message = message });
        }

        private bool HasModuleRole(int userId, params string[] roles)
        {
            if (roles == null || roles.Length == 0) return false;
            return ScalarInt(@"SELECT COUNT(1) FROM dbo.DC_ModuleUser
                               WHERE UserId=@UserId AND IsActive=1 AND RoleCode IN ("
                + String.Join(",", roles.Select((x, i) => "@Role" + i)) + ")", command =>
                {
                    P(command, "@UserId", userId);
                    for (var i = 0; i < roles.Length; i++) P(command, "@Role" + i, roles[i]);
                }) > 0;
        }

        private DbParameter P(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
            return parameter;
        }

        private T WithConnection<T>(Func<DbConnection, T> work)
        {
            var connection = db.Database.Connection;
            var openedHere = connection.State != ConnectionState.Open;

            try
            {
                if (openedHere) connection.Open();
                return work(connection);
            }
            finally
            {
                if (openedHere && connection.State == ConnectionState.Open) connection.Close();
            }
        }

        private int ScalarInt(string sql, Action<DbCommand> parameters = null)
        {
            return WithConnection(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    if (parameters != null) parameters(command);
                    var value = command.ExecuteScalar();
                    return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
            });
        }

        private List<LookupItem> LoadLookup(string sql, Action<DbCommand> parameters = null)
        {
            return WithConnection(connection =>
            {
                var result = new List<LookupItem>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    if (parameters != null) parameters(command);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new LookupItem
                            {
                                Id = Convert.ToInt32(reader[0]),
                                Title = Convert.ToString(reader[1]),
                                Meta = reader.FieldCount > 2 ? Convert.ToString(reader[2]) : "",
                                RelationIds = reader.FieldCount > 3 ? Convert.ToString(reader[3]) : ""
                            });
                        }
                    }
                }

                return result;
            });
        }

        [HttpGet]
        public ActionResult Index()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Content("دسترسی غیر مجاز");

            var vm = new DocumentControlIndexVm
            {
                CanConfigure = HasModuleRole(currentUserId.Value, "Admin"),
                CanManage = HasModuleRole(currentUserId.Value, "Admin", "Manager"),
                Documents = LoadLookup(@"
                    SELECT d.Id, d.Title, d.AllowedExtensions,
                           STUFF((SELECT ',' + CONVERT(varchar(12),dc.ContractId)
                                  FROM dbo.DC_DocumentContract dc
                                  WHERE dc.DocumentDefinitionId=d.Id
                                  ORDER BY dc.ContractId
                                  FOR XML PATH(''),TYPE).value('.','varchar(max)'),1,1,'') ContractIds
                    FROM dbo.DC_DocumentDefinition d WHERE d.IsActive=1 ORDER BY d.Title"),
                ResultTitles = LoadLookup(@"
                    SELECT Id, Title, CONVERT(varchar(10),ResultBehavior)
                    FROM dbo.DC_ResultTitle WHERE IsActive=1 ORDER BY Id"),
                SourceEndpoints = LoadLookup(@"
                    SELECT Id, Title, ProcedureName
                    FROM dbo.DC_IntegrationEndpoint WHERE IsActive=1 AND Direction=1 ORDER BY Title"),
                OutputEndpoints = LoadLookup(@"
                    SELECT Id, Title, ProcedureName
                    FROM dbo.DC_IntegrationEndpoint WHERE IsActive=1 AND Direction=2 ORDER BY Title")
            };

            vm.Contracts = db.tbPeymanContracts
                .Where(x => x.Inactive != true)
                .OrderBy(x => x.pec_ContractNumber).ThenBy(x => x.pec_Title)
                .Select(x => new LookupItem
                {
                    Id = x.pec_ID,
                    Title = (x.pec_ContractNumber ?? "بدون شماره") + " - " + (x.pec_Title ?? ""),
                    Meta = x.pec_BriefTitle
                })
                .ToList();

            vm.Users = db.tbUsers
                .Where(x => x.usr_IsActive == true)
                .OrderBy(x => x.usr_Family).ThenBy(x => x.usr_Name)
                .Select(x => new LookupItem
                {
                    Id = x.usr_ID,
                    Title = (x.usr_Name ?? "") + " " + (x.usr_Family ?? ""),
                    Meta = x.usr_NationalCode
                })
                .ToList();

            return View(vm);
        }

        [HttpGet]
        public ActionResult RoleManagement()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return new HttpStatusCodeResult(401, "دسترسی غیر مجاز");
            //if (!HasModuleRole(currentUserId.Value, "Admin"))
            //    return new HttpStatusCodeResult(403, "فقط مدیر سیستم اجازه مدیریت دسترسی‌ها را دارد.");

            var vm = new ModuleRoleManagementVm { CurrentUserId = currentUserId.Value };
            vm.Users = db.tbUsers
                .Where(x => x.usr_IsActive == true)
                .OrderBy(x => x.usr_Family).ThenBy(x => x.usr_Name)
                .Select(x => new ModuleRoleUserVm
                {
                    UserId = x.usr_ID,
                    FullName = (x.usr_Name ?? "") + " " + (x.usr_Family ?? ""),
                    NationalCode = x.usr_NationalCode
                })
                .ToList();

            var activeRoles = WithConnection(connection =>
            {
                var result = new List<Tuple<int, string>>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"SELECT UserId,RoleCode
                                            FROM dbo.DC_ModuleUser
                                            WHERE IsActive=1 AND RoleCode IN ('Admin','Manager')";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            result.Add(Tuple.Create(Convert.ToInt32(reader[0]), Convert.ToString(reader[1])));
                    }
                }
                return result;
            });

            foreach (var user in vm.Users)
            {
                user.IsAdmin = activeRoles.Any(x => x.Item1 == user.UserId && x.Item2 == "Admin");
                user.IsManager = activeRoles.Any(x => x.Item1 == user.UserId && x.Item2 == "Manager");
            }

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SetModuleUserRole(int targetUserId, string roleCode, bool isActive)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Fail("دسترسی غیر مجاز.");
            //if (!HasModuleRole(currentUserId.Value, "Admin"))
            //    return Fail("فقط مدیر سیستم اجازه مدیریت دسترسی‌ها را دارد.");

            roleCode = (roleCode ?? "").Trim();
            if (!ManageableModuleRoles.Contains(roleCode, StringComparer.Ordinal))
                return Fail("نقش انتخاب‌شده معتبر نیست.");
            if (!db.tbUsers.Any(x => x.usr_ID == targetUserId && x.usr_IsActive == true))
                return Fail("کاربر فعال موردنظر پیدا نشد.");
            if (targetUserId == currentUserId.Value && roleCode == "Admin" && !isActive)
                return Fail("نمی‌توانید دسترسی مدیر خودتان را غیرفعال کنید.");

            try
            {
                WithConnection(connection =>
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            IF EXISTS(SELECT 1 FROM dbo.DC_ModuleUser WHERE UserId=@UserId AND RoleCode=@RoleCode)
                                UPDATE dbo.DC_ModuleUser SET IsActive=@IsActive
                                WHERE UserId=@UserId AND RoleCode=@RoleCode;
                            ELSE IF @IsActive=1
                                INSERT dbo.DC_ModuleUser(UserId,RoleCode,IsActive)
                                VALUES(@UserId,@RoleCode,1);";
                        P(command, "@UserId", targetUserId);
                        P(command, "@RoleCode", roleCode);
                        P(command, "@IsActive", isActive);
                        command.ExecuteNonQuery();
                    }
                    return 0;
                });

                return Json(new
                {
                    success = true,
                    message = isActive ? "دسترسی فعال شد." : "دسترسی غیرفعال شد."
                });
            }
            catch (Exception ex)
            {
                return Fail("خطا در ذخیره دسترسی: " + RootMessage(ex));
            }
        }

        [HttpGet]
        public ActionResult DocumentTypes()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Content("دسترسی غیر مجاز");
            if (!HasModuleRole(currentUserId.Value, "Admin")) return new HttpStatusCodeResult(403, "اجازه تعریف نوع مدرک را ندارید.");

            var vm = new DocumentControlIndexVm
            {
                CanConfigure = true,
                Documents = LoadLookup(@"
                    SELECT d.Id, d.Title, d.AllowedExtensions,
                           STUFF((SELECT ',' + CONVERT(varchar(12),dc.ContractId)
                                  FROM dbo.DC_DocumentContract dc
                                  WHERE dc.DocumentDefinitionId=d.Id
                                  ORDER BY dc.ContractId
                                  FOR XML PATH(''),TYPE).value('.','varchar(max)'),1,1,'') ContractIds
                    FROM dbo.DC_DocumentDefinition d WHERE d.IsActive=1 ORDER BY d.Title")
            };

            vm.Contracts = db.tbPeymanContracts
                .Where(x => x.Inactive != true)
                .OrderBy(x => x.pec_ContractNumber).ThenBy(x => x.pec_Title)
                .Select(x => new LookupItem
                {
                    Id = x.pec_ID,
                    Title = (x.pec_ContractNumber ?? "بدون شماره") + " - " + (x.pec_Title ?? ""),
                    Meta = x.pec_BriefTitle
                })
                .ToList();

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SaveDocumentDefinition(string payload)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");
            if (!HasModuleRole(userId.Value, "Admin")) return Fail("اجازه تنظیم مدارک را ندارید.");

            DocumentDefinitionInput input;
            try { input = serializer.Deserialize<DocumentDefinitionInput>(payload ?? ""); }
            catch { return Fail("اطلاعات تعریف مدرک معتبر نیست."); }

            if (input == null || String.IsNullOrWhiteSpace(input.Title)) return Fail("نام مدرک الزامی است.");
            if (input.ContractIds == null || input.ContractIds.Count == 0) return Fail("حداقل یک قرارداد انتخاب کنید.");
            if (input.MaxFileSizeMb < 1 || input.MaxFileSizeMb > 500) return Fail("حداکثر حجم فایل باید بین ۱ تا ۵۰۰ مگابایت باشد.");

            var extensions = NormalizeExtensions(input.AllowedExtensions);
            if (extensions.Count == 0) return Fail("حداقل یک پسوند مجاز مانند .pdf تعریف کنید.");

            try
            {
                int savedId = 0;
                using (var scope = new TransactionScope())
                {
                    savedId = WithConnection(connection =>
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = input.Id.HasValue
                                ? @"UPDATE dbo.DC_DocumentDefinition SET Title=@Title, Alias=@Alias,
                                    AllowedExtensions=@Extensions, MaxFileSizeMb=@MaxSize,
                                    StatementMonthOffset=@MonthOffset, IsPeriodic=@IsPeriodic,
                                    IsActive=@IsActive, ModifiedAt=SYSDATETIME()
                                    WHERE Id=@Id; SELECT @Id;"
                                : @"INSERT dbo.DC_DocumentDefinition
                                    (Title,Alias,AllowedExtensions,MaxFileSizeMb,StatementMonthOffset,IsPeriodic,IsActive,CreatedBy)
                                    VALUES(@Title,@Alias,@Extensions,@MaxSize,@MonthOffset,@IsPeriodic,@IsActive,@UserId);
                                    SELECT CONVERT(int,SCOPE_IDENTITY());";

                            P(command, "@Title", input.Title.Trim());
                            P(command, "@Alias", NullIfEmpty(input.Alias));
                            P(command, "@Extensions", String.Join(",", extensions));
                            P(command, "@MaxSize", input.MaxFileSizeMb);
                            P(command, "@MonthOffset", input.StatementMonthOffset);
                            P(command, "@IsPeriodic", input.IsPeriodic);
                            P(command, "@IsActive", input.IsActive);
                            P(command, "@UserId", userId.Value);
                            P(command, "@Id", input.Id);
                            return Convert.ToInt32(command.ExecuteScalar());
                        }
                    });

                    WithConnection(connection =>
                    {
                        using (var delete = connection.CreateCommand())
                        {
                            delete.CommandText = "DELETE dbo.DC_DocumentContract WHERE DocumentDefinitionId=@Id";
                            P(delete, "@Id", savedId);
                            delete.ExecuteNonQuery();
                        }

                        foreach (var contractId in input.ContractIds.Distinct())
                        {
                            using (var insert = connection.CreateCommand())
                            {
                                insert.CommandText = @"INSERT dbo.DC_DocumentContract(DocumentDefinitionId,ContractId)
                                                       VALUES(@DocumentId,@ContractId)";
                                P(insert, "@DocumentId", savedId);
                                P(insert, "@ContractId", contractId);
                                insert.ExecuteNonQuery();
                            }
                        }
                        return 0;
                    });

                    scope.Complete();
                }

                return Json(new { success = true, id = savedId, message = "تعریف مدرک ذخیره شد." });
            }
            catch (Exception ex) { return Fail("خطا در ذخیره تعریف مدرک: " + RootMessage(ex)); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SaveResultTitle(string payload)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue) return Fail("دسترسی غیر مجاز.");
            if (!HasModuleRole(currentUserId.Value, "Admin")) return Fail("اجازه تعریف عناوین نتیجه را ندارید.");

            ResultTitleInput input;
            try { input = serializer.Deserialize<ResultTitleInput>(payload ?? ""); }
            catch { return Fail("اطلاعات عنوان نتیجه معتبر نیست."); }

            if (input == null || String.IsNullOrWhiteSpace(input.Title)) return Fail("عنوان نتیجه الزامی است.");
            if (input.ResultBehavior < 1 || input.ResultBehavior > 3) return Fail("رفتار نتیجه معتبر نیست.");

            try
            {
                var id = WithConnection(connection =>
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = input.Id.HasValue
                            ? @"UPDATE dbo.DC_ResultTitle SET Title=@Title, ResultBehavior=@Behavior,
                                ColorCode=@Color, RequiresComment=@RequiresComment,
                                IsFinalApproval=@Final, IsActive=@Active WHERE Id=@Id; SELECT @Id;"
                            : @"INSERT dbo.DC_ResultTitle
                                (Title,ResultBehavior,ColorCode,RequiresComment,IsFinalApproval,IsActive)
                                VALUES(@Title,@Behavior,@Color,@RequiresComment,@Final,@Active);
                                SELECT CONVERT(int,SCOPE_IDENTITY());";

                        P(command, "@Title", input.Title.Trim());
                        P(command, "@Behavior", input.ResultBehavior);
                        P(command, "@Color", NullIfEmpty(input.ColorCode));
                        P(command, "@RequiresComment", input.RequiresComment);
                        P(command, "@Final", input.IsFinalApproval);
                        P(command, "@Active", input.IsActive);
                        P(command, "@Id", input.Id);
                        return Convert.ToInt32(command.ExecuteScalar());
                    }
                });

                return Json(new { success = true, id = id, message = "عنوان نتیجه ذخیره شد." });
            }
            catch (Exception ex) { return Fail("خطا در ذخیره عنوان نتیجه: " + RootMessage(ex)); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SaveWorkflow(string payload)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");
            if (!HasModuleRole(userId.Value, "Admin")) return Fail("اجازه تنظیم گردش پیگیری را ندارید.");

            WorkflowInput input;
            try { input = serializer.Deserialize<WorkflowInput>(payload ?? ""); }
            catch { return Fail("اطلاعات مراحل معتبر نیست."); }

            var validation = ValidateWorkflow(input);
            if (validation != null) return Fail(validation);

            if (input.Id.HasValue && ScalarInt(
                "SELECT COUNT(1) FROM dbo.DC_DocumentInstance WHERE WorkflowId=@Id",
                c => P(c, "@Id", input.Id.Value)) > 0)
                return Fail("این گردش استفاده شده است؛ برای حفظ تاریخچه، یک نسخه جدید از گردش بسازید.");

            try
            {
                int workflowId;
                using (var scope = new TransactionScope())
                {
                    workflowId = WithConnection(connection =>
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = input.Id.HasValue
                                ? @"UPDATE dbo.DC_Workflow SET ContractId=@ContractId,
                                    DocumentDefinitionId=@DocumentId, Alias=@Alias,
                                    SourceEndpointId=@SourceId, OutputEndpointId=@OutputId,
                                    IsActive=@Active, ModifiedAt=SYSDATETIME() WHERE Id=@Id; SELECT @Id;"
                                : @"INSERT dbo.DC_Workflow
                                    (ContractId,DocumentDefinitionId,Alias,SourceEndpointId,OutputEndpointId,IsActive,CreatedBy)
                                    VALUES(@ContractId,@DocumentId,@Alias,@SourceId,@OutputId,@Active,@UserId);
                                    SELECT CONVERT(int,SCOPE_IDENTITY());";

                            P(command, "@ContractId", input.ContractId);
                            P(command, "@DocumentId", input.DocumentDefinitionId);
                            P(command, "@Alias", input.Alias.Trim());
                            P(command, "@SourceId", input.SourceEndpointId);
                            P(command, "@OutputId", input.OutputEndpointId);
                            P(command, "@Active", input.IsActive);
                            P(command, "@UserId", userId.Value);
                            P(command, "@Id", input.Id);
                            return Convert.ToInt32(command.ExecuteScalar());
                        }
                    });

                    WithConnection(connection =>
                    {
                        if (input.Id.HasValue)
                        {
                            using (var delete = connection.CreateCommand())
                            {
                                delete.CommandText = "DELETE dbo.DC_WorkflowStep WHERE WorkflowId=@WorkflowId";
                                P(delete, "@WorkflowId", workflowId);
                                delete.ExecuteNonQuery();
                            }
                        }

                        var orderedSteps = input.Steps.OrderBy(x => x.SortOrder).ToList();
                        for (var index = 0; index < orderedSteps.Count; index++)
                            InsertStep(connection, workflowId, orderedSteps[index], index + 1);

                        return 0;
                    });

                    scope.Complete();
                }

                return Json(new { success = true, id = workflowId, message = "مراحل پیگیری ذخیره شد." });
            }
            catch (Exception ex) { return Fail("خطا در ذخیره مراحل: " + RootMessage(ex)); }
        }

        private void InsertStep(DbConnection connection, int workflowId, WorkflowStepInput step, int order)
        {
            int stepId;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"INSERT dbo.DC_WorkflowStep
                    (WorkflowId,Title,Alias,SortOrder,AllowedHours,IsUploadStep,IsFinalStep)
                    VALUES(@WorkflowId,@Title,@Alias,@Order,@Hours,@Upload,@Final);
                    SELECT CONVERT(int,SCOPE_IDENTITY());";
                P(command, "@WorkflowId", workflowId);
                P(command, "@Title", step.Title.Trim());
                P(command, "@Alias", NullIfEmpty(step.Alias));
                P(command, "@Order", order);
                P(command, "@Hours", step.AllowedHours);
                P(command, "@Upload", step.IsUploadStep);
                P(command, "@Final", step.IsFinalStep);
                stepId = Convert.ToInt32(command.ExecuteScalar());
            }

            InsertStepUsers(connection, stepId, step.ActionUserIds, 1);
            InsertStepUsers(connection, stepId, step.ViewUserIds, 2);

            foreach (var resultId in (step.ResultTitleIds ?? new List<int>()).Distinct())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT dbo.DC_WorkflowStepResult(WorkflowStepId,ResultTitleId)
                                            VALUES(@StepId,@ResultId)";
                    P(command, "@StepId", stepId);
                    P(command, "@ResultId", resultId);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void InsertStepUsers(DbConnection connection, int stepId, IEnumerable<int> ids, byte accessType)
        {
            foreach (var id in (ids ?? Enumerable.Empty<int>()).Distinct())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"INSERT dbo.DC_WorkflowStepUser(WorkflowStepId,UserId,AccessType)
                                            VALUES(@StepId,@UserId,@AccessType)";
                    P(command, "@StepId", stepId);
                    P(command, "@UserId", id);
                    P(command, "@AccessType", accessType);
                    command.ExecuteNonQuery();
                }
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDashboard(int? contractId, string status, short? periodYear, byte? periodMonth, string search)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");
            var canManage = HasModuleRole(userId.Value, "Admin", "Manager");
            var searchTerm = NullIfEmpty(search);
            if (searchTerm != null) searchTerm = "%" + searchTerm.Trim() + "%";

            try
            {
                return WithConnection(connection =>
                {
                    var contracts = new List<object>();
                    var documents = new List<object>();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT c.pec_ID,c.pec_ContractNumber,c.pec_Title,
                                   COUNT(i.Id) TotalCount,
                                   SUM(CASE WHEN i.Status='Approved' THEN 1 ELSE 0 END) ApprovedCount,
                                   SUM(CASE WHEN i.IsOpen=1 THEN 1 ELSE 0 END) OpenCount,
                                   SUM(CASE WHEN i.IsOpen=1 AND i.DueAt<SYSDATETIME() THEN 1 ELSE 0 END) LateCount
                            FROM [Contract].[tbPeymanContracts] c
                            LEFT JOIN dbo.DC_DocumentInstance i ON i.ContractId=c.pec_ID
                            WHERE ISNULL(c.Inactive,0)=0 AND (@ContractId IS NULL OR c.pec_ID=@ContractId)
                              AND (@Search IS NULL OR c.pec_ContractNumber LIKE @Search OR c.pec_Title LIKE @Search
                                   OR EXISTS(SELECT 1 FROM dbo.DC_DocumentInstance si
                                             JOIN dbo.DC_DocumentDefinition sd ON sd.Id=si.DocumentDefinitionId
                                             WHERE si.ContractId=c.pec_ID
                                               AND (sd.Title LIKE @Search OR si.StatementNo LIKE @Search)))
                              AND (@CanManage=1 OR EXISTS(
                                  SELECT 1 FROM dbo.DC_Workflow w
                                  JOIN dbo.DC_WorkflowStep s ON s.WorkflowId=w.Id
                                  JOIN dbo.DC_WorkflowStepUser su ON su.WorkflowStepId=s.Id
                                  WHERE w.ContractId=c.pec_ID AND su.UserId=@UserId))
                            GROUP BY c.pec_ID,c.pec_ContractNumber,c.pec_Title ORDER BY c.pec_ContractNumber";
                        P(command, "@ContractId", contractId);
                        P(command, "@Search", searchTerm);
                        P(command, "@CanManage", canManage);
                        P(command, "@UserId", userId.Value);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                                contracts.Add(new
                                {
                                    id = Convert.ToInt32(reader[0]),
                                    contractNo = Convert.ToString(reader[1]),
                                    title = Convert.ToString(reader[2]),
                                    total = DbInt(reader[3]), approved = DbInt(reader[4]),
                                    open = DbInt(reader[5]), late = DbInt(reader[6])
                                });
                        }
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT i.Id,c.pec_ContractNumber,d.Title,i.StatementNo,i.PeriodYear,i.PeriodMonth,
                                   i.Status,s.Title StepTitle,i.DueAt,i.CreatedAt,
                                   CASE WHEN i.IsOpen=1 AND i.DueAt<SYSDATETIME() THEN DATEDIFF(day,i.DueAt,SYSDATETIME()) ELSE 0 END LateDays,
                                   CASE WHEN EXISTS(SELECT 1 FROM dbo.DC_WorkflowStepUser su WHERE su.WorkflowStepId=i.CurrentStepId AND su.UserId=@UserId AND su.AccessType=1) THEN 1 ELSE 0 END CanAct
                            FROM dbo.DC_DocumentInstance i
                            JOIN [Contract].[tbPeymanContracts] c ON c.pec_ID=i.ContractId
                            JOIN dbo.DC_DocumentDefinition d ON d.Id=i.DocumentDefinitionId
                            LEFT JOIN dbo.DC_WorkflowStep s ON s.Id=i.CurrentStepId
                            WHERE (@ContractId IS NULL OR i.ContractId=@ContractId)
                              AND (@Status IS NULL OR i.Status=@Status)
                              AND (@Year IS NULL OR i.PeriodYear=@Year)
                              AND (@Month IS NULL OR i.PeriodMonth=@Month)
                              AND (@Search IS NULL OR c.pec_ContractNumber LIKE @Search OR c.pec_Title LIKE @Search
                                   OR d.Title LIKE @Search OR i.StatementNo LIKE @Search)
                              AND (@CanManage=1 OR EXISTS(SELECT 1 FROM dbo.DC_WorkflowStepUser su
                                   JOIN dbo.DC_WorkflowStep sx ON sx.Id=su.WorkflowStepId
                                   WHERE sx.WorkflowId=i.WorkflowId AND su.UserId=@UserId) OR i.CreatedBy=@UserId)
                            ORDER BY i.CreatedAt DESC";
                        P(command, "@ContractId", contractId);
                        P(command, "@Status", NullIfEmpty(status));
                        P(command, "@Year", periodYear);
                        P(command, "@Month", periodMonth);
                        P(command, "@Search", searchTerm);
                        P(command, "@UserId", userId.Value);
                        P(command, "@CanManage", canManage);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                                documents.Add(new
                                {
                                    id = Convert.ToInt64(reader[0]), contractNo = Convert.ToString(reader[1]),
                                    documentTitle = Convert.ToString(reader[2]), statementNo = Convert.ToString(reader[3]),
                                    period = Convert.ToString(reader[4]) + "/" + Convert.ToInt32(reader[5]).ToString("00"),
                                    status = Convert.ToString(reader[6]), stepTitle = Convert.ToString(reader[7]),
                                    dueAt = DbDateText(reader[8]), createdAt = DbDateText(reader[9]),
                                    lateDays = DbInt(reader[10]), canAct = Convert.ToBoolean(reader[11])
                                });
                        }
                    }

                    return Json(new { success = true, contracts = contracts, documents = documents });
                });
            }
            catch (Exception ex) { return Fail("خطا در دریافت میز مدیریت: " + RootMessage(ex)); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetDocumentHistory(long id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");

            try
            {
                return WithConnection(connection =>
                {
                    if (!CanViewInstance(connection, id, userId.Value)) return Fail("اجازه مشاهده این مدرک را ندارید.");

                    object header = null;
                    var steps = new List<object>();
                    var actions = new List<object>();
                    var files = new List<object>();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT i.Id,c.pec_ContractNumber,c.pec_Title,d.Title,i.StatementNo,
                            i.PeriodYear,i.PeriodMonth,i.Status,i.ApprovedTotal,i.DueAt
                            FROM dbo.DC_DocumentInstance i
                            JOIN [Contract].[tbPeymanContracts] c ON c.pec_ID=i.ContractId
                            JOIN dbo.DC_DocumentDefinition d ON d.Id=i.DocumentDefinitionId WHERE i.Id=@Id";
                        P(command, "@Id", id);
                        using (var reader = command.ExecuteReader())
                            if (reader.Read()) header = new
                            {
                                id = Convert.ToInt64(reader[0]), contractNo = Convert.ToString(reader[1]),
                                contractTitle = Convert.ToString(reader[2]), documentTitle = Convert.ToString(reader[3]),
                                statementNo = Convert.ToString(reader[4]),
                                period = Convert.ToString(reader[5]) + "/" + Convert.ToInt32(reader[6]).ToString("00"),
                                status = Convert.ToString(reader[7]), approvedTotal = reader[8] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader[8]),
                                dueAt = DbDateText(reader[9])
                            };
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT s.Id,s.Title,s.SortOrder,ISNULL(st.Status,'Pending'),st.StartedAt,st.DueAt,st.CompletedAt,ISNULL(st.ReturnCount,0)
                            FROM dbo.DC_DocumentInstance i JOIN dbo.DC_WorkflowStep s ON s.WorkflowId=i.WorkflowId
                            LEFT JOIN dbo.DC_DocumentStepState st ON st.DocumentInstanceId=i.Id AND st.WorkflowStepId=s.Id
                            WHERE i.Id=@Id ORDER BY s.SortOrder";
                        P(command, "@Id", id);
                        using (var reader = command.ExecuteReader())
                            while (reader.Read()) steps.Add(new
                            {
                                id = Convert.ToInt32(reader[0]), title = Convert.ToString(reader[1]), order = Convert.ToInt32(reader[2]),
                                status = Convert.ToString(reader[3]), startedAt = DbDateText(reader[4]), dueAt = DbDateText(reader[5]),
                                completedAt = DbDateText(reader[6]), returnCount = DbInt(reader[7])
                            });
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT a.Id,a.ActionType,s.Title,r.Title,a.Comment,a.ApprovedTotal,
                            a.DiscrepanciesJson,a.FromStatus,a.ToStatus,a.PerformedBy,a.PerformedAt
                            FROM dbo.DC_DocumentAction a
                            LEFT JOIN dbo.DC_WorkflowStep s ON s.Id=a.WorkflowStepId
                            LEFT JOIN dbo.DC_ResultTitle r ON r.Id=a.ResultTitleId
                            WHERE a.DocumentInstanceId=@Id ORDER BY a.PerformedAt DESC,a.Id DESC";
                        P(command, "@Id", id);
                        using (var reader = command.ExecuteReader())
                            while (reader.Read()) actions.Add(new
                            {
                                id = Convert.ToInt64(reader[0]), actionType = Convert.ToString(reader[1]), step = Convert.ToString(reader[2]),
                                result = Convert.ToString(reader[3]), comment = Convert.ToString(reader[4]),
                                approvedTotal = reader[5] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader[5]),
                                discrepanciesJson = Convert.ToString(reader[6]), fromStatus = Convert.ToString(reader[7]),
                                toStatus = Convert.ToString(reader[8]), userId = Convert.ToInt32(reader[9]),
                                at = DbDateText(reader[10])
                            });
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT Id,OriginalName,Extension,FileSize,UploadedAt,IsCurrent
                                                FROM dbo.DC_DocumentFile WHERE DocumentInstanceId=@Id ORDER BY UploadedAt DESC";
                        P(command, "@Id", id);
                        using (var reader = command.ExecuteReader())
                            while (reader.Read()) files.Add(new
                            {
                                id = Convert.ToInt64(reader[0]), name = Convert.ToString(reader[1]), extension = Convert.ToString(reader[2]),
                                size = Convert.ToInt64(reader[3]), at = DbDateText(reader[4]), current = Convert.ToBoolean(reader[5])
                            });
                    }

                    return Json(new { success = true, header = header, steps = steps, actions = actions, files = files });
                });
            }
            catch (Exception ex) { return Fail("خطا در دریافت تاریخچه: " + RootMessage(ex)); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult GetSourceData(long id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");

            try
            {
                return WithConnection(connection =>
                {
                    if (!CanViewInstance(connection, id, userId.Value)) return Fail("اجازه مشاهده این مدرک را ندارید.");

                    int contractId;
                    string procedure;
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT i.ContractId,e.ProcedureName
                            FROM dbo.DC_DocumentInstance i
                            JOIN dbo.DC_Workflow w ON w.Id=i.WorkflowId
                            JOIN dbo.DC_IntegrationEndpoint e ON e.Id=w.SourceEndpointId AND e.Direction=1 AND e.IsActive=1
                            WHERE i.Id=@Id";
                        P(command, "@Id", id);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read()) return Fail("نقطه دریافت اطلاعات برای این گردش تعریف نشده است.");
                            contractId = Convert.ToInt32(reader[0]);
                            procedure = Convert.ToString(reader[1]);
                        }
                    }

                    var columns = new List<string>();
                    var rows = new List<Dictionary<string, object>>();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "EXEC " + SafeProcedureName(procedure) + " @ContractId,@DocumentInstanceId";
                        P(command, "@ContractId", contractId);
                        P(command, "@DocumentInstanceId", id);
                        using (var reader = command.ExecuteReader())
                        {
                            for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
                            while (reader.Read())
                            {
                                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                for (var i = 0; i < reader.FieldCount; i++)
                                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                rows.Add(row);
                            }
                        }
                    }

                    return Json(new { success = true, columns = columns, rows = rows });
                });
            }
            catch (Exception ex) { return Fail("خطا در دریافت اطلاعات متناظر: " + RootMessage(ex)); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult RegisterDocument(string payload, HttpPostedFileBase file)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");

            RegisterDocumentInput input;
            try { input = serializer.Deserialize<RegisterDocumentInput>(payload ?? ""); }
            catch { return Fail("اطلاعات ثبت مدرک معتبر نیست."); }

            if (input == null || input.ContractId <= 0 || input.DocumentDefinitionId <= 0) return Fail("قرارداد و نوع مدرک الزامی است.");
            if (input.PeriodYear < 1300 || input.PeriodYear > 1600 || input.PeriodMonth < 1 || input.PeriodMonth > 12) return Fail("دوره مدرک معتبر نیست.");
            if (file == null || file.ContentLength <= 0) return Fail("فایل مدرک انتخاب نشده است.");

            string savedFilePath = null;
            try
            {
                return WithConnection(connection =>
                {
                    int workflowId, firstStepId, hours, maxMb;
                    string allowed;

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT TOP(1) w.Id,s.Id,s.AllowedHours,d.AllowedExtensions,d.MaxFileSizeMb
                            FROM dbo.DC_Workflow w
                            JOIN dbo.DC_DocumentContract dc ON dc.DocumentDefinitionId=w.DocumentDefinitionId AND dc.ContractId=w.ContractId
                            JOIN dbo.DC_DocumentDefinition d ON d.Id=w.DocumentDefinitionId
                            JOIN dbo.DC_WorkflowStep s ON s.WorkflowId=w.Id
                            WHERE w.ContractId=@ContractId AND w.DocumentDefinitionId=@DocumentId AND w.IsActive=1
                              AND EXISTS(SELECT 1 FROM dbo.DC_WorkflowStepUser su
                                  WHERE su.WorkflowStepId=s.Id AND su.UserId=@UserId AND su.AccessType=1)
                            ORDER BY s.SortOrder";
                        P(command, "@ContractId", input.ContractId);
                        P(command, "@DocumentId", input.DocumentDefinitionId);
                        P(command, "@UserId", userId.Value);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read()) return Fail("برای این قرارداد و مدرک، گردش فعال تعریف نشده است.");
                            workflowId = Convert.ToInt32(reader[0]); firstStepId = Convert.ToInt32(reader[1]);
                            hours = Convert.ToInt32(reader[2]); allowed = Convert.ToString(reader[3]); maxMb = Convert.ToInt32(reader[4]);
                        }
                    }

                    using (var duplicate = connection.CreateCommand())
                    {
                        duplicate.CommandText = @"SELECT COUNT(1) FROM dbo.DC_DocumentInstance
                            WHERE ContractId=@ContractId AND DocumentDefinitionId=@DocumentId
                              AND PeriodYear=@Year AND PeriodMonth=@Month";
                        P(duplicate, "@ContractId", input.ContractId);
                        P(duplicate, "@DocumentId", input.DocumentDefinitionId);
                        P(duplicate, "@Year", input.PeriodYear);
                        P(duplicate, "@Month", input.PeriodMonth);
                        if (Convert.ToInt32(duplicate.ExecuteScalar()) > 0)
                            return Fail("این مدرک برای قرارداد و دوره انتخاب‌شده قبلاً ثبت شده است.");
                    }

                    var extension = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
                    if (!NormalizeExtensions(allowed).Contains(extension)) return Fail("نوع فایل مجاز نیست. پسوندهای مجاز: " + allowed);
                    if (file.ContentLength > maxMb * 1024L * 1024L) return Fail("حجم فایل از " + maxMb + " مگابایت بیشتر است.");

                    var storedName = Guid.NewGuid().ToString("N") + extension;
                    var directory = Server.MapPath("~/App_Data/DocumentControl");
                    Directory.CreateDirectory(directory);
                    var fullPath = Path.Combine(directory, storedName);
                    savedFilePath = fullPath;
                    long instanceId = 0;

                    using (var scope = new TransactionScope())
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"INSERT dbo.DC_DocumentInstance
                                (ContractId,DocumentDefinitionId,WorkflowId,StatementNo,PeriodYear,PeriodMonth,CurrentStepId,Status,DueAt,CreatedBy)
                                VALUES(@ContractId,@DocumentId,@WorkflowId,@StatementNo,@Year,@Month,@StepId,'InProgress',DATEADD(hour,@Hours,SYSDATETIME()),@UserId);
                                SELECT CONVERT(bigint,SCOPE_IDENTITY());";
                            P(command, "@ContractId", input.ContractId); P(command, "@DocumentId", input.DocumentDefinitionId);
                            P(command, "@WorkflowId", workflowId); P(command, "@StatementNo", NullIfEmpty(input.StatementNo));
                            P(command, "@Year", input.PeriodYear); P(command, "@Month", input.PeriodMonth);
                            P(command, "@StepId", firstStepId); P(command, "@Hours", hours); P(command, "@UserId", userId.Value);
                            instanceId = Convert.ToInt64(command.ExecuteScalar());
                        }

                        file.SaveAs(fullPath);
                        var sha = FileSha256(fullPath);

                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"INSERT dbo.DC_DocumentFile
                                (DocumentInstanceId,OriginalName,StoredName,Extension,ContentType,FileSize,Sha256,UploadedBy)
                                VALUES(@InstanceId,@Original,@Stored,@Ext,@ContentType,@Size,@Sha,@UserId);
                                INSERT dbo.DC_DocumentStepState(DocumentInstanceId,WorkflowStepId,Status,StartedAt,DueAt)
                                VALUES(@InstanceId,@StepId,'Active',SYSDATETIME(),DATEADD(hour,@Hours,SYSDATETIME()));
                                INSERT dbo.DC_DocumentAction(DocumentInstanceId,WorkflowStepId,ActionType,FromStatus,ToStatus,PerformedBy)
                                VALUES(@InstanceId,@StepId,'Register',NULL,'InProgress',@UserId);";
                            P(command, "@InstanceId", instanceId); P(command, "@Original", Path.GetFileName(file.FileName));
                            P(command, "@Stored", storedName); P(command, "@Ext", extension); P(command, "@ContentType", file.ContentType);
                            P(command, "@Size", file.ContentLength); P(command, "@Sha", sha); P(command, "@UserId", userId.Value);
                            P(command, "@StepId", firstStepId); P(command, "@Hours", hours);
                            command.ExecuteNonQuery();
                        }

                        scope.Complete();
                    }

                    return Json(new { success = true, id = instanceId, message = "مدرک ثبت و گردش پیگیری آغاز شد." });
                });
            }
            catch (Exception ex)
            {
                if (!String.IsNullOrWhiteSpace(savedFilePath) && System.IO.File.Exists(savedFilePath))
                {
                    try { System.IO.File.Delete(savedFilePath); }
                    catch { /* پاک‌سازی فایل یتیم نباید خطای اصلی را مخفی کند. */ }
                }
                return Fail("خطا در ثبت مدرک: " + RootMessage(ex));
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public JsonResult SubmitStepResult(string payload)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return Fail("دسترسی غیر مجاز.");

            StepResultInput input;
            try { input = serializer.Deserialize<StepResultInput>(payload ?? ""); }
            catch { return Fail("نتیجه ارسالی معتبر نیست."); }

            if (input == null || input.DocumentInstanceId <= 0 || input.ResultTitleId <= 0) return Fail("مدرک و نتیجه الزامی است.");

            try
            {
                using (var scope = new TransactionScope())
                {
                    var response = WithConnection(connection => ApplyStepResult(connection, input, userId.Value));
                    if (response is JsonResult && ((JsonResult)response).Data != null)
                    {
                        // Fail نیز JsonResult است؛ تکمیل Transaction فقط در پاسخ موفق داخل Apply انجام نمی‌شود.
                    }
                    scope.Complete();
                    return response;
                }
            }
            catch (Exception ex) { return Fail("خطا در ثبت نتیجه: " + RootMessage(ex)); }
        }

        private JsonResult ApplyStepResult(DbConnection connection, StepResultInput input, int userId)
        {
            int workflowId, currentStepId, currentOrder;
            string oldStatus;
            byte behavior;
            bool requiresComment, finalApproval;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT i.WorkflowId,i.CurrentStepId,s.SortOrder,i.Status,
                    r.ResultBehavior,r.RequiresComment,r.IsFinalApproval
                    FROM dbo.DC_DocumentInstance i
                    JOIN dbo.DC_WorkflowStep s ON s.Id=i.CurrentStepId
                    JOIN dbo.DC_WorkflowStepResult sr ON sr.WorkflowStepId=s.Id AND sr.ResultTitleId=@ResultId
                    JOIN dbo.DC_ResultTitle r ON r.Id=sr.ResultTitleId AND r.IsActive=1
                    WHERE i.Id=@Id AND i.IsOpen=1
                      AND EXISTS(SELECT 1 FROM dbo.DC_WorkflowStepUser su WHERE su.WorkflowStepId=i.CurrentStepId AND su.UserId=@UserId AND su.AccessType=1)";
                P(command, "@ResultId", input.ResultTitleId); P(command, "@Id", input.DocumentInstanceId); P(command, "@UserId", userId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return Fail("مرحله، نتیجه یا دسترسی اقدام معتبر نیست.");
                    workflowId = Convert.ToInt32(reader[0]); currentStepId = Convert.ToInt32(reader[1]);
                    currentOrder = Convert.ToInt32(reader[2]); oldStatus = Convert.ToString(reader[3]);
                    behavior = Convert.ToByte(reader[4]); requiresComment = Convert.ToBoolean(reader[5]); finalApproval = Convert.ToBoolean(reader[6]);
                }
            }

            if (requiresComment && String.IsNullOrWhiteSpace(input.Comment)) return Fail("برای این نتیجه، ثبت توضیح الزامی است.");

            int? targetStepId = null;
            int targetHours = 0;
            if (behavior == 1) LoadTargetStep(connection, workflowId, currentOrder, true, out targetStepId, out targetHours);
            else if (behavior == 3) LoadTargetStep(connection, workflowId, currentOrder, false, out targetStepId, out targetHours);
            else { targetStepId = currentStepId; LoadStepHours(connection, currentStepId, out targetHours); }

            // عودت از مرحله اول نباید CurrentStepId را null کند؛ در همان مرحله می‌ماند.
            if (behavior == 3 && !targetStepId.HasValue)
            {
                targetStepId = currentStepId;
                LoadStepHours(connection, currentStepId, out targetHours);
            }

            var closes = behavior == 1 && !targetStepId.HasValue;
            var newStatus = closes ? (finalApproval ? "Approved" : "ConditionallyApproved")
                : behavior == 3 ? "Rejected"
                : behavior == 2 ? "ConditionallyApproved" : "InProgress";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"UPDATE dbo.DC_DocumentStepState SET Status=@StepStatus,
                    CompletedAt=CASE WHEN @Behavior=1 THEN SYSDATETIME() ELSE CompletedAt END,
                    ReturnCount=ReturnCount+CASE WHEN @Behavior=3 THEN 1 ELSE 0 END
                    WHERE DocumentInstanceId=@Id AND WorkflowStepId=@CurrentStepId;

                    UPDATE dbo.DC_DocumentInstance SET CurrentStepId=@TargetStepId,Status=@NewStatus,
                    ApprovedTotal=COALESCE(@ApprovedTotal,ApprovedTotal),DueAt=CASE WHEN @TargetStepId IS NULL THEN NULL ELSE DATEADD(hour,@Hours,SYSDATETIME()) END,
                    IsOpen=CASE WHEN @Close=1 THEN 0 ELSE 1 END,ClosedAt=CASE WHEN @Close=1 THEN SYSDATETIME() ELSE NULL END WHERE Id=@Id;

                    INSERT dbo.DC_DocumentAction(DocumentInstanceId,WorkflowStepId,ActionType,ResultTitleId,Comment,
                    ApprovedTotal,DiscrepanciesJson,FromStatus,ToStatus,PerformedBy)
                    VALUES(@Id,@CurrentStepId,'StepResult',@ResultId,@Comment,@ApprovedTotal,@Discrepancies,@OldStatus,@NewStatus,@UserId);";
                P(command, "@StepStatus", behavior == 1 ? "Completed" : behavior == 3 ? "Returned" : "NeedsCorrection");
                P(command, "@Behavior", behavior); P(command, "@Id", input.DocumentInstanceId);
                P(command, "@CurrentStepId", currentStepId); P(command, "@TargetStepId", targetStepId);
                P(command, "@NewStatus", newStatus); P(command, "@ApprovedTotal", input.ApprovedTotal);
                P(command, "@Hours", targetHours); P(command, "@Close", closes); P(command, "@ResultId", input.ResultTitleId);
                P(command, "@Comment", NullIfEmpty(input.Comment)); P(command, "@Discrepancies", NullIfEmpty(input.DiscrepanciesJson));
                P(command, "@OldStatus", oldStatus); P(command, "@UserId", userId);
                command.ExecuteNonQuery();
            }

            if (targetStepId.HasValue && targetStepId.Value != currentStepId)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"MERGE dbo.DC_DocumentStepState AS t
                        USING(SELECT @Id DocumentInstanceId,@StepId WorkflowStepId) s
                        ON t.DocumentInstanceId=s.DocumentInstanceId AND t.WorkflowStepId=s.WorkflowStepId
                        WHEN MATCHED THEN UPDATE SET Status='Active',StartedAt=SYSDATETIME(),DueAt=DATEADD(hour,@Hours,SYSDATETIME()),CompletedAt=NULL
                        WHEN NOT MATCHED THEN INSERT(DocumentInstanceId,WorkflowStepId,Status,StartedAt,DueAt)
                        VALUES(@Id,@StepId,'Active',SYSDATETIME(),DATEADD(hour,@Hours,SYSDATETIME()));";
                    P(command, "@Id", input.DocumentInstanceId); P(command, "@StepId", targetStepId.Value); P(command, "@Hours", targetHours);
                    command.ExecuteNonQuery();
                }
            }

            if (closes && finalApproval) ExecuteApprovedOutput(connection, workflowId, input, userId);

            return Json(new { success = true, status = newStatus, closed = closes, message = "نتیجه مرحله ثبت شد." });
        }

        [HttpGet]
        public ActionResult DownloadFile(long id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue) return new HttpStatusCodeResult(401);

            return WithConnection<ActionResult>(connection =>
            {
                long instanceId;
                string original, stored, contentType;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT DocumentInstanceId,OriginalName,StoredName,ContentType FROM dbo.DC_DocumentFile WHERE Id=@Id";
                    P(command, "@Id", id);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) return HttpNotFound();
                        instanceId = Convert.ToInt64(reader[0]); original = Convert.ToString(reader[1]);
                        stored = Convert.ToString(reader[2]); contentType = Convert.ToString(reader[3]);
                    }
                }

                if (!CanViewInstance(connection, instanceId, userId.Value)) return new HttpStatusCodeResult(403);
                var path = Server.MapPath("~/App_Data/DocumentControl/" + stored);
                if (!System.IO.File.Exists(path)) return HttpNotFound();
                return File(path, String.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, original);
            });
        }

        private bool CanViewInstance(DbConnection connection, long id, int userId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT COUNT(1) FROM dbo.DC_DocumentInstance i
                    WHERE i.Id=@Id AND (i.CreatedBy=@UserId OR EXISTS(
                    SELECT 1 FROM dbo.DC_WorkflowStepUser su
                    JOIN dbo.DC_WorkflowStep s ON s.Id=su.WorkflowStepId
                    WHERE s.WorkflowId=i.WorkflowId AND su.UserId=@UserId))";
                P(command, "@Id", id); P(command, "@UserId", userId);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private void LoadTargetStep(DbConnection connection, int workflowId, int currentOrder, bool next, out int? stepId, out int hours)
        {
            stepId = null; hours = 0;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = next
                    ? "SELECT TOP(1) Id,AllowedHours FROM dbo.DC_WorkflowStep WHERE WorkflowId=@WorkflowId AND SortOrder>@Order ORDER BY SortOrder"
                    : "SELECT TOP(1) Id,AllowedHours FROM dbo.DC_WorkflowStep WHERE WorkflowId=@WorkflowId AND SortOrder<@Order ORDER BY SortOrder DESC";
                P(command, "@WorkflowId", workflowId); P(command, "@Order", currentOrder);
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) { stepId = Convert.ToInt32(reader[0]); hours = Convert.ToInt32(reader[1]); }
            }
        }

        private void LoadStepHours(DbConnection connection, int stepId, out int hours)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT AllowedHours FROM dbo.DC_WorkflowStep WHERE Id=@Id";
                P(command, "@Id", stepId); hours = Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private void ExecuteApprovedOutput(DbConnection connection, int workflowId, StepResultInput input, int userId)
        {
            string procedure = null;
            int contractId = 0;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT e.ProcedureName,i.ContractId FROM dbo.DC_Workflow w
                    JOIN dbo.DC_DocumentInstance i ON i.WorkflowId=w.Id AND i.Id=@InstanceId
                    JOIN dbo.DC_IntegrationEndpoint e ON e.Id=w.OutputEndpointId AND e.Direction=2 AND e.IsActive=1
                    WHERE w.Id=@Id";
                P(command, "@Id", workflowId);
                P(command, "@InstanceId", input.DocumentInstanceId);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        procedure = Convert.ToString(reader[0]);
                        contractId = Convert.ToInt32(reader[1]);
                    }
                }
            }
            if (String.IsNullOrWhiteSpace(procedure)) return;

            var safeName = SafeProcedureName(procedure);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "EXEC " + safeName
                    + " @ContractId=@ContractId,@DocumentInstanceId=@DocumentInstanceId,"
                    + "@ApprovedTotal=@ApprovedTotal,@DiscrepanciesJson=@DiscrepanciesJson,@UserId=@UserId";
                P(command, "@ContractId", contractId);
                P(command, "@DocumentInstanceId", input.DocumentInstanceId); P(command, "@ApprovedTotal", input.ApprovedTotal);
                P(command, "@DiscrepanciesJson", NullIfEmpty(input.DiscrepanciesJson)); P(command, "@UserId", userId);
                command.ExecuteNonQuery();
            }
        }

        private string SafeProcedureName(string value)
        {
            var parts = (value ?? "").Split('.');
            if (parts.Length < 1 || parts.Length > 2 || parts.Any(x => !Regex.IsMatch(x, @"^[A-Za-z0-9_]+$")))
                throw new InvalidOperationException("نام Stored Procedure اتصال معتبر نیست.");
            return String.Join(".", parts.Select(x => "[" + x + "]"));
        }

        private string ValidateWorkflow(WorkflowInput input)
        {
            if (input == null) return "اطلاعات گردش ارسال نشده است.";
            if (input.ContractId <= 0 || input.DocumentDefinitionId <= 0) return "قرارداد و مدرک الزامی است.";
            if (String.IsNullOrWhiteSpace(input.Alias)) return "نام مستعار گردش الزامی است.";
            if (input.Steps == null || input.Steps.Count == 0) return "حداقل یک مرحله تعریف کنید.";
            if (input.Steps.Any(x => String.IsNullOrWhiteSpace(x.Title))) return "نام تمام مراحل الزامی است.";
            if (input.Steps.Any(x => x.AllowedHours <= 0)) return "مهلت هر مرحله باید بیشتر از صفر باشد.";
            if (input.Steps.Any(x => x.ActionUserIds == null || x.ActionUserIds.Count == 0)) return "برای هر مرحله حداقل یک اقدام‌کننده انتخاب کنید.";
            if (input.Steps.Any(x => x.ResultTitleIds == null || x.ResultTitleIds.Count == 0)) return "برای هر مرحله حداقل یک نتیجه مجاز انتخاب کنید.";
            if (input.Steps.Count(x => x.IsFinalStep) != 1) return "دقیقاً یک مرحله باید مرحله نهایی باشد.";
            if (!input.Steps.OrderBy(x => x.SortOrder).Last().IsFinalStep) return "مرحله نهایی باید آخرین مرحله باشد.";
            return null;
        }

        private List<string> NormalizeExtensions(string value)
        {
            return (value ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant())
                .Select(x => x.StartsWith(".") ? x : "." + x)
                .Where(x => Regex.IsMatch(x, @"^\.[a-z0-9]{1,10}$"))
                .Distinct().ToList();
        }

        private string FileSha256(string path)
        {
            using (var stream = System.IO.File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string NullIfEmpty(string value) { return String.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        private static int DbInt(object value) { return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value); }
        private static string DbDateText(object value) { return value == null || value == DBNull.Value ? "" : Convert.ToDateTime(value).ToString("yyyy/MM/dd HH:mm"); }
        private static string RootMessage(Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; return ex.Message; }
    }
}
