using System;
using System.ComponentModel.DataAnnotations;

namespace NCM3.Models
{
    public class ConfigTemplate
    {
        public int Id { get; set; }
        
        [Required]
        [Display(Name = "Tên Template")]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "Nội dung")]
        public string Content { get; set; } = string.Empty;
        
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }
        
        [Display(Name = "Loại thiết bị")]
        public string? DeviceType { get; set; }
        
        [Display(Name = "Ngày tạo")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        [Display(Name = "Người tạo")]
        public string? CreatedBy { get; set; }
        
        [Display(Name = "Phiên bản")]
        public string? Version { get; set; }
    }
} 