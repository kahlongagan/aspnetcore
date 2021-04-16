// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace Microsoft.AspNetCore.Components
{
    internal class ProtectedPrerenderComponentApplicationStore : PrerenderComponentApplicationStore
    {
        private IDataProtector _protector;

        public ProtectedPrerenderComponentApplicationStore(IDataProtectionProvider dataProtectionProvider) : base()
        {
            CreateProtector(dataProtectionProvider);
        }

        public ProtectedPrerenderComponentApplicationStore(string existingState, IDataProtectionProvider dataProtectionProvider)
        {
            CreateProtector(dataProtectionProvider);
            DeserializeState(_protector.Unprotect(Convert.FromBase64String(existingState)));
        }

        protected override PooledByteBufferWriter SerializeState(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state)
        {
            var bytes = base.SerializeState(state);
            if (_protector != null)
            {
                var newBuffer = new PooledByteBufferWriter(_protector.Protect(bytes.WrittenMemory.Span.ToArray()));
                bytes.Dispose();
                return newBuffer;
            }

            return bytes;
        }

        private void CreateProtector(IDataProtectionProvider dataProtectionProvider) =>
            _protector = dataProtectionProvider.CreateProtector("Microsoft.AspNetCore.Components.Server.State");
    }
}
