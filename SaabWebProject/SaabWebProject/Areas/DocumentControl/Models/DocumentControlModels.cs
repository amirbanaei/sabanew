using System;
using System.Collections.Generic;

namespace SaabWebProject.Areas.DocumentControl.Models
{
    public sealed class LookupItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Meta { get; set; }
        public string RelationIds { get; set; }
    }

    public sealed class DocumentControlIndexVm
    {
        public bool CanConfigure { get; set; }
        public bool CanManage { get; set; }
        public IList<LookupItem> Contracts { get; set; }
        public IList<LookupItem> Documents { get; set; }
        public IList<LookupItem> ResultTitles { get; set; }
        public IList<LookupItem> Users { get; set; }
        public IList<LookupItem> SourceEndpoints { get; set; }
        public IList<LookupItem> OutputEndpoints { get; set; }

        public DocumentControlIndexVm()
        {
            Contracts = new List<LookupItem>();
            Documents = new List<LookupItem>();
            ResultTitles = new List<LookupItem>();
            Users = new List<LookupItem>();
            SourceEndpoints = new List<LookupItem>();
            OutputEndpoints = new List<LookupItem>();
        }
    }

    public sealed class ModuleRoleManagementVm
    {
        public int CurrentUserId { get; set; }
        public IList<ModuleRoleUserVm> Users { get; set; }

        public ModuleRoleManagementVm()
        {
            Users = new List<ModuleRoleUserVm>();
        }
    }

    public sealed class ModuleRoleUserVm
    {
        public int UserId { get; set; }
        public string FullName { get; set; }
        public string NationalCode { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public sealed class DocumentDefinitionInput
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string Alias { get; set; }
        public string AllowedExtensions { get; set; }
        public int MaxFileSizeMb { get; set; }
        public short StatementMonthOffset { get; set; }
        public bool IsPeriodic { get; set; }
        public bool IsActive { get; set; }
        public List<int> ContractIds { get; set; }
    }

    public sealed class ResultTitleInput
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public byte ResultBehavior { get; set; }
        public string ColorCode { get; set; }
        public bool RequiresComment { get; set; }
        public bool IsFinalApproval { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class WorkflowInput
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int DocumentDefinitionId { get; set; }
        public string Alias { get; set; }
        public int? SourceEndpointId { get; set; }
        public int? OutputEndpointId { get; set; }
        public bool IsActive { get; set; }
        public List<WorkflowStepInput> Steps { get; set; }
    }

    public sealed class WorkflowStepInput
    {
        public string ClientKey { get; set; }
        public string Title { get; set; }
        public string Alias { get; set; }
        public int SortOrder { get; set; }
        public int AllowedHours { get; set; }
        public bool IsUploadStep { get; set; }
        public bool IsFinalStep { get; set; }
        public List<int> ActionUserIds { get; set; }
        public List<int> ViewUserIds { get; set; }
        public List<int> ResultTitleIds { get; set; }
    }

    public sealed class RegisterDocumentInput
    {
        public int ContractId { get; set; }
        public int DocumentDefinitionId { get; set; }
        public string StatementNo { get; set; }
        public short PeriodYear { get; set; }
        public byte PeriodMonth { get; set; }
    }

    public sealed class StepResultInput
    {
        public long DocumentInstanceId { get; set; }
        public int ResultTitleId { get; set; }
        public string Comment { get; set; }
        public decimal? ApprovedTotal { get; set; }
        public string DiscrepanciesJson { get; set; }
    }

    public sealed class DashboardFilter
    {
        public int? ContractId { get; set; }
        public string Status { get; set; }
        public short? PeriodYear { get; set; }
        public byte? PeriodMonth { get; set; }
    }
}
