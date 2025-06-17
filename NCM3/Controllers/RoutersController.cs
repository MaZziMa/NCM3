using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NCM3.Models;
using NCM3.Services;
using NCM3.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // Added for S3 settings

namespace NCM3.Controllers
{
    public class RoutersController : Controller
    {
        private readonly NCMDbContext _context;
        private readonly RouterService _routerService;
        private readonly IEncryptionService _encryptionService;
        private readonly ConfigurationManagementService _configService;
        private readonly ITelegramNotificationService _telegramService;
        private readonly NotificationHelper _notificationHelper;
        private readonly ILogger<RoutersController> _logger;
        private readonly IS3BackupService _s3Service; // Added for S3 backup
        private readonly IConfiguration _configuration; // Added for S3 settings

        public RoutersController(NCMDbContext context, 
                              RouterService routerService, 
                              IEncryptionService encryptionService,
                              ConfigurationManagementService configService,
                              ITelegramNotificationService telegramService,
                              NotificationHelper notificationHelper,
                              ILogger<RoutersController> logger,
                              IS3BackupService s3BackupService, // Added
                              IConfiguration configuration) // Added
        {
            _context = context;
            _routerService = routerService;
            _encryptionService = encryptionService;
            _configService = configService;
            _telegramService = telegramService;
            _notificationHelper = notificationHelper;
            _logger = logger;
            _s3Service = s3BackupService; // Added
            _configuration = configuration; // Added
        }

        // GET: Routers
        public async Task<IActionResult> Index()
        {
            return View(await _context.Routers.ToListAsync());
        }

        // GET: Routers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var router = await _context.Routers
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (router == null)
            {
                return NotFound();
            }

            return View(router);
        }

