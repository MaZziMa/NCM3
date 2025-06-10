using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using NCM3.Services.Events;
using System.Net;
using SnmpSharpNet;
using Microsoft.EntityFrameworkCore;

namespace NCM3.Services.ChangeDetection
{
    /// <summary>
    /// Uses SNMP polling to detect configuration changes on routers
    /// </summary>
    public class SNMPPollingStrategy : IChangeDetectionStrategy
    {
        private readonly ILogger<SNMPPollingStrategy> _logger;
        private readonly IConfiguration _configuration;
        private readonly Func<RouterService> _routerServiceFactory;
        private readonly Func<NCMDbContext> _dbContextFactory;
        private readonly Dictionary<int, DateTime> _lastModifiedTimes = new();
        private System.Timers.Timer? _pollingTimer;
        private IEventBus? _eventBus;
        private CancellationToken _stoppingToken;
        
        public string Name => "SNMP Polling";
        public int Priority => 1; // Higher priority than SSH (lower number = higher priority)
        
        public bool IsEnabled
        {
            get
            {
                return _configuration.GetValue<bool>("ChangeDetection:Strategies:SNMPPolling:Enabled", false);
            }
        }
        
        private int PollingIntervalMinutes
        {
            get
            {
                return _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:IntervalMinutes", 5);
            }
        }
          private string OidConfigLastChanged
        {
            get
            {
                return _configuration.GetValue<string>(
                    "ChangeDetection:Strategies:SNMPPolling:OIDConfigLastChanged", 
                    "1.3.6.1.4.1.9.9.43.1.1.1.0") ?? "1.3.6.1.4.1.9.9.43.1.1.1.0";
            }
        }
        
        private string SnmpCommunity
        {
            get
            {
                return _configuration.GetValue<string>("ChangeDetection:Strategies:SNMPPolling:Community", "public") ?? "public";
            }
        }
          private string SnmpVersionSetting
        {
            get
            {
                return _configuration.GetValue<string>("ChangeDetection:Strategies:SNMPPolling:Version", "Auto") ?? "Auto";
            }
        }
        
        private int SnmpTimeout
        {
            get
            {
                return _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:Timeout", 2000);
            }
        }
        
        private int SnmpRetries
        {
            get
            {
                return _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:Retries", 2);
            }
        }
        
        private int SnmpPort
        {
            get
            {
                return _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:Port", 161);
            }
        }

        public SNMPPollingStrategy(
            ILogger<SNMPPollingStrategy> logger,
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
            _logger.LogInformation("Initializing SNMP Polling Strategy");
            
            return Task.CompletedTask;
        }
        
        public Task StartDetectionAsync(IEventBus eventBus, CancellationToken stoppingToken = default)
        {
            if (!IsEnabled)
            {
                _logger.LogInformation("SNMP Polling Strategy is disabled, not starting");
                return Task.CompletedTask;
            }
            
            _eventBus = eventBus;
            _stoppingToken = stoppingToken;
            
            _logger.LogInformation("Starting SNMP Polling Strategy with interval of {IntervalMinutes} minutes", 
                PollingIntervalMinutes);
            
            // Setup timer for polling
            _pollingTimer = new System.Timers.Timer(PollingIntervalMinutes * 60 * 1000); // Convert to milliseconds
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;
            _pollingTimer.Start();
            
            // Run an initial check immediately
            Task.Run(RunPollingCycle);
            
            return Task.CompletedTask;
        }
        
        public Task StopDetectionAsync()
        {
            _logger.LogInformation("Stopping SNMP Polling Strategy");
            
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
                _logger.LogWarning("Previous SNMP polling cycle still running, skipping this cycle");
            }
        }
        
        private Task? _pollingTask;
        
