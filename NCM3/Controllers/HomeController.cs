using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NCM3.Models;
using NCM3.Models.ViewModels;

namespace NCM3.Controllers
{    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly NCMDbContext _context;
        private readonly Services.NotificationLogger? _notificationLogger;

        public HomeController(
            ILogger<HomeController> logger, 
            NCMDbContext context,
            Services.NotificationLogger? notificationLogger = null)
        {
            _logger = logger;
            _context = context;
            _notificationLogger = notificationLogger;
        }

        public async Task<IActionResult> Index()
        {
            // Check if there are routers in the system
            if (!_context.Routers.Any())
            {
                // If no routers exist, redirect to routers page to add one
                return RedirectToAction("Index", "Routers");
            }

            // Create and populate the dashboard view model
            var dashboardViewModel = new DashboardViewModel();
            
            // Device statistics
            dashboardViewModel.TotalRouters = await _context.Routers.CountAsync();
            dashboardViewModel.ConnectedRouters = await _context.Routers.CountAsync(r => r.IsAvailable);
            dashboardViewModel.DisconnectedRouters = dashboardViewModel.TotalRouters - dashboardViewModel.ConnectedRouters;
            
            // Backup statistics
            dashboardViewModel.TotalBackups = await _context.RouterConfigurations.CountAsync();
            var now = DateTime.UtcNow;
            dashboardViewModel.BackupsLast24Hours = await _context.RouterConfigurations
                .CountAsync(c => c.BackupDate >= now.AddDays(-1));
            dashboardViewModel.BackupsLast7Days = await _context.RouterConfigurations
                .CountAsync(c => c.BackupDate >= now.AddDays(-7));
            
            var lastBackup = await _context.RouterConfigurations
                .OrderByDescending(c => c.BackupDate)
                .FirstOrDefaultAsync();
            dashboardViewModel.LastBackupTime = lastBackup?.BackupDate;
            
            // Recent configurations - get 5 most recent backups
            var recentConfigs = await _context.RouterConfigurations
                .Include(c => c.Router)
                .OrderByDescending(c => c.BackupDate)
                .Take(5)
                .Select(c => new RouterConfigurationSummary
                {
                    Id = c.Id,
                    RouterId = c.RouterId,
                    RouterHostname = c.Router != null ? c.Router.Hostname : "Unknown",
                    RouterIpAddress = c.Router != null ? c.Router.IpAddress : "Unknown",
                    BackupDate = c.BackupDate,
                    BackupType = c.BackupType ?? "Manual",
                    BackupBy = c.BackupBy ?? "System",
                    Version = c.Version ?? "Unknown"
                })
                .ToListAsync();
            dashboardViewModel.RecentConfigurations = recentConfigs;
            
            // Compliance issues
            dashboardViewModel.TotalComplianceIssues = await _context.ComplianceResults
                .CountAsync(c => !c.IsCompliant);
            dashboardViewModel.CriticalComplianceIssues = await _context.ComplianceResults
                .Where(c => !c.IsCompliant)
                .Join(_context.ComplianceRules, 
                      cr => cr.RuleId,
                      r => r.Id, 
                      (cr, r) => new { ComplianceResult = cr, Rule = r })
                .CountAsync(j => j.Rule.Severity == ComplianceSeverity.Critical);
            
            // Recent notifications - get through the notification logger if available
            if (_notificationLogger != null)
            {
                var notificationHistory = await _notificationLogger.GetNotificationHistoryAsync("all", 1, 5);
                dashboardViewModel.RecentNotifications = notificationHistory.RecentNotifications;
            }
              // Routers without recent backup (more than 7 days old)
            // First, get the basic router data from the database
            var routersWithoutBackup = await _context.Routers
                .Where(r => r.LastBackup == null || r.LastBackup < now.AddDays(-7))
                .Select(r => new 
                {
                    Id = r.Id,
                    Hostname = r.Hostname,
                    IpAddress = r.IpAddress,
                    LastBackup = r.LastBackup
                })
                .ToListAsync();
                
            // Then perform the days calculation in memory
            var routersWithoutRecentBackup = routersWithoutBackup
                .Select(r => new RouterWithoutBackup
                {
                    Id = r.Id,
                    Hostname = r.Hostname,
                    IpAddress = r.IpAddress,
                    LastBackup = r.LastBackup,
                    DaysSinceLastBackup = r.LastBackup.HasValue ? 
                        (int)(now - r.LastBackup.Value).TotalDays : 999
                })
                .OrderByDescending(r => r.DaysSinceLastBackup)
                .Take(5)
                .ToList();
                
            dashboardViewModel.RoutersWithoutRecentBackup = routersWithoutRecentBackup;

            return View(dashboardViewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }        // GET: /Home/Restore
        public IActionResult Restore()
        {
            return View();
        }

        // GET: /Home/GetRouters
        [HttpGet]
        public async Task<IActionResult> GetRouters()
        {
            try
            {
                var routers = await _context.Routers                .AsNoTracking()
                .OrderBy(r => r.Hostname)
                .Select(r => new { r.Id, r.Hostname, r.IpAddress, r.IsAvailable })
                .ToListAsync();

                return Json(routers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách router");
                return Json(new { error = "Không thể tải danh sách router: " + ex.Message });
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
