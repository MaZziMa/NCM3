using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NCM3.Extensions
{
    public static class AppSettingsExtensions
    {
        public static IServiceCollection ConfigureBackupFolders(this IServiceCollection services, IConfiguration configuration)
        {
            // Lấy đường dẫn thư mục từ cấu hình
            var backupFolder = configuration["AppSettings:BackupFolder"];
            var configBackupFolder = configuration["AppSettings:ConfigBackupFolder"];
            var logFolder = configuration["AppSettings:LogFolder"];
            var templatePath = configuration["AppSettings:TemplatePath"];
            var complianceRulesPath = configuration["AppSettings:ComplianceRulesPath"];
            
            // Tạo các thư mục nếu chúng chưa tồn tại
            CreateDirectoryIfNotExists(backupFolder);
            CreateDirectoryIfNotExists(configBackupFolder);
            CreateDirectoryIfNotExists(logFolder);
            CreateDirectoryIfNotExists(templatePath);
            CreateDirectoryIfNotExists(complianceRulesPath);
            
            return services;
        }
        
        private static void CreateDirectoryIfNotExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    // Log lỗi nếu không thể tạo thư mục
                    Console.WriteLine($"Không thể tạo thư mục {path}: {ex.Message}");
                }
            }
        }
    }
}
