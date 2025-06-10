using System;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace NCM3.Extensions
{
    public static class LoggingExtensions
    {
        public static IServiceCollection AddFileLogger(this IServiceCollection services, IConfiguration configuration)
        {
            // Đảm bảo thư mục logs tồn tại
            var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddConsole();
                builder.AddDebug();
                
                // Thêm file logger nếu đã cấu hình đường dẫn
                var logFilePath = configuration["Logging:File:Path"];
                if (!string.IsNullOrEmpty(logFilePath))
                {
                    builder.AddFile(configuration.GetSection("Logging:File"));
                }
            });

            return services;
        }
    }
}
