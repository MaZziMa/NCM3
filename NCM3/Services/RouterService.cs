using System;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Net.Sockets;
using NCM3.Models;
using NCM3.Constants;
using Renci.SshNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace NCM3.Services
{
    public class RouterService : IDisposable
    {
        private readonly IEncryptionService _encryptionService;
        private readonly NCMDbContext _context;
        private readonly ILogger<RouterService> _logger;
        private readonly IS3BackupService _s3Service;
        private readonly IConfiguration _configuration;
        private bool _disposed;

        public RouterService(
            IEncryptionService encryptionService, 
            NCMDbContext context, 
            ILogger<RouterService> logger,
            IS3BackupService s3Service,
            IConfiguration configuration)
        {
            _encryptionService = encryptionService;
            _context = context;
            _logger = logger;
            _s3Service = s3Service;
            _configuration = configuration;
        }

        public async Task<string> GetConfigurationAsync(Router router)
        {
            try
            {
                // Giải mã mật khẩu trước khi sử dụng
                string decryptedPassword = _encryptionService.Decrypt(router.Password);
                string? decryptedEnablePassword = !string.IsNullOrEmpty(router.EnablePassword) 
                    ? _encryptionService.Decrypt(router.EnablePassword) 
                    : string.Empty;
                
                // Test if the router's SSH port is reachable before attempting connection
                using (var tcpClient = new TcpClient())
                {
                    try
                    {
                        var connectTask = tcpClient.ConnectAsync(router.IpAddress, DefaultSettings.SSHPort);
                        if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                        {
                            return $"Error: Cannot establish TCP connection to router SSH port ({DefaultSettings.SSHPort}). Check if the device is online and port {DefaultSettings.SSHPort} is accessible.";
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"Error: TCP connection failed - {ex.Message}";
                    }
                }
                
                // Configure SSH connection with longer timeouts and Cisco compatibility
                var connectionInfo = new Renci.SshNet.ConnectionInfo(
                    router.IpAddress,
                    22,
                    router.Username,
                    new PasswordAuthenticationMethod(router.Username, decryptedPassword)
                );
                
                // Longer timeouts and connection settings
                connectionInfo.Timeout = TimeSpan.FromSeconds(45);  // Even longer timeout
                connectionInfo.RetryAttempts = 2;
                
                // Cisco compatibility: use a client that's compatible with older Cisco routers
                using var client = new SshClient(connectionInfo);
                client.KeepAliveInterval = TimeSpan.FromSeconds(60);
                
                try
                {
                client.Connect();
                }
                catch (Exception ex)
                {
                    return $"Error: SSH connection failed - {ex.Message}";
                }
                
                if (client.IsConnected)
                {
                    try
                    {
                        ShellStream shellStream = client.CreateShellStream("vt100", 80, 24, 800, 600, 1024);
                        
                        // Wait for the initial prompt
                        await Task.Delay(2000);
                        
                        // Clear any initial text
                        shellStream.Read();
                        
                        // If enable password is provided, enter privileged mode
                        if (!string.IsNullOrEmpty(decryptedEnablePassword))
                        {
                            shellStream.WriteLine(SSHCommands.Enable);
                            await Task.Delay(1000);
                            shellStream.WriteLine(decryptedEnablePassword);
                            await Task.Delay(1000);
                        }
                        
                        // Execute the show running-config command
                        shellStream.WriteLine(SSHCommands.TerminalLength);
                        await Task.Delay(1000);
                        shellStream.WriteLine(SSHCommands.ShowRunningConfig);
                        await Task.Delay(5000); // Longer wait for configuration to load
                        
                        // Read the output
                        string output = shellStream.Read();
                        client.Disconnect();
                        return output;
                    }
                    catch (Exception ex)
                    {
                        return $"Error during SSH session: {ex.Message}";
                    }
                    finally
                    {
                        if (client.IsConnected)
                        {
                    client.Disconnect();
                        }
                    }
                }
                
                return "Failed to connect to the router.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
        
        // Debug method to test connectivity with detailed logging
        public async Task<string> DebugSshConnectionAsync(Router router)
        {
            StringBuilder log = new StringBuilder();
            
            try
            {
                // Giải mã mật khẩu trước khi sử dụng
                string decryptedPassword = _encryptionService.Decrypt(router.Password);
                string? decryptedEnablePassword = !string.IsNullOrEmpty(router.EnablePassword) 
                    ? _encryptionService.Decrypt(router.EnablePassword) 
                    : string.Empty;

                log.AppendLine($"[DEBUG] Attempting to connect to {router.IpAddress} with username {router.Username}");
                log.AppendLine($"[DEBUG] Enable password is {(string.IsNullOrEmpty(decryptedEnablePassword) ? "NOT SET" : "SET")}");
                log.AppendLine($"[DEBUG] Using SSH.NET library version: {typeof(Renci.SshNet.SshClient).Assembly.GetName().Version}");
                
                // Test if the port is even reachable
                log.AppendLine("[DEBUG] Testing TCP connectivity to port 22...");
                using (var tcpClient = new TcpClient())
                {
                    try
                    {
                        var connectTask = tcpClient.ConnectAsync(router.IpAddress, 22);
                        if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                        {
                            log.AppendLine("[DEBUG] ERROR: TCP connection timed out - port 22 is unreachable");
                            return log.ToString();
                        }
                        log.AppendLine("[DEBUG] TCP connection successful - port 22 is reachable");
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"[DEBUG] ERROR: TCP connection failed - {ex.Message}");
                        return log.ToString();
                    }
                }
                
                // Configure SSH connection with longer timeouts and Cisco compatibility
                var connectionInfo = new Renci.SshNet.ConnectionInfo(
                    router.IpAddress,
                    22,
                    router.Username,
                    new PasswordAuthenticationMethod(router.Username, decryptedPassword)
                );
                
                // Longer timeouts and connection settings
                connectionInfo.Timeout = TimeSpan.FromSeconds(45);  // Even longer timeout
                connectionInfo.RetryAttempts = 2;
                log.AppendLine("[DEBUG] Connection timeout set to 45 seconds with 2 retry attempts");
                log.AppendLine("[DEBUG] Attempting to connect with standard SSH settings");
                
                // Cisco compatibility: use a client that's compatible with older Cisco routers
                using var client = new SshClient(connectionInfo);
                client.KeepAliveInterval = TimeSpan.FromSeconds(60);
                
                log.AppendLine("[DEBUG] Connecting via SSH...");
                try
                {
                    client.Connect();
                }
                catch (Exception ex)
                {
                    log.AppendLine($"[DEBUG] ERROR: {ex.Message}");
                    log.AppendLine($"[DEBUG] Stack trace: {ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        log.AppendLine($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                    }
                    
                    log.AppendLine("[DEBUG] ------------ Cisco-specific troubleshooting ------------");
                    log.AppendLine("[DEBUG] This error typically occurs with Cisco devices when:");
                    log.AppendLine("[DEBUG] 1. The SSH service on the router needs to be reset");
                    log.AppendLine("[DEBUG] 2. The router has restrictions on allowed cipher suites");
                    log.AppendLine("[DEBUG] 3. The router's SSH version is incompatible");
                    log.AppendLine("[DEBUG] 4. There are resource constraints on the router");
                    log.AppendLine("[DEBUG] 5. The router's terminal monitor shows: SSH1: Session disconnected");
                    log.AppendLine("[DEBUG] ");
                    log.AppendLine("[DEBUG] Try the following on the router:");
                    log.AppendLine("[DEBUG] - no ip ssh server");
                    log.AppendLine("[DEBUG] - ip ssh server");
                    log.AppendLine("[DEBUG] - ip ssh version 2");
                    log.AppendLine("[DEBUG] - crypto key generate rsa modulus 2048");
                    log.AppendLine("[DEBUG] ------------ End Cisco troubleshooting tips ------------");
                    
                    return log.ToString();
                }
                
                if (client.IsConnected)
                {
                    log.AppendLine("[DEBUG] Successfully connected via SSH");
                    
                    log.AppendLine("[DEBUG] Creating shell stream");
                    ShellStream shellStream = client.CreateShellStream("vt100", 80, 24, 800, 600, 1024);
                    
                    log.AppendLine("[DEBUG] Waiting for initial prompt (3 seconds)");
                    await Task.Delay(3000);
                    
                    string initialOutput = shellStream.Read();
                    log.AppendLine($"[DEBUG] Initial prompt received: {initialOutput}");
                    
                    // If enable password is provided, enter privileged mode
                    if (!string.IsNullOrEmpty(decryptedEnablePassword))
                    {
                        log.AppendLine("[DEBUG] Sending 'enable' command");
                        shellStream.WriteLine("enable");
                        await Task.Delay(1000);
                        
                        string enablePrompt = shellStream.Read();
                        log.AppendLine($"[DEBUG] Enable prompt response: {enablePrompt}");
                        
                        log.AppendLine("[DEBUG] Sending enable password");
                        shellStream.WriteLine(decryptedEnablePassword);
                        await Task.Delay(1000);
                        
                        string passwordResponse = shellStream.Read();
                        log.AppendLine($"[DEBUG] Password response: {passwordResponse}");
                    }
                    else
                    {
                        log.AppendLine("[DEBUG] No enable password provided - skipping 'enable' command");
                    }
                    
                    // Test a simple command first
                    log.AppendLine("[DEBUG] Sending test command 'terminal length 0'");
                    shellStream.WriteLine("terminal length 0");
                    await Task.Delay(1000);
                    
                    string terminalResponse = shellStream.Read();
                    log.AppendLine($"[DEBUG] Terminal command response: {terminalResponse}");
                    
                    log.AppendLine("[DEBUG] Sending 'show version' command");
                    shellStream.WriteLine("show version");
                    await Task.Delay(3000);
                    
                    string versionOutput = shellStream.Read();
                    log.AppendLine($"[DEBUG] Version output (truncated): {versionOutput.Substring(0, Math.Min(100, versionOutput.Length))}...");
                    
                    log.AppendLine("[DEBUG] Disconnecting");
                    client.Disconnect();
                    log.AppendLine("[DEBUG] Disconnected successfully");
                    
                    return log.ToString();
                }
                else
                {
                    log.AppendLine("[DEBUG] Failed to connect to the router");
                    return log.ToString();
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"[DEBUG] ERROR: {ex.Message}");
                log.AppendLine($"[DEBUG] Stack trace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    log.AppendLine($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                }
                
                return log.ToString();
            }
        }
        
        public async Task<bool> CheckConnectionAsync(Router router)
        {
            try
            {
                // Giải mã mật khẩu trước khi sử dụng
                string decryptedPassword = _encryptionService.Decrypt(router.Password);

                // Test if the router's SSH port is reachable before attempting connection
                using (var tcpClient = new TcpClient())
                {
                    try
                    {
                        var connectTask = tcpClient.ConnectAsync(router.IpAddress, 22);
                        if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                        {
                            router.Status = "Unreachable";
                            return false;
                        }
                    }
                    catch (Exception)
                    {
                        router.Status = "Unreachable";
                        router.IsAvailable = false;
                        return false;
                    }
                }
                
                // Configure SSH connection with longer timeouts and Cisco compatibility
                var connectionInfo = new Renci.SshNet.ConnectionInfo(
                    router.IpAddress,
                    22,
                    router.Username,
                    new PasswordAuthenticationMethod(router.Username, decryptedPassword)
                );
                
                // Longer timeouts and connection settings
                connectionInfo.Timeout = TimeSpan.FromSeconds(45);  // Even longer timeout
                connectionInfo.RetryAttempts = 2;
                
                // Cisco compatibility: use a client that's compatible with older Cisco routers
                using var client = new SshClient(connectionInfo);
                client.KeepAliveInterval = TimeSpan.FromSeconds(60);
                
                try
                {
                client.Connect();
                }
                catch (Exception)
                {
                    router.Status = "Authentication Failed";
                    router.IsAvailable = false;
                    return false;
                }
                
                bool isConnected = client.IsConnected;
                
                if (isConnected)
                {
                    var command = client.CreateCommand("show version");
                    string version = await Task.FromResult(command.Execute());
                    
                    // Parse the output to extract OS version and model information
                    // This is a simplified example - actual parsing would depend on the router's output format
                    if (!string.IsNullOrEmpty(version))
                    {
                        router.Status = "Connected";
                        router.IsAvailable = true;
                        
                        // Extract OS version (this is simplified)
                        if (version.Contains("Version"))
                        {
                            int startIndex = version.IndexOf("Version");
                            if (startIndex > 0)
                            {
                                int endIndex = version.IndexOf(",", startIndex);
                                if (endIndex > startIndex)
                                {
                                    router.OSVersion = version.Substring(startIndex, endIndex - startIndex).Trim();
                                }
                            }
                        }
                        
                        // Extract model information (simplified)
                        if (version.Contains("cisco"))
                        {
                            int startIndex = version.IndexOf("cisco");
                            if (startIndex > 0)
                            {
                                int endIndex = version.IndexOf("\n", startIndex);
                                if (endIndex > startIndex)
                                {
                                    router.Model = version.Substring(startIndex, endIndex - startIndex).Trim();
                                }
                            }
                        }
                    }
                    
                    client.Disconnect();
                    return true;
                }
                
                router.Status = "Disconnected";
                router.IsAvailable = false;
                return false;
            }
            catch (Exception)
            {
                router.Status = "Error";
                router.IsAvailable = false;
                return false;
            }
        }
        
        public async Task<bool> RestoreConfigurationAsync(Router router, string configuration)
        {
            // Validate input
            if (router == null)
            {
                _logger.LogError("Router cannot be null");
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(configuration))
            {
                _logger.LogError("Configuration cannot be empty");
                return false;
            }
            
            // Validate IP address
            if (string.IsNullOrWhiteSpace(router.IpAddress))
            {
                _logger.LogError("Router {RouterName} has no IP address configured", router.Hostname);
                return false;
            }
            
            if (!System.Net.IPAddress.TryParse(router.IpAddress, out var ipAddress))
            {
                _logger.LogError("Router {RouterName} has invalid IP address: {IPAddress}", 
                    router.Hostname, router.IpAddress);
                return false;
            }

            _logger.LogInformation("Starting configuration restore for router {RouterName} at {IPAddress}", 
                router.Hostname, ipAddress.ToString());

            try
            {
                // Giải mã mật khẩu trước khi sử dụng
                string decryptedPassword = _encryptionService.Decrypt(router.Password);
                string? decryptedEnablePassword = !string.IsNullOrEmpty(router.EnablePassword) 
                    ? _encryptionService.Decrypt(router.EnablePassword) 
                    : string.Empty;
                
                using (var client = new SshClient(router.IpAddress, DefaultSettings.SSHPort, router.Username, decryptedPassword))
                {
                    _logger.LogInformation("Connecting to router {RouterName} at {IPAddress} to restore configuration", 
                        router.Hostname, router.IpAddress);
                    
                    // More generous timeout settings to avoid connection issues
                    client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(45);
                    client.KeepAliveInterval = TimeSpan.FromSeconds(10);
                    
                    try
                    {
                        client.Connect();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SSH connection failed to router {RouterName}: {ErrorMessage}", 
                            router.Hostname, ex.Message);
                        return false;
                    }
                    
                    if (!client.IsConnected)
                    {
                        _logger.LogError("SSH connection failed - Unable to connect to router {RouterName}", router.Hostname);
                        return false;
                    }

                    // Create SSH shell with appropriate settings
                    using (var shell = client.CreateShellStream("dumb", 80, 24, 800, 600, 1024))
                    {
                        try
                        {
                            // Wait for initial prompt
                            _logger.LogDebug("Waiting for initial router prompt");
                            string output = await ReadUntilPromptAsync(shell, 8000);
                            
                            // Enter privileged mode if enable password is provided
                            if (!string.IsNullOrEmpty(decryptedEnablePassword))
                            {
                                _logger.LogDebug("Attempting to enter privileged mode");
                                shell.WriteLine("enable");
                                await Task.Delay(500);
                                
                                output = await ReadUntilPromptAsync(shell, 3000, new[] { "Password:", "password:" });
                                if (output.Contains("assword:"))
                                {
                                    shell.WriteLine(decryptedEnablePassword);
                                    await Task.Delay(1000);
                                    output = await ReadUntilPromptAsync(shell, 3000);
                                }
                                
                                if (!output.Contains("#"))
                                {
                                    _logger.LogWarning("Failed to enter privileged mode - continuing anyway but restoration may fail");
                                }
                                else
                                {
                                    _logger.LogDebug("Successfully entered privileged mode");
                                }
                            }
                            
                            // Enter configuration mode
                            _logger.LogDebug("Entering configuration mode");
                            shell.WriteLine("configure terminal");
                            await Task.Delay(1000);
                            output = await ReadUntilPromptAsync(shell, 3000);
                            
                            if (!output.Contains("config") && !output.Contains("conf t"))
                            {
                                _logger.LogWarning("Configuration mode prompt not detected, but continuing with restoration");
                            }
                            
                            // Process configuration line by line
                            int configLinesCount = 0;
                            int errorLinesCount = 0;
                            string[] configLines = configuration.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            
                            _logger.LogInformation("Starting configuration restore with {LineCount} lines", configLines.Length);
                            
                            foreach (string line in configLines)
                            {
                                string trimmedLine = line.Trim();
                                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("!") || trimmedLine.StartsWith("#"))
                                {
                                    continue; // Skip comments and empty lines
                                }
                                
                                shell.WriteLine(trimmedLine);
                                await Task.Delay(150); // Wait a bit between commands to avoid overwhelming the router
                                configLinesCount++;
                                
                                // Check for errors in router response
                                output = await ReadUntilPromptAsync(shell, 2000, new[] { 
                                    "% Invalid", "% Error", "% Incomplete", "% Ambiguous" 
                                });
                                
                                if (output.Contains("% Invalid") || output.Contains("% Error") || 
                                    output.Contains("% Incomplete") || output.Contains("% Ambiguous"))
                                {
                                    // Log error but continue with other commands
                                    _logger.LogWarning("Error encountered while applying line: {Line} - Response: {Output}", 
                                        trimmedLine, output.Trim());
                                    errorLinesCount++;
                                }
                            }
                            
                            double errorRate = configLinesCount > 0 ? (double)errorLinesCount / configLinesCount * 100 : 0;
                            _logger.LogInformation(
                                "Processed {ConfigLines} configuration lines with {ErrorCount} errors ({ErrorRate:F1}%)", 
                                configLinesCount, errorLinesCount, errorRate);
                            
                            // Exit config mode and save configuration
                            _logger.LogDebug("Exiting configuration mode");
                            shell.WriteLine("end");
                            await Task.Delay(1000);
                            output = await ReadUntilPromptAsync(shell, 3000);
                            
                            _logger.LogDebug("Saving configuration to NVRAM");
                            shell.WriteLine("write memory");
                            await Task.Delay(2000);
                            output = await ReadUntilPromptAsync(shell, 8000);
                            
                            // Check for failure conditions in the output
                            if (output.ToLower().Contains("fail") || output.ToLower().Contains("error") || 
                                output.ToLower().Contains("invalid"))
                            {
                                _logger.LogError("Failed to save configuration to NVRAM for router {RouterName}: {Output}", 
                                    router.Hostname, output.Trim());
                                return false;
                            }
                            
                            // If too many errors occurred, consider the restoration a failure
                            if (errorRate > 50 && configLinesCount > 10)
                            {
                                _logger.LogError(
                                    "Configuration restore had too many errors ({ErrorCount}/{TotalLines}, {ErrorRate:F1}%)", 
                                    errorLinesCount, configLinesCount, errorRate);
                                return false;
                            }
                            
                            _logger.LogInformation("Successfully restored and saved configuration for router {RouterName}", 
                                router.Hostname);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during configuration restore for router {RouterName}: {ErrorMessage}", 
                                router.Hostname, ex.Message);
                            return false;
                        }
                        finally
                        {
                            try
                            {
                                // Ensure we disconnect cleanly
                                if (client.IsConnected)
                                {
                                    _logger.LogDebug("Disconnecting from router {RouterName}", router.Hostname);
                                    client.Disconnect();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error while disconnecting from router {RouterName}", router.Hostname);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during configuration restore for router {RouterName}: {ErrorMessage}", 
                    router.Hostname, ex.Message);
                return false;
            }
        }
        
        private async Task<string> ReadUntilPromptAsync(ShellStream shell, int timeout, string[]? errorStrings = null)
        {
            var result = new StringBuilder();
            var startTime = DateTime.Now;
            
            // Define common prompt patterns for different router types
            string[] promptPatterns = new[] { 
                "#",                 // Standard privileged mode prompt
                ">",                 // Standard user mode prompt
                ":",                 // Sometimes seen in prompts
                "Password:",         // Password prompt
                "(config)#",         // Global configuration mode
                "(config-if)#",      // Interface configuration mode
                "(config-line)#",    // Line configuration mode
                "(config-router)#",  // Router protocol configuration
                "(config-dhcp)#",    // DHCP configuration
                "More--",            // Pager prompt
                "[yes/no]:",         // Confirmation prompt
                "[confirm]"          // Another confirmation pattern
            };
            
            _logger.LogDebug("Waiting for router prompt with {TimeoutMs}ms timeout", timeout);
            
            // Keep track of how much data we've received to detect stalled connections
            int lastResultLength = 0;
            int stallCounter = 0;
            
            while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                if (shell.DataAvailable)
                {
                    string data = shell.Read();
                    if (!string.IsNullOrEmpty(data))
                    {
                        result.Append(data);
                        _logger.LogTrace("Received {DataLength} characters from router", data.Length);
                        
                        // Reset stall counter when we receive data
                        stallCounter = 0;
                        lastResultLength = result.Length;
                    }
                    
                    // Check for error strings if provided
                    if (errorStrings != null)
                    {
                        foreach (var errorString in errorStrings)
                        {
                            if (data.Contains(errorString) || result.ToString().Contains(errorString))
                            {
                                _logger.LogDebug("Detected error string in output: {ErrorString}", errorString);
                                return result.ToString();
                            }
                        }
                    }
                    
                    // Check for common prompt patterns
                    foreach (var pattern in promptPatterns)
                    {
                        if (data.Contains(pattern))
                        {
                            _logger.LogDebug("Detected prompt pattern: {Pattern}", pattern);
                            return result.ToString();
                        }
                    }
                    
                    // Use regex to detect general prompt patterns like hostname# or hostname>
                    // This helps with routers that have custom hostnames in their prompts
                    if (System.Text.RegularExpressions.Regex.IsMatch(data, @"\S+[#>](\s|$)"))
                    {
                        _logger.LogDebug("Detected generic prompt pattern with regex");
                        return result.ToString();
                    }
                }
                else
                {
                    // If the result hasn't changed in a while, we might be at a prompt but didn't match our patterns
                    if (result.Length > 0 && result.Length == lastResultLength)
                    {
                        stallCounter++;
                        if (stallCounter >= 10) // After ~1 second of no new data
                        {
                            _logger.LogDebug("No new data received for ~1 second, assuming prompt is available");
                            return result.ToString();
                        }
                    }
                }
                
                await Task.Delay(100);
            }
            
            _logger.LogWarning("Timeout reached while waiting for prompt. Timeout: {TimeoutMs}ms", timeout);
            _logger.LogWarning("Last received data: {LastData}", 
                result.Length > 0 ? result.ToString().Substring(Math.Max(0, result.Length - 50)) : "No data");
            return result.ToString();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _context.Dispose();
                }
                _disposed = true;
            }
        }
    }
}