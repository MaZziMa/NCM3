using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NCM3.Models;

namespace NCM3.Services
{
    public interface IWebhookNotificationService
    {
        Task SendWebhookNotificationAsync(string eventType, object payload);
    }
      public class WebhookNotificationService : IWebhookNotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebhookNotificationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly NotificationLogger? _notificationLogger;
        
        public WebhookNotificationService(
            HttpClient httpClient,
            ILogger<WebhookNotificationService> logger,
            IConfiguration configuration,
            NotificationLogger? notificationLogger = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _notificationLogger = notificationLogger;
        }
          public async Task SendWebhookNotificationAsync(string eventType, object payload)
        {
            var webhookUrl = _configuration["Notification:WebhookUrl"];
            if (string.IsNullOrEmpty(webhookUrl))
            {
                _logger.LogWarning("Webhook không được cấu hình. Bỏ qua gửi thông báo.");
                return;
            }
            
            try
            {
                var notificationPayload = new
                {
                    eventType,
                    timestamp = DateTime.UtcNow,
                    data = payload
                };
                
                var content = new StringContent(
                    JsonConvert.SerializeObject(notificationPayload),
                    Encoding.UTF8,
                    "application/json");
                
                var response = await _httpClient.PostAsync(webhookUrl, content);
                response.EnsureSuccessStatusCode();
                
                _logger.LogInformation(
                    "Gửi webhook thành công cho sự kiện {EventType}",
                    eventType);
                  // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    // Xác định loại thông báo dựa trên eventType
                    if (eventType == "configuration_change")
                    {
                        var jsonPayload = JsonConvert.SerializeObject(payload);
                        var configData = JsonConvert.DeserializeObject<dynamic>(jsonPayload);
                        await _notificationLogger.LogConfigurationChangeNotificationAsync(
                            configData?.routerName?.ToString() ?? "Unknown",
                            configData?.changeType?.ToString() ?? "Unknown",
                            configData?.diffDetails?.ToString() ?? "No details",
                            true);
                    }
                    else if (eventType == "connectivity_alert")
                    {
                        var jsonPayload = JsonConvert.SerializeObject(payload);
                        var connectData = JsonConvert.DeserializeObject<dynamic>(jsonPayload);
                        await _notificationLogger.LogConnectivityNotificationAsync(
                            connectData?.routerName?.ToString() ?? "Unknown",
                            connectData?.status?.ToString() ?? "Unknown",
                            connectData?.details?.ToString() ?? "No details",
                            true);
                    }
                    else if (eventType == "compliance_alert")
                    {
                        var jsonPayload = JsonConvert.SerializeObject(payload);
                        var complianceData = JsonConvert.DeserializeObject<dynamic>(jsonPayload);
                        await _notificationLogger.LogComplianceNotificationAsync(
                            complianceData?.routerName?.ToString() ?? "Unknown",
                            complianceData?.ruleName?.ToString() ?? "Unknown",
                            complianceData?.severity?.ToString() ?? "Unknown",
                            complianceData?.details?.ToString() ?? "No details",
                            true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Lỗi khi gửi webhook cho sự kiện {EventType}: {Message}",
                    eventType,
                    ex.Message);
                  // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    try
                    {
                        var jsonPayload = JsonConvert.SerializeObject(payload);
                        var data = JsonConvert.DeserializeObject<dynamic>(jsonPayload);
                        string routerName = data?.routerName?.ToString() ?? "Unknown";
                        
                        if (eventType == "configuration_change")
                        {
                            await _notificationLogger.LogConfigurationChangeNotificationAsync(
                                routerName,
                                data?.changeType?.ToString() ?? "Unknown",
                                $"Lỗi gửi webhook: {ex.Message}",
                                false);
                        }
                    }
                    catch
                    {
                        // Bỏ qua lỗi khi xử lý dữ liệu payload
                        _logger.LogWarning("Không thể ghi log thông báo do định dạng dữ liệu không hợp lệ");
                    }
                }
            }
        }
    }
}
