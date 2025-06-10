using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DiffPlex.DiffBuilder.Model;
using NCM3.Models;
using NCM3.Services;

namespace NCM3.Controllers
{
    public class ConfigManagementController : Controller
    {
        private readonly NCMDbContext _context;
        private readonly ConfigurationManagementService _configService;

        public ConfigManagementController(NCMDbContext context, ConfigurationManagementService configService)
        {
            _context = context;
            _configService = configService;
        }

        // GET: ConfigManagement/CompareSelection/{routerId}
        public async Task<IActionResult> CompareSelection(int? routerId)
        {
            if (routerId == null)
            {
                return NotFound();
            }

            var router = await _context.Routers.FindAsync(routerId);
            if (router == null)
            {
                return NotFound();
            }

            var configs = await _context.RouterConfigurations
                .Where(c => c.RouterId == routerId)
                .OrderByDescending(c => c.BackupDate)
                .ToListAsync();

            ViewBag.Router = router;
            ViewBag.ConfigList = new SelectList(configs, "Id", "Version");

            return View();
        }

        // POST: ConfigManagement/Compare
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Compare(int configId1, int configId2)
        {
            var config1 = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId1);
                
            var config2 = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId2);

            if (config1 == null || config2 == null)
            {
                return NotFound();
            }

            var diff = await _configService.CompareConfigurationsAsync(configId1, configId2);

            ViewBag.Config1 = config1;
            ViewBag.Config2 = config2;
            ViewBag.FullConfig1 = config1.Content;
            ViewBag.FullConfig2 = config2.Content;
            
            return View(diff);
        }
        
        // GET: ConfigManagement/UnifiedCompareWithTemplate/{configId}/{templateId}
        [HttpGet]
        public async Task<IActionResult> UnifiedCompareWithTemplate(int configId, int templateId)
        {
            var config = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId);
                
            var template = await _context.ConfigTemplates.FindAsync(templateId);

            if (config == null || template == null)
            {
                return NotFound();
            }

            var diff = await _configService.CompareWithTemplateAsync(configId, templateId);

            ViewBag.Config = config;
            ViewBag.Template = template;
            ViewBag.FullConfig = config.Content;
            ViewBag.FullTemplate = template.Content;
            
            return View(diff);
        }

        // GET: ConfigManagement/Search
        public IActionResult Search()
        {
            ViewBag.Routers = new SelectList(_context.Routers, "Id", "Hostname");
            return View();
        }

        // POST: ConfigManagement/SearchResults
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SearchResults(string searchTerm, int? routerId = null)
        {
            if (string.IsNullOrEmpty(searchTerm))
            {
                return RedirectToAction(nameof(Search));
            }

            var results = await _configService.SearchInConfigurationsAsync(searchTerm, routerId);
            
            ViewBag.SearchTerm = searchTerm;
            if (routerId.HasValue)
            {
                ViewBag.Router = await _context.Routers.FindAsync(routerId);
            }
            
            return View(results);
        }

        // GET: ConfigManagement/Templates
        public async Task<IActionResult> Templates()
        {
            return View(await _context.ConfigTemplates.ToListAsync());
        }
        
        // GET: ConfigManagement/CreateTemplate
        public IActionResult CreateTemplate()
        {
            return View();
        }
        
        // POST: ConfigManagement/CreateTemplate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTemplate([Bind("Name,Content,Description,DeviceType")] ConfigTemplate template)
        {
            if (ModelState.IsValid)
            {
                template.CreatedDate = DateTime.Now;
                template.CreatedBy = "System"; // Sau này sẽ là người dùng đăng nhập
                template.Version = "1.0";
                
                _context.Add(template);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Templates));
            }
            return View(template);
        }
        
        // GET: ConfigManagement/EditTemplate/5
        public async Task<IActionResult> EditTemplate(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var template = await _context.ConfigTemplates.FindAsync(id);
            if (template == null)
            {
                return NotFound();
            }
            
            return View(template);
        }
        
        // POST: ConfigManagement/EditTemplate/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTemplate(int id, [Bind("Id,Name,Content,Description,DeviceType,CreatedDate,CreatedBy")] ConfigTemplate template)
        {
            if (id != template.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Cập nhật phiên bản
                    if (!string.IsNullOrEmpty(template.Version))
                    {
                        var versionParts = template.Version.Split('.');
                        if (versionParts.Length >= 2 && int.TryParse(versionParts[1], out int minorVersion))
                        {
                            template.Version = $"{versionParts[0]}.{minorVersion + 1}";
                        }
                    }
                    else
                    {
                        template.Version = "1.0";
                    }
                    
                    _context.Update(template);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TemplateExists(template.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Templates));
            }
            return View(template);
        }
        
        // GET: ConfigManagement/DeleteTemplate/5
        public async Task<IActionResult> DeleteTemplate(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var template = await _context.ConfigTemplates
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (template == null)
            {
                return NotFound();
            }

            return View(template);
        }
        
        // POST: ConfigManagement/DeleteTemplateConfirmed/5
        [HttpPost, ActionName("DeleteTemplate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTemplateConfirmed(int id)
        {
            var template = await _context.ConfigTemplates.FindAsync(id);
            if (template != null)
            {
                _context.ConfigTemplates.Remove(template);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(Templates));
        }
        
        // GET: ConfigManagement/CompareWithTemplate/{configId}
        public async Task<IActionResult> CompareWithTemplateSelection(int? configId)
        {
            if (configId == null)
            {
                return NotFound();
            }

            var config = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId);
                
            if (config == null)
            {
                return NotFound();
            }

            var templates = await _context.ConfigTemplates.ToListAsync();
            if (!templates.Any())
            {
                TempData["Message"] = "Không có template nào. Vui lòng tạo template trước.";
                return RedirectToAction("Templates");
            }

            ViewBag.Config = config;
            ViewBag.Templates = new SelectList(templates, "Id", "Name");

            return View();
        }
        
        // POST: ConfigManagement/CompareWithTemplate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompareWithTemplate(int configId, int templateId)
        {
            var config = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId);
                
            var template = await _context.ConfigTemplates.FindAsync(templateId);

            if (config == null || template == null)
            {
                return NotFound();
            }

            var diff = await _configService.CompareWithTemplateAsync(configId, templateId);

            ViewBag.Config = config;
            ViewBag.Template = template;
            
            return View(diff);
        }

        // GET: ConfigManagement/ComplianceRules
        public async Task<IActionResult> ComplianceRules()
        {
            return View(await _context.ComplianceRules.ToListAsync());
        }
        
        // GET: ConfigManagement/CreateRule
        public IActionResult CreateRule()
        {
            return View();
        }
        
        // POST: ConfigManagement/CreateRule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRule([Bind("Name,Pattern,Description,Severity,DeviceType,ExpectedResult,Notes")] ComplianceRule rule)
        {
            if (ModelState.IsValid)
            {
                rule.CreatedDate = DateTime.Now;
                rule.CreatedBy = "System"; // Sau này sẽ là người dùng đăng nhập
                rule.IsActive = true;
                
                _context.Add(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ComplianceRules));
            }
            return View(rule);
        }
        
        // GET: ConfigManagement/EditRule/5
        public async Task<IActionResult> EditRule(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var rule = await _context.ComplianceRules.FindAsync(id);
            if (rule == null)
            {
                return NotFound();
            }
            
            return View(rule);
        }
        
        // POST: ConfigManagement/EditRule/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRule(int id, [Bind("Id,Name,Pattern,Description,Severity,DeviceType,ExpectedResult,CreatedBy,CreatedDate,IsActive,Notes")] ComplianceRule rule)
        {
            if (id != rule.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(rule);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!RuleExists(rule.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(ComplianceRules));
            }
            return View(rule);
        }
        
        // GET: ConfigManagement/DeleteRule/5
        public async Task<IActionResult> DeleteRule(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var rule = await _context.ComplianceRules
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (rule == null)
            {
                return NotFound();
            }

            return View(rule);
        }
        
        // POST: ConfigManagement/DeleteRule/5
        [HttpPost, ActionName("DeleteRule")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRuleConfirmed(int id)
        {
            var rule = await _context.ComplianceRules.FindAsync(id);
            if (rule != null)
            {
                _context.ComplianceRules.Remove(rule);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(ComplianceRules));
        }
        
        // GET: ConfigManagement/CheckCompliance/{configId}
        public async Task<IActionResult> CheckCompliance(int? configId)
        {
            if (configId == null)
            {
                return NotFound();
            }

            var config = await _context.RouterConfigurations
                .Include(c => c.Router)
                .FirstOrDefaultAsync(c => c.Id == configId);
                
            if (config == null)
            {
                return NotFound();
            }

            var results = await _configService.CheckComplianceAsync(config.Id);
            
            // Lưu kết quả vào database nếu cần
            _context.ComplianceResults.AddRange(results);
            await _context.SaveChangesAsync();
            
            ViewBag.Config = config;
            ViewBag.Router = config.Router;
            
            // Lấy thông tin các quy tắc
            var ruleIds = results.Select(r => r.RuleId).ToList();
            var rules = await _context.ComplianceRules
                .Where(r => ruleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);
            
            ViewBag.Rules = rules;
            
            return View(results);
        }
        
        private bool TemplateExists(int id)
        {
            return _context.ConfigTemplates.Any(e => e.Id == id);
        }
        
        private bool RuleExists(int id)
        {
            return _context.ComplianceRules.Any(e => e.Id == id);
        }
    }
}