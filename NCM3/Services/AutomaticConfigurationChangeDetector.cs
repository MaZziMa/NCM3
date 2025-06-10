using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NCM3.Models;
using NCM3.Services;
using NCM3.Constants;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace NCM3.Services
{
    public class AutomaticConfigurationChangeDetector : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutomaticConfigurationChangeDetector> _logger;
        private readonly IConfiguration _configuration;
        
        // Khoảng thời gian kiểm tra mặc định: 30 phút
        private TimeSpan _checkInterval = TimeSpan.FromMinutes(30);
        
        public AutomaticConfigurationChangeDetector(
            IServiceProvider serviceProvider,
            ILogger<AutomaticConfigurationChangeDetector> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            
            // Cập nhật _checkInterval từ cấu hình
            if (int.TryParse(_configuration["AutoDetection:CheckIntervalMinutes"], out int minutes) && minutes >= 5)
            {
                _checkInterval = TimeSpan.FromMinutes(minutes);
            }
        }        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dịch vụ AutomaticConfigurationChangeDetector đã khởi động.");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                // Kiểm tra xem dịch vụ có được bật hay không
                bool enabled = false;
                if (bool.TryParse(_configuration["AutoDetection:Enabled"], out bool isEnabled))
                {
                    enabled = isEnabled;
                }
                
                // Cập nhật _checkInterval từ cấu hình
                if (int.TryParse(_configuration["AutoDetection:CheckIntervalMinutes"], out int minutes) && minutes >= 5)
                {
                    _checkInterval = TimeSpan.FromMinutes(minutes);
                }
                
                if (enabled)
                {
                    _logger.LogInformation("Đang thực hiện kiểm tra thay đổi cấu hình. Sẽ kiểm tra lại sau {Interval} phút.",
                        _checkInterval.TotalMinutes);
                    
                    try
                    {
                        var startTime = DateTime.Now;
                        int changesDetected = await CheckForConfigurationChangesAsync();
                        var duration = DateTime.Now - startTime;
                        
                        _logger.LogInformation(
                            "Hoàn thành kiểm tra thay đổi, phát hiện {ChangesCount} thay đổi, thời gian xử lý: {Duration:hh\\:mm\\:ss}",
                            changesDetected,
                            duration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi trong quá trình kiểm tra thay đổi cấu hình tự động");
                    }
                }
                else
                {
                    _logger.LogDebug("Dịch vụ tự động phát hiện thay đổi đang tắt. Sẽ kiểm tra lại sau {Interval} phút.",
                        _checkInterval.TotalMinutes);
                }
                
                // Đợi đến lần kiểm tra tiếp theo
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }        private async Task<int> CheckForConfigurationChangesAsync()
        {
            _logger.LogInformation("Bắt đầu kiểm tra thay đổi cấu hình tự động...");
            int changesDetected = 0;
            
            // Kiểm tra loại phát hiện được bật
            bool detectConfigChanges = true;
            bool detectConnectivityChanges = true;
            bool detectComplianceIssues = true;
            
            if (_configuration["AutoDetection:DetectConfigChanges"] != null)
            {
                bool.TryParse(_configuration["AutoDetection:DetectConfigChanges"], out detectConfigChanges);
            }
            
            if (_configuration["AutoDetection:DetectConnectivityChanges"] != null)
            {
                bool.TryParse(_configuration["AutoDetection:DetectConnectivityChanges"], out detectConnectivityChanges);
            }
            
            if (_configuration["AutoDetection:DetectComplianceIssues"] != null)
            {
                bool.TryParse(_configuration["AutoDetection:DetectComplianceIssues"], out detectComplianceIssues);
            }
              using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<NCMDbContext>();
            var routerService = scope.ServiceProvider.GetRequiredService<RouterService>();
            var connectionService = scope.ServiceProvider.GetRequiredService<RouterConnectionService>();
            var notificationHelper = scope.ServiceProvider.GetRequiredService<NotificationHelper>();
            
            // Lấy danh sách router cần kiểm tra
            var routers = await dbContext.Routers
                .Include(r => r.RouterConfigurations)
                .ToListAsync();
                
            _logger.LogInformation("Tìm thấy {Count} router để kiểm tra", routers.Count);
            
            foreach (var router in routers)
            {
                try
                {                    // Kiểm tra kết nối nếu được bật
                    if (detectConnectivityChanges)
                    {
                        bool isConnected = await connectionService.TestConnectionAsync(router);
                        if (!isConnected)
                        {
                            _logger.LogWarning("Router {RouterName} không kết nối được", router.Hostname);
                            
                            // Gửi thông báo nếu cấu hình kết nối thay đổi                            if (router.IsAvailable) // Nếu trước đó vẫn đang kết nối được
                            {
                                var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramNotificationService>();
                                await telegramService.SendConnectivityAlertAsync(
                                    router.Hostname,
                                    "Mất kết nối",
                                    $"Không thể kết nối đến router tại thời điểm {DateTime.Now:dd/MM/yyyy HH:mm:ss}"
                                );
                                changesDetected++;
                            }
                        }
                        else if (!router.IsAvailable) // Nếu trước đó không kết nối được, nhưng giờ đã kết nối được
                        {
                            _logger.LogInformation("Router {RouterName} đã khôi phục kết nối", router.Hostname);
                              var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramNotificationService>();
                            await telegramService.SendConnectivityAlertAsync(
                                router.Hostname,
                                "Đã khôi phục kết nối",
                                $"Kết nối đến router đã được khôi phục tại thời điểm {DateTime.Now:dd/MM/yyyy HH:mm:ss}"
                            );
                            changesDetected++;
                        }
                        
                        // Cập nhật trạng thái kết nối
                        router.IsAvailable = isConnected;
                        await dbContext.SaveChangesAsync();
                    }
                    
                    // Kiểm tra thay đổi cấu hình nếu được bật và router vẫn kết nối được
                    if (detectConfigChanges && router.IsAvailable)
                    {
                        // Lấy cấu hình hiện tại từ router 
                        var currentConfig = await routerService.GetConfigurationAsync(router);
                        
                        // Tìm cấu hình cuối cùng trong database
                        var lastConfig = router.RouterConfigurations
                            .OrderByDescending(c => c.BackupDate)
                            .FirstOrDefault();
                            
                        // So sánh và phát hiện thay đổi
                        var hasChanges = lastConfig == null || lastConfig.Content != currentConfig;
                        
                        if (hasChanges)
                        {
                            _logger.LogInformation("Phát hiện thay đổi cấu hình trên router {RouterName}", router.Hostname);
                              // Lưu cấu hình mới
                            var newConfig = new RouterConfiguration
                            {
                                RouterId = router.Id,
                                BackupDate = DateTime.UtcNow,
                                Content = currentConfig,
                                Version = $"Auto_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
                                BackupBy = "AutoDetector",
                                BackupType = BackupTypes.Automatic
                            };
                            
                            dbContext.RouterConfigurations.Add(newConfig);
                            router.LastBackup = newConfig.BackupDate;
                            await dbContext.SaveChangesAsync();
                            
                            // Tải lên S3 nếu tính năng được kích hoạt
                            bool enableS3Backup = _configuration.GetValue<bool>("AWS:S3:EnableS3Backup", false);
                            bool backupToS3OnChange = _configuration.GetValue<bool>("AWS:S3:BackupToS3OnChange", false);
                            
                            if (enableS3Backup && backupToS3OnChange)
                            {
                                var s3Service = scope.ServiceProvider.GetRequiredService<IS3BackupService>();
                                try
                                {
                                    // Corrected arguments for UploadBackupAsync
                                    bool s3UploadSuccess = await s3Service.UploadBackupAsync(router.Id, currentConfig, newConfig.Version, BackupTypes.Automatic);
                                    if (s3UploadSuccess)
                                    {
                                        _logger.LogInformation($"Đã tải bản sao lưu tự động {newConfig.Version} của router {router.Hostname} lên S3.");
                                        // Optionally, store S3 key or identifier if the UploadBackupAsync method is modified to return it.
                                        // For now, it returns bool. If you need the key, S3BackupService.UploadBackupAsync must be changed.
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Không tải được bản sao lưu tự động {newConfig.Version} của router {router.Hostname} lên S3.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Lỗi khi tải bản sao lưu tự động {newConfig.Version} của router {router.Hostname} lên S3.");
                                }
                            }
                            
                            // Gửi thông báo
                            await notificationHelper.SendConfigurationChangeNotificationAsync(
                                router.Hostname,
                                "Phát hiện thay đổi tự động",
                                lastConfig?.Content ?? string.Empty,
                                currentConfig
                            );
                            
                            // Tăng bộ đếm thay đổi
                            changesDetected++;
                        }
                        else
                        {
                            _logger.LogDebug("Không có thay đổi cấu hình trên router {RouterName}", router.Hostname);
                        }
                    }
                    
                    // TODO: Kiểm tra tuân thủ nếu được bật
                    if (detectComplianceIssues && router.IsAvailable)
                    {
                        // Đoạn code kiểm tra tuân thủ sẽ được thêm vào sau
                        _logger.LogDebug("Kiểm tra tuân thủ đối với router {RouterName}", router.Hostname);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi kiểm tra thay đổi cấu hình cho router {RouterName}", router.Hostname);
                }
            }            _logger.LogInformation("Hoàn thành kiểm tra thay đổi cấu hình tự động");
            return changesDetected;
        }
    }
}
