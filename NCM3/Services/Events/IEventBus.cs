using System;
using System.Threading.Tasks;

namespace NCM3.Services.Events
{
    /// <summary>
    /// Base interface for application events
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// When the event occurred
        /// </summary>
        DateTime Timestamp { get; }
        
        /// <summary>
        /// Unique identifier for the event
        /// </summary>
        Guid Id { get; }
    }
    
    /// <summary>
    /// The event bus interface
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// Publish an event
        /// </summary>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
        
        /// <summary>
        /// Subscribe to events of a specific type
        /// </summary>
        void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent;
        
        /// <summary>
        /// Unsubscribe from events of a specific type
        /// </summary>
        void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent;
    }
}