using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NCM3.Models
{
    public class ComplianceResult
    {
        public int Id { get; set; }
        
        [Required]
        public int RouterId { get; set; }
        
        [Required]
        public int ConfigurationId { get; set; }
        
        [Required]
        public int RuleId { get; set; }
        
        [Required]
        [Display(Name = "Kết quả")]
        public bool Result { get; set; }
        
        [Display(Name = "Tuân thủ")]
        public bool IsCompliant { get; set; }
        
        [Display(Name = "Dòng")]
        public int? LineNumber { get; set; }
        
        [Display(Name = "Nội dung")]
        public string? MatchedContent { get; set; }
        
        [Display(Name = "Ngày kiểm tra")]
        public DateTime CheckDate { get; set; } = DateTime.Now;
        
        // Navigation properties
        [ForeignKey("RouterId")]
        public virtual Router? Router { get; set; }
        
        [ForeignKey("ConfigurationId")]
        public virtual RouterConfiguration? Configuration { get; set; }
        
        [ForeignKey("RuleId")]
        public virtual ComplianceRule? Rule { get; set; }
    }
} 