using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SaabWebProject.Models.ViewModels.Setting
{
    public class ReferralOptionModel
    {
        public long Id { get; set; }
        public string Title { get; set; }
    }

    public class ReferralFieldOptionModel : ReferralOptionModel
    {
        public long? ParentId { get; set; }
        public string ParentTitle { get; set; }
    }

    public class ReferralReportUniqueFieldModel : ReferralOptionModel
    {
        public long ReportId { get; set; }
    }

    public class ReferralIndicatorOptionModel
    {
        public int IndicatorId { get; set; }
        public string Title { get; set; }
        public string Unit { get; set; }
    }

    public class ReferralBaseFieldRowModel
    {
        public int BaseFieldId { get; set; }
        public long ReportId { get; set; }
        public string ReportTitle { get; set; }
        public long FieldId { get; set; }
        public string FieldTitle { get; set; }
    }

    public class ReferralPriorityRowModel
    {
        public long FieldId { get; set; }
        public string FieldTitle { get; set; }
        public long? SubsetId { get; set; }
        public string SubsetTitle { get; set; }
        public decimal? MinimumAmount { get; set; }
        public decimal? MaximumAmount { get; set; }
        public IDictionary<int, decimal> Weights { get; set; } = new Dictionary<int, decimal>();
    }

    public class ReferralListRowModel
    {
        public int ListId { get; set; }
        public string Title { get; set; }
        public long ReportId { get; set; }
        public string ReportTitle { get; set; }
        public long KeyFieldId { get; set; }
        public string KeyFieldTitle { get; set; }
        public int DailyAmount { get; set; }
        public int DurationDays { get; set; }
        public string RoutingType { get; set; }
        public bool AllowDuplicate { get; set; }
        public int? CityId { get; set; }
        public string CityTitle { get; set; }
        public int ExceptionCount { get; set; }
    }

    public class ReferralConditionRowModel
    {
        public int ConditionId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ExpressionText { get; set; }
    }

    public class HeadOfficeReferralPageViewModel
    {
        public string ActiveTab { get; set; }
        public string Scope { get; set; }
        public int? CityId { get; set; }
        public IList<ReferralOptionModel> Reports { get; set; } = new List<ReferralOptionModel>();
        public IList<ReferralFieldOptionModel> Fields { get; set; } = new List<ReferralFieldOptionModel>();
        public IList<ReferralReportUniqueFieldModel> ReportUniqueFields { get; set; } = new List<ReferralReportUniqueFieldModel>();
        public IList<ReferralFieldOptionModel> Subsets { get; set; } = new List<ReferralFieldOptionModel>();
        public IList<ReferralOptionModel> Cities { get; set; } = new List<ReferralOptionModel>();
        public IList<int> ConfiguredCityIds { get; set; } = new List<int>();
        public IList<ReferralIndicatorOptionModel> Indicators { get; set; } = new List<ReferralIndicatorOptionModel>();
        public IList<ReferralBaseFieldRowModel> BaseFields { get; set; } = new List<ReferralBaseFieldRowModel>();
        public IList<ReferralPriorityRowModel> PriorityRows { get; set; } = new List<ReferralPriorityRowModel>();
        public IList<ReferralListRowModel> Lists { get; set; } = new List<ReferralListRowModel>();
        public IList<ReferralConditionRowModel> Conditions { get; set; } = new List<ReferralConditionRowModel>();
    }

    public class SaveBaseFieldsModel
    {
        public long ReportId { get; set; }
        public IList<long> FieldIds { get; set; } = new List<long>();
    }

    public class ReferralPriorityWeightInputModel
    {
        public int IndicatorId { get; set; }
        public decimal Weight { get; set; }
    }

    public class ReferralPriorityRuleInputModel
    {
        public long FieldId { get; set; }
        public long? SubsetId { get; set; }
        public decimal? MinimumAmount { get; set; }
        public decimal? MaximumAmount { get; set; }
        public IList<ReferralPriorityWeightInputModel> Weights { get; set; } = new List<ReferralPriorityWeightInputModel>();
    }

    public class SavePriorityRulesModel
    {
        public string Scope { get; set; }
        public int? CityId { get; set; }
        public int? CopyFromCityId { get; set; }
        public IList<ReferralPriorityRuleInputModel> Rules { get; set; } = new List<ReferralPriorityRuleInputModel>();
    }

    public class SaveReferralListModel
    {
        public int ListId { get; set; }

        [Required(ErrorMessage = "عنوان لیست را وارد کنید.")]
        public string Title { get; set; }

        public long ReportId { get; set; }
        public long KeyFieldId { get; set; }
        public int DailyAmount { get; set; }
        public int DurationDays { get; set; }
        public string RoutingType { get; set; }
        public bool AllowDuplicate { get; set; }
        public string Scope { get; set; }
        public int? CityId { get; set; }
        public IList<long> ExceptionSubsetIds { get; set; } = new List<long>();
    }

    public class SaveReferralConditionModel
    {
        public int ConditionId { get; set; }

        [Required(ErrorMessage = "عنوان شرط را وارد کنید.")]
        public string Title { get; set; }

        public string Description { get; set; }

        [Required(ErrorMessage = "عبارت شرط را بسازید.")]
        public string ExpressionText { get; set; }
    }
}
