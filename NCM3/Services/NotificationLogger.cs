using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NCM3.Models;

namespace NCM3.Services
{
    /// <summary>
    /// Dịch vụ ghi log chi tiết cho hoạt động thông báo
    /// </summary>
    public class NotificationLogger
    {
        private readonly ILogger<NotificationLogger> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _notificationLogPath;
        
        public NotificationLogger(
            ILogger<NotificationLogger> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Tạo thư mục log nếu cần
            var logFolder = _configuration["AppSettings:LogFolder"] ?? "Logs";
            _notificationLogPath = Path.Combine(logFolder, "notifications");
            
            if (!Directory.Exists(_notificationLogPath))
            {
                Directory.CreateDirectory(_notificationLogPath);
            }
        }
        
        /// <summary>
        /// Ghi log thông báo thay đổi cấu hình
        /// </summary>
        /// <param name="routerName">Tên router</param>
        /// <param name="changeType">Loại thay đổi</param>
        /// <param name="details">Chi tiết thay đổi</param>
        /// <param name="success">Thành công hay thất bại</param>
        /// <returns>Task ghi log</returns>
        public async Task LogConfigurationChangeNotificationAsync(
            string routerName, 
            string changeType, 
            string details, 
            bool success)
        {
            try
            {
                var logEntry = new
                {
                    Type = "ConfigurationChange",
                    Router = routerName,
                    ChangeType = changeType,
                    Timestamp = DateTime.Now,
                    Success = success,
                    Details = details
                };
                
                await WriteLogEntryAsync("config_changes", logEntry);
                
                if (success)
                {
                    _logger.LogInformation(
                        "Đã gửi thông báo thay đổi cấu hình cho router {RouterName}, loại thay đổi: {ChangeType}",
                        routerName,
                        changeType);
                }
                else
                {
                    _logger.LogWarning(
                        "Không thể gửi thông báo thay đổi cấu hình cho router {RouterName}, loại thay đổi: {ChangeType}",
                        routerName,
                        changeType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Lỗi khi ghi log thông báo: {Error}",
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Ghi log thông báo kết nối
        /// </summary>
        /// <param name="routerName">Tên router</param>
        /// <param name="status">Trạng thái kết nối</param>
        /// <param name="details">Chi tiết</param>
        /// <param name="success">Thành công hay thất bại</param>
        /// <returns>Task ghi log</returns>
        public async Task LogConnectivityNotificationAsync(
            string routerName, 
            string status, 
            string details, 
            bool success)
        {
            try
            {
                var logEntry = new
                {
                    Type = "Connectivity",
                    Router = routerName,
                    Status = status,
                    Timestamp = DateTime.Now,
                    Success = success,
                    Details = details
                };
                
                await WriteLogEntryAsync("connectivity", logEntry);
                
                if (success)
                {
                    _logger.LogInformation(
                        "Đã gửi thông báo kết nối cho router {RouterName}, trạng thái: {Status}",
                        routerName,
                        status);
                }
                else
                {
                    _logger.LogWarning(
                        "Không thể gửi thông báo kết nối cho router {RouterName}, trạng thái: {Status}",
                        routerName,
                        status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Lỗi khi ghi log thông báo: {Error}",
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Ghi log thông báo tuân thủ
        /// </summary>
        /// <param name="routerName">Tên router</param>
        /// <param name="ruleName">Tên quy tắc</param>
        /// <param name="severity">Mức độ nghiêm trọng</param>
        /// <param name="details">Chi tiết</param>
        /// <param name="success">Thành công hay thất bại</param>
        /// <returns>Task ghi log</returns>
        public async Task LogComplianceNotificationAsync(
            string routerName, 
            string ruleName, 
            string severity,
            string details, 
            bool success)
        {
            try
            {
                var logEntry = new
                {
                    Type = "Compliance",
                    Router = routerName,
                    Rule = ruleName,
                    Severity = severity,
                    Timestamp = DateTime.Now,
                    Success = success,
                    Details = details
                };
                
                await WriteLogEntryAsync("compliance", logEntry);
                
                if (success)
                {
                    _logger.LogInformation(
                        "Đã gửi thông báo tuân thủ cho router {RouterName}, quy tắc: {RuleName}, mức độ: {Severity}",
                        routerName,
                        ruleName,
                        severity);
                }
                else
                {
                    _logger.LogWarning(
                        "Không thể gửi thông báo tuân thủ cho router {RouterName}, quy tắc: {RuleName}, mức độ: {Severity}",
                        routerName,
                        ruleName,
                        severity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Lỗi khi ghi log thông báo: {Error}",
                    ex.Message);
            }
        }
          private async Task WriteLogEntryAsync(string category, object logEntry)
        {
            try
            {
                string fileName = $"{DateTime.Now:yyyyMMdd}_{category}.log";
                string filePath = Path.Combine(_notificationLogPath, fileName);
                
                string logLine = JsonConvert.SerializeObject(logEntry) + Environment.NewLine;
                
                await File.AppendAllTextAsync(filePath, logLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Lỗi khi ghi log vào tệp: {Error}",
                    ex.Message);
            }
        }
        
        /// <summary>
        /// Lấy lịch sử thông báo gần đây
        /// </summary>
        /// <param name="filter">Bộ lọc: "all", "config", "connectivity", "compliance"</param>
        /// <param name="page">Trang hiện tại, bắt đầu từ 1</param>
        /// <param name="pageSize">Kích thước trang</param>
        /// <returns>Danh sách thông báo và thông tin phân trang</returns>
        public async Task<Models.ViewModels.NotificationHistoryViewModel> GetNotificationHistoryAsync(
            string filter = "all", 
            int page = 1, 
            int pageSize = 20)
        {
            var result = new Models.ViewModels.NotificationHistoryViewModel
            {
                CurrentFilter = filter,
                Page = page,
                PageSize = pageSize
            };
            
            try
            {
                var allLogs = new List<Models.ViewModels.NotificationLogEntry>();
                string[] filesToSearch = Directory.GetFiles(_notificationLogPath, "*.log")
                    .OrderByDescending(f => f)  // Sắp xếp theo thời gian tạo giảm dần
                    .Take(7)  // Lấy tối đa log của 7 ngày gần đây
                    .ToArray();
                
                foreach (var file in filesToSearch)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    
                    // Bỏ qua các file không phù hợp với bộ lọc
                    if (filter != "all")
                    {
                        if (filter == "config" && !fileName.Contains("config_changes")) continue;
                        if (filter == "connectivity" && !fileName.Contains("connectivity")) continue;
                        if (filter == "compliance" && !fileName.Contains("compliance")) continue;
                    }
                    
                    if (File.Exists(file))
                    {
                        var lines = await File.ReadAllLinesAsync(file);
                        foreach (var line in lines)
                        {
                            try
                            {
                                var entry = JsonConvert.DeserializeObject<dynamic>(line);
                                var logEntry = new Models.ViewModels.NotificationLogEntry
                                {
                                    Type = entry.Type.ToString(),
                                    Router = entry.Router.ToString(),
                                    Timestamp = (DateTime)entry.Timestamp,
                                    Success = (bool)entry.Success,
                                    Details = entry.Details.ToString()
                                };
                                
                                if (entry.Type.ToString() == "Compliance")
                                {
                                    logEntry.Status = entry.Severity.ToString();
                                    logEntry.AdditionalInfo = entry.Rule.ToString();
                                }
                                else if (entry.Type.ToString() == "Connectivity")
                                {
                                    logEntry.Status = entry.Status.ToString();
                                }
                                else if (entry.Type.ToString() == "ConfigurationChange")
                                {
                                    logEntry.Status = entry.ChangeType.ToString();
                                }
                                
                                allLogs.Add(logEntry);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Không thể phân tích log entry: {Line}", line);
                            }
                        }
                    }
                }
                
                // Sắp xếp theo thời gian giảm dần
                var sortedLogs = allLogs
                    .OrderByDescending(l => l.Timestamp)
                    .ToList();
                
                result.TotalCount = sortedLogs.Count;
                
                // Lấy dữ liệu theo trang
                result.RecentNotifications = sortedLogs
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy lịch sử thông báo: {Error}", ex.Message);
                return result;
            }
        }
    }
}
