// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.AspNetCore.Components.Lifetime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Mvc.Microbenchmarks
{
    public class PreserveComponentStateBenchmark
    {
        private readonly PersistComponentStateTagHelper _tagHelper = new()
        {
            PersistenceMode = PersistenceMode.WebAssembly
        };

        TagHelperAttributeList _attributes = new();

        private TagHelperContext _context;
        private Func<bool, HtmlEncoder, Task<TagHelperContent>> _childContent =
            (_, __) => Task.FromResult(new DefaultTagHelperContent() as TagHelperContent);
        private IServiceProvider _serviceProvider;
        private IServiceScope _serviceScope;
        private TagHelperOutput _output;
        private Dictionary<string, byte[]> _entries = new();

        private byte[] _entryValue;

        public PreserveComponentStateBenchmark()
        {
            _context = new TagHelperContext(_attributes, new Dictionary<object, object>(), "asdf");
            _serviceProvider = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                .AddScoped(typeof(ILogger<>), typeof(NullLogger<>))
                .AddMvc().Services.BuildServiceProvider();
        }

        [Params(0,1,10,100,1000)]
        public int Entries;

        [Params(0, 1, 10, 100, 1000)]
        public int EntrySize;

        [GlobalSetup]
        public void Setup()
        {
            _entryValue = new byte[EntrySize];
            RandomNumberGenerator.Fill(_entryValue);
            for (int i = 0; i < Entries; i++)
            {
                _entries.Add(i.ToString(CultureInfo.InvariantCulture), _entryValue);
            }
        }

        [IterationCleanup]
        public void TearDown()
        {
            _serviceScope.Dispose();
        }

        [Benchmark(Description = "Persist component state tag helper webassembly")]
        public async Task PersistComponentStateTagHelperWebAssemblyAsync()
        {
            _tagHelper.ViewContext = GetViewContext();
            var state = _tagHelper.ViewContext.HttpContext.RequestServices.GetRequiredService<PersistentComponentState>();
            foreach (var (key,value) in _entries)
            {
                state.Persist(key, writer => writer.Write(value));
            }

            _output = new TagHelperOutput("persist-component-state", _attributes, _childContent);
            _output.Content = new DefaultTagHelperContent();
            await _tagHelper.ProcessAsync(_context, _output);
            _output.Content.GetContent();
        }

        private ViewContext GetViewContext()
        {
            _serviceScope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var httpContext = new DefaultHttpContext
            {
                RequestServices = _serviceScope.ServiceProvider
            };

            return new ViewContext
            {
                HttpContext = httpContext,
            };
        }
    }
}
