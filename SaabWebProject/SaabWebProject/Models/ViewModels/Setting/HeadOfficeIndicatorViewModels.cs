using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SaabWebProject.Models.ViewModels.Setting
{
    public class HeadOfficeIndicatorInputModel
    {
        public int IndicatorId { get; set; }

        [Required(ErrorMessage = "عنوان شاخص اصلی را وارد کنید.")]
        [StringLength(150, ErrorMessage = "عنوان شاخص نمی‌تواند بیشتر از ۱۵۰ نویسه باشد.")]
        [Display(Name = "عنوان شاخص اصلی")]
        public string Title { get; set; }

        [Required(ErrorMessage = "واحد شاخص را وارد کنید.")]
        [StringLength(50, ErrorMessage = "واحد شاخص نمی‌تواند بیشتر از ۵۰ نویسه باشد.")]
        [Display(Name = "واحد")]
        public string Unit { get; set; }
    }

    public class HeadOfficeIndicatorRowModel
    {
        public int IndicatorId { get; set; }
        public string Title { get; set; }
        public string Unit { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class HeadOfficeIndicatorPageViewModel
    {
        public HeadOfficeIndicatorPageViewModel()
        {
            Form = new HeadOfficeIndicatorInputModel();
            Indicators = new List<HeadOfficeIndicatorRowModel>();
        }

        public HeadOfficeIndicatorInputModel Form { get; set; }
        public IList<HeadOfficeIndicatorRowModel> Indicators { get; set; }
    }
}
