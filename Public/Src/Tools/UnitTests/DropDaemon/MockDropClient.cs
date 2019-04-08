// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.Services.Drop.WebApi;
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using System.Threading.Tasks;
using Tool.DropDaemon;

namespace Test.Tool.DropDaemon
{
    public class MockDropClient : IDropClient
    {
        public delegate Task<DropItem> CreateDelegate();

        public delegate Task<AddFileResult> AddFileDelegate(IDropItem item);

        public delegate Task<FinalizeResult> FinalizeDelegate();

        private readonly CreateDelegate m_createFunc;
        private readonly AddFileDelegate m_addFileFunc;
        private readonly FinalizeDelegate m_finalizeFunc;

        public string DropUrl { get; }

        public MockDropClient(
            string dropUrl = null,
            CreateDelegate createFunc = null,
            AddFileDelegate addFileFunc = null,
            FinalizeDelegate finalizeFunc = null)
        {
            AppDomain.CurrentDomain.AssemblyResolve += MockDropClient.CurrentDomain_AssemblyResolve;
            DropUrl = dropUrl;
            m_createFunc = createFunc ?? new CreateDelegate(() => Task.FromResult(new DropItem()));
            m_addFileFunc = addFileFunc ?? new AddFileDelegate((item) => Task.FromResult(AddFileResult.Associated));
            m_finalizeFunc = finalizeFunc ?? new FinalizeDelegate(() => Task.FromResult(new FinalizeResult()));
        }

        public static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Force Newtonsoft.Json to version 11 for unit tests
            AssemblyName name = new AssemblyName(args.Name);
            if (name.Name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase))
            {
                name.Version = new Version(11, 0);
                return Assembly.Load(name);
            }

            // WE have some issues with versioning old legacy assemblies
            if (name.Version.Major == 15 && name.Version.Minor == 1280)
            {
                name.Version = new Version(1, 0);
                return Assembly.Load(name);
            }

            return Assembly.Load(args.Name);
        }

        public MockDropClient(
            string dropUrl = null,
            bool createSucceeds = true,
            bool addFileSucceeds = true,
            bool finalizeSucceeds = true)
            : this(
                  dropUrl,
                  new CreateDelegate(() => ReturnOrFail(new DropItem(), !createSucceeds)),
                  new AddFileDelegate((item) => ReturnOrFail(AddFileResult.Associated, !addFileSucceeds)),
                  new FinalizeDelegate(() => ReturnOrFail(new FinalizeResult(), !finalizeSucceeds)))
        { }

        public MockDropClient(string dropUrl = null)
            : this(dropUrl, true, true, true) { }

        public Task<DropItem> CreateAsync() => m_createFunc();

        public Task<AddFileResult> AddFileAsync(IDropItem item)
        {
            Contract.Requires(item != null);

            return m_addFileFunc(item);
        }

        public Task<FinalizeResult> FinalizeAsync() => m_finalizeFunc();

        public IDictionary<string, long> GetStats() => new Dictionary<string, long>(0);

        public void Dispose() { }

        internal static Task<T> FailTask<T>(string errorMessage = null)
        {
            return Task.Run(() => Fail<T>(errorMessage));
        }

        internal static Task<T> CreateFailingTask<T>(Func<Exception> exceptionFactory = null)
        {
            return Task.Run(() => Fail<T>(exceptionFactory));
        }

        internal static T Fail<T>(string message = "drop service failure")
        {
            return Fail<T>(() => new DropServiceException(message));
        }

        internal static T Fail<T>(Func<Exception> exceptionFactory)
        {
            throw exceptionFactory();
        }

        private static Task<T> ReturnOrFail<T>(T result, bool shouldFail)
        {
            return Task.Run(() =>
            {
                if (shouldFail)
                {
                    Fail<T>();
                }

                return result;
            });
        }
    }
}
