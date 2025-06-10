using System;
using System.ComponentModel.DataAnnotations;

namespace NCM3.Models
{
    public class Router
    {
        public int Id { get; set; }
        
        [Required]
        [Display(Name = "Hostname")]
        public string Hostname { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "IP Address")]
        public string IpAddress { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "Password")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
        
        [Display(Name = "Enable Password")]
        [DataType(DataType.Password)]
        public string? EnablePassword { get; set; }
        
        [Display(Name = "Model")]
        public string? Model { get; set; }
        
        [Display(Name = "OS Version")]
        public string? OSVersion { get; set; }
        
        [Display(Name = "Last Configuration Backup")]
        public DateTime? LastBackup { get; set; }
        
        [Display(Name = "Status")]
        public string Status { get; set; } = "Unknown";
        
        [Display(Name = "Available")]
        public bool IsAvailable { get; set; } = false;
        
        [Display(Name = "Group")]
        public string? Group { get; set; }

        // Navigation property
        public virtual ICollection<RouterConfiguration> RouterConfigurations { get; set; } = new List<RouterConfiguration>();
    }
} 