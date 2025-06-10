using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using NCM3.Services;
using System.IO;

namespace NCM3.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RestoreController : ControllerBase
    {
        private readonly ILogger<RestoreController> _logger;
        private readonly NCMDbContext _context;
        private readonly RouterService _routerService;

        public RestoreController(ILogger<RestoreController> logger, NCMDbContext context, RouterService routerService)
        {
            _logger = logger;
            _context = context;
            _routerService = routerService;
        }

        // GET: api/restore/router/{id}/backups
        [HttpGet("router/{id}/backups")]
        public async Task<IActionResult> GetRouterBackups(int id)
        {
            _logger.LogInformation("Getting backup list for router with ID {RouterId}", id);
            
            var router = await _context.Routers.FindAsync(id);
            if (router == null)
            {
                _logger.LogWarning("Router with ID {RouterId} not found", id);
                return NotFound(new { success = false, message = "Router không tồn tại" });
            }

            var backups = await _context.RouterConfigurations
                .Where(c => c.RouterId == id)
                .OrderByDescending(c => c.BackupDate)
                .Select(c => new {
                    c.Id,
                    c.BackupDate,
                    c.BackupType,
                    ConfigSize = c.Content.Length,
                    c.Comment
                })
                .ToListAsync();

            _logger.LogInformation("Retrieved {BackupCount} backups for router {RouterName}", backups.Count, router.Hostname);
            
            return Ok(new { 
                success = true, 
                router = new { router.Id, router.Hostname, router.IpAddress },
                backups 
            });
        }

        // GET: api/restore/backup/{id}
        [HttpGet("backup/{id}")]
        public async Task<IActionResult> GetBackupDetail(int id)
        {
            _logger.LogInformation("Getting details for backup with ID {BackupId}", id);
            
            var backup = await _context.RouterConfigurations.FindAsync(id);
            if (backup == null)
            {
                _logger.LogWarning("Backup with ID {BackupId} not found", id);
                return NotFound(new { success = false, message = "Bản sao lưu không tồn tại" });
            }

            var router = await _context.Routers.FindAsync(backup.RouterId);
            if (router == null)
            {
                _logger.LogWarning("Associated router for backup ID {BackupId} not found", id);
                return NotFound(new { success = false, message = "Router không tồn tại" });
            }

            _logger.LogInformation("Retrieved details for backup {BackupId} of router {RouterName}", 
                id, router.Hostname);
            
            return Ok(new { 
                success = true, 
                backup = new {
                    backup.Id,
                    backup.BackupDate,
                    backup.BackupType,
                    backup.Content,
                    backup.Comment,
                    Router = new { router.Id, router.Hostname, router.IpAddress }
                }
            });
        }

        // POST: api/restore/router/{id}
        [HttpPost("router/{id}")]
        public async Task<IActionResult> RestoreRouter(int id, [FromBody] RestoreRequest request)
        {
            _logger.LogInformation("Received restore request for router ID {RouterId} with backup ID {BackupId}",
                id, request?.BackupId);
                
            if (request == null || request.BackupId <= 0)
            {
                _logger.LogWarning("Invalid restore request received for router {RouterId}", id);
                return BadRequest(new { success = false, message = "Yêu cầu không hợp lệ" });
            }

            // Fetch router with configurations included
            var router = await _context.Routers
                .Include(r => r.RouterConfigurations)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (router == null)
            {
                _logger.LogWarning("Router with ID {RouterId} not found", id);
                return NotFound(new { success = false, message = "Router không tồn tại" });
            }

            // Get the specific backup to restore
            var backup = await _context.RouterConfigurations.FindAsync(request.BackupId);
            if (backup == null || backup.RouterId != id)
            {
                _logger.LogWarning("Backup {BackupId} not found or doesn't belong to router {RouterName}", 
                    request.BackupId, router.Hostname);
                return NotFound(new { 
                    success = false, 
                    message = "Bản sao lưu không tồn tại hoặc không thuộc về router này" 
                });
            }

            // Validate IP address before proceeding
            if (string.IsNullOrWhiteSpace(router.IpAddress))
            {
                _logger.LogError("Router {RouterName} (ID: {RouterId}) has no IP address configured", 
                    router.Hostname, router.Id);
                return BadRequest(new { 
                    success = false, 
                    message = $"Router {router.Hostname} không có địa chỉ IP" 
                });
            }

            if (!System.Net.IPAddress.TryParse(router.IpAddress, out var ipAddress))
            {
                _logger.LogError("Router {RouterName} has invalid IP address: {IPAddress}", 
                    router.Hostname, router.IpAddress);
                return BadRequest(new { 
                    success = false, 
                    message = $"Router {router.Hostname} có địa chỉ IP không hợp lệ: {router.IpAddress}" 
                });
            }

            try
            {
                // Create a backup of the current configuration if requested
                if (request.CreateBackupBeforeRestore)
                {
                    try
                    {
                        _logger.LogInformation("Creating pre-restore backup for router {RouterName}", router.Hostname);
                        
                        // Get the current configuration before restoring
                        string currentConfig = await _routerService.GetConfigurationAsync(router);
                        
                        // Check if the configuration retrieval failed
                        if (string.IsNullOrEmpty(currentConfig) || currentConfig.StartsWith("Error:"))
                        {
                            _logger.LogWarning("Could not create pre-restore backup for router {RouterName}: {ErrorMessage}", 
                                router.Hostname, currentConfig ?? "No configuration received");
                            
                            // Continue with restore anyway, but inform the user
                            _logger.LogInformation("Continuing with restore operation despite backup failure");
                        }
                        else
                        {
                            // Create a backup of the current configuration
                            var currentBackup = new RouterConfiguration
                            {
                                RouterId = router.Id,
                                BackupDate = DateTime.UtcNow,
                                BackupType = "Pre-Restore",
                                Content = currentConfig,
                                BackupBy = User.Identity?.Name ?? "System",
                                Comment = "Bản sao lưu tự động trước khi khôi phục"
                            };
                            
                            _context.RouterConfigurations.Add(currentBackup);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Successfully created pre-restore backup (ID: {BackupId}) for router {RouterName}", 
                                currentBackup.Id, router.Hostname);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create pre-restore backup for router {RouterName}", router.Hostname);
                        // Continue with restore anyway since this is just a precaution
                    }
                }
                
                // Log the actual restoration attempt
                _logger.LogInformation("Attempting to restore configuration to router {RouterName} ({IPAddress}) from backup created on {BackupDate}", 
                    router.Hostname, ipAddress.ToString(), backup.BackupDate);

                // Perform the actual restoration
                bool success = await _routerService.RestoreConfigurationAsync(router, backup.Content);
                
                if (success)
                {
                    _logger.LogInformation("Successfully restored configuration for router {RouterName} from backup {BackupId}", 
                        router.Hostname, backup.Id);
                    
                    // Record the restoration in the database
                    var restoreRecord = new RouterConfiguration
                    {
                        RouterId = router.Id,
                        BackupDate = DateTime.UtcNow,
                        BackupType = "Restore",
                        Content = backup.Content,
                        BackupBy = User.Identity?.Name ?? "System",
                        Comment = $"Khôi phục từ bản sao lưu ngày {backup.BackupDate:yyyy-MM-dd HH:mm:ss}"
                    };
                    
                    _context.RouterConfigurations.Add(restoreRecord);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Created restore record (ID: {RecordId}) for router {RouterName}", 
                        restoreRecord.Id, router.Hostname);
                    
                    return Ok(new { 
                        success = true, 
                        message = $"Đã khôi phục cấu hình thành công cho router {router.Hostname}" 
                    });
                }
                else
                {
                    _logger.LogError("Failed to restore configuration for router {RouterName} from backup {BackupId}", 
                        router.Hostname, backup.Id);
                    
                    return BadRequest(new { 
                        success = false, 
                        message = $"Không thể khôi phục cấu hình cho router {router.Hostname}" 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while restoring configuration for router {RouterName}: {Error}",
                    router.Hostname, ex.Message);
                
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Lỗi khi khôi phục cấu hình: {ex.Message}" 
                });
            }
        }
    }

    public class RestoreRequest
    {
        public int BackupId { get; set; }
        public bool CreateBackupBeforeRestore { get; set; } = true;
    }
}
