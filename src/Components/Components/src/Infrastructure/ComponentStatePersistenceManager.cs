// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components.Infrastructure
{
    /// <summary>
    /// Manages the persistent state of components in an application.
    /// </summary>
    public class ComponentStatePersistenceManager : IDisposable
    {
        private bool _stateIsPersisted;
        private readonly List<Func<Task>> _pauseCallbacks = new();
        private readonly Dictionary<string, PooledByteBufferWriter> _currentState = new(StringComparer.Ordinal);
        private readonly ILogger<ComponentStatePersistenceManager> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="ComponentStatePersistenceManager"/>.
        /// </summary>
        public ComponentStatePersistenceManager(ILogger<ComponentStatePersistenceManager> logger)
        {
            State = new PersistentComponentState(_currentState, _pauseCallbacks);
            _logger = logger;
        }

        /// <summary>
        /// Gets the <see cref="ComponentStatePersistenceManager"/> associated with the <see cref="ComponentStatePersistenceManager"/>.
        /// </summary>
        public PersistentComponentState State { get; }

        /// <summary>
        /// Restores the component application state from the given <see cref="IPersistentComponentStateStore"/>.
        /// </summary>
        /// <param name="store">The <see cref="IPersistentComponentStateStore"/> to restore the application state from.</param>
        /// <returns>A <see cref="Task"/> that will complete when the state has been restored.</returns>
        public async Task RestoreStateAsync(IPersistentComponentStateStore store)
        {
            var data = await store.GetPersistedStateAsync();
            State.InitializeExistingState(data);
        }

        /// <summary>
        /// Persists the component application state into the given <see cref="IPersistentComponentStateStore"/>.
        /// </summary>
        /// <param name="store">The <see cref="IPersistentComponentStateStore"/> to restore the application state from.</param>
        /// <param name="renderer">The <see cref="Renderer"/> that components are being rendered.</param>
        /// <returns>A <see cref="Task"/> that will complete when the state has been restored.</returns>
        public Task PersistStateAsync(IPersistentComponentStateStore store, Renderer renderer)
        {
            if (_stateIsPersisted)
            {
                throw new InvalidOperationException("State already persisted.");
            }

            _stateIsPersisted = true;

            return renderer.Dispatcher.InvokeAsync(PauseAndPersistState);

            async Task PauseAndPersistState()
            {
                await PauseAsync();

                var data = new Dictionary<string, ReadOnlySequence<byte>>(StringComparer.Ordinal);
                foreach (var (key, value) in _currentState)
                {
                    data[key] = new ReadOnlySequence<byte>(value.WrittenMemory);
                }

                await store.PersistStateAsync(data);

                foreach (var value in _currentState.Values)
                {
                    value.Dispose();
                }
                _currentState.Clear();
            }
        }

        internal Task PauseAsync()
        {
            List<Task>? pendingCallbackTasks = null;

            for (var i = 0; i < _pauseCallbacks.Count; i++)
            {
                var callback = _pauseCallbacks[i];
                var result = ExecuteCallback(callback, _logger);
                if (!result.IsCompletedSuccessfully)
                {
                    pendingCallbackTasks ??= new();
                    pendingCallbackTasks.Add(result);
                }
            }

            if (pendingCallbackTasks != null)
            {
                return Task.WhenAll(pendingCallbackTasks);
            }
            else
            {
                return Task.CompletedTask;
            }

            static Task ExecuteCallback(Func<Task> callback, ILogger<ComponentStatePersistenceManager> logger)
            {
                try
                {
                    var current = callback();
                    if (current.IsCompletedSuccessfully)
                    {
                        return current;
                    }
                    else
                    {
                        return Awaited(current, logger);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(new EventId(1000, "PersistenceCallbackError"), ex, "There was an error executing a callback while pausing the application.");
                    return Task.CompletedTask;
                }

                static async Task Awaited(Task task, ILogger<ComponentStatePersistenceManager> logger)
                {
                    try
                    {
                        await task;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(new EventId(1000, "PersistenceCallbackError"), ex, "There was an error executing a callback while pausing the application.");
                        return;
                    }
                }
            }
        }

        void IDisposable.Dispose()
        {
            foreach (var value in _currentState.Values)
            {
                value.Dispose();
            }
            _currentState.Clear();
        }
    }
}
