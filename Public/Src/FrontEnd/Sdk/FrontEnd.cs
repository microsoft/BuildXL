// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Helper base class for 
    /// </summary>
    public abstract class FrontEnd<TWorkspaceResolver>
        : IFrontEnd
        where TWorkspaceResolver : class, IWorkspaceModuleResolver, new()
    {
        /// <nodoc />
        protected FrontEndContext Context { get; private set; }

        /// <nodoc />
        protected FrontEndHost Host { get; private set; }

        /// <nodoc />
        protected IConfiguration Configuration { get; private set; }

        private ConcurrentDictionary<IResolverSettings, TWorkspaceResolver> m_workspaceResolverCache = new ConcurrentDictionary<IResolverSettings, TWorkspaceResolver>();

        /// <inheritdoc />
        public void InitializeFrontEnd([NotNull] FrontEndHost host, [NotNull] FrontEndContext context, [NotNull] IConfiguration configuration)
        {
            Host = host;
            Context = context;
            Configuration = configuration;
        }

        /// <inheritdoc />
        public virtual void LogStatistics(Dictionary<string, long> statistics)
        {
        }

        /// <inheritdoc />
        public abstract IReadOnlyCollection<string> SupportedResolvers { get; }

        /// <inheritdoc />
        public abstract IResolver CreateResolver([NotNull] string kind);

        /// <inheritdoc />
        public virtual bool TryCreateWorkspaceResolver(
            [NotNull] IResolverSettings resolverSettings,
            [NotNull] out IWorkspaceModuleResolver workspaceResolver)
        {
            workspaceResolver = m_workspaceResolverCache.GetOrAdd(
                resolverSettings,
                (settings) =>
                {
                    var resolver = new TWorkspaceResolver();
                    if (resolver.TryInitialize(Host, Context, Configuration, settings))
                    {
                        return resolver;
                    }

                    return default(TWorkspaceResolver);
                });

            return workspaceResolver != default(TWorkspaceResolver);
        }
    }
}
