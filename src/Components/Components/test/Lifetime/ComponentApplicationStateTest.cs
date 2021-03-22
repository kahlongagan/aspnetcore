// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNetCore.Components
{
    public class ComponentApplicationStateTest
    {
        [Fact]
        public void InitializeExistingState_SetupsState()
        {
            // Arrange
            var applicationState = new PersistentComponentState(new Dictionary<string, Pipe>(), new List<Func<Task>>());
            var existingState = new Dictionary<string, ReadOnlySequence<byte>>
            {
                ["MyState"] = new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4 })
            };

            // Act
            applicationState.InitializeExistingState(existingState);

            // Assert
            Assert.True(applicationState.TryTake("MyState", out var existing));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, existing.ToArray());
        }

        [Fact]
        public void InitializeExistingState_ThrowsIfAlreadyInitialized()
        {
            // Arrange
            var applicationState = new PersistentComponentState(new Dictionary<string, Pipe>(), new List<Func<Task>>());
            var existingState = new Dictionary<string, ReadOnlySequence<byte>>
            {
                ["MyState"] = new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4 })
            };

            applicationState.InitializeExistingState(existingState);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => applicationState.InitializeExistingState(existingState));
        }

        [Fact]
        public void TryRetrieveState_ReturnsStateWhenItExists()
        {
            // Arrange
            var applicationState = new PersistentComponentState(new Dictionary<string, Pipe>(), new List<Func<Task>>());
            var existingState = new Dictionary<string, ReadOnlySequence<byte>>
            {
                ["MyState"] = new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4 })
            };

            // Act
            applicationState.InitializeExistingState(existingState);

            // Assert
            Assert.True(applicationState.TryTake("MyState", out var existing));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, existing.ToArray());
            Assert.False(applicationState.TryTake("MyState", out var gone));
        }

        [Fact]
        public async Task PersistState_SavesDataToTheStoreAsync()
        {
            // Arrange
            var currentState = new Dictionary<string, Pipe>();
            var applicationState = new PersistentComponentState(currentState, new List<Func<Task>>());
            var myState = new byte[] { 1, 2, 3, 4 };

            // Act
            applicationState.Persist("MyState", writer => writer.Write(myState));

            // Assert
            Assert.True(currentState.TryGetValue("MyState", out var stored));
            Assert.Equal(myState, await ReadAllAsync(stored.Reader));
        }

        [Fact]
        public void PersistState_ThrowsForDuplicateKeys()
        {
            // Arrange
            var currentState = new Dictionary<string, Pipe>();
            var applicationState = new PersistentComponentState(currentState, new List<Func<Task>>());
            var myState = new byte[] { 1, 2, 3, 4 };

            applicationState.Persist("MyState", writer => writer.Write(myState));

            // Act & Assert
            Assert.Throws<ArgumentException>(() => applicationState.Persist("MyState", writer => writer.Write(myState)));
        }

        [Fact]
        public async Task PersistAsJson_SerializesTheDataToJsonAsync()
        {
            // Arrange
            var currentState = new Dictionary<string, Pipe>();
            var applicationState = new PersistentComponentState(currentState, new List<Func<Task>>());
            var myState = new byte[] { 1, 2, 3, 4 };

            // Act
            applicationState.PersistAsJson("MyState", myState);

            // Assert
            Assert.True(currentState.TryGetValue("MyState", out var stored));
            Assert.Equal(myState, JsonSerializer.Deserialize<byte[]>(await ReadAllAsync(stored.Reader)));
        }

        [Fact]
        public async Task PersistAsJson_NullValueAsync()
        {
            // Arrange
            var currentState = new Dictionary<string, Pipe>();
            var applicationState = new PersistentComponentState(currentState, new List<Func<Task>>());

            // Act
            applicationState.PersistAsJson<byte []>("MyState", null);

            // Assert
            Assert.True(currentState.TryGetValue("MyState", out var stored));
            Assert.Null(JsonSerializer.Deserialize<byte[]>(await ReadAllAsync(stored.Reader)));
        }

        [Fact]
        public void TryRetrieveFromJson_DeserializesTheDataFromJson()
        {
            // Arrange
            var myState = new byte[] { 1, 2, 3, 4 };
            var serialized = JsonSerializer.SerializeToUtf8Bytes(myState);
            var existingState = new Dictionary<string, ReadOnlySequence<byte>>() { ["MyState"] = new ReadOnlySequence<byte>(serialized) };
            var applicationState = new PersistentComponentState(new Dictionary<string, Pipe>(), new List<Func<Task>>());

            applicationState.InitializeExistingState(existingState);

            // Act
            Assert.True(applicationState.TryTakeFromJson<byte []>("MyState", out var stored));

            // Assert
            Assert.Equal(myState, stored);
            Assert.False(applicationState.TryTakeFromJson<byte[]>("MyState", out _));
        }

        [Fact]
        public void TryRetrieveFromJson_NullValue()
        {
            // Arrange
            var serialized = JsonSerializer.SerializeToUtf8Bytes<byte []>(null);
            var existingState = new Dictionary<string, ReadOnlySequence<byte>>() { ["MyState"] = new ReadOnlySequence<byte>(serialized) };
            var applicationState = new PersistentComponentState(new Dictionary<string, Pipe>(), new List<Func<Task>>());

            applicationState.InitializeExistingState(existingState);

            // Act
            Assert.True(applicationState.TryTakeFromJson<byte[]>("MyState", out var stored));

            // Assert
            Assert.Null(stored);
            Assert.False(applicationState.TryTakeFromJson<byte[]>("MyState", out _));
        }

        private static async Task<byte[]> ReadAllAsync(PipeReader pipeReader)
        {
            while (true)
            {
                var result = await pipeReader.ReadAsync();

                if (result.IsCompleted)
                {
                    return result.Buffer.ToArray();
                }

                // Consume nothing, just wait for everything
                pipeReader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }
        }
    }
}
