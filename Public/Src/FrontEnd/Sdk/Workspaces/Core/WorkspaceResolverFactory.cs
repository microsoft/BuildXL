// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A workspace resolver factory where particular resolvers can be associated with a specific kind
    /// </summary>
    public class WorkspaceResolverFactory<T> : IWorkspaceResolverFactory<T> where T : IWorkspaceModuleResolver
    {
        /// <summary>
        /// Dictionary of registered kinds
        /// </summary>
        protected Dictionary<string, Func<IResolverSettings, T>> RegisteredKinds { get; }

        /// <nodoc/>
        public WorkspaceResolverFactory()
        {
            RegisteredKinds = new Dictionary<string, Func<IResolverSettings, T>>();
        }

        /// <summary>
        /// Whether the provided <param name="kind"/> is already registered with this factory
        /// </summary>
        [Pure]
        public bool IsRegistered(string kind)
        {
            return RegisteredKinds.ContainsKey(kind);
        }

        /// <summary>
        /// Registers a specific resolver for a particular <param name="kind"/>. The provided <param name="constructor"/>
        /// is used for creating the resolver
        /// </summary>
        public void RegisterResolver(string kind, Func<IResolverSettings, T> constructor)
        {
            Contract.Requires(!IsRegistered(kind));
            RegisteredKinds[kind] = constructor;
        }

        /// <inheritdoc/>
        public virtual Possible<T> TryGetResolver(IResolverSettings resolverSettings)
        {
            Contract.Requires(resolverSettings != null);
            Contract.Assert(IsRegistered(resolverSettings.Kind));
            return RegisteredKinds[resolverSettings.Kind](resolverSettings);
        }
    }
}