        // GET: Routers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Routers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Hostname,IpAddress,Username,Password,EnablePassword,Model,OSVersion")] Router router)
        {
            if (ModelState.IsValid)
            {
                // Mã hóa mật khẩu trước khi lưu vào database
                router.Password = _encryptionService.Encrypt(router.Password);
                if (!string.IsNullOrEmpty(router.EnablePassword))
                {
                    router.EnablePassword = _encryptionService.Encrypt(router.EnablePassword);
                }
                
                router.Status = RouterStatus.Unknown;
                router.IsAvailable = false; // Set default availability status to false
                _context.Add(router);
                await _context.SaveChangesAsync();
                
                // Gửi thông báo đến Telegram về router mới
                try
                {
                    await _telegramService.SendRouterAddedNotificationAsync(
                        router.Hostname,
                        router.IpAddress,
                        router.Model ?? "Không xác định",
                        router.OSVersion ?? "Không xác định"
                    );
                    _logger.LogInformation("Đã gửi thông báo Telegram về router mới {RouterName}", router.Hostname);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi gửi thông báo Telegram về router mới {RouterName}: {Message}", 
                        router.Hostname, ex.Message);
                    // Không dừng luồng chính nếu gặp lỗi khi gửi thông báo
                }
                
                return RedirectToAction(nameof(Index));
            }
            return View(router);
        }

        // GET: Routers/Edit/5
        public async Task<IActionResult> Edit(int? id)
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
            return View(router);
        }

        // POST: Routers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Hostname,IpAddress,Username,Password,EnablePassword,Model,OSVersion,Status,IsAvailable")] Router router)
        {
            if (id != router.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Check if password was left blank (meaning keep the existing one)
                    var existingRouter = await _context.Routers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
                    if (existingRouter != null)
                    {
                        if (string.IsNullOrEmpty(router.Password))
                        {
                            router.Password = existingRouter.Password;
                        }
                        else
                        {
                            // Mã hóa mật khẩu mới
                            router.Password = _encryptionService.Encrypt(router.Password);
                        }
                        
                        if (string.IsNullOrEmpty(router.EnablePassword))
                        {
                            router.EnablePassword = existingRouter.EnablePassword;
                        }
                        else if (!string.IsNullOrEmpty(router.EnablePassword))
                        {
                            // Mã hóa enable password mới
                            router.EnablePassword = _encryptionService.Encrypt(router.EnablePassword);
                        }
                    }
                    
                    _context.Update(router);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!RouterExists(router.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(router);
        }

        // GET: Routers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var router = await _context.Routers
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (router == null)
            {
                return NotFound();
            }

            return View(router);
        }

        // POST: Routers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var router = await _context.Routers.FindAsync(id);
            if (router != null)
            {
                _context.Routers.Remove(router);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(Index));
        }

        // GET: Routers/TestConnection/5
        public async Task<IActionResult> TestConnection(int? id)
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

            bool isConnected = await _routerService.CheckConnectionAsync(router);
            
            // Update the router status in the database
            _context.Update(router);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(Details), new { id = router.Id });
        }

        // GET: Routers/BackupConfiguration/5
        public async Task<IActionResult> BackupConfiguration(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var router = await _context.Routers
                .Include(r => r.RouterConfigurations)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (router == null)
            {
                return NotFound();
            }

            try
            {
                string configContent = await _routerService.GetConfigurationAsync(router);
                
                // Create a new configuration backup
                var routerConfig = new RouterConfiguration
                {
                    RouterId = router.Id,
                    BackupDate = DateTime.UtcNow, 
                    Content = configContent, // Use the fetched config content
                    Version = $"Manual_{DateTime.UtcNow:yyyyMMdd_HHmmss}", // Changed prefix to Manual
                    BackupBy = User?.Identity?.Name ?? "System", // Get current user or default to System
                    BackupType = BackupTypes.Manual 
                };
                
                _context.RouterConfigurations.Add(routerConfig);
                
                router.LastBackup = routerConfig.BackupDate;
                _context.Update(router);
                
                await _context.SaveChangesAsync();

                // Upload to S3 if enabled
                bool enableS3Backup = _configuration.GetValue<bool>("AWS:S3:EnableS3Backup", false);
                if (enableS3Backup)
                {
                    try
                    {
                        bool s3UploadSuccess = await _s3Service.UploadBackupAsync(router.Id, configContent, routerConfig.Version, routerConfig.BackupBy);
                        if (s3UploadSuccess)
                        {
                            _logger.LogInformation($"Đã tải bản sao lưu thủ công {routerConfig.Version} của router {router.Hostname} lên S3.");
                        }
                        else
                        {
                            _logger.LogWarning($"Không tải được bản sao lưu thủ công {routerConfig.Version} của router {router.Hostname} lên S3.");
                            // Optionally, add a TempData message for the user if S3 upload fails
                            // TempData["WarningMessage"] = "Đã sao lưu cục bộ thành công, nhưng không thể tải lên S3.";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Lỗi khi tải bản sao lưu thủ công {routerConfig.Version} của router {router.Hostname} lên S3.");
                        // Optionally, add a TempData message for the user
                        // TempData["WarningMessage"] = "Đã sao lưu cục bộ thành công, nhưng đã xảy ra lỗi khi tải lên S3.";
                    }
                }
                
                // Kiểm tra và gửi thông báo nếu có thay đổi
                await _notificationHelper.DetectAndNotifyConfigurationChangeAsync(
                    router,
                    configContent, // Use the fetched config content
                    "Sao lưu cấu hình thủ công"
                );
                
                _logger.LogInformation("Đã sao lưu cấu hình cho router {RouterName}", router.Hostname);
                TempData["SuccessMessage"] = "Đã sao lưu cấu hình thành công.";
                
                return RedirectToAction(nameof(ConfigurationHistory), new { id = router.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi sao lưu cấu hình router {RouterId}: {Message}", id, ex.Message);
                TempData["ErrorMessage"] = $"Lỗi khi sao lưu cấu hình: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id = router.Id });
            }
        }

        // GET: Routers/ConfigurationHistory/5
        public async Task<IActionResult> ConfigurationHistory(int? id)
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

            var configs = await _context.RouterConfigurations
                .Where(c => c.RouterId == id)
                .OrderByDescending(c => c.BackupDate)
                .ToListAsync();
                
            ViewBag.Router = router;
            
            return View(configs);
        }

        // GET: Routers/ViewConfiguration/5
        public async Task<IActionResult> ViewConfiguration(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var config = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (config == null)
            {
                return NotFound();
            }

            return View(config);
        }

        // GET: Routers/DebugSsh/5
        public async Task<IActionResult> DebugSsh(int? id)
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
            ViewBag.DebugLog = await _routerService.DebugSshConnectionAsync(router);
            
            return View();
        }

        private bool RouterExists(int id)
        {
            return _context.Routers.Any(e => e.Id == id);
        }
    }
}