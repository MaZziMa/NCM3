using System;
using System.Threading.Tasks;
using NCM3.Models;
using NCM3.Services;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace NCM3.Services
{
    public class NotificationHelper
    {        private readonly ITelegramNotificationService _telegramService;
        private readonly ConfigurationManagementService _configService;
        private readonly ILogger<NotificationHelper> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebhookNotificationService? _webhookService;
        private readonly NotificationLogger? _notificationLogger;
        
        public NotificationHelper(
            ITelegramNotificationService telegramService,
            ConfigurationManagementService configService,
            ILogger<NotificationHelper> logger,
            IConfiguration configuration,
            IWebhookNotificationService? webhookService = null,
            NotificationLogger? notificationLogger = null)
        {
            _telegramService = telegramService;
            _configService = configService;
            _logger = logger;
            _configuration = configuration;
            _webhookService = webhookService;
            _notificationLogger = notificationLogger;
        }
          /// <summary>
        /// Gửi thông báo về sự thay đổi cấu hình
        /// </summary>
        /// <param name="routerName">Tên router</param>
        /// <param name="changeType">Loại thay đổi (Cập nhật, Sao lưu, ...)</param>
        /// <param name="oldConfig">Cấu hình cũ</param>
        /// <param name="newConfig">Cấu hình mới</param>
        /// <returns>Task thực hiện việc gửi thông báo</returns>
        public async Task SendConfigurationChangeNotificationAsync(
            string routerName, 
            string changeType, 
            string oldConfig, 
            string newConfig)
        {
            try
            {
                // Tìm sự khác biệt giữa cấu hình cũ và mới
                string diffDetails = string.IsNullOrEmpty(oldConfig)
                    ? "Cấu hình mới được tạo"
                    : await _configService.GetConfigurationDiffAsync(oldConfig, newConfig);
                  // Gửi thông báo qua Telegram nếu được bật
                bool telegramEnabled = !string.IsNullOrEmpty(_configuration["Telegram:BotToken"]) &&
                                      !string.IsNullOrEmpty(_configuration["Telegram:ChatId"]);
                
                bool notifyOnConfigChange = true;
                if (_configuration["Notification:NotifyOnConfigChange"] != null)
                {
                    bool.TryParse(_configuration["Notification:NotifyOnConfigChange"], out notifyOnConfigChange);
                }
                
                if (telegramEnabled && notifyOnConfigChange)
                {
                    await _telegramService.SendConfigChangeNotificationAsync(
                        routerName,
                        changeType,
                        diffDetails
                    );
                }
                
                // Gửi webhook nếu được bật
                bool webhookEnabled = _webhookService != null && 
                                     !string.IsNullOrEmpty(_configuration["Notification:WebhookUrl"]);
                
                bool enableWebhook = false;
                if (_configuration["Notification:EnableWebhook"] != null)
                {
                    bool.TryParse(_configuration["Notification:EnableWebhook"], out enableWebhook);
                }
                
                if (webhookEnabled && enableWebhook)
                {
                    var payload = new 
                    {
                        routerName,
                        changeType,
                        diffDetails,
                        timestamp = DateTime.UtcNow
                    };
                    
                    await _webhookService!.SendWebhookNotificationAsync("configuration_change", payload);
                }
                
                _logger.LogInformation(
                    "Đã gửi thông báo thay đổi cấu hình cho router {RouterName}, loại thay đổi: {ChangeType}",
                    routerName,
                    changeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Lỗi khi gửi thông báo thay đổi cấu hình cho router {RouterName}: {Error}",
                    routerName,
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Kiểm tra sự thay đổi cấu hình và gửi thông báo nếu có thay đổi
        /// </summary>
        /// <param name="routerId">ID của router</param>
        /// <param name="newConfig">Cấu hình mới</param>
        /// <param name="changeType">Loại thay đổi (mặc định: Cập nhật cấu hình)</param>
        /// <returns>True nếu có thay đổi và thông báo được gửi, False nếu không có thay đổi</returns>
        public async Task<bool> DetectAndNotifyConfigurationChangeAsync(
            Router router,
            string newConfig,
            string changeType = "Cập nhật cấu hình")
        {
            if (router == null)
            {
                _logger.LogWarning("Không thể kiểm tra thay đổi cấu hình: router là null");
                return false;
            }
            
            // Kiểm tra cấu hình mới có thay đổi so với cấu hình cũ hay không
            var lastConfig = router.RouterConfigurations
                .OrderByDescending(c => c.BackupDate)
                .FirstOrDefault();
                
            var hasChanges = lastConfig == null || lastConfig.Content != newConfig;
            
            if (hasChanges)
            {
                string oldConfig = lastConfig?.Content ?? string.Empty;
                await SendConfigurationChangeNotificationAsync(
                    router.Hostname,
                    changeType,
                    oldConfig,
                    newConfig);
                return true;
            }
            
            return false;
        }
    }
}