        private async Task RunPollingCycle()
        {
            try
            {
                _logger.LogDebug("Running SNMP polling cycle");
                
                if (_stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                
                if (_eventBus == null)
                {
                    _logger.LogError("Event bus not set for SNMP Polling Strategy");
                    return;
                }
                
                // Get all routers
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
                _logger.LogError(ex, "Error in SNMP polling cycle: {ErrorMessage}", ex.Message);
            }
        }
        
        public async Task<bool> CheckForChangesAsync(Router router, IEventBus eventBus)
        {
            if (!IsEnabled)
            {
                return false;
            }
              _logger.LogDebug("Checking for changes via SNMP on router {RouterName}", router.Hostname);
            
            // Validate IP address
            if (string.IsNullOrWhiteSpace(router.IpAddress))
            {
                _logger.LogWarning("Router {RouterName} has no IP address configured", router.Hostname);
                return false;
            }
            
            try
            {
                // Try to parse the IP address, which may fail if the IP is invalid
                if (!IPAddress.TryParse(router.IpAddress, out IPAddress? ipAddress))
                {
                    _logger.LogWarning("Router {RouterName} has invalid IP address: {IPAddress}", 
                        router.Hostname, router.IpAddress);
                    return false;
                }
                
                var community = new OctetString(SnmpCommunity);
                var param = new AgentParameters(community);
                var target = new UdpTarget(ipAddress, SnmpPort, SnmpTimeout, SnmpRetries);
                
                try
                {
                    // Create Pdu for SNMP GET
                    var pdu = new Pdu(PduType.Get);
                    var oid = new Oid(OidConfigLastChanged);
                    pdu.VbList.Add(oid);
                      // Determine SNMP version to use based on config
                    string snmpVersion = SnmpVersionSetting?.ToLower() ?? "auto";
                    string versionUsed = "";
                    SnmpPacket? result = null;
                    
                    if (snmpVersion == "v1" || snmpVersion == "1")
                    {
                        // Use SNMPv1
                        param.Version = SnmpSharpNet.SnmpVersion.Ver1;
                        result = target.Request(pdu, param);
                        versionUsed = "SNMPv1";
                    }
                    else if (snmpVersion == "v2" || snmpVersion == "v2c" || snmpVersion == "2")
                    {
                        // Use SNMPv2c
                        param.Version = SnmpSharpNet.SnmpVersion.Ver2;
                        result = target.Request(pdu, param);
                        versionUsed = "SNMPv2c";
                    }
                    else // "auto" or any other value
                    {
                        // Try SNMPv2c first, then fallback to SNMPv1 if it fails
                        try
                        {
                            param.Version = SnmpSharpNet.SnmpVersion.Ver2;
                            result = target.Request(pdu, param);
                            versionUsed = "SNMPv2c";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation("SNMPv2c request failed for router {RouterName}, falling back to SNMPv1: {ErrorMessage}", 
                                router.Hostname, ex.Message);
                            
                            // Reset PDU for new request
                            pdu = new Pdu(PduType.Get);
                            pdu.VbList.Add(oid);
                            
                            param.Version = SnmpSharpNet.SnmpVersion.Ver1;
                            result = target.Request(pdu, param);
                            versionUsed = "SNMPv1";
                        }
                    }
                    
                    // Process the result based on the actual type returned
                    if (result != null)
                    {
                        string? lastModifiedSnmp = null;
                        bool success = false;
                        
                        if (result is SnmpV2Packet v2Packet && v2Packet.Pdu.ErrorStatus == 0)
                        {
                            foreach (var vb in v2Packet.Pdu.VbList)
                            {
                                if (vb.Oid.ToString() == OidConfigLastChanged)
                                {
                                    lastModifiedSnmp = vb.Value.ToString();
                                    success = true;
                                }
                            }
                        }
                        else if (result is SnmpV1Packet v1Packet && v1Packet.Pdu.ErrorStatus == 0)
                        {
                            foreach (var vb in v1Packet.Pdu.VbList)
                            {
                                if (vb.Oid.ToString() == OidConfigLastChanged)
                                {
                                    lastModifiedSnmp = vb.Value.ToString();
                                    success = true;
                                }
                            }
                        }
                        
                        if (success && lastModifiedSnmp != null)
                        {
                            // The value is in TimeTicks (1/100th seconds since device boot)
                            // We need to convert it to a DateTime
                            if (long.TryParse(lastModifiedSnmp, out long ticks))
                            {
                                // Convert timeticks (in 1/100 seconds) to DateTime
                                var lastModified = DateTime.UtcNow.AddMilliseconds(-(ticks * 10));
                                
                                // Check if we have seen this router before
                                if (_lastModifiedTimes.TryGetValue(router.Id, out var lastRecordedTime))
                                {
                                    if (lastModified > lastRecordedTime)
                                    {
                                        _logger.LogInformation(
                                            "Detected configuration change on router {RouterName} via SNMP ({Version}). " +
                                            "Last modified time: {LastModified}, previous recorded time: {PreviousTime}",
                                            router.Hostname, versionUsed, lastModified, lastRecordedTime);
                                        
                                        // Update last modified time
                                        _lastModifiedTimes[router.Id] = lastModified;
                                        
                                        // Get the current configuration to send the change event
                                        using var routerService = _routerServiceFactory();
                                        var currentConfig = await routerService.GetConfigurationAsync(router);
                                        var lastConfig = router.RouterConfigurations
                                            .OrderByDescending(c => c.BackupDate)
                                            .FirstOrDefault();
                                        
                                        // Only raise event if we have both configs and they're different
                                        if (lastConfig != null && lastConfig.Content != currentConfig)
                                        {
                                            // Determine priority based on config differences
                                            var priority = DeterminePriority(lastConfig.Content, currentConfig);
                                            
                                            var configChangedEvent = new ConfigurationChangedEvent(
                                                router,
                                                lastConfig?.Content ?? string.Empty,
                                                currentConfig,
                                                priority,
                                                Name,
                                                $"Configuration change detected via SNMP polling ({versionUsed})");
                                            
                                            await eventBus.PublishAsync(configChangedEvent);
                                            return true;
                                        }
                                    }
                                }
                                else
                                {
                                    // First time seeing this router, just record the time
                                    _lastModifiedTimes[router.Id] = lastModified;
                                    _logger.LogInformation(
                                        "Established baseline SNMP time for router {RouterName} ({Version}): {LastModified}",
                                        router.Hostname, versionUsed, lastModified);
                                }
                            }
                            else if (TryParseUptimeString(lastModifiedSnmp, out var lastModified))
                            {
                                // Check if we have seen this router before
                                if (_lastModifiedTimes.TryGetValue(router.Id, out var lastRecordedTime))
                                {
                                    if (lastModified > lastRecordedTime)
                                    {
                                        _logger.LogInformation(
                                            "Detected configuration change on router {RouterName} via SNMP ({Version}). " +
                                            "Last modified time: {LastModified}, previous recorded time: {PreviousTime}",
                                            router.Hostname, versionUsed, lastModified, lastRecordedTime);
                                        
                                        // Update last modified time
                                        _lastModifiedTimes[router.Id] = lastModified;
                                        
                                        // Get the current configuration to send the change event
                                        using var routerService = _routerServiceFactory();
                                        var currentConfig = await routerService.GetConfigurationAsync(router);
                                        var lastConfig = router.RouterConfigurations
                                            .OrderByDescending(c => c.BackupDate)
                                            .FirstOrDefault();
                                        
                                        // Only raise event if we have both configs and they're different
                                        if (lastConfig != null && lastConfig.Content != currentConfig)
                                        {
                                            // Determine priority based on config differences
                                            var priority = DeterminePriority(lastConfig.Content, currentConfig);
                                            
                                            var configChangedEvent = new ConfigurationChangedEvent(
                                                router,
                                                lastConfig?.Content ?? string.Empty,
                                                currentConfig,
                                                priority,
                                                Name,
                                                $"Configuration change detected via SNMP polling ({versionUsed})");
                                            
                                            await eventBus.PublishAsync(configChangedEvent);
                                            return true;
                                        }
                                    }
                                }
                                else
                                {
                                    // First time seeing this router, just record the time
                                    _lastModifiedTimes[router.Id] = lastModified;
                                    _logger.LogInformation(
                                        "Established baseline SNMP time for router {RouterName} ({Version}): {LastModified}",
                                        router.Hostname, versionUsed, lastModified);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Could not parse SNMP time ticks for router {RouterName}: {Ticks}",
                                    router.Hostname, lastModifiedSnmp);
                            }
                        }
                        else
                        {
                            string errorStatus = "unknown";
                            int errorIndex = -1;
                            
                            if (result is SnmpV2Packet v2P)
                            {
                                errorStatus = v2P.Pdu.ErrorStatus.ToString();
                                errorIndex = v2P.Pdu.ErrorIndex;
                            }
                            else if (result is SnmpV1Packet v1P)
                            {
                                errorStatus = v1P.Pdu.ErrorStatus.ToString();
                                errorIndex = v1P.Pdu.ErrorIndex;
                            }
                            
                            _logger.LogWarning("SNMP GET failed for router {RouterName} using {Version}. Error: {ErrorStatus}, Index: {ErrorIndex}",
                                router.Hostname, versionUsed, errorStatus, errorIndex);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No SNMP response received from router {RouterName} using {Version}",
                            router.Hostname, versionUsed);
                    }
                }
                finally
                {
                    target.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking SNMP for router {RouterName}: {ErrorMessage}",
                    router.Hostname, ex.Message);
            }
            
            return false;
        }
        
        private string DeterminePriority(string oldConfig, string newConfig)
        {
            // This is a simplified example of priority determination
            // In a real implementation, you would perform more sophisticated analysis
            
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
        
        private bool TryParseUptimeString(string uptime, out DateTime lastChanged)
        {
            lastChanged = DateTime.MinValue;
            try
            {
                int days = 0, hours = 0, minutes = 0, seconds = 0, milliseconds = 0;
                var parts = uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (part.EndsWith("d")) int.TryParse(part.TrimEnd('d'), out days);
                    else if (part.EndsWith("h")) int.TryParse(part.TrimEnd('h'), out hours);
                    else if (part.EndsWith("m") && !part.EndsWith("ms")) int.TryParse(part.TrimEnd('m'), out minutes);
                    else if (part.EndsWith("s") && !part.EndsWith("ms")) int.TryParse(part.TrimEnd('s'), out seconds);
                    else if (part.EndsWith("ms")) int.TryParse(part.TrimEnd("ms".ToCharArray()), out milliseconds);
                }
                var span = new TimeSpan(days, hours, minutes, seconds, milliseconds);
                lastChanged = DateTime.UtcNow - span;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}