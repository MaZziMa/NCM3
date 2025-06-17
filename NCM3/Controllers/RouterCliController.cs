using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NCM3.Models;
using NCM3.Services;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace NCM3.Controllers
{
    public class RouterCliController : Controller
    {
        private readonly NCMDbContext _context;
        private readonly IRouterCliService _routerCliService;
        private readonly ILogger<RouterCliController> _logger;

        public RouterCliController(
            NCMDbContext context,
            IRouterCliService routerCliService,
            ILogger<RouterCliController> logger)
        {
            _context = context;
            _routerCliService = routerCliService;
            _logger = logger;
        }

        // GET: RouterCli/Index/5
        public async Task<IActionResult> Index(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var router = await _context.Routers.FindAsync(id);
            if (router == null)
            {
                return NotFound();
            }

            ViewBag.Router = router;
            return View();
        }        // POST: RouterCli/Connect/5
        [HttpPost]
        [Route("RouterCli/Connect/{id}")]
        public async Task<IActionResult> Connect(int id)
        {
            try
            {
                bool connected = await _routerCliService.ConnectAsync(id);
                
                if (connected) {
                    // Kiểm tra lại trạng thái kết nối
                    bool stillConnected = await _routerCliService.IsConnectedAsync(id);
                    if (!stillConnected) {
                        _logger.LogWarning("Kết nối đã mất ngay sau khi kết nối đến router ID {RouterId}", id);
                        return Json(new { success = false, message = "Kết nối bị mất ngay sau khi thiết lập." });
                    }
                }
                
                return Json(new { success = connected, message = connected ? 
                    "Kết nối thành công" : "Không thể kết nối đến router" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kết nối đến router ID {RouterId}: {Message}", id, ex.Message);
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }// POST: RouterCli/Disconnect/5
        [HttpPost]
        [Route("RouterCli/Disconnect/{id}")]
        public async Task<IActionResult> Disconnect(int id)
        {
            try
            {
                await _routerCliService.DisconnectAsync(id);
                return Json(new { success = true, message = "Đã ngắt kết nối" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi ngắt kết nối đến router ID {RouterId}: {Message}", id, ex.Message);
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }        // POST: RouterCli/ExecuteCommand/5
        [HttpPost]
        [Route("RouterCli/ExecuteCommand/{id}")]
        public async Task<IActionResult> ExecuteCommand(int id, [FromBody] CommandRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                return BadRequest(new { success = false, message = "Lệnh không hợp lệ" });
            }

            try
            {
                string result = await _routerCliService.ExecuteCommandAsync(id, request.Command);
                string prompt = await _routerCliService.GetPromptAsync(id);
                
                return Json(new { success = true, output = result, prompt = prompt });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thực hiện lệnh trên router ID {RouterId}: {Command}", 
                    id, request.Command);
                    
                return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }// GET: RouterCli/CheckConnection/5
        [HttpGet]
        [Route("RouterCli/CheckConnection/{id}")]
        public async Task<IActionResult> CheckConnection(int id)
        {
            try
            {
                bool connected = await _routerCliService.IsConnectedAsync(id);
                string prompt = connected ? await _routerCliService.GetPromptAsync(id) : string.Empty;
                
                return Json(new { connected = connected, prompt = prompt });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kiểm tra kết nối đến router ID {RouterId}: {Message}", id, ex.Message);
                return Json(new { connected = false, message = $"Lỗi: {ex.Message}" });
            }
        }
    }    public class CommandRequest
    {
        public required string Command { get; set; }
    }
}
