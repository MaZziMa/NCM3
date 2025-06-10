using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using NCM3.Services.Events;

namespace NCM3.Services.ChangeDetection
{
    /// <summary>
    /// Orchestrates multiple configuration change detection strategies
    /// </summary>
    public class ChangeDetectionOrchestrator : BackgroundService
    {        private readonly ILogger<ChangeDetectionOrchestrator> _logger;
        private readonly IConfiguration _configuration;
        private readonly Func<IEnumerable<IChangeDetectionStrategy>> _strategiesFactory;
        private readonly IEventBus _eventBus;
        
        public ChangeDetectionOrchestrator(
            ILogger<ChangeDetectionOrchestrator> logger,
            IConfiguration configuration,
            Func<IEnumerable<IChangeDetectionStrategy>> strategiesFactory,
            IEventBus eventBus)
        {
            _logger = logger;
            _configuration = configuration;
            _strategiesFactory = strategiesFactory;
            _eventBus = eventBus;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Starting Change Detection Orchestrator");
                  // Initialize all strategies
                var strategies = _strategiesFactory();
                foreach (var strategy in strategies)
                {
                    try
                    {
                        await strategy.InitializeAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error initializing strategy {StrategyName}: {ErrorMessage}",
                            strategy.Name, ex.Message);
                    }
                }
                
                // Start all enabled strategies
                foreach (var strategy in strategies)
                {
                    try
                    {
                        if (strategy.IsEnabled)
                        {
                            await strategy.StartDetectionAsync(_eventBus, stoppingToken);
                            _logger.LogInformation("Started change detection strategy: {StrategyName} (Priority: {Priority})",
                                strategy.Name, strategy.Priority);
                        }
                        else
                        {
                            _logger.LogInformation("Strategy {StrategyName} is disabled and will not be started",
                                strategy.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error starting strategy {StrategyName}: {ErrorMessage}",
                            strategy.Name, ex.Message);
                    }
                }
                
                // Keep the service running until cancellation is requested
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, no need to log an error
                _logger.LogInformation("Change Detection Orchestrator was stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Change Detection Orchestrator: {ErrorMessage}", ex.Message);
                throw;
            }
            finally
            {                // Clean shutdown - stop all strategies
                var strategies = _strategiesFactory();
                foreach (var strategy in strategies)
                {
                    try
                    {
                        if (strategy.IsEnabled)
                        {
                            await strategy.StopDetectionAsync();
                            _logger.LogInformation("Stopped change detection strategy: {StrategyName}", strategy.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error stopping strategy {StrategyName}: {ErrorMessage}",
                            strategy.Name, ex.Message);
                    }
                }
            }
        }
    }
}
