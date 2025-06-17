using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using NCM3.Services;
using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NCM3.Controllers
{
    public class SettingsController : Controller
    {        private readonly ILogger<SettingsController> _logger;
        private readonly IConfiguration _configuration;
        private readonly ITelegramNotificationService _telegramService;
        private readonly IWebhookNotificationService _webhookService;
        private readonly string _appSettingsFilePath;
        private readonly NotificationLogger? _notificationLogger;
        
        public SettingsController(
            ILogger<SettingsController> logger,
            IConfiguration configuration,
            ITelegramNotificationService telegramService,
            IWebhookNotificationService webhookService,
            NotificationLogger? notificationLogger = null)
        {
            _logger = logger;
            _configuration = configuration;
            _telegramService = telegramService;
            _webhookService = webhookService;
            _notificationLogger = notificationLogger;
            
            // Lấy đường dẫn tới tệp appsettings.json
            _appSettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        }
        
        public IActionResult Index()
        {
            var viewModel = new SettingsViewModel
            {
                NotificationSettings = new NotificationSettings
                {
                    TelegramBotToken = _configuration["Telegram:BotToken"] ?? string.Empty,
                    TelegramChatId = _configuration["Telegram:ChatId"] ?? string.Empty,
                    EnableTelegram = !string.IsNullOrEmpty(_configuration["Telegram:BotToken"]) && 
                                    !string.IsNullOrEmpty(_configuration["Telegram:ChatId"]),
                    WebhookUrl = _configuration["Notification:WebhookUrl"] ?? string.Empty,
                    EnableWebhook = bool.TryParse(_configuration["Notification:EnableWebhook"], out bool enableWebhook) && enableWebhook,
                    NotifyOnConfigChange = !bool.TryParse(_configuration["Notification:NotifyOnConfigChange"], out bool notifyConfig) || notifyConfig,
                    NotifyOnComplianceIssue = !bool.TryParse(_configuration["Notification:NotifyOnComplianceIssue"], out bool notifyCompliance) || notifyCompliance,
                    NotifyOnConnectivityChange = !bool.TryParse(_configuration["Notification:NotifyOnConnectivityChange"], out bool notifyConn) || notifyConn
                },
                
                AutoDetectionSettings = new AutoDetectionSettings
                {
                    EnableAutoDetection = bool.TryParse(_configuration["AutoDetection:Enabled"], out bool enabled) && enabled,
                    CheckIntervalMinutes = int.TryParse(_configuration["AutoDetection:CheckIntervalMinutes"], out int interval) 
                        ? interval 
                        : 30,
                    DetectConfigChanges = !bool.TryParse(_configuration["AutoDetection:DetectConfigChanges"], out bool detectConfig) || detectConfig,
                    DetectConnectivityChanges = !bool.TryParse(_configuration["AutoDetection:DetectConnectivityChanges"], out bool detectConn) || detectConn,
                    DetectComplianceIssues = !bool.TryParse(_configuration["AutoDetection:DetectComplianceIssues"], out bool detectComp) || detectComp
                },
                
                // Kiểm tra trạng thái dịch vụ
                DetectionServiceRunning = bool.TryParse(_configuration["AutoDetection:Enabled"], out bool serviceRunning) && serviceRunning,
                TelegramEnabled = !string.IsNullOrEmpty(_configuration["Telegram:BotToken"]) && 
                                  !string.IsNullOrEmpty(_configuration["Telegram:ChatId"])
            };
            
            return View(viewModel);
        }
          public async Task<IActionResult> SaveNotificationSettings(NotificationSettings model)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction(nameof(Index));
            }
            
            try
            {
                // Đọc tệp cấu hình
                var json = await System.IO.File.ReadAllTextAsync(_appSettingsFilePath);
                var jsonObj = JObject.Parse(json);
                
                // Cập nhật cấu hình Telegram
                if (jsonObj["Telegram"] == null)
                {
                    jsonObj["Telegram"] = new JObject();
                }
                
                if (jsonObj["Telegram"] != null)
                {
                    jsonObj["Telegram"]["BotToken"] = model.EnableTelegram ? model.TelegramBotToken : string.Empty;
                    jsonObj["Telegram"]["ChatId"] = model.EnableTelegram ? model.TelegramChatId : string.Empty;
                }
                
                // Cập nhật cấu hình Notification
                if (jsonObj["Notification"] == null)
                {
                    jsonObj["Notification"] = new JObject();
                }
                
                if (jsonObj["Notification"] != null)
                {
                    jsonObj["Notification"]["EnableWebhook"] = model.EnableWebhook.ToString().ToLower();
                    jsonObj["Notification"]["WebhookUrl"] = model.WebhookUrl;
                    jsonObj["Notification"]["NotifyOnConfigChange"] = model.NotifyOnConfigChange.ToString().ToLower();
                    jsonObj["Notification"]["NotifyOnComplianceIssue"] = model.NotifyOnComplianceIssue.ToString().ToLower();
                    jsonObj["Notification"]["NotifyOnConnectivityChange"] = model.NotifyOnConnectivityChange.ToString().ToLower();
                }
                
                // Ghi lại tệp cấu hình
                await System.IO.File.WriteAllTextAsync(_appSettingsFilePath, jsonObj.ToString(Formatting.Indented));
                
                _logger.LogInformation("Đã cập nhật thiết lập thông báo");
                TempData["SuccessMessage"] = "Đã lưu thiết lập thông báo thành công";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu thiết lập thông báo: {Error}", ex.Message);
                TempData["ErrorMessage"] = $"Lỗi khi lưu thiết lập: {ex.Message}";
            }
            
            return RedirectToAction(nameof(Index));
        }
        
        [HttpPost]
        public async Task<IActionResult> SaveAutoDetectionSettings(AutoDetectionSettings model)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction(nameof(Index));
            }
            
            try
            {
                // Đọc tệp cấu hình
                var json = await System.IO.File.ReadAllTextAsync(_appSettingsFilePath);
                var jsonObj = JObject.Parse(json);
                
                // Cập nhật cấu hình AutoDetection
                if (jsonObj["AutoDetection"] == null)
                {
                    jsonObj["AutoDetection"] = new JObject();
                }
                
                jsonObj["AutoDetection"]["Enabled"] = model.EnableAutoDetection.ToString().ToLower();
                jsonObj["AutoDetection"]["CheckIntervalMinutes"] = model.CheckIntervalMinutes.ToString();
                jsonObj["AutoDetection"]["DetectConfigChanges"] = model.DetectConfigChanges.ToString().ToLower();
                jsonObj["AutoDetection"]["DetectConnectivityChanges"] = model.DetectConnectivityChanges.ToString().ToLower();
                jsonObj["AutoDetection"]["DetectComplianceIssues"] = model.DetectComplianceIssues.ToString().ToLower();
                
                // Ghi lại tệp cấu hình
                await System.IO.File.WriteAllTextAsync(_appSettingsFilePath, jsonObj.ToString(Formatting.Indented));
                
                _logger.LogInformation("Đã cập nhật thiết lập tự động phát hiện");
                TempData["SuccessMessage"] = "Đã lưu thiết lập tự động phát hiện thành công";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu thiết lập tự động phát hiện: {Error}", ex.Message);
                TempData["ErrorMessage"] = $"Lỗi khi lưu thiết lập: {ex.Message}";
            }
            
            return RedirectToAction(nameof(Index));
        }        [HttpPost]
        public async Task<IActionResult> TestTelegramConnection()
        {
            var viewModel = new SettingsViewModel();
            
            try
            {
                // Sử dụng phương thức SendTestMessageAsync mới
                bool success = await _telegramService.SendTestMessageAsync();
                
                if (success)
                {
                    viewModel.TestResult = "Kết nối thành công! Đã gửi tin nhắn kiểm tra đến Telegram. Kiểm tra xem tin nhắn có hiển thị đúng và không bị lỗi.";
                    viewModel.TestSuccessful = true;
                    
                    _logger.LogInformation("Kiểm tra kết nối Telegram thành công");
                }
                else
                {
                    viewModel.TestResult = "Không thể gửi tin nhắn kiểm tra đến Telegram. Vui lòng kiểm tra cấu hình Bot Token và Chat ID.";
                    viewModel.TestSuccessful = false;
                    
                    _logger.LogWarning("Kiểm tra kết nối Telegram thất bại - không thể gửi tin nhắn");
                }
                
                TempData["TestResult"] = viewModel.TestResult;
                TempData["TestSuccessful"] = viewModel.TestSuccessful;
            }
            catch (Exception ex)
            {
                viewModel.TestResult = $"Lỗi kết nối: {ex.Message}";
                viewModel.TestSuccessful = false;
                
                _logger.LogError(ex, "Lỗi kiểm tra kết nối Telegram: {Error}", ex.Message);
                TempData["TestResult"] = viewModel.TestResult;
                TempData["TestSuccessful"] = false;
            }
            
            return RedirectToAction(nameof(Index));
        }
        
        // Helper method to generate a long message for testing
        private string GenerateLongTestMessage()
        {
            var result = new StringBuilder();
            result.AppendLine("Đây là một tin nhắn dài để kiểm tra chức năng xử lý tin nhắn dài trong Telegram:");
            result.AppendLine();
            result.AppendLine("```");
            
            // Generate fake router config changes
            for (int i = 1; i <= 100; i++)
            {
                if (i % 2 == 0)
                {
                    result.AppendLine($"+ interface GigabitEthernet0/{i}");
                    result.AppendLine($"+  description Added connection to Server{i}");
                    result.AppendLine($"+  ip address 192.168.{i}.1 255.255.255.0");
                    result.AppendLine($"+  no shutdown");
                }
                else
                {
                    result.AppendLine($"- interface GigabitEthernet0/{i}");
                    result.AppendLine($"-  description Old connection to Switch{i}");
                    result.AppendLine($"-  ip address 10.0.{i}.1 255.255.255.0");
                    result.AppendLine($"-  shutdown");
                }
            }
            
            result.AppendLine("```");
            return result.ToString();
        }
        
        /// <summary>
        /// Hiển thị lịch sử thông báo
        /// </summary>
        public async Task<IActionResult> NotificationHistory(string filter = "all", int page = 1, int pageSize = 20)
        {
            if (_notificationLogger == null)
            {
                return View(new Models.ViewModels.NotificationHistoryViewModel());
            }
            
            var model = await _notificationLogger.GetNotificationHistoryAsync(filter, page, pageSize);
            return View(model);
        }
    }
}
