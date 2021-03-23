// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Lifetime;

namespace Microsoft.AspNetCore.Components
{
    internal class PrerenderComponentApplicationStore : IPersistentComponentStateStore
    {
        public PrerenderComponentApplicationStore()
        {
            ExistingState = new();
        }

        public PrerenderComponentApplicationStore(string existingState)
        {
            if (existingState is null)
            {
                throw new ArgumentNullException(nameof(existingState));
            }

            var state = JsonSerializer.Deserialize<Dictionary<string, byte[]>>(Convert.FromBase64String(existingState));
            if (state == null)
            {
                throw new ArgumentException("Could not deserialize state correctly", nameof(existingState));
            }

            var stateDictionary = new Dictionary<string, ReadOnlySequence<byte>>();
            foreach (var (key, value) in state)
            {
                stateDictionary.Add(key, new ReadOnlySequence<byte>(value));
            }

            ExistingState = stateDictionary;
        }

#nullable enable
        public string? PersistedState { get; private set; }
#nullable disable

        public Dictionary<string, ReadOnlySequence<byte>> ExistingState { get; protected set; }

        public Task<IDictionary<string, ReadOnlySequence<byte>>> GetPersistedStateAsync()
        {
            return Task.FromResult((IDictionary<string, ReadOnlySequence<byte>>)ExistingState);
        }

        protected virtual byte[] SerializeState(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state)
        {
            // System.Text.Json doesn't support serializing ReadonlySequence<byte> so we need to buffer
            // the data with a memory pool here. We will change our serialization strategy in the future here
            // so that we can avoid this step.
            var pool = MemoryPool<byte>.Shared;
            var memory = new List<IMemoryOwner<byte>>();
            var serialization = new Dictionary<string, Memory<byte>>();
            try
            {
                foreach (var (key, value) in state)
                {
                    IMemoryOwner<byte> buffer = null;
                    if (value.Length < pool.MaxBufferSize)
                    {
                        buffer = pool.Rent((int)value.Length);
                        memory.Add(buffer);
                        value.CopyTo(buffer.Memory.Span.Slice(0, (int)value.Length));
                    }

                    serialization.Add(key, buffer != null ? buffer.Memory.Slice(0, (int)value.Length) : value.ToArray());
                }

                return JsonSerializer.SerializeToUtf8Bytes(serialization, JsonSerializerOptionsProvider.Options);
            }
            finally
            {
                serialization.Clear();
                foreach (var item in memory)
                {
                    item.Dispose();
                }
            }
        }

        public Task PersistStateAsync(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state)
        {
            var bytes = SerializeState(state);

            var result = Convert.ToBase64String(bytes);
            PersistedState = result;
            return Task.CompletedTask;
        }
    }
}
