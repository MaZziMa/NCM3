using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using NCM3.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NCM3.Constants;

namespace NCM3.Services
{
    public class ConfigurationManagementService
    {
        private readonly NCMDbContext _context;
        private readonly IDiffer _differ;
        private readonly ISideBySideDiffBuilder _diffBuilder;
        private readonly ILogger<ConfigurationManagementService> _logger;
        private readonly ITelegramNotificationService _telegramService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private NotificationHelper? _notificationHelper;

        public ConfigurationManagementService(
            NCMDbContext context,
            ILogger<ConfigurationManagementService> logger,
            ITelegramNotificationService telegramService,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _context = context;
            _differ = new Differ();
            _diffBuilder = new SideBySideDiffBuilder(_differ);
            _logger = logger;
            _telegramService = telegramService;
            _configuration = configuration;
        }
        
        // Thiết lập NotificationHelper (để tránh lỗi circular dependency)
        public void SetNotificationHelper(NotificationHelper notificationHelper)
        {
            _notificationHelper = notificationHelper;
        }
        
        // So sánh cấu hình giữa các phiên bản
        public async Task<SideBySideDiffModel> CompareConfigurationsAsync(int configId1, int configId2)
        {
            _logger.LogInformation("So sánh cấu hình giữa ID {ConfigId1} và ID {ConfigId2}", configId1, configId2);
            
            var config1 = await _context.RouterConfigurations.FirstOrDefaultAsync(c => c.Id == configId1);
            var config2 = await _context.RouterConfigurations.FirstOrDefaultAsync(c => c.Id == configId2);
            
            if (config1 == null || config2 == null)
            {
                _logger.LogWarning("Không tìm thấy một trong các cấu hình để so sánh. ConfigId1: {ConfigId1}, ConfigId2: {ConfigId2}", 
                    configId1, configId2);
                return new SideBySideDiffModel();
            }
            
            // Ensure content is not null
            string content1 = config1.Content ?? string.Empty;
            string content2 = config2.Content ?? string.Empty;
            
            var diff = _diffBuilder.BuildDiffModel(content1, content2);
            return diff;
        }
        
        // So sánh cấu hình hiện tại với template
        public async Task<SideBySideDiffModel> CompareWithTemplateAsync(int configId, int templateId)
        {
            var config = await _context.RouterConfigurations.FirstOrDefaultAsync(c => c.Id == configId);
            var template = await _context.ConfigTemplates.FirstOrDefaultAsync(t => t.Id == templateId);
            
            if (config == null || template == null)
            {
                return new SideBySideDiffModel();
            }
            
            var diff = _diffBuilder.BuildDiffModel(template.Content, config.Content);
            return diff;
        }
        
        // Tìm kiếm trong cấu hình
        public async Task<List<SearchResult>> SearchInConfigurationsAsync(string searchTerm, int? routerId = null)
        {
            var results = new List<SearchResult>();
            
            try
            {
                IQueryable<RouterConfiguration> query = _context.RouterConfigurations
                    .Include(c => c.Router);
                
                if (routerId.HasValue)
                {
                    query = query.Where(c => c.RouterId == routerId.Value);
                }
                
                var configurations = await query.ToListAsync();
                
                foreach (var config in configurations)
                {
                    var matches = SearchInConfig(config.Content, searchTerm);
                    
                    if (matches.Any())
                    {
                        string routerName = "Unknown";
                        if (config.Router != null)
                        {
                            routerName = config.Router.Hostname;
                        }
                        
                        results.Add(new SearchResult
                        {
                            RouterId = config.RouterId,
                            RouterName = routerName,
                            ConfigId = config.Id,
                            BackupDate = config.BackupDate,
                            Version = config.Version,
                            Matches = matches
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Xử lý ngoại lệ
            }
            
            return results;
        }

        private List<Match> SearchInConfig(string content, string searchTerm)
        {
            List<Match> matches = new List<Match>();
            
            try
            {
                Regex regex = new Regex(searchTerm, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                var lines = content.Split('\n');
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var regexMatches = regex.Matches(line);
                    
                    if (regexMatches.Count > 0)
                    {
                        matches.Add(new Match
                        {
                            LineNumber = i + 1,
                            LineContent = line,
                            MatchCount = regexMatches.Count
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Xử lý ngoại lệ
            }
            
            return matches;
        }
        
        // Kiểm tra tuân thủ
        public async Task<List<ComplianceResult>> CheckComplianceAsync(int configId)
        {
            var results = new List<ComplianceResult>();
            
            var config = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId);
            
            if (config == null)
            {
                return results;
            }
            
            var rules = await _context.ComplianceRules.ToListAsync();
            
            // Lọc các quy tắc phù hợp với loại thiết bị
            var filteredRules = rules.Where(r => r.IsActive && 
                (string.IsNullOrEmpty(r.DeviceType) || 
                 (config.Router != null && r.DeviceType == config.Router.Model))).ToList();
            
            foreach (var rule in filteredRules)
            {
                var complianceResult = new ComplianceResult
                {
                    RouterId = config.RouterId,
                    ConfigurationId = config.Id,
                    RuleId = rule.Id,
                    CheckDate = DateTime.Now
                };
                
                try
                {
                    bool patternFound = false;
                    string? matchedContent = null;
                    int? lineNumber = null;
                    
                    // Tìm pattern trong cấu hình
                    var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    var match = regex.Match(config.Content);
                    
                    if (match.Success)
                    {
                        patternFound = true;
                        matchedContent = match.Value;
                        
                        // Tính số dòng
                        var textBeforeMatch = config.Content.Substring(0, match.Index);
                        lineNumber = textBeforeMatch.Count(c => c == '\n') + 1;
                    }
                    
                    complianceResult.Result = patternFound;
                    complianceResult.IsCompliant = (patternFound == rule.ExpectedResult);
                    complianceResult.MatchedContent = matchedContent;
                    complianceResult.LineNumber = lineNumber;
                }
                catch (Exception)
                {
                    complianceResult.Result = false;
                    complianceResult.IsCompliant = false;
                }
                
                results.Add(complianceResult);
            }
            
            return results;
        }

        public async Task<RouterConfiguration> SaveConfigurationAsync(int routerId, string configText)
        {
            var router = await _context.Routers
                .Include(r => r.RouterConfigurations)
                .FirstOrDefaultAsync(r => r.Id == routerId);

            if (router == null)
            {
                throw new ArgumentException("Router not found");
            }

            var lastConfig = router.RouterConfigurations.OrderByDescending(c => c.BackupDate).FirstOrDefault();
            var hasChanges = lastConfig == null || lastConfig.Content != configText;

            if (hasChanges)
            {
                var newConfig = new RouterConfiguration
                {
                    RouterId = routerId,
                    Content = configText,
                    BackupDate = DateTime.UtcNow,
                    Version = lastConfig != null 
                        ? $"v{int.Parse(lastConfig.Version?.Replace("v", "") ?? "0") + 1}"
                        : "v1"
                };

                _context.RouterConfigurations.Add(newConfig);
                await _context.SaveChangesAsync();

                // Sử dụng NotificationHelper nếu đã được thiết lập
                if (_notificationHelper != null)
                {
                    await _notificationHelper.SendConfigurationChangeNotificationAsync(
                        router.Hostname,
                        lastConfig != null ? "Cập nhật cấu hình" : "Tạo cấu hình mới",
                        lastConfig?.Content ?? string.Empty,
                        configText
                    );
                }
                else
                {
                    // Sử dụng cách cũ nếu chưa thiết lập NotificationHelper
                    var diffDetails = lastConfig != null 
                        ? await GetDiffDetailsAsync(lastConfig.Content, configText)
                        : "Cấu hình mới được tạo";

                    await _telegramService.SendConfigChangeNotificationAsync(
                        router.Hostname,
                        lastConfig != null ? "Cập nhật cấu hình" : "Tạo cấu hình mới",
                        diffDetails
                    );
                }

                return newConfig;
            }

            return lastConfig;
        }

        private Task<string> GetDiffDetailsAsync(string oldConfig, string newConfig)
        {
            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(oldConfig, newConfig);

            // Get the maximum number of diff lines from configuration
            int maxDiffLines = _configuration.GetValue<int>("Telegram:MaxDiffLines", 10);
            // Make sure we have a reasonable value
            maxDiffLines = Math.Max(5, maxDiffLines);

            // Get changes by type
            var deletedLines = diff.Lines.Where(l => l.Type == ChangeType.Deleted).ToList();
            var insertedLines = diff.Lines.Where(l => l.Type == ChangeType.Inserted).ToList();
            
            // Count total changes
            int totalChanges = deletedLines.Count + insertedLines.Count;
            
            var changes = new StringBuilder();
            changes.AppendLine($"Tổng thay đổi: {totalChanges} dòng ({deletedLines.Count} xóa, {insertedLines.Count} thêm)");
            
            // Limit how many lines we show of each type
            int deletedToShow = Math.Min(deletedLines.Count, maxDiffLines / 2);
            int insertedToShow = Math.Min(insertedLines.Count, maxDiffLines / 2);
            
            // If one type has fewer changes, allow the other type to use more of the quota
            if (deletedToShow < maxDiffLines / 2)
            {
                insertedToShow = Math.Min(insertedLines.Count, maxDiffLines - deletedToShow);
            }
            else if (insertedToShow < maxDiffLines / 2)
            {
                deletedToShow = Math.Min(deletedLines.Count, maxDiffLines - insertedToShow);
            }
            
            // Show deleted lines first
            if (deletedLines.Any())
            {
                changes.AppendLine("\nNội dung bị xóa:");
                foreach (var line in deletedLines.Take(deletedToShow))
                {
                    changes.AppendLine($"- {line.Text}");
                }
                
                if (deletedLines.Count > deletedToShow)
                {
                    changes.AppendLine($"- ... và {deletedLines.Count - deletedToShow} dòng xóa khác...");
                }
            }
            
            // Then show inserted lines
            if (insertedLines.Any())
            {
                changes.AppendLine("\nNội dung mới thêm vào:");
                foreach (var line in insertedLines.Take(insertedToShow))
                {
                    changes.AppendLine($"+ {line.Text}");
                }
                
                if (insertedLines.Count > insertedToShow)
                {
                    changes.AppendLine($"+ ... và {insertedLines.Count - insertedToShow} dòng thêm khác...");
                }
            }

            return Task.FromResult(changes.ToString());
        }
        
        // Thêm phương thức công khai để so sánh cấu hình từ bên ngoài
        public Task<string> GetConfigurationDiffAsync(string oldConfig, string newConfig)
        {
            return GetDiffDetailsAsync(oldConfig, newConfig);
        }
    }

    // Lớp kết quả tìm kiếm
    public class SearchResult
    {
        public int RouterId { get; set; }
        public string RouterName { get; set; } = string.Empty;
        public int ConfigId { get; set; }
        public DateTime BackupDate { get; set; }
        public string? Version { get; set; }
        public List<Match> Matches { get; set; } = new List<Match>();
    }

    public class Match
    {
        public int LineNumber { get; set; }
        public string LineContent { get; set; } = string.Empty;
        public int MatchCount { get; set; }
    }
} 