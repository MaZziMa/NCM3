using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using NCM3.Services.Events;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace NCM3.Services.ChangeDetection
{
    /// <summary>
    /// Uses SSH polling to detect configuration changes on routers with a lower frequency than SNMP polling
    /// </summary>
    public class SSHPollingStrategy : IChangeDetectionStrategy
    {        private readonly ILogger<SSHPollingStrategy> _logger;
        private readonly IConfiguration _configuration;
        private readonly Func<RouterService> _routerServiceFactory;
        private readonly Func<NCMDbContext> _dbContextFactory;
        private readonly Dictionary<int, DateTime> _lastBackupDates = new();
        private readonly Dictionary<int, string> _lastKnownConfigurations = new();
        private System.Timers.Timer? _pollingTimer;
        private IEventBus? _eventBus;
        private CancellationToken _stoppingToken;
        
        public string Name => "SSH Polling";
        public int Priority => 2; // Lower priority than SNMP (higher number = lower priority)
        
        public bool IsEnabled
        {
            get
            {
                return _configuration.GetValue<bool>("ChangeDetection:Strategies:SSHPolling:Enabled", false);
            }
        }
        
        private int PollingIntervalHours
        {
            get
            {
                return _configuration.GetValue<int>("ChangeDetection:Strategies:SSHPolling:IntervalHours", 24);
            }
        }          public SSHPollingStrategy(
            ILogger<SSHPollingStrategy> logger,
            IConfiguration configuration,
            Func<RouterService> routerServiceFactory,
            Func<NCMDbContext> dbContextFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _routerServiceFactory = routerServiceFactory;
            _dbContextFactory = dbContextFactory;
        }
        
        public Task InitializeAsync(CancellationToken stoppingToken = default)
        {
            _stoppingToken = stoppingToken;
            _logger.LogInformation("Initializing SSH Polling Strategy");
            
            return Task.CompletedTask;
        }
        
        public Task StartDetectionAsync(IEventBus eventBus, CancellationToken stoppingToken = default)
        {
            if (!IsEnabled)
            {
                _logger.LogInformation("SSH Polling Strategy is disabled, not starting");
                return Task.CompletedTask;
            }
            
            _eventBus = eventBus;
            _stoppingToken = stoppingToken;
            
            _logger.LogInformation("Starting SSH Polling Strategy with interval of {IntervalHours} hours", 
                PollingIntervalHours);
            
            // Setup timer for polling
            _pollingTimer = new System.Timers.Timer(PollingIntervalHours * 60 * 60 * 1000); // Convert to milliseconds
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;
            _pollingTimer.Start();
            
            // Run an initial check immediately
            Task.Run(RunPollingCycle);
            
            return Task.CompletedTask;
        }
        
        public Task StopDetectionAsync()
        {
            _logger.LogInformation("Stopping SSH Polling Strategy");
            
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            
            return Task.CompletedTask;
        }
        
        private void OnPollingTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            // Don't start a new polling cycle if the previous one is still running
            if (_pollingTask == null || _pollingTask.IsCompleted)
            {
                _pollingTask = RunPollingCycle();
            }
            else
            {
                _logger.LogWarning("Previous SSH polling cycle still running, skipping this cycle");
            }
        }
        
        private Task? _pollingTask;
        
        private async Task RunPollingCycle()
        {
            try
            {
                _logger.LogDebug("Running SSH polling cycle");
                
                if (_stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                
                if (_eventBus == null)
                {
                    _logger.LogError("Event bus not set for SSH Polling Strategy");
                    return;
                }                // Get all routers using a scoped DbContext
                using var dbContext = _dbContextFactory();
                var routers = await dbContext.Routers.ToListAsync();
                
                foreach (var router in routers)
                {
                    if (_stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    
                    if (!router.IsAvailable)
                    {
                        _logger.LogDebug("Skipping unavailable router: {RouterName}", router.Hostname);
                        continue;
                    }
                    
                    try
                    {
                        await CheckForChangesAsync(router, _eventBus);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking for changes on router {RouterName}: {ErrorMessage}", 
                            router.Hostname, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SSH polling cycle: {ErrorMessage}", ex.Message);
            }
        }
        
        public async Task<bool> CheckForChangesAsync(Router router, IEventBus eventBus)
        {
            if (!IsEnabled)
            {
                return false;
            }
            
            _logger.LogDebug("Checking for changes via SSH on router {RouterName}", router.Hostname);
            
            try
            {                // Get the current configuration via SSH using a scoped RouterService
                using var routerService = _routerServiceFactory();
                string currentConfig = await routerService.GetConfigurationAsync(router);
                
                // If it's an error message, skip this router
                if (currentConfig.StartsWith("Error:"))
                {
                    _logger.LogWarning("Unable to retrieve configuration from router {RouterName}: {ErrorMessage}",
                        router.Hostname, currentConfig);
                    return false;
                }
                
                // Check if we have seen this router before
                if (_lastKnownConfigurations.TryGetValue(router.Id, out var lastConfig))
                {
                    // If the configuration has changed
                    if (lastConfig != currentConfig)
                    {
                        _logger.LogInformation(
                            "Detected configuration change on router {RouterName} via SSH polling.",
                            router.Hostname);
                        
                        // Update our cached configuration
                        _lastKnownConfigurations[router.Id] = currentConfig;
                        _lastBackupDates[router.Id] = DateTime.UtcNow;
                        
                        // Determine priority based on config differences
                        var priority = DeterminePriority(lastConfig, currentConfig);
                        
                        var configChangedEvent = new ConfigurationChangedEvent(
                            router,
                            lastConfig,
                            currentConfig,
                            priority,
                            Name,
                            "Configuration change detected via SSH polling");
                        
                        await eventBus.PublishAsync(configChangedEvent);
                        return true;
                    }
                }
                else
                {
                    // First time seeing this router or first run
                    _logger.LogInformation("Initial configuration captured for router {RouterName} via SSH", 
                        router.Hostname);
                    
                    _lastKnownConfigurations[router.Id] = currentConfig;
                    _lastBackupDates[router.Id] = DateTime.UtcNow;
                    
                    // Get the latest configuration from the database for comparison
                    var dbConfig = router.RouterConfigurations
                        .OrderByDescending(c => c.BackupDate)
                        .FirstOrDefault();
                        
                    if (dbConfig != null && dbConfig.Content != currentConfig)
                    {
                        _logger.LogInformation(
                            "Detected difference between database configuration and current configuration for router {RouterName}",
                            router.Hostname);
                            
                        // Determine priority based on config differences
                        var priority = DeterminePriority(dbConfig.Content, currentConfig);
                        
                        var configChangedEvent = new ConfigurationChangedEvent(
                            router,
                            dbConfig.Content,
                            currentConfig,
                            priority,
                            Name,
                            "Configuration change detected during initial SSH poll");
                        
                        await eventBus.PublishAsync(configChangedEvent);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking SSH for router {RouterName}: {ErrorMessage}",
                    router.Hostname, ex.Message);
            }
            
            return false;
        }
        
        private string DeterminePriority(string oldConfig, string newConfig)
        {
            // Use the same priority determination as SNMP strategy for consistency
            if (ContainsSecurityChanges(oldConfig, newConfig))
            {
                return "High";
            }
            
            if (ContainsInterfaceChanges(oldConfig, newConfig))
            {
                return "High";
            }
            
            if (ContainsRouteChanges(oldConfig, newConfig))
            {
                return "Medium";
            }
            
            if (ContainsACLChanges(oldConfig, newConfig))
            {
                return "Medium";
            }
            
            return "Low";
        }
        
        private bool ContainsSecurityChanges(string oldConfig, string newConfig)
        {
            return ContainsAnyKeyword(oldConfig, newConfig, new[] {
                "password", "secret", "key", "enable secret", "crypto", "ssh",
                "authentication", "authorization"
            });
        }
        
        private bool ContainsInterfaceChanges(string oldConfig, string newConfig)
        {
            return ContainsAnyKeyword(oldConfig, newConfig, new[] {
                "interface", "shutdown", "ip address", "no shutdown", "mtu"
            });
        }
        
        private bool ContainsRouteChanges(string oldConfig, string newConfig)
        {
            return ContainsAnyKeyword(oldConfig, newConfig, new[] {
                "ip route", "router ospf", "router bgp", "router eigrp", "network"
            });
        }
        
        private bool ContainsACLChanges(string oldConfig, string newConfig)
        {
            return ContainsAnyKeyword(oldConfig, newConfig, new[] {
                "access-list", "permit", "deny", "ip access-group"
            });
        }
        
        private bool ContainsAnyKeyword(string oldConfig, string newConfig, string[] keywords)
        {
            // Get the diff as a list of line changes
            var diffLines = GetDiffLines(oldConfig, newConfig);
            
            foreach (var line in diffLines)
            {
                foreach (var keyword in keywords)
                {
                    if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        private List<string> GetDiffLines(string oldConfig, string newConfig)
        {
            // This is a simple diff implementation
            // In a real implementation, you'd use a proper diff algorithm
            var oldLines = oldConfig.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var newLines = newConfig.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            var result = new List<string>();
            
            // Just collect lines that are in new but not in old
            foreach (var line in newLines)
            {
                if (!oldLines.Contains(line))
                {
                    result.Add(line);
                }
            }
            
            return result;
        }
    }
}