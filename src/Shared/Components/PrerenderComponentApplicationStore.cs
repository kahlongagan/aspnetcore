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

            ExistingState = JsonSerializer.Deserialize<Dictionary<string, ReadOnlySequence<byte>>>(Convert.FromBase64String(existingState)) ??
                throw new ArgumentException(nameof(existingState));
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
            return JsonSerializer.SerializeToUtf8Bytes(state);
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
