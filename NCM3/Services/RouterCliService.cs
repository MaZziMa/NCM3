using System;
using System.Threading.Tasks;
using System.Text;
using NCM3.Models;
using Renci.SshNet;
using Microsoft.Extensions.Logging;

namespace NCM3.Services
{
    public interface IRouterCliService
    {
        Task<string> ExecuteCommandAsync(int routerId, string command);
        Task<bool> IsConnectedAsync(int routerId);
        Task<bool> ConnectAsync(int routerId);
        Task DisconnectAsync(int routerId);
        Task<string> GetPromptAsync(int routerId);
    }    public class RouterCliService : IRouterCliService, IDisposable
    {
        private readonly IEncryptionService _encryptionService;
        private readonly NCMDbContext _context;
        private readonly ILogger<RouterCliService> _logger;
        
        // Lưu trữ các kết nối SSH đang mở
        private readonly Dictionary<int, SshClient> _activeConnections = new();
        private readonly Dictionary<int, ShellStream> _activeShells = new();
        private readonly Dictionary<int, DateTime> _lastActivity = new();
        
        // Timeout cho kết nối không hoạt động (20 phút)
        private readonly TimeSpan _connectionTimeout = TimeSpan.FromMinutes(20);
        
        // Timer để kiểm tra và đóng các kết nối không hoạt động
        private readonly System.Threading.Timer _cleanupTimer;

        public RouterCliService(
            IEncryptionService encryptionService,
            NCMDbContext context,
            ILogger<RouterCliService> logger)
        {
            _encryptionService = encryptionService;
            _context = context;
            _logger = logger;
            
            // Thiết lập timer để dọn dẹp các kết nối không hoạt động
            _cleanupTimer = new System.Threading.Timer(
                CleanupInactiveConnections, 
                null, 
                TimeSpan.FromMinutes(5), 
                TimeSpan.FromMinutes(5));
        }
          private void CleanupInactiveConnections(object? state)
        {
            try
            {
                var now = DateTime.Now;
                var inactiveIds = new List<int>();
                
                foreach (var entry in _lastActivity)
                {
                    if (now - entry.Value > _connectionTimeout)
                    {
                        inactiveIds.Add(entry.Key);
                    }
                }
                
                foreach (var id in inactiveIds)
                {
                    _logger.LogInformation("Đóng kết nối không hoạt động cho router ID {RouterId}", id);
                    DisconnectRouterAsync(id).Wait();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi dọn dẹp kết nối không hoạt động");
            }
        }

        public async Task<string> ExecuteCommandAsync(int routerId, string command)
        {
            // Cập nhật thời gian hoạt động cuối cùng
            _lastActivity[routerId] = DateTime.Now;
            
            if (!IsConnected(routerId))
            {
                var connected = await ConnectAsync(routerId);
                if (!connected)
                {
                    return "Không thể kết nối đến router. Vui lòng kiểm tra lại cài đặt kết nối.";
                }
            }
            
            try
            {
                if (_activeShells.TryGetValue(routerId, out var shellStream))
                {
                    // Đọc bất kỳ dữ liệu còn lại nào trong buffer
                    string initialOutput = shellStream.Read();
                    
                    // Xử lý các lệnh đặc biệt
                    if (command.Trim().Equals("configure terminal", StringComparison.OrdinalIgnoreCase) || 
                        command.Trim().Equals("conf t", StringComparison.OrdinalIgnoreCase))
                    {
                        // Tăng thời gian chờ cho chế độ cấu hình
                        _logger.LogInformation("Đang chuyển sang chế độ cấu hình trên router ID {RouterId}", routerId);
                    }
                    else if (command.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase) || 
                             command.Trim().Equals("end", StringComparison.OrdinalIgnoreCase))
                    {
                        // Giảm thời gian chờ khi thoát chế độ cấu hình
                        _logger.LogInformation("Đang thoát chế độ cấu hình trên router ID {RouterId}", routerId);
                    }
                    
                    // Gửi lệnh
                    shellStream.WriteLine(command);
                    
                    // Xác định thời gian chờ dựa trên lệnh
                    int waitTime = DetermineCommandWaitTime(command);
                    await Task.Delay(waitTime);
                    
                    // Đọc kết quả
                    string output = shellStream.Read();
                    
                    // Xử lý kết quả - loại bỏ lệnh echo
                    string[] lines = output.Split('\n');
                    var result = new StringBuilder();
                    
                    // Bỏ qua dòng đầu (lệnh echo) nếu có
                    bool skipFirstLine = false;
                    if (lines.Length > 0 && lines[0].Trim().Contains(command))
                    {
                        skipFirstLine = true;
                    }
                    
                    for (int i = skipFirstLine ? 1 : 0; i < lines.Length; i++)
                    {
                        result.AppendLine(lines[i]);
                    }
                    
                    // Xử lý các trường hợp đặc biệt
                    string processedOutput = ProcessSpecialCommandOutput(command, result.ToString());
                    
                    return processedOutput;
                }
                else
                {
                    return "Phiên CLI không tồn tại. Vui lòng kết nối lại.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thực hiện lệnh trên router ID {RouterId}: {Command}", routerId, command);
                return $"Lỗi: {ex.Message}";
            }
        }
        
        private int DetermineCommandWaitTime(string command)
        {
            // Xác định thời gian chờ dựa trên loại lệnh
            string normalizedCommand = command.Trim().ToLowerInvariant();
            
            if (normalizedCommand.StartsWith("show running-config") || 
                normalizedCommand.StartsWith("show startup-config"))
            {
                return 5000; // 5 giây cho lệnh hiển thị cấu hình
            }
            else if (normalizedCommand.Contains("show") && 
                    (normalizedCommand.Contains("log") || normalizedCommand.Contains("history")))
            {
                return 3000; // 3 giây cho lệnh hiển thị log hoặc lịch sử
            }
            else if (normalizedCommand.StartsWith("ping") || normalizedCommand.StartsWith("traceroute"))
            {
                return 10000; // 10 giây cho lệnh ping hoặc traceroute
            }
            else if (normalizedCommand.StartsWith("reload") || normalizedCommand.Contains("reset"))
            {
                return 1000; // 1 giây cho lệnh reload hoặc reset (thường cần xác nhận)
            }
            
            // Mặc định: 2 giây
            return 2000;
        }
        
        private string ProcessSpecialCommandOutput(string command, string output)
        {
            string normalizedCommand = command.Trim().ToLowerInvariant();
            
            // Thêm chú thích cho lệnh nguy hiểm
            if (normalizedCommand.StartsWith("reload") || 
                normalizedCommand.StartsWith("erase") || 
                normalizedCommand.StartsWith("format") || 
                normalizedCommand.StartsWith("write erase") ||
                normalizedCommand.StartsWith("delete /recursive"))
            {
                return $"⚠️ CẢNH BÁO: Lệnh này có thể gây mất dữ liệu hoặc gián đoạn dịch vụ.\n\n{output}";
            }
            
            // Định dạng đầu ra của lệnh cấu hình
            if (normalizedCommand.StartsWith("show run") || 
                normalizedCommand.StartsWith("show start"))
            {
                return output; // Giữ nguyên định dạng
            }
            
            // Thêm chú thích cho lệnh cấu hình
            if (normalizedCommand.Equals("configure terminal") || normalizedCommand.Equals("conf t"))
            {
                return $"[Đã chuyển sang chế độ cấu hình toàn cục]\n{output}";
            }
            
            // Đánh dấu lỗi cú pháp
            if (output.Contains("% Invalid") || output.Contains("% Incomplete"))
            {
                return $"❌ Lỗi cú pháp: {output}";
            }
            
            return output;
        }        public Task<bool> IsConnectedAsync(int routerId)
        {
            // Không cần async await vì IsConnected không có async operations
            return Task.FromResult(IsConnected(routerId));
        }
        
        private bool IsConnected(int routerId)
        {
            // Kiểm tra chi tiết trạng thái kết nối
            try {
                // Kiểm tra client có tồn tại không
                if (!_activeConnections.TryGetValue(routerId, out var client)) {
                    _logger.LogDebug("Router ID {RouterId} không có kết nối SSH hoạt động", routerId);
                    return false;
                }
                
                // Kiểm tra client có kết nối không
                if (!client.IsConnected) {
                    _logger.LogDebug("Router ID {RouterId} có kết nối SSH nhưng đã mất kết nối", routerId);
                    _activeConnections.Remove(routerId); // Loại bỏ kết nối hỏng
                    return false;
                }
                
                // Kiểm tra shell có tồn tại không
                if (!_activeShells.ContainsKey(routerId)) {
                    _logger.LogDebug("Router ID {RouterId} có kết nối SSH nhưng không có shell", routerId);
                    return false;
                }
                
                _lastActivity[routerId] = DateTime.Now; // Cập nhật thời gian hoạt động
                return true;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Lỗi khi kiểm tra kết nối cho router ID {RouterId}", routerId);
                return false;
            }
        }        public async Task<bool> ConnectAsync(int routerId)
        {
            // Kiểm tra xem đã kết nối chưa
            if (IsConnected(routerId))
            {
                _logger.LogInformation("Router ID {RouterId} đã kết nối, sử dụng kết nối hiện có", routerId);
                return true;
            }
            
            // Xóa bất kỳ kết nối cũ nào có thể còn sót lại
            await DisconnectRouterAsync(routerId);
            
            try
            {
                // Lấy thông tin router từ database
                var router = await _context.Routers.FindAsync(routerId);
                if (router == null)
                {
                    _logger.LogWarning("Không tìm thấy router với ID {RouterId}", routerId);
                    return false;
                }
                
                _logger.LogInformation("Đang kết nối đến router {RouterName} (ID: {RouterId}, IP: {RouterIP})", 
                    router.Hostname, routerId, router.IpAddress);
                
                // Giải mã mật khẩu
                string decryptedPassword = _encryptionService.Decrypt(router.Password);
                string? decryptedEnablePassword = !string.IsNullOrEmpty(router.EnablePassword) 
                    ? _encryptionService.Decrypt(router.EnablePassword) 
                    : string.Empty;
                  // Thiết lập kết nối SSH
                var connectionInfo = new Renci.SshNet.ConnectionInfo(
                    router.IpAddress,
                    Constants.DefaultSettings.SSHPort,
                    router.Username,
                    new Renci.SshNet.PasswordAuthenticationMethod(router.Username, decryptedPassword)
                );
                
                connectionInfo.Timeout = TimeSpan.FromSeconds(Constants.DefaultSettings.SSHTimeout);
                connectionInfo.RetryAttempts = Constants.DefaultSettings.SSHRetryAttempts;
                
                // Tạo và mở kết nối SSH
                var client = new SshClient(connectionInfo);
                client.KeepAliveInterval = TimeSpan.FromSeconds(Constants.DefaultSettings.SSHKeepAliveInterval);
                  try 
                {
                    _logger.LogDebug("Đang thử kết nối SSH đến {IpAddress}...", router.IpAddress);
                    client.Connect();
                }
                catch (Exception ex) 
                {
                    _logger.LogError(ex, "Lỗi khi kết nối SSH đến router {RouterName} (ID: {RouterId}, IP: {IpAddress})",
                        router.Hostname, routerId, router.IpAddress);
                    return false;
                }
                
                if (client.IsConnected)
                {
                    _logger.LogDebug("Kết nối SSH thành công, đang tạo shell stream...");
                    
                    try
                    {
                        // Tạo shell stream
                        var shellStream = client.CreateShellStream("vt100", 80, 24, 800, 600, 1024);
                        
                        // Đợi initial prompt
                        _logger.LogDebug("Đang đợi initial prompt...");
                        await Task.Delay(2000);
                        shellStream.Read();
                        
                        // Nếu có enable password, vào chế độ privileged
                        if (!string.IsNullOrEmpty(decryptedEnablePassword))
                        {
                            _logger.LogDebug("Đang nhập enable password...");
                            shellStream.WriteLine(Constants.SSHCommands.Enable);
                            await Task.Delay(1000);
                            shellStream.WriteLine(decryptedEnablePassword);
                            await Task.Delay(1000);
                            shellStream.Read(); // Đọc kết quả của lệnh enable
                        }
                        
                        // Thiết lập terminal length
                        _logger.LogDebug("Đang thiết lập terminal length...");
                        shellStream.WriteLine(Constants.SSHCommands.TerminalLength);
                        await Task.Delay(1000);
                        shellStream.Read(); // Đọc kết quả của lệnh terminal length
                        
                        // Kiểm tra xem shell có phản hồi không
                        _logger.LogDebug("Kiểm tra shell stream có phản hồi không...");
                        shellStream.WriteLine("");
                        await Task.Delay(1000);
                        string response = shellStream.Read();
                        
                        if (string.IsNullOrWhiteSpace(response))
                        {
                            _logger.LogWarning("Shell stream không phản hồi, đóng kết nối và thử lại");
                            shellStream.Close();
                            client.Disconnect();
                            return false;
                        }
                        
                        // Lưu kết nối và shell
                        _activeConnections[routerId] = client;
                        _activeShells[routerId] = shellStream;
                        _lastActivity[routerId] = DateTime.Now;
                        
                        _logger.LogInformation("Đã kết nối thành công đến router {RouterName} (ID: {RouterId})", 
                            router.Hostname, routerId);
                        
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi tạo shell stream cho router {RouterName} (ID: {RouterId})",
                            router.Hostname, routerId);
                        
                        if (client.IsConnected)
                        {
                            client.Disconnect();
                        }
                        
                        return false;
                    }
                }
                else
                {
                    _logger.LogWarning("Không thể kết nối đến router {RouterName} (ID: {RouterId})", 
                        router.Hostname, routerId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kết nối đến router ID {RouterId}: {Message}", routerId, ex.Message);
                return false;
            }
        }

        public async Task DisconnectAsync(int routerId)
        {
            await DisconnectRouterAsync(routerId);
        }
        
        private async Task DisconnectRouterAsync(int routerId)
        {
            try
            {
                if (_activeShells.TryGetValue(routerId, out var shellStream))
                {
                    shellStream.Close();
                    _activeShells.Remove(routerId);
                }
                
                if (_activeConnections.TryGetValue(routerId, out var client))
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                    
                    client.Dispose();
                    _activeConnections.Remove(routerId);
                }
                
                _lastActivity.Remove(routerId);
                
                _logger.LogInformation("Đã đóng kết nối đến router ID {RouterId}", routerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đóng kết nối đến router ID {RouterId}: {Message}", routerId, ex.Message);
            }
        }        public async Task<string> GetPromptAsync(int routerId)
        {
            if (IsConnected(routerId))
            {
                try
                {
                    if (!_activeShells.TryGetValue(routerId, out var shellStream))
                    {
                        _logger.LogWarning("Không thể lấy shell stream cho router ID {RouterId}", routerId);
                        return "> ";
                    }
                    
                    // Thử đọc bất kỳ dữ liệu nào trong buffer trước
                    try {
                        string initialOutput = shellStream.Read();
                    } catch (Exception) {
                        // Bỏ qua lỗi nếu không thể đọc
                    }
                    
                    // Gửi lệnh trống để lấy prompt
                    shellStream.WriteLine("");
                    await Task.Delay(800); // Tăng thời gian chờ để đảm bảo phản hồi
                    
                    string output = shellStream.Read();
                    
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        // Thử lại một lần nữa với thời gian chờ dài hơn nếu không nhận được phản hồi
                        shellStream.WriteLine("");
                        await Task.Delay(1000);
                        output = shellStream.Read();
                    }
                    
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        _logger.LogWarning("Không nhận được phản hồi từ router ID {RouterId} khi lấy prompt", routerId);
                        return "> ";
                    }
                    
                    // Lấy dòng cuối cùng là prompt
                    string[] lines = output.Split('\n');
                    var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
                    
                    if (nonEmptyLines.Count > 0)
                    {
                        return nonEmptyLines.Last().Trim();
                    }
                    
                    return "> ";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi lấy prompt từ router ID {RouterId}", routerId);
                    return "> ";
                }
            }
            
            return "> ";
        }

        public void Dispose()
        {
            // Đóng tất cả kết nối đang mở
            foreach (var routerId in _activeConnections.Keys.ToList())
            {
                DisconnectRouterAsync(routerId).Wait();
            }
            
            _cleanupTimer?.Dispose();
        }
    }
}
