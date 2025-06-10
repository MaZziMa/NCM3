using System;
using System.Threading.Tasks;
using System.Net.Sockets;
using NCM3.Models;
using NCM3.Constants;
using Microsoft.Extensions.Logging;

namespace NCM3.Services
{
    public class RouterConnectionService
    {
        private readonly ILogger<RouterConnectionService> _logger;
        
        public RouterConnectionService(ILogger<RouterConnectionService> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Kiểm tra kết nối đến router
        /// </summary>
        /// <param name="router">Router cần kiểm tra</param>
        /// <returns>True nếu kết nối thành công, False nếu không kết nối được</returns>
        public async Task<bool> TestConnectionAsync(Router router)
        {
            if (router == null || string.IsNullOrEmpty(router.IpAddress))
            {
                _logger.LogWarning("Không thể kiểm tra kết nối đến router với IP rỗng hoặc null");
                return false;
            }
            
            _logger.LogDebug("Kiểm tra kết nối đến router {RouterName} ({IP})", 
                router.Hostname, router.IpAddress);
                
            // Kiểm tra kết nối TCP đến cổng SSH (mặc định là 22)
            using (var tcpClient = new TcpClient())
            {
                try
                {
                    var connectTask = tcpClient.ConnectAsync(router.IpAddress, DefaultSettings.SSHPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                    {
                        _logger.LogInformation("Kết nối đến router {RouterName} ({IP}) thành công", 
                            router.Hostname, router.IpAddress);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Hết thời gian chờ kết nối đến router {RouterName} ({IP})", 
                            router.Hostname, router.IpAddress);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể kết nối đến router {RouterName} ({IP}): {Message}", 
                        router.Hostname, router.IpAddress, ex.Message);
                    return false;
                }
            }
        }
    }
}
