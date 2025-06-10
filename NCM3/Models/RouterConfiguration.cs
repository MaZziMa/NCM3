using System;
using System.ComponentModel.DataAnnotations;

namespace NCM3.Models
{
    public class RouterConfiguration
    {
        public int Id { get; set; }
        
        [Required]
        public int RouterId { get; set; }
        
        [Required]
        public DateTime BackupDate { get; set; }
        
        [Required]
        [Display(Name = "Configuration Content")]
        public string Content { get; set; } = string.Empty;
        
        [Display(Name = "Version/Label")]
        public string? Version { get; set; }        
        [Display(Name = "Backup By")]
        public string? BackupBy { get; set; }
        
        [Display(Name = "Backup Type")]
        public string? BackupType { get; set; }
        
        [Display(Name = "Comment")]
        public string? Comment { get; set; }
        
        // Navigation property
        public virtual Router? Router { get; set; }
    }
} 