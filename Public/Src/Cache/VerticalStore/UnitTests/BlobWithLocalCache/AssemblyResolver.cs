// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// CacheFactory.InitializeCacheAsync uses Assembly.Load by simple name to load
// cache implementation assemblies (e.g. "BuildXL.Cache.MemoizationStoreAdapter").
// On .NET Core, these aren't in deps.json so the default AssemblyLoadContext
// can't find them. This module initializer registers a resolver that probes
// the application directory as a fallback.

#if NETCOREAPP
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace BuildXL.Cache.Tests
{
    internal static class AssemblyResolver
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll");
                if (File.Exists(path))
                {
                    return context.LoadFromAssemblyPath(path);
                }

                return null;
            };
        }
    }
}
#endif
