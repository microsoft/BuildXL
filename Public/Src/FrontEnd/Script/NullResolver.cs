// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Resolver that does nothing.
    /// </summary>
    /// <remarks>
    /// In some cases, specifically, during configuration analysis, we need to have special resolver implementation that does nothing.
    /// One way to achieve this behavior is to implement <see cref="IResolver"/> interface by a type that needs it.
    /// Another approach is to use null-object pattern properly.
    /// </remarks>
    internal sealed class NullResolver : IResolver
    {
        /// <summary>
        /// Gest the global instance of the empty <see cref="IResolver"/>.
        /// </summary>
        public static NullResolver Instance => new NullResolver();

        private NullResolver()
        {
        }

        string IResolver.Name
        {
            get { throw new System.NotImplementedException(); }
        }

        Task<bool> IResolver.InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);
            throw new System.NotImplementedException();
        }

        public Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            Contract.Requires(module != null);
            Contract.Requires(workspace != null);
            throw new System.NotImplementedException();
        }

        public Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition module, QualifierId qualifierId)
        {
            Contract.Requires(scheduler != null);
            Contract.Requires(module != null);
            Contract.Requires(qualifierId.IsValid);
            throw new System.NotImplementedException();
        }

        public void NotifyEvaluationFinished()
        {
            throw new System.NotImplementedException();
        }

        void IResolver.LogStatistics()
        {
            throw new System.NotImplementedException();
        }
    }
}
