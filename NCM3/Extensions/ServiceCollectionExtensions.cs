using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCM3.Services;
using NCM3.Services.Events;
using NCM3.Services.ChangeDetection;
using NCM3.Models;

namespace NCM3.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddNotificationServices(this IServiceCollection services)
        {
            // Register a hosted service that will handle the circular dependency initialization
            services.AddHostedService<NotificationInitializerService>();
        }          public static void AddChangeDetectionServices(this IServiceCollection services)
        {
            // Register the event bus as a singleton
            services.AddSingleton<IEventBus, InMemoryEventBus>();
            
            // Create factory for RouterService to avoid scoping issues
            services.AddSingleton<Func<RouterService>>(serviceProvider => () => 
            {
                // Create a scope to resolve the scoped RouterService
                var scope = serviceProvider.CreateScope();
                return scope.ServiceProvider.GetRequiredService<RouterService>();
            });
            
            // Create factory for DbContext to avoid scoping issues
            services.AddSingleton<Func<NCMDbContext>>(serviceProvider => () => 
            {
                // Create a scope to resolve the scoped DbContext
                var scope = serviceProvider.CreateScope();
                return scope.ServiceProvider.GetRequiredService<NCMDbContext>();
            });
            
            // Register change detection strategies with factory dependencies
            services.AddSingleton<SNMPPollingStrategy>(serviceProvider => 
            {
                var logger = serviceProvider.GetRequiredService<ILogger<SNMPPollingStrategy>>();
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var routerServiceFactory = serviceProvider.GetRequiredService<Func<RouterService>>();
                var dbContextFactory = serviceProvider.GetRequiredService<Func<NCMDbContext>>();
                
                return new SNMPPollingStrategy(logger, configuration, routerServiceFactory, dbContextFactory);
            });
            
            services.AddSingleton<SSHPollingStrategy>(serviceProvider => 
            {
                var logger = serviceProvider.GetRequiredService<ILogger<SSHPollingStrategy>>();
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var routerServiceFactory = serviceProvider.GetRequiredService<Func<RouterService>>();
                var dbContextFactory = serviceProvider.GetRequiredService<Func<NCMDbContext>>();
                
                return new SSHPollingStrategy(logger, configuration, routerServiceFactory, dbContextFactory);
            });
            
            // Create a singleton factory that will provide the strategies
            services.AddSingleton<Func<IEnumerable<IChangeDetectionStrategy>>>(serviceProvider => () =>
            {
                var strategies = new List<IChangeDetectionStrategy>
                {
                    serviceProvider.GetRequiredService<SNMPPollingStrategy>(),
                    serviceProvider.GetRequiredService<SSHPollingStrategy>()
                };
                
                return strategies;
            });
            
            // Register the orchestrator as a hosted service
            services.AddHostedService<ChangeDetectionOrchestrator>();
        }

        public static IServiceCollection AddCustomServices(this IServiceCollection services)
        {
            services.AddScoped<NCMDbContext>();
            services.AddSingleton<Func<NCMDbContext>>(serviceProvider => () =>
            {
                var scope = serviceProvider.CreateScope();
                return scope.ServiceProvider.GetRequiredService<NCMDbContext>();
            });

            services.AddScoped<RouterService>();
            services.AddSingleton<Func<RouterService>>(serviceProvider => () =>
            {
                var scope = serviceProvider.CreateScope();
                return scope.ServiceProvider.GetRequiredService<RouterService>();
            });

            services.AddScoped<IChangeDetectionStrategy, SNMPPollingStrategy>();
            services.AddScoped<IChangeDetectionStrategy, SSHPollingStrategy>();
            services.AddSingleton<ChangeDetectionOrchestrator>();

            return services;
        }
    }    // This hosted service will run after all services are built and initialize the circular dependency
    public class NotificationInitializerService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public NotificationInitializerService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Create a scope to resolve scoped services
            using (var scope = _serviceProvider.CreateScope())
            {
                var configService = scope.ServiceProvider.GetRequiredService<ConfigurationManagementService>();
                var notificationHelper = scope.ServiceProvider.GetRequiredService<NotificationHelper>();
                var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramNotificationService>();
                
                // Set up the circular dependency
                configService.SetNotificationHelper(notificationHelper);
                
                // Initialize the Telegram notification service
                telegramService.Initialize();
            }
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
