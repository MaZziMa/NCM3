using System;
using System.Collections.Generic;

namespace NCM3.Models.ViewModels
{
    public class DashboardViewModel
    {
        // Statistics summary
        public int TotalRouters { get; set; }
        public int ConnectedRouters { get; set; }
        public int DisconnectedRouters { get; set; }
        
        // Backup statistics
        public int TotalBackups { get; set; }
        public int BackupsLast24Hours { get; set; }
        public int BackupsLast7Days { get; set; }
        public DateTime? LastBackupTime { get; set; }
        
        // Recent configurations
        public List<RouterConfigurationSummary> RecentConfigurations { get; set; } = new List<RouterConfigurationSummary>();
        
        // Compliance summary
        public int TotalComplianceIssues { get; set; }
        public int CriticalComplianceIssues { get; set; }
        
        // Recent alerts/notifications
        public List<NotificationLogEntry> RecentNotifications { get; set; } = new List<NotificationLogEntry>();
        
        // Routers without recent backups
        public List<RouterWithoutBackup> RoutersWithoutRecentBackup { get; set; } = new List<RouterWithoutBackup>();
    }

    public class RouterConfigurationSummary
    {
        public int Id { get; set; }
        public int RouterId { get; set; }
        public string RouterHostname { get; set; } = string.Empty;
        public string RouterIpAddress { get; set; } = string.Empty;
        public DateTime BackupDate { get; set; }
        public string BackupType { get; set; } = string.Empty;
        public string BackupBy { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    public class RouterWithoutBackup
    {
        public int Id { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime? LastBackup { get; set; }
        public int DaysSinceLastBackup { get; set; }
    }
}
