// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.AspNetCore.Components.Lifetime;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Components
{
    public class ComponentApplicationLifetimeTest
    {
        [Fact]
        public async Task RestoreStateAsync_InitializesStateWithDataFromTheProvidedStore()
        {
            // Arrange
            var data = new ReadOnlySequence<byte>(new byte[] { 0, 1, 2, 3, 4 });
            var state = new Dictionary<string, ReadOnlySequence<byte>>
            {
                ["MyState"] = data
            };
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);

            // Act
            await lifetime.RestoreStateAsync(store);

            // Assert
            Assert.True(lifetime.State.TryTake("MyState", out var retrieved));
            Assert.Empty(state);
            Assert.Equal(data, retrieved);
        }

        [Fact]
        public async Task RestoreStateAsync_ThrowsOnDoubleInitialization()
        {
            // Arrange
            var state = new Dictionary<string, ReadOnlySequence<byte>>
            {
                ["MyState"] = new ReadOnlySequence<byte>(new byte[] { 0, 1, 2, 3, 4 })
            };
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);

            await lifetime.RestoreStateAsync(store);

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.RestoreStateAsync(store));
        }

        [Fact]
        public async Task PersistStateAsync_SavesPersistedStateToTheStore()
        {
            // Arrange
            var state = new Dictionary<string, ReadOnlySequence<byte>>();
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);

            var renderer = new TestRenderer();
            var data = new byte[] { 1, 2, 3, 4 };

            lifetime.State.Persist("MyState", writer => writer.Write(new byte[] { 1, 2, 3, 4 }));

            // Act
            await lifetime.PersistStateAsync(store, renderer);

            // Assert
            Assert.True(store.State.TryGetValue("MyState", out var persisted));
            Assert.Equal(data, persisted.ToArray());
        }

        [Fact]
        public async Task PersistStateAsync_InvokesPauseCallbacksDuringPersist()
        {
            // Arrange
            var state = new Dictionary<string, ReadOnlySequence<byte>>();
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);
            var renderer = new TestRenderer();
            var data = new byte[] { 1, 2, 3, 4 };
            var invoked = false;

            lifetime.State.RegisterOnPersisting(() => { invoked = true; return default; });

            // Act
            await lifetime.PersistStateAsync(store, renderer);

            // Assert
            Assert.True(invoked);
        }

        [Fact]
        public async Task PersistStateAsync_FiresCallbacksInParallel()
        {
            // Arrange
            var state = new Dictionary<string, ReadOnlySequence<byte>>();
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);
            var renderer = new TestRenderer();

            var sequence = new List<int> { };

            var tcs = new TaskCompletionSource();
            var tcs2 = new TaskCompletionSource();

            lifetime.State.RegisterOnPersisting(async () => { sequence.Add(1); await tcs.Task; sequence.Add(3); });
            lifetime.State.RegisterOnPersisting(async () => { sequence.Add(2); await tcs2.Task; sequence.Add(4); });

            // Act
            var persistTask = lifetime.PersistStateAsync(store, renderer);
            tcs.SetResult();
            tcs2.SetResult();

            await persistTask;

            // Assert
            Assert.Equal(new[] { 1, 2, 3, 4 }, sequence);
        }

        [Fact]
        public async Task PersistStateAsync_ContinuesInvokingPauseCallbacksDuringPersistIfACallbackThrows()
        {
            // Arrange
            var sink = new TestSink();
            var loggerFactory = new TestLoggerFactory(sink, true);
            var logger = loggerFactory.CreateLogger<ComponentStatePersistenceManager>();
            var state = new Dictionary<string, ReadOnlySequence<byte>>();
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(logger);
            var renderer = new TestRenderer();
            var data = new byte[] { 1, 2, 3, 4 };
            var invoked = false;

            lifetime.State.RegisterOnPersisting(() => throw new InvalidOperationException());
            lifetime.State.RegisterOnPersisting(() => { invoked = true; return Task.CompletedTask; });

            // Act
            await lifetime.PersistStateAsync(store, renderer);

            // Assert
            Assert.True(invoked);
            var log = Assert.Single(sink.Writes);
            Assert.Equal(LogLevel.Error, log.LogLevel);
        }

        [Fact]
        public async Task PersistStateAsync_ContinuesInvokingPauseCallbacksDuringPersistIfACallbackThrowsAsynchonously()
        {
            // Arrange
            var sink = new TestSink();
            var loggerFactory = new TestLoggerFactory(sink, true);
            var logger = loggerFactory.CreateLogger<ComponentStatePersistenceManager>();
            var state = new Dictionary<string, ReadOnlySequence<byte>>();
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(logger);
            var renderer = new TestRenderer();
            var invoked = false;
            var tcs = new TaskCompletionSource();

            lifetime.State.RegisterOnPersisting(async () => { await tcs.Task; throw new InvalidOperationException(); });
            lifetime.State.RegisterOnPersisting(() => { invoked = true; return Task.CompletedTask; });

            // Act
            var persistTask = lifetime.PersistStateAsync(store, renderer);
            tcs.SetResult();

            await persistTask;

            // Assert
            Assert.True(invoked);
            var log = Assert.Single(sink.Writes);
            Assert.Equal(LogLevel.Error, log.LogLevel);
        }

        [Fact]
        public async Task PersistStateAsync_ThrowsWhenDeveloperTriesToPersistStateMultipleTimes()
        {
            // Arrange
            var state = new Dictionary<string, ReadOnlySequence<byte>>();
            var store = new TestStore(state);
            var lifetime = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);

            var renderer = new TestRenderer();
            var data = new byte[] { 1, 2, 3, 4 };

            lifetime.State.Persist("MyState", writer => writer.Write(new byte[] { 1, 2, 3, 4 }));

            // Act
            await lifetime.PersistStateAsync(store, renderer);

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.PersistStateAsync(store, renderer));
        }

        private class TestRenderer : Renderer
        {
            public TestRenderer() : base(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance)
            {
            }

            private Dispatcher _dispatcher = Dispatcher.CreateDefault();

            public override Dispatcher Dispatcher => _dispatcher;

            protected override void HandleException(Exception exception)
            {
                throw new NotImplementedException();
            }

            protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
            {
                throw new NotImplementedException();
            }
        }

        private class TestStore : IPersistentComponentStateStore
        {
            public TestStore(IDictionary<string, ReadOnlySequence<byte>> initialState)
            {
                State = initialState;
            }

            public IDictionary<string, ReadOnlySequence<byte>> State { get; set; }

            public Task<IDictionary<string, ReadOnlySequence<byte>>> GetPersistedStateAsync()
            {
                return Task.FromResult(State);
            }

            public Task PersistStateAsync(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state)
            {
                // We copy the data here because it's no longer available after this call completes.
                State = state.ToDictionary(kvp => kvp.Key, kvp => new ReadOnlySequence<byte>(kvp.Value.ToArray()));
                return Task.CompletedTask;
            }
        }
    }
}
