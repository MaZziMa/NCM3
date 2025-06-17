using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using NCM3.Services.Events;

namespace NCM3.Services
{
    public interface ITelegramNotificationService
    {
        Task SendConfigChangeNotificationAsync(string routerName, string changeType, string details, string priority = "Low");
        Task SendComplianceAlertAsync(string routerName, string ruleName, string severity, string details);
        Task SendConnectivityAlertAsync(string routerName, string status, string details);
        Task SendConfigurationChangedEventAsync(ConfigurationChangedEvent configChangedEvent);
        Task SendRouterAddedNotificationAsync(string routerName, string ipAddress, string model, string osVersion);
        Task SendDailySummaryAsync();
        Task<bool> SendTestMessageAsync();
        void Initialize();
    }    public class TelegramNotificationService : ITelegramNotificationService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string? _botToken;
        private readonly string? _chatId;
        private readonly ILogger<TelegramNotificationService> _logger;
        private readonly NotificationLogger? _notificationLogger;
        private readonly IConfiguration _configuration;
        private readonly bool _useProxy;
        private readonly string? _proxyApiUrl;
        
        private System.Timers.Timer? _consolidationTimer;
        private System.Timers.Timer? _dailySummaryTimer;
        private readonly ConcurrentDictionary<string, List<ConfigurationChangedEvent>> _pendingNotifications = new();
        private readonly ConcurrentBag<ConfigurationChangedEvent> _allEvents = new();
        
        private bool _isInitialized = false;        public TelegramNotificationService(
            IConfiguration configuration, 
            HttpClient httpClient, 
            ILogger<TelegramNotificationService> logger,
            NotificationLogger? notificationLogger = null)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _botToken = configuration["Telegram:BotToken"];
            _chatId = configuration["Telegram:ChatId"];
            _useProxy = configuration.GetValue<bool>("Telegram:UseProxy", false);
            _proxyApiUrl = configuration["Telegram:ProxyApiUrl"];
            _logger = logger;
            _notificationLogger = notificationLogger;
        }    public async Task SendConfigChangeNotificationAsync(string routerName, string changeType, string details, string priority = "Low")
        {
            if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
            {
                _logger.LogWarning("Telegram không được cấu hình. Bỏ qua gửi thông báo.");
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConfigurationChangeNotificationAsync(
                        routerName, 
                        changeType, 
                        "Bỏ qua do Telegram chưa được cấu hình", 
                        false).ConfigureAwait(false);
                }
                
                return; // Skip if Telegram is not configured
            }

            var message = $"🔔 *Thay đổi Cấu hình*\n\n" +
                         $"*Router:* {routerName}\n" +
                         $"*Loại thay đổi:* {changeType}\n" +
                         $"*Mức độ ưu tiên:* {priority}\n" +
                         $"*Chi tiết:*\n{details}";

            bool success = await SendTelegramMessageAsync(message).ConfigureAwait(false);
            
            // Ghi log thông báo
            if (_notificationLogger != null)
            {
                await _notificationLogger.LogConfigurationChangeNotificationAsync(
                    routerName, 
                    changeType, 
                    details, 
                    success).ConfigureAwait(false);
            }
        }
        public async Task SendComplianceAlertAsync(string routerName, string ruleName, string severity, string details)
        {
            if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
            {
                _logger.LogWarning("Telegram không được cấu hình. Bỏ qua gửi thông báo.");
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogComplianceNotificationAsync(
                        routerName, 
                        ruleName, 
                        severity,
                        "Bỏ qua do Telegram chưa được cấu hình", 
                        false).ConfigureAwait(false);
                }
                
                return;
            }

            var message = $"⚠️ *Cảnh báo tuân thủ*\n\n" +
                         $"*Router:* {routerName}\n" +
                         $"*Quy tắc:* {ruleName}\n" +
                         $"*Mức độ:* {severity}\n" +
                         $"*Chi tiết:*\n{details}";

            bool success = await SendTelegramMessageAsync(message).ConfigureAwait(false);
            
            // Ghi log thông báo
            if (_notificationLogger != null)
            {
                await _notificationLogger.LogComplianceNotificationAsync(
                    routerName, 
                    ruleName, 
                    severity,
                    details, 
                    success).ConfigureAwait(false);
            }
        }
        public async Task SendConnectivityAlertAsync(string routerName, string status, string details)
        {
            if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
            {
                _logger.LogWarning("Telegram không được cấu hình. Bỏ qua gửi thông báo.");
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConnectivityNotificationAsync(
                        routerName, 
                        status, 
                        "Bỏ qua do Telegram chưa được cấu hình", 
                        false).ConfigureAwait(false);
                }
                
                return;
            }

            var message = $"🔌 *Cảnh báo kết nối*\n\n" +
                         $"*Router:* {routerName}\n" +
                         $"*Trạng thái:* {status}\n" +
                         $"*Chi tiết:*\n{details}";

            bool success = await SendTelegramMessageAsync(message).ConfigureAwait(false);
            
            // Ghi log thông báo
            if (_notificationLogger != null)
            {
                await _notificationLogger.LogConnectivityNotificationAsync(
                    routerName, 
                    status, 
                    details, 
                    success).ConfigureAwait(false);
            }
        }    private async Task<bool> SendTelegramMessageAsync(string message)
    {
        try
        {
            string baseUrl = _useProxy && !string.IsNullOrEmpty(_proxyApiUrl) 
                ? _proxyApiUrl 
                : "https://api.telegram.org";
                
            var url = $"{baseUrl}/bot{_botToken}/sendMessage";
            string? parseMode = _configuration.GetValue<string>("Telegram:NotificationFormat", "MarkdownV2");
            parseMode ??= "MarkdownV2";
            bool enableMarkdown = _configuration.GetValue<bool>("Telegram:EnableMarkdownFormatting", true);
            
            // Format the message according to parse mode
            string formattedMessage = FormatMessage(message, parseMode, enableMarkdown);
            
            // Check if message is too long (Telegram limit is 4096 characters)
            const int telegramMaxMessageLength = 4096;
            
            if (formattedMessage.Length > telegramMaxMessageLength)
            {
                _logger.LogWarning("Message exceeds Telegram's 4096 character limit. Current length: {Length}. Truncating message.", formattedMessage.Length);
                
                // Find a good breaking point - preferably before a code block
                int truncateIndex = telegramMaxMessageLength - 100; // Leave room for the truncation notice
                
                // Try to find a natural break point like a newline
                int lastNewLine = formattedMessage.LastIndexOf('\n', truncateIndex);
                if (lastNewLine > telegramMaxMessageLength / 2)
                {
                    truncateIndex = lastNewLine;
                }
                
                // Truncate and add notice
                string truncatedMessage = formattedMessage.Substring(0, truncateIndex);
                truncatedMessage += "\n\n...[Nội dung bị cắt ngắn do quá dài]...";
                
                formattedMessage = truncatedMessage;
            }
            
            var payload = new
            {
                chat_id = _chatId,
                text = formattedMessage,
                parse_mode = parseMode
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json");

            _logger.LogDebug("Sending Telegram message with parse_mode: {ParseMode}", parseMode);
            _logger.LogTrace("Message content length: {Length}", formattedMessage.Length);

            HttpResponseMessage response;
            
            if (_useProxy && !string.IsNullOrEmpty(_proxyApiUrl))
            {
                // Use proxy configuration
                _logger.LogInformation("Sending Telegram message via proxy: {ProxyApiUrl}", _proxyApiUrl);
                
                // For Cloudflare worker proxy
                response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
            }
            else
            {
                // Direct connection
                response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogError("Telegram API error: {StatusCode} {Response}", response.StatusCode, errorResponse);
                return false;
            }

            _logger.LogInformation("Successfully sent Telegram notification");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Telegram notification: {Message}", ex.Message);
            return false;
        }
    }
    
    private string FormatMessage(string message, string parseMode, bool enableMarkdown)
    {
        // If we're using HTML format or markdown is disabled, ensure no Markdown formatting is present
        if (!enableMarkdown)
        {
            message = message.Replace("*", "")
                           .Replace("_", "")
                           .Replace("`", "");
            return message;
        }
        
        if (parseMode.Equals("HTML", StringComparison.OrdinalIgnoreCase))
        {
            // Properly convert text formatting to paired HTML tags
            return ConvertMarkdownToHtml(message);
        }
        
        if (parseMode.Equals("MarkdownV2", StringComparison.OrdinalIgnoreCase))
        {
            // Replace code blocks first
            message = message.Replace("```", "ːːː"); // Temporary replacement
            
            // Escape special characters for MarkdownV2
            message = message.Replace("\\", "\\\\") // Must be first
                           .Replace("_", "\\_")
                           .Replace("*", "\\*")
                           .Replace("[", "\\[")
                           .Replace("]", "\\]")
                           .Replace("(", "\\(")
                           .Replace(")", "\\)")
                           .Replace("~", "\\~")
                           .Replace("`", "\\`")
                           .Replace(">", "\\>")
                           .Replace("#", "\\#")
                           .Replace("+", "\\+")
                           .Replace("-", "\\-")
                           .Replace("=", "\\=")
                           .Replace("|", "\\|")
                           .Replace(".", "\\.")
                           .Replace("!", "\\!")
                           .Replace("{", "\\{")
                           .Replace("}", "\\}");

            // Restore code blocks
            message = message.Replace("ːːː", "```");
        }
        
        return message;
    }public void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }
            
            // Set up consolidation timer
            int consolidationInterval = _configuration.GetValue<int>("ChangeDetection:NotificationSettings:ConsolidationIntervalMinutes", 
                _configuration.GetValue<int>("Telegram:ConsolidationIntervalMinutes", 30));
            
            _logger.LogInformation("Setting up notification consolidation timer with interval of {Minutes} minutes", 
                consolidationInterval);
            
            // Dispose existing timer if any    
            if (_consolidationTimer != null)
            {
                _consolidationTimer.Stop();
                _consolidationTimer.Dispose();
            }
                
            _consolidationTimer = new System.Timers.Timer(consolidationInterval * 60 * 1000); // Convert to milliseconds
            _consolidationTimer.Elapsed += OnConsolidationTimerElapsed;
            _consolidationTimer.AutoReset = true;
            _consolidationTimer.Start();
              // Set up daily summary timer
            bool dailySummaryEnabled = _configuration.GetValue<bool>("ChangeDetection:NotificationSettings:DailySummaryEnabled", true);
            
            if (dailySummaryEnabled)
            {
                int dailySummaryHour = _configuration.GetValue<int>("ChangeDetection:NotificationSettings:DailySummaryHour", 
                    _configuration.GetValue<int>("Telegram:DailySummaryHour", 9));
                    
                int dailySummaryMinute = _configuration.GetValue<int>("ChangeDetection:NotificationSettings:DailySummaryMinute",
                    _configuration.GetValue<int>("Telegram:DailySummaryMinute", 0));
                
                _logger.LogInformation("Setting up daily summary timer for {Hour}:{Minute}", 
                    dailySummaryHour, dailySummaryMinute.ToString("00"));
                
                // Dispose existing timer if any
                if (_dailySummaryTimer != null)
                {
                    _dailySummaryTimer.Stop();
                    _dailySummaryTimer.Dispose();
                }
                
                var now = DateTime.Now;
                var summaryTime = new DateTime(now.Year, now.Month, now.Day, dailySummaryHour, dailySummaryMinute, 0);
                
                if (now > summaryTime)
                {
                    summaryTime = summaryTime.AddDays(1);
                }
                
                var timeToNextSummary = summaryTime - now;
                _logger.LogInformation("Next daily summary will run in {Hours} hours and {Minutes} minutes", 
                    timeToNextSummary.Hours, timeToNextSummary.Minutes);
                    
                _dailySummaryTimer = new System.Timers.Timer(timeToNextSummary.TotalMilliseconds);
                _dailySummaryTimer.Elapsed += OnDailySummaryTimerElapsed;
                _dailySummaryTimer.AutoReset = true;
                _dailySummaryTimer.Start();
            }
            else
            {
                // Make sure the timer is disposed if it was previously created but now disabled
                if (_dailySummaryTimer != null)
                {
                    _dailySummaryTimer.Stop();
                    _dailySummaryTimer.Dispose();
                    _dailySummaryTimer = null;
                }
                
                _logger.LogInformation("Daily summary notifications are disabled");
            }
            
            // Subscribe to the event bus if available
            try
            {
                var eventBus = GetEventBus();
                if (eventBus != null)
                {
                    eventBus.Subscribe<ConfigurationChangedEvent>(async (configChangedEvent) => 
                    {
                        await SendConfigurationChangedEventAsync(configChangedEvent);
                    });
                    _logger.LogInformation("Successfully subscribed to ConfigurationChangedEvent");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to event bus: {ErrorMessage}", ex.Message);
            }
            
            _isInitialized = true;
        }
        
        private IEventBus? GetEventBus()
        {
            try
            {
                // Create a service scope to resolve the event bus (which might be registered as singleton)
                var serviceProvider = _httpClient.GetType().Assembly
                    .GetType("Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions")?
                    .GetMethod("GetService")?
                    .MakeGenericMethod(typeof(IServiceProvider))
                    .Invoke(null, new object[] { _httpClient }) as IServiceProvider;
                    
                if (serviceProvider != null)
                {
                    return serviceProvider.GetService(typeof(IEventBus)) as IEventBus;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving event bus: {ErrorMessage}", ex.Message);
            }
            
            return null;
        }
          public async Task SendConfigurationChangedEventAsync(ConfigurationChangedEvent configChangedEvent)
        {
            if (configChangedEvent == null)
            {
                _logger.LogWarning("Received null configuration change event. Skipping notification.");
                return;
            }

            if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
            {
                _logger.LogWarning("Telegram không được cấu hình. Bỏ qua gửi thông báo.");
                return;
            }
            
            // Add to all events for summary
            _allEvents.Add(configChangedEvent);
            
            // Check if this is a high-priority notification that should be sent immediately
            var sendImmediatelyFor = _configuration
                .GetSection("ChangeDetection:NotificationSettings:SendImmediatelyForPriorities")
                .Get<List<string>>() ?? new List<string> { "High" };
                
            if (sendImmediatelyFor.Any(p => string.Equals(p, configChangedEvent.Priority, StringComparison.OrdinalIgnoreCase)))
            {
                await SendImmediateNotificationAsync(configChangedEvent).ConfigureAwait(false);
                return;
            }
            
            // Check if we should consolidate by router group
            bool consolidateByRouterGroup = _configuration.GetValue<bool>("ChangeDetection:NotificationSettings:ConsolidateByRouterGroup", true);
            
            // Get router key for grouping notifications
            string routerKey;
            if (consolidateByRouterGroup && !string.IsNullOrEmpty(configChangedEvent.Router.Group))
            {
                // Use router group for consolidation
                routerKey = $"Group:{configChangedEvent.Router.Group}";
                _logger.LogDebug("Using router group '{Group}' for consolidation of router {RouterName}", 
                    configChangedEvent.Router.Group, configChangedEvent.Router.Hostname);
            }
            else
            {
                // Use individual router for consolidation
                routerKey = configChangedEvent.Router.Hostname;
            }
            
            // Add to pending notifications for consolidation
            _pendingNotifications.AddOrUpdate(
                routerKey,
                new List<ConfigurationChangedEvent> { configChangedEvent },
                (_, existingList) => 
                {
                    existingList.Add(configChangedEvent);
                    
                    // Check if we've exceeded the warning threshold
                    int warningThreshold = _configuration.GetValue<int>(
                        "ChangeDetection:NotificationSettings:AlertThresholds:WarningChangesCount", 5);
                    int criticalThreshold = _configuration.GetValue<int>(
                        "ChangeDetection:NotificationSettings:AlertThresholds:CriticalChangesCount", 10);
                    
                    if (existingList.Count == warningThreshold)
                    {
                        // Log a warning about the number of pending changes
                        _logger.LogWarning("Warning threshold reached: {Count} pending changes for {RouterKey}", 
                            existingList.Count, routerKey);
                    }
                    else if (existingList.Count == criticalThreshold)
                    {
                        // Log a critical warning and trigger immediate processing for this router
                        _logger.LogCritical("Critical threshold reached: {Count} pending changes for {RouterKey}. Processing immediately.", 
                            existingList.Count, routerKey);
                            
                        // Schedule immediate processing for this router
                        Task.Run(async () => 
                        {
                            try
                            {
                                await ProcessConsolidatedNotificationsForKeyAsync(routerKey);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing immediate consolidated notifications for {RouterKey}", routerKey);
                            }
                        });
                    }
                    
                    return existingList;
                });
                
            _logger.LogDebug("Added configuration change for router {RouterName} to pending notifications queue under key {RouterKey}", 
                configChangedEvent.Router.Hostname, routerKey);
        }
          private async Task SendImmediateNotificationAsync(ConfigurationChangedEvent configChangedEvent)
        {
            _logger.LogInformation("Sending immediate notification for high-priority change on router {RouterName}", 
                configChangedEvent.Router.Hostname);
                
            bool sendDiffs = _configuration.GetValue<bool>("Telegram:SendDiffs", true);
            int maxDiffLines = _configuration.GetValue<int>("Telegram:MaxDiffLines", 10);
            
            string diffDetails = "Change details not available";
            
            if (sendDiffs && !string.IsNullOrEmpty(configChangedEvent.OldContent) && 
                !string.IsNullOrEmpty(configChangedEvent.NewContent))
            {
                diffDetails = GetDiffSummary(configChangedEvent.OldContent, configChangedEvent.NewContent, maxDiffLines);
            }
                
            var message = $"🚨 *IMPORTANT Configuration Change*\n\n" +
                         $"*Router:* {configChangedEvent.Router.Hostname}\n" +
                         $"*Detection Method:* {configChangedEvent.DetectionStrategy}\n" +
                         $"*Priority:* {configChangedEvent.Priority}\n" +
                         $"*Time:* {configChangedEvent.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
                         $"*Description:* {configChangedEvent.ChangeDescription}\n\n" +
                         $"*Changes:*\n```\n{diffDetails}\n```";
                         
            await SendTelegramMessageAsync(message).ConfigureAwait(false);
            
            // Log notification
            if (_notificationLogger != null)
            {
                await _notificationLogger.LogConfigurationChangeNotificationAsync(
                    configChangedEvent.Router.Hostname, 
                    "High Priority - " + configChangedEvent.ChangeDescription, 
                    diffDetails, 
                    true).ConfigureAwait(false);
            }
        }
          private async Task ProcessConsolidatedNotificationsAsync()
        {
            _logger.LogInformation("Processing consolidated notifications");
            
            int count = _pendingNotifications.Count;
            if (count == 0)
            {
                _logger.LogInformation("No pending notifications to process");
                return;
            }
            
            // Process each router's or group's notifications
            foreach (var routerKey in _pendingNotifications.Keys.ToArray())
            {
                await ProcessConsolidatedNotificationsForKeyAsync(routerKey);
            }
        }
          private async Task ProcessConsolidatedNotificationsForKeyAsync(string routerKey)
        {
            if (!_pendingNotifications.TryGetValue(routerKey, out var events) || events.Count == 0)
            {
                _logger.LogInformation("No pending notifications to process for {RouterKey}", routerKey);
                return;
            }
            
            _logger.LogInformation("Processing consolidated notifications for {RouterKey} with {Count} events", 
                routerKey, events.Count);
                
            bool sendDiffs = _configuration.GetValue<bool>("Telegram:SendDiffs", true);
            int maxDiffLines = _configuration.GetValue<int>("Telegram:MaxDiffLines", 10);
            int maxChangesPerMessage = _configuration.GetValue<int>(
                "ChangeDetection:NotificationSettings:MaxChangesPerConsolidatedMessage", 10);
            
            // Build a consolidated message
            var message = new StringBuilder();
            
            // Check if this is a group or individual router
            bool isGroup = routerKey.StartsWith("Group:", StringComparison.OrdinalIgnoreCase);
            string displayName = isGroup ? routerKey.Substring(6) : routerKey; // Remove "Group:" prefix if present
            
            message.AppendLine($"📊 *Consolidated Configuration Changes*");
            message.AppendLine();
            
            if (isGroup)
            {
                message.AppendLine($"*Router Group:* {displayName}");
                
                // Count unique routers in this group
                var uniqueRouters = events.Select(e => e.Router.Hostname).Distinct().ToList();
                message.AppendLine($"*Affected Routers:* {uniqueRouters.Count}");
                message.AppendLine($"*Routers:* {string.Join(", ", uniqueRouters.Take(5))}{(uniqueRouters.Count > 5 ? $" and {uniqueRouters.Count - 5} more..." : "")}");
            }
            else
            {
                message.AppendLine($"*Router:* {displayName}");
            }
            
            message.AppendLine($"*Number of changes:* {events.Count}");
            message.AppendLine($"*Time period:* {events.Min(e => e.Timestamp).ToLocalTime():HH:mm} - {events.Max(e => e.Timestamp).ToLocalTime():HH:mm}");
            message.AppendLine();
            
            // Group by priority
            var byPriority = events
                .GroupBy(e => e.Priority)
                .OrderBy(g => g.Key == "High" ? 0 : g.Key == "Medium" ? 1 : 2);
                
            message.AppendLine("*Changes by priority:*");
            foreach (var priorityGroup in byPriority)
            {
                string priorityEmoji = priorityGroup.Key == "High" ? "🔴" : 
                                       priorityGroup.Key == "Medium" ? "🟠" : "🟢";
                message.AppendLine($"{priorityEmoji} *{priorityGroup.Key}:* {priorityGroup.Count()} changes");
            }
            
            message.AppendLine();
            message.AppendLine("*Most significant changes:*");
            
            // Get the most recent high-priority event, if any
            var mostSignificantEvent = events
                .OrderBy(e => e.Priority == "High" ? 0 : e.Priority == "Medium" ? 1 : 2)
                .ThenByDescending(e => e.Timestamp)
                .FirstOrDefault();
                
            if (mostSignificantEvent != null && sendDiffs)
            {
                string diffDetails = GetDiffSummary(
                    mostSignificantEvent.OldContent, 
                    mostSignificantEvent.NewContent, 
                    maxDiffLines);
                    
                message.AppendLine("```");
                message.AppendLine(diffDetails);
                message.AppendLine("```");
            }
            
            await SendTelegramMessageAsync(message.ToString()).ConfigureAwait(false);
            
            // Log consolidated notification
            if (_notificationLogger != null)
            {
                await _notificationLogger.LogConfigurationChangeNotificationAsync(
                    routerKey, 
                    "Consolidated Changes", 
                    $"{events.Count} changes detected between {events.Min(e => e.Timestamp)} and {events.Max(e => e.Timestamp)}", 
                    true).ConfigureAwait(false);
            }
            
            // Remove processed notifications
            List<ConfigurationChangedEvent>? removedEvents;
            _pendingNotifications.TryRemove(routerKey, out removedEvents);
        }
          public async Task SendDailySummaryAsync()
        {
            _logger.LogInformation("Generating daily notification summary");
            
            if (_allEvents.IsEmpty)
            {
                _logger.LogInformation("No events to include in the daily summary");
                return;
            }
            
            // Filter events from the last 24 hours
            var last24Hours = DateTime.UtcNow.AddDays(-1);
            
            // Thread-safe copy of events for processing
            var recentEvents = _allEvents
                .Where(e => e != null && e.Timestamp >= last24Hours)
                .ToList();
            
            if (recentEvents.Count == 0)
            {
                _logger.LogInformation("No recent events to include in the daily summary");
                return;
            }
            
            var message = new StringBuilder();
            message.AppendLine($"📅 *Daily Configuration Change Summary*");
            message.AppendLine($"*Period:* {last24Hours.ToLocalTime():yyyy-MM-dd HH:mm} - {DateTime.Now:yyyy-MM-dd HH:mm}");
            message.AppendLine();
            
            // Group by router
            var byRouter = recentEvents
                .Where(e => e.Router != null)
                .GroupBy(e => e.Router.Hostname)
                .OrderBy(g => g.Key);
                
            message.AppendLine($"*Total changes:* {recentEvents.Count} across {byRouter.Count()} routers");
            message.AppendLine();
            
            // Group by priority
            var byPriority = recentEvents
                .GroupBy(e => e.Priority ?? "Unknown")
                .OrderBy(g => g.Key == "High" ? 0 : g.Key == "Medium" ? 1 : g.Key == "Low" ? 2 : 3);
                
            message.AppendLine("*Changes by priority:*");
            foreach (var priorityGroup in byPriority)
            {
                string priorityEmoji = priorityGroup.Key == "High" ? "🔴" : 
                                     priorityGroup.Key == "Medium" ? "🟠" : "🟢";
                message.AppendLine($"{priorityEmoji} *{priorityGroup.Key}:* {priorityGroup.Count()} changes");
            }
            
            message.AppendLine();
            message.AppendLine("*Changes by router:*");
            
            foreach (var routerGroup in byRouter.Take(15)) // Limit to avoid message too long
            {
                message.AppendLine($"• *{routerGroup.Key}:* {routerGroup.Count()} changes");
                
                // Group by detection strategy
                var byStrategy = routerGroup
                    .GroupBy(e => e.DetectionStrategy ?? "Unknown")
                    .OrderBy(g => g.Key);
                    
                foreach (var strategyGroup in byStrategy)
                {
                    message.AppendLine($"  - {strategyGroup.Key}: {strategyGroup.Count()} changes");
                }
            }
            
            // If there are more routers than we displayed, add a note
            if (byRouter.Count() > 15)
            {
                message.AppendLine($"\n*Note:* {byRouter.Count() - 15} more routers not shown.");
            }
            
            await SendTelegramMessageAsync(message.ToString()).ConfigureAwait(false);
            
            // Log daily summary
            if (_notificationLogger != null)
            {
                await _notificationLogger.LogConfigurationChangeNotificationAsync(
                    "All Routers", 
                    "Daily Summary", 
                    $"{recentEvents.Count} changes across {byRouter.Count()} routers in the last 24 hours", 
                    true).ConfigureAwait(false);
            }
        }          private string GetDiffSummary(string oldContent, string newContent, int maxLines)
        {
            try
            {
                // Get diff settings from configuration
                bool ignoreWhitespace = _configuration.GetValue<bool>("ChangeDetection:DiffGeneration:IgnoreWhitespace", true);
                bool ignoreCase = _configuration.GetValue<bool>("ChangeDetection:DiffGeneration:IgnoreCase", false);
                
                // Apply filters based on configuration
                bool ignoreCommentChanges = _configuration.GetValue<bool>("ChangeDetection:NotificationSettings:FilterRules:IgnoreCommentChanges", true);
                bool ignoreDateTimeChanges = _configuration.GetValue<bool>("ChangeDetection:NotificationSettings:FilterRules:IgnoreDateTimeChanges", true);
                
                // More aggressive filtering to reduce message size
                bool compactMode = true; // Always use compact mode to reduce size
                
                // Simple diff implementation - split and compare lines
                var oldLines = FilterLines(oldContent, ignoreWhitespace, ignoreCase, ignoreCommentChanges, ignoreDateTimeChanges);
                var newLines = FilterLines(newContent, ignoreWhitespace, ignoreCase, ignoreCommentChanges, ignoreDateTimeChanges);
                
                var addedLines = newLines.Except(oldLines).ToList();
                var removedLines = oldLines.Except(newLines).ToList();
                
                var result = new StringBuilder();
                
                // Add a small summary
                int totalChanges = addedLines.Count + removedLines.Count;
                result.AppendLine($"Tổng thay đổi: {totalChanges} dòng ({removedLines.Count} xóa, {addedLines.Count} thêm)");
                
                if (totalChanges > 0)
                {
                    result.AppendLine();
                    
                    if (compactMode)
                    {
                        // In compact mode, just show limited number of the most significant changes
                        int shownChanges = Math.Min(maxLines, Math.Max(1, totalChanges));
                        
                        // Prioritize important keywords in the changes
                        var prioritizedRemovals = PrioritizeImportantChanges(removedLines);
                        var prioritizedAdditions = PrioritizeImportantChanges(addedLines);
                        
                        // Show most important removed lines first (with - prefix)
                        int removalsToShow = Math.Min(prioritizedRemovals.Count, maxLines / 2);
                        if (removalsToShow > 0)
                        {
                            result.AppendLine("Nội dung bị xóa:");
                            foreach (var line in prioritizedRemovals.Take(removalsToShow))
                            {
                                result.AppendLine($"- {line}");
                            }
                            
                            if (removedLines.Count > removalsToShow)
                            {
                                result.AppendLine($"- ... và {removedLines.Count - removalsToShow} dòng khác...");
                            }
                        }
                        
                        if (prioritizedAdditions.Any() && prioritizedRemovals.Any())
                        {
                            result.AppendLine();
                        }
                        
                        // Show most important added lines (with + prefix)
                        int additionsToShow = Math.Min(prioritizedAdditions.Count, maxLines / 2);
                        if (additionsToShow > 0)
                        {
                            result.AppendLine("Nội dung mới thêm vào:");
                            foreach (var line in prioritizedAdditions.Take(additionsToShow))
                            {
                                result.AppendLine($"+ {line}");
                            }
                            
                            if (addedLines.Count > additionsToShow)
                            {
                                result.AppendLine($"+ ... và {addedLines.Count - additionsToShow} dòng khác...");
                            }
                        }
                    }
                    else
                    {
                        // Standard mode - shows equal number of removed and added lines
                        // Show removed lines first (with - prefix)
                        foreach (var line in removedLines.Take(maxLines / 2))
                        {
                            result.AppendLine($"- {line}");
                        }
                        
                        if (removedLines.Count > maxLines / 2)
                        {
                            result.AppendLine($"- ... và {removedLines.Count - maxLines / 2} dòng xóa khác...");
                        }
                        
                        if (removedLines.Any() && addedLines.Any())
                        {
                            result.AppendLine("---");
                        }
                        
                        // Then show added lines (with + prefix)
                        foreach (var line in addedLines.Take(maxLines / 2))
                        {
                            result.AppendLine($"+ {line}");
                        }
                        
                        if (addedLines.Count > maxLines / 2)
                        {
                            result.AppendLine($"+ ... và {addedLines.Count - maxLines / 2} dòng thêm khác...");
                        }
                    }
                }
                else
                {
                    result.AppendLine("Không phát hiện thay đổi");
                }
                
                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating diff summary: {Message}", ex.Message);
                return "Error generating diff";
            }
        }
        
        private List<string> FilterLines(string content, bool ignoreWhitespace, bool ignoreCase, bool ignoreCommentChanges, bool ignoreDateTimeChanges)
        {
            return content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => {
                    // Apply filters based on configuration
                    if (ignoreWhitespace)
                        line = line.Trim();
                    if (ignoreCase)
                        line = line.ToLowerInvariant();
                    if (ignoreCommentChanges && (line.TrimStart().StartsWith("!") || line.TrimStart().StartsWith("//")))
                        return null; // Skip comment lines
                    if (ignoreDateTimeChanges && (line.Contains("uptime") || line.Contains("Last configuration change")))
                        return null; // Skip timestamp lines
                    return line;
                })
                .Where(line => line != null)
                .ToList();
        }
        
        private List<string> PrioritizeImportantChanges(List<string> lines)
        {
            // Keywords that indicate important configuration changes
            var importantKeywords = new[] {
                "interface", "ip address", "router", "hostname", "enable", "password", 
                "crypto", "access-list", "route", "nat", "firewall", "policy", "vlan", 
                "security", "username", "service", "tunnel"
            };
            
            // Prioritize lines containing important keywords
            var prioritized = lines
                .Select(line => new { 
                    Line = line, 
                    Priority = importantKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)) ? 0 : 1
                })
                .OrderBy(x => x.Priority)
                .Select(x => x.Line)
                .ToList();
                
            return prioritized;
        }
        
        private string ConvertMarkdownToHtml(string markdown)
        {
            var result = new StringBuilder();
            var chars = markdown.ToCharArray();
            var i = 0;
            var openTags = new Stack<char>();

            while (i < chars.Length)
            {
                switch (chars[i])
                {
                    case '*':
                        if (openTags.Count > 0 && openTags.Peek() == '*')
                        {
                            result.Append("</b>");
                            openTags.Pop();
                        }
                        else
                        {
                            result.Append("<b>");
                            openTags.Push('*');
                        }
                        break;
                    case '_':
                        if (openTags.Count > 0 && openTags.Peek() == '_')
                        {
                            result.Append("</i>");
                            openTags.Pop();
                        }
                        else
                        {
                            result.Append("<i>");
                            openTags.Push('_');
                        }
                        break;
                    case '`':
                        if (i + 2 < chars.Length && chars[i + 1] == '`' && chars[i + 2] == '`')
                        {
                            // Handle code blocks (```
                            if (openTags.Count > 0 && openTags.Peek() == '3')
                            {
                                result.Append("</pre>");
                                openTags.Pop();
                            }
                            else
                            {
                                result.Append("<pre>");
                                openTags.Push('3');
                            }
                            i += 2; // Skip the next two backticks
                        }
                        else
                        {
                            // Handle inline code (`
                            if (openTags.Count > 0 && openTags.Peek() == '`')
                            {
                                result.Append("</code>");
                                openTags.Pop();
                            }
                            else
                            {
                                result.Append("<code>");
                                openTags.Push('`');
                            }
                        }
                        break;
                    default:
                        result.Append(chars[i]);
                        break;
                }
                i++;
            }

            // Close any remaining open tags
            while (openTags.Count > 0)
            {
                var tag = openTags.Pop();
                switch (tag)
                {
                    case '*':
                        result.Append("</b>");
                        break;
                    case '_':
                        result.Append("</i>");
                        break;
                    case '`':
                        result.Append("</code>");
                        break;
                    case '3':
                        result.Append("</pre>");
                        break;
                }
            }

            return result.ToString();
        }
          // Flag to track whether Dispose has been called
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            if (disposing)
            {
                // Dispose managed resources
                if (_consolidationTimer != null)
                {
                    _consolidationTimer.Elapsed -= OnConsolidationTimerElapsed;
                    _consolidationTimer.Stop();
                    _consolidationTimer.Dispose();
                    _consolidationTimer = null;
                }
                
                if (_dailySummaryTimer != null)
                {
                    _dailySummaryTimer.Elapsed -= OnDailySummaryTimerElapsed;
                    _dailySummaryTimer.Stop();
                    _dailySummaryTimer.Dispose();
                    _dailySummaryTimer = null;
                }
                
                // Clear collections
                _pendingNotifications.Clear();
            }
            
            _disposed = true;
        }
        
        // Finalizer
        ~TelegramNotificationService()
        {
            Dispose(false);
        }

        private async void OnConsolidationTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                await ProcessConsolidatedNotificationsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing consolidated notifications: {Message}", ex.Message);
            }
        }
          private async void OnDailySummaryTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                await SendDailySummaryAsync().ConfigureAwait(false);
                
                // Readjust timer for next 24 hours
                if (_dailySummaryTimer != null)
                {
                    _dailySummaryTimer.Interval = 24 * 60 * 60 * 1000; // 24 hours in milliseconds
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending daily summary: {Message}", ex.Message);
            }
        }        public async Task<bool> SendTestMessageAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
                {
                    _logger.LogWarning("Telegram không được cấu hình. Bỏ qua gửi thông báo kiểm tra.");
                    
                    // Ghi log thông báo
                    if (_notificationLogger != null)
                    {
                        await _notificationLogger.LogConfigurationChangeNotificationAsync(
                            "Kiểm tra kết nối", 
                            "Thử nghiệm", 
                            "Bỏ qua do Telegram chưa được cấu hình", 
                            false).ConfigureAwait(false);
                    }
                    
                    return false; // Telegram is not configured
                }
                
                // Thu thập thông tin hệ thống và cấu hình
                string proxyStatus = _useProxy && !string.IsNullOrEmpty(_proxyApiUrl) 
                    ? $"*Proxy:* Bật ({_proxyApiUrl})"
                    : "*Proxy:* Tắt (kết nối trực tiếp)";
                
                string parseMode = _configuration.GetValue<string>("Telegram:NotificationFormat", "MarkdownV2") ?? "MarkdownV2";
                bool enableMarkdown = _configuration.GetValue<bool>("Telegram:EnableMarkdownFormatting", true);
                string appVersion = typeof(TelegramNotificationService).Assembly.GetName().Version?.ToString() ?? "Unknown";
                
                // Lấy thông tin thêm về hệ thống
                string osInfo = Environment.OSVersion.ToString();
                string dotnetInfo = Environment.Version.ToString();
                
                // Tạo tin nhắn kiểm tra với thông tin chi tiết hơn
                var message = $"✅ *Kiểm tra kết nối Telegram*\n\n" +
                             $"*Thời gian:* {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                             $"*Định dạng:* {parseMode}\n" +
                             $"{proxyStatus}\n\n" +
                             $"*Thông tin hệ thống:*\n" +
                             $"NCM3 phiên bản: {appVersion}\n" +
                             $"OS: {osInfo}\n" +
                             $".NET: {dotnetInfo}\n\n" +
                             $"*Thông báo:* Kết nối đến Telegram hoạt động bình thường.\n\n" +
                             $"Nếu bạn nhìn thấy thông báo này, NCM3 đã kết nối thành công đến bot Telegram.";
                
                // Thử với số lần thử lại nếu gặp lỗi
                int maxRetries = 2; // Thử tối đa 3 lần (lần đầu + 2 lần thử lại)
                int currentTry = 0;
                bool success = false;
                Exception? lastException = null;
                
                while (currentTry <= maxRetries && !success)
                {
                    try
                    {
                        if (currentTry > 0)
                        {
                            _logger.LogInformation("Đang thử lại kết nối Telegram (lần {RetryCount})...", currentTry);
                            message += $"\n\n(Thử lại lần {currentTry})";
                            
                            // Đợi một chút trước khi thử lại với thời gian tăng dần
                            await Task.Delay(1000 * currentTry * 2).ConfigureAwait(false);
                        }
                        
                        success = await SendTelegramMessageAsync(message).ConfigureAwait(false);
                        
                        if (success)
                            break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _logger.LogWarning(ex, "Lỗi khi gửi tin nhắn kiểm tra (lần thử {RetryCount}): {Message}", 
                            currentTry, ex.Message);
                    }
                    
                    currentTry++;
                }
                
                // Thu thập thông tin chi tiết về kết nối
                string connectionDetails = success 
                    ? "Kết nối thành công" 
                    : $"Không thể kết nối sau {currentTry + 1} lần thử";
                    
                if (lastException != null)
                {
                    connectionDetails += $" - Lỗi: {lastException.Message}";
                    
                    // Kiểm tra các loại lỗi phổ biến và đề xuất giải pháp
                    if (lastException is HttpRequestException httpEx)
                    {
                        if (httpEx.Message.Contains("Forbidden") || httpEx.Message.Contains("403"))
                            connectionDetails += " - Bot Token có thể không hợp lệ hoặc bot đã bị vô hiệu hóa.";
                        else if (httpEx.Message.Contains("Not Found") || httpEx.Message.Contains("404"))
                            connectionDetails += " - API Telegram không tìm thấy. Bot Token có thể không đúng.";
                        else if (httpEx.Message.Contains("Bad Request") || httpEx.Message.Contains("400"))
                            connectionDetails += " - Chat ID có thể không chính xác hoặc bot không có quyền gửi tin nhắn đến chat này.";
                        else if (httpEx.Message.Contains("Timeout") || httpEx.Message.Contains("timed out"))
                            connectionDetails += " - Kết nối tới Telegram bị timeout. Kiểm tra cấu hình mạng và proxy.";
                    }
                }
                
                // Ghi log kết quả
                if (success)
                {
                    _logger.LogInformation("Kết nối Telegram thành công{RetryInfo}",
                        currentTry > 0 ? $" sau {currentTry} lần thử lại" : "");
                }
                else
                {
                    _logger.LogError(lastException, "Kiểm tra kết nối Telegram thất bại sau {RetryCount} lần thử",
                        currentTry);
                }
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConfigurationChangeNotificationAsync(
                        "Kiểm tra kết nối", 
                        "Thử nghiệm", 
                        $"Kiểm tra kết nối Telegram: {connectionDetails}", 
                        success).ConfigureAwait(false);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi tin nhắn kiểm tra đến Telegram: {Message}", ex.Message);
                
                // Ghi log thông báo lỗi
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConfigurationChangeNotificationAsync(
                        "Kiểm tra kết nối", 
                        "Thử nghiệm", 
                        $"Lỗi không xác định: {ex.Message}", 
                        false).ConfigureAwait(false);
                }
                
                return false;
            }
        }

        public async Task SendRouterAddedNotificationAsync(string routerName, string ipAddress, string model, string osVersion)
        {
            if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(_chatId))
            {
                _logger.LogWarning("Telegram không được cấu hình. Bỏ qua gửi thông báo.");
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConfigurationChangeNotificationAsync(
                        routerName, 
                        "Thêm router mới", 
                        "Bỏ qua do Telegram chưa được cấu hình", 
                        false).ConfigureAwait(false);
                }
                
                return; // Skip if Telegram is not configured
            }

            var message = $"🆕 *Thêm Router Mới*\n\n" +
                         $"*Tên Router:* {routerName}\n" +
                         $"*Địa chỉ IP:* {ipAddress}\n" +
                         $"*Model:* {model}\n" +
                         $"*Phiên bản OS:* {osVersion}\n" +
                         $"*Thời gian:* {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            try
            {
                bool success = await SendTelegramMessageAsync(message).ConfigureAwait(false);
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConfigurationChangeNotificationAsync(
                        routerName, 
                        "Thêm router mới", 
                        "Thông báo gửi thành công", 
                        true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi thông báo Telegram về router mới {RouterName}: {Message}", routerName, ex.Message);
                
                // Ghi log thông báo
                if (_notificationLogger != null)
                {
                    await _notificationLogger.LogConfigurationChangeNotificationAsync(
                        routerName, 
                        "Thêm router mới", 
                        $"Lỗi khi gửi thông báo: {ex.Message}", 
                        false).ConfigureAwait(false);
                }
            }
        }
    }
}