// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Workspaces;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A DScript workspace resolver factory. Since some context is needed for creating a <see cref="IDScriptWorkspaceModuleResolver"/>
    /// that is only available later in time, after registration, <see name="Initialize"/> needs to be called before creating a resolver.
    /// </summary>
    public sealed class DScriptWorkspaceResolverFactory : IWorkspaceResolverFactory<IDScriptWorkspaceModuleResolver>
    {
        private FrontEndContext m_frontEndContext;
        private FrontEndHost m_frontEndHost;
        private IConfiguration m_configuration;

        private readonly Dictionary<string, Func<IDScriptWorkspaceModuleResolver>> m_registeredKinds;
        private readonly Dictionary<IResolverSettings, IDScriptWorkspaceModuleResolver> m_instantiatedResolvers;

        /// <nodoc/>
        public DScriptWorkspaceResolverFactory()
        {
            m_registeredKinds = new Dictionary<string, Func<IDScriptWorkspaceModuleResolver>>();
            m_instantiatedResolvers = new Dictionary<IResolverSettings, IDScriptWorkspaceModuleResolver>();
        }

        /// <nodoc/>
        public bool IsInitialized { get; private set; } = false;

        /// <summary>
        /// Whether the provided <param name="kind"/> is already registered with this factory
        /// </summary>
        [Pure]
        public bool IsRegistered(string kind)
        {
            Contract.Assert(!string.IsNullOrEmpty(kind));
            return m_registeredKinds.ContainsKey(kind);
        }

        /// <summary>
        /// Registers a specific resolver for a particular <param name="kind"/>. The provided <param name="constructor"/>
        /// is used for creating the resolver
        /// </summary>
        public void RegisterResolver(string kind, Func<IDScriptWorkspaceModuleResolver> constructor)
        {
            Contract.Requires(!IsRegistered(kind));
            m_registeredKinds[kind] = constructor;
        }

        /// <summary>
        /// Sets DScript-specific objects that <see cref="IDScriptWorkspaceModuleResolver"/> need at creation time
        /// </summary>
        /// <remarks>
        /// This is exposed as a separate methods since the set context is usually available after resolvers are registered.
        /// </remarks>
        public void Initialize(FrontEndContext context, FrontEndHost host, IConfiguration configuration)
        {
            Contract.Requires(context != null);
            Contract.Requires(host != null);
            Contract.Requires(!IsInitialized);
            Contract.Ensures(IsInitialized);

            IsInitialized = true;
            m_frontEndContext = context;
            m_frontEndHost = host;
            m_configuration = configuration;
        }

        /// <summary>
        /// To avoid re-computations, the factory keeps track of resolvers already instantiated for a given <param name="resolverSettings"/>.
        /// An existing resolver is returned (instead of creating a new one) if it has been previously instantiated.
        /// </summary>
        /// <remarks>
        /// This method is not thread safe.
        /// </remarks>
        public Possible<IDScriptWorkspaceModuleResolver> TryGetResolver(IResolverSettings resolverSettings)
        {
            Contract.Requires(resolverSettings != null);
            Contract.Assert(IsRegistered(resolverSettings.Kind), "Kind '" + resolverSettings.Kind + "' is not registered.");
            Contract.Assert(IsInitialized);

            // Check if there is an already instantiated resolver for this settings
            if (m_instantiatedResolvers.TryGetValue(resolverSettings, out var resolver))
            {
                return new Possible<IDScriptWorkspaceModuleResolver>(resolver);
            }

            // There is not, so we need to create and instantiate one.
            resolver = m_registeredKinds[resolverSettings.Kind]();
            if (!resolver.TryInitialize(m_frontEndHost, m_frontEndContext, m_configuration, resolverSettings))
            {
                return new WorkspaceModuleResolverGenericInitializationFailure(resolverSettings.Kind);
            }

            m_instantiatedResolvers[resolverSettings] = resolver;

            return new Possible<IDScriptWorkspaceModuleResolver>(resolver);
        }
    }
}
