using System;
using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    /// <summary>
    /// Simple typed publish/subscribe hub so systems (and the Unity layer) can react to
    /// world changes without knowing about each other.
    /// </summary>
    public sealed class EventBus
    {
        readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public void Publish<T>(T evt)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0)
                return;
            // Copy so handlers may unsubscribe while being called.
            foreach (var handler in list.ToArray())
                ((Action<T>)handler)(evt);
        }

        sealed class Subscription : IDisposable
        {
            Action _dispose;

            public Subscription(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}
