using Microsoft.EntityFrameworkCore;
using NCM3.Models;
using NCM3.Services;
using NCM3.Middleware;
using NCM3.Extensions;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using FluentValidation;
using NCM3.Validators;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Amazon.S3;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Đọc file appsettings.secrets.json nếu tồn tại
string secretsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.secrets.json");
if (File.Exists(secretsFilePath))
{
    builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
}

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        policy.WithOrigins("https://localhost:5001", "https://localhost:5000") 
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add health checks
builder.Services.AddHealthChecks();

// Add DB context - using SQL Server
builder.Services.AddDbContext<NCMDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services
builder.Services.AddScoped<RouterService>();
builder.Services.AddScoped<RouterConnectionService>();
builder.Services.AddScoped<ConfigurationManagementService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITelegramNotificationService, TelegramNotificationService>();
builder.Services.AddScoped<IWebhookNotificationService, WebhookNotificationService>();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<NotificationHelper>();
builder.Services.AddScoped<NotificationLogger>();
builder.Services.AddScoped<IRouterCliService, RouterCliService>();

// Add AWS S3 services
var awsOptions = builder.Configuration.GetAWSOptions();
// Ensure credentials are explicitly configured from appsettings.json
var accessKey = builder.Configuration["AWS:AccessKeyId"];
var secretKey = builder.Configuration["AWS:SecretAccessKey"];
if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
{
    // Always set credentials explicitly to ensure they're used
    awsOptions.Credentials = new BasicAWSCredentials(accessKey, secretKey);
    Console.WriteLine($"AWS Credentials configured explicitly from appsettings.json");
}
else
{
    Console.WriteLine("Warning: AWS credentials not found in appsettings.json");
}

// Đảm bảo Region được thiết lập
if (awsOptions.Region == null && !string.IsNullOrEmpty(builder.Configuration["AWS:Region"]))
{
    awsOptions.Region = Amazon.RegionEndpoint.GetBySystemName(builder.Configuration["AWS:Region"]);
    Console.WriteLine($"AWS Region set to: {awsOptions.Region?.SystemName}");
}

builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<IS3BackupService, S3BackupService>();

// Add change detection services
builder.Services.AddChangeDetectionServices();

// Thêm dịch vụ tự động phát hiện thay đổi cấu hình
builder.Services.AddHostedService<AutomaticConfigurationChangeDetector>();

// Thêm dịch vụ xử lý thông báo
builder.Services.AddNotificationServices();

// Thêm File Logger
builder.Services.AddFileLogger(builder.Configuration);

// Cấu hình thư mục sao lưu
builder.Services.ConfigureBackupFolders(builder.Configuration);

// Đăng ký FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<RouterValidator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    // Trong môi trường phát triển, sử dụng middleware xử lý ngoại lệ toàn cục
    app.UseGlobalExceptionHandler();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Thêm CORS
app.UseCors("DefaultPolicy");

// Thêm Security Headers
app.Use(async (context, next) =>
{
    // Các security headers cơ bản
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    
    // Content-Security-Policy đã được cập nhật để cho phép các nguồn tài nguyên cần thiết
    context.Response.Headers.Append("Content-Security-Policy", 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " + 
        "font-src 'self' https://cdn.jsdelivr.net; " + 
        "img-src 'self' data: https:; " +
        "connect-src 'self';"
    );
    
    await next();
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Map health check endpoint
app.MapHealthChecks("/health");

app.Run();
