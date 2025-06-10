using System.ComponentModel.DataAnnotations;

namespace NCM3.Models
{
    /// <summary>
    /// Cài đặt chung của ứng dụng
    /// </summary>
    public class AppSettings
    {
        public string BackupFolder { get; set; } = string.Empty;
        public string ConfigBackupFolder { get; set; } = string.Empty;
        public string LogFolder { get; set; } = string.Empty;
        public string TemplatePath { get; set; } = string.Empty;
        public string ComplianceRulesPath { get; set; } = string.Empty;
        public int MaxBackupsPerRouter { get; set; } = 10;
        public int AutoBackupIntervalHours { get; set; } = 24;
    }
    
    /// <summary>
    /// Cài đặt thông báo
    /// </summary>
    public class NotificationSettings
    {
        public bool EnableTelegram { get; set; }
        public string TelegramBotToken { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;
        public bool EnableWebhook { get; set; }
        public string WebhookUrl { get; set; } = string.Empty;
        public bool NotifyOnConfigChange { get; set; } = true;
        public bool NotifyOnComplianceIssue { get; set; } = true;
        public bool NotifyOnConnectivityChange { get; set; } = true;
    }
    
    /// <summary>
    /// Cài đặt tự động phát hiện thay đổi
    /// </summary>
    public class AutoDetectionSettings
    {
        public bool EnableAutoDetection { get; set; }
        
        [Range(5, 1440, ErrorMessage = "Khoảng thời gian kiểm tra phải từ 5 đến 1440 phút")]
        public int CheckIntervalMinutes { get; set; } = 30;
        
        public bool DetectConfigChanges { get; set; } = true;
        public bool DetectConnectivityChanges { get; set; } = true;
        public bool DetectComplianceIssues { get; set; } = true;
    }
    
    /// <summary>
    /// View model cho trang cài đặt
    /// </summary>
    public class SettingsViewModel
    {
        public NotificationSettings NotificationSettings { get; set; } = new NotificationSettings();
        public AutoDetectionSettings AutoDetectionSettings { get; set; } = new AutoDetectionSettings();
        public bool DetectionServiceRunning { get; set; }
        public bool TelegramEnabled { get; set; }
        public string? TestResult { get; set; }
        public bool TestSuccessful { get; set; }
    }
}
