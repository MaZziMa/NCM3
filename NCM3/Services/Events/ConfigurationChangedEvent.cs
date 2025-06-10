using System;
using NCM3.Models;

namespace NCM3.Services.Events
{
    /// <summary>
    /// Event raised when a router's configuration has changed
    /// </summary>
    public class ConfigurationChangedEvent : IEvent
    {
        /// <summary>
        /// When the event occurred
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        
        /// <summary>
        /// Unique identifier for the event
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();
        
        /// <summary>
        /// The router that had its configuration changed
        /// </summary>
        public Router Router { get; }
        
        /// <summary>
        /// The old configuration content
        /// </summary>
        public string OldContent { get; }
        
        /// <summary>
        /// The new configuration content
        /// </summary>
        public string NewContent { get; }
        
        /// <summary>
        /// The priority of this change
        /// </summary>
        public string Priority { get; }
        
        /// <summary>
        /// The strategy that detected the change
        /// </summary>
        public string DetectionStrategy { get; }
        
        /// <summary>
        /// A human-readable description of the change
        /// </summary>
        public string ChangeDescription { get; }
        
        public ConfigurationChangedEvent(
            Router router, 
            string oldContent, 
            string newContent, 
            string priority,
            string detectionStrategy,
            string changeDescription)
        {
            Router = router;
            OldContent = oldContent;
            NewContent = newContent;
            Priority = priority;
            DetectionStrategy = detectionStrategy;
            ChangeDescription = changeDescription;
        }
    }
}