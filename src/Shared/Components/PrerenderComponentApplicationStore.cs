// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
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

            DeserializeState(Convert.FromBase64String(existingState));
        }

        protected void DeserializeState(byte[] existingState)
        {
            var state = JsonSerializer.Deserialize<Dictionary<string, byte[]>>(existingState);
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

        protected virtual async Task<byte[]> SerializeState(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state)
        {
            // System.Text.Json doesn't support serializing ReadonlySequence<byte> so we need to buffer
            // the data with a memory pool here. We will change our serialization strategy in the future here
            // so that we can avoid this step.
            var pipe = new Pipe();
            var pipeWriter = pipe.Writer;
            var jsonWriter = new Utf8JsonWriter(pipeWriter);
            jsonWriter.WriteStartObject();
            foreach (var (key, value) in state)
            {
                if (value.IsSingleSegment)
                {
                    jsonWriter.WriteBase64String(key, value.First.Span);
                }
                else
                {
                    WriteMultipleSegments(jsonWriter, key, value);
                }
                jsonWriter.Flush();
            }

            jsonWriter.WriteEndObject();
            jsonWriter.Flush();
            pipe.Writer.Complete();
            var result = await ReadToEnd(pipe.Reader);
            return result.ToArray();

            async Task<ReadOnlySequence<byte>> ReadToEnd(PipeReader reader)
            {
                var result = await reader.ReadAsync();
                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                while (!result.IsCompleted)
                {
                    // Consume nothing, just wait for everything
                    result = await reader.ReadAsync();
                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }

                return result.Buffer;
            }

            static void WriteMultipleSegments(Utf8JsonWriter jsonWriter, string key, ReadOnlySequence<byte> value)
            {
                byte[] unescapedArray = null;
                var valueLenght = (int)value.Length;

                Span<byte> valueSegment = value.Length <= 256 ?
                    stackalloc byte[valueLenght] :
                    (unescapedArray = ArrayPool<byte>.Shared.Rent(valueLenght)).AsSpan().Slice(0, valueLenght);

                value.CopyTo(valueSegment);
                jsonWriter.WriteBase64String(key, valueSegment);

                if (unescapedArray != null)
                {
                    valueSegment.Clear();
                    ArrayPool<byte>.Shared.Return(unescapedArray);
                }
            }
        }

        public async Task PersistStateAsync(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state)
        {
            var bytes = await SerializeState(state);

            var result = Convert.ToBase64String(bytes);
            PersistedState = result;
        }
    }
}
