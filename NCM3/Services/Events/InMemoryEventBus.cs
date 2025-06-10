using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NCM3.Services.Events
{
    /// <summary>
    /// An in-memory implementation of the event bus
    /// </summary>
    public class InMemoryEventBus : IEventBus
    {
        private readonly ILogger<InMemoryEventBus> _logger;
        private readonly ConcurrentDictionary<Type, List<Func<IEvent, Task>>> _handlers = new();

        public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Publish an event to all subscribers
        /// </summary>
        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            _logger.LogDebug("Publishing event of type {EventType} with ID {EventId}", eventType.Name, @event.Id);

            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                _logger.LogDebug("No handlers registered for event type {EventType}", eventType.Name);
                return;
            }

            var tasks = new List<Task>();
            foreach (var handler in handlers.ToList()) // Create a copy to avoid concurrent modification issues
            {
                try
                {
                    tasks.Add(handler(@event));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling event of type {EventType}: {ErrorMessage}", 
                        eventType.Name, ex.Message);
                }
            }

            // Wait for all handlers to complete
            await Task.WhenAll(tasks);
            _logger.LogDebug("All handlers for event type {EventType} completed", eventType.Name);
        }

        /// <summary>
        /// Subscribe to events of a specific type
        /// </summary>
        public void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            _logger.LogDebug("Subscribing to event type {EventType}", eventType.Name);

            // Wrap the typed handler in a handler that takes IEvent
            Func<IEvent, Task> wrappedHandler = async (e) => 
            {
                if (e is TEvent typedEvent)
                {
                    await handler(typedEvent);
                }
            };

            _handlers.AddOrUpdate(
                eventType,
                new List<Func<IEvent, Task>> { wrappedHandler },
                (_, existingHandlers) =>
                {
                    existingHandlers.Add(wrappedHandler);
                    return existingHandlers;
                });
        }

        /// <summary>
        /// Unsubscribe from events of a specific type
        /// </summary>
        public void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
        {
            // This is a simplified implementation that doesn't actually remove the handler
            // In a real implementation, we would need to track the wrapped handlers
            _logger.LogWarning("Unsubscribe operation not fully implemented for InMemoryEventBus");
        }
    }
}