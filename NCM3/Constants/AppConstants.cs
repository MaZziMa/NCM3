namespace NCM3.Constants
{
    public static class RouterStatus
    {
        public const string Unknown = "Unknown";
        public const string Connected = "Connected";
        public const string Disconnected = "Disconnected";
        public const string Error = "Error";
        public const string ConfigBackupSuccessful = "ConfigBackupSuccessful";
        public const string ConfigBackupFailed = "ConfigBackupFailed";
    }
    
    public static class BackupTypes
    {
        public const string Manual = "Manual";
        public const string Scheduled = "Scheduled";
        public const string PreChange = "PreChange";
        public const string PostChange = "PostChange";
        public const string Automatic = "Automatic"; // Added
    }
    
    public static class DefaultSettings
    {
        public const int SSHPort = 22;
        public const int SSHTimeout = 45; // seconds
        public const int SSHRetryAttempts = 2;
        public const int SSHKeepAliveInterval = 60; // seconds
    }
    
    public static class SSHCommands
    {
        public const string TerminalLength = "terminal length 0";
        public const string ShowRunningConfig = "show running-config";
        public const string Enable = "enable";
    }
    
    public static class NotificationMessages
    {
        public const string ConfigChangeTitle = "🔔 Thay đổi Cấu hình";
        public const string ComplianceCheckTitle = "⚠️ Kiểm tra tuân thủ";
        public const string RouterConnectivityTitle = "🔌 Kết nối thiết bị";
    }
}
