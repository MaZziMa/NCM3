using System;
using System.ComponentModel.DataAnnotations;

namespace NCM3.Models
{
    public class ComplianceRule
    {
        public int Id { get; set; }
        
        [Required]
        [Display(Name = "Tên quy tắc")]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "Biểu thức tìm kiếm")]
        public string Pattern { get; set; } = string.Empty;
        
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }
        
        [Display(Name = "Mức độ")]
        public ComplianceSeverity Severity { get; set; } = ComplianceSeverity.Warning;
        
        [Display(Name = "Loại thiết bị")]
        public string? DeviceType { get; set; }
        
        [Display(Name = "Kết quả mong đợi")]
        public bool ExpectedResult { get; set; } = true;
        
        [Display(Name = "Người tạo")]
        public string? CreatedBy { get; set; }
        
        [Display(Name = "Ngày tạo")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        [Display(Name = "Kích hoạt")]
        public bool IsActive { get; set; } = true;
        
        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }
    }
    
    public enum ComplianceSeverity
    {
        [Display(Name = "Thông tin")]
        Info = 0,
        
        [Display(Name = "Cảnh báo")]
        Warning = 1,
        
        [Display(Name = "Nghiêm trọng")]
        Critical = 2
    }
} 