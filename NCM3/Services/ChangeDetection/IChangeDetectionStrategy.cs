using System;
using System.Threading;
using System.Threading.Tasks;
using NCM3.Models;
using NCM3.Services.Events;

namespace NCM3.Services.ChangeDetection
{
    /// <summary>
    /// Interface defining the contract for configuration change detection strategies
    /// </summary>
    public interface IChangeDetectionStrategy
    {
        /// <summary>
        /// Name of the strategy
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Priority level of this detection strategy (lower number = higher priority)
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// Whether this strategy is enabled in the configuration
        /// </summary>
        bool IsEnabled { get; }
        
        /// <summary>
        /// Initialize the strategy with any prerequisites
        /// </summary>
        Task InitializeAsync(CancellationToken stoppingToken = default);
        
        /// <summary>
        /// Start the detection process
        /// </summary>
        /// <param name="eventBus">Event bus to publish change events</param>
        /// <param name="stoppingToken">Cancellation token</param>
        Task StartDetectionAsync(IEventBus eventBus, CancellationToken stoppingToken = default);
        
        /// <summary>
        /// Stop the detection process
        /// </summary>
        Task StopDetectionAsync();
        
        /// <summary>
        /// Check a specific router for changes
        /// </summary>
        /// <param name="router">The router to check</param>
        /// <param name="eventBus">Event bus to publish change events</param>
        /// <returns>True if changes were detected, false otherwise</returns>
        Task<bool> CheckForChangesAsync(Router router, IEventBus eventBus);
    }
}