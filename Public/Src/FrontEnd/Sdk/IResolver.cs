// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// A resolver is a class that maps names to objects.
    /// </summary>
    public interface IResolver
    {
        /// <summary>
        /// Get the name of the front-end.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Initializes a resolver for a given resolver settings.
        /// </summary>
        /// <remarks>
        /// Unfortunately we have to use object for the workspace resolver for now
        /// untill we untangle workspace and runtime resolvers.
        /// </remarks>
        Task<bool> InitResolverAsync([NotNull]IResolverSettings resolverSettings, object workspaceResolver);
        
        /// <summary>
        /// If this resolver owns the module, returns a task
        /// that converts the module to evaluation model.
        /// Returns 'null' as a task result otherwise.
        ///
        /// The resulting task returns true if module processing succeeds, otherwise returns false
        /// and logs the errors.
        /// </summary>
        /// TODO: This is an intermediate step. An actual solution can store 'resolver' into the parsed module itself
        Task<bool?> TryConvertModuleToEvaluationAsync([NotNull]IModuleRegistry moduleRegistry, [NotNull]ParsedModule module, [NotNull]IWorkspace workspace);

        /// <summary>
        /// If the resolver owns the module, returns a task
        /// that evaluates all the specs of the module for the given qualifier. Null otherwise.
        /// </summary>
        Task<bool?> TryEvaluateModuleAsync([NotNull]IEvaluationScheduler scheduler, [NotNull]ModuleDefinition module, QualifierId qualifierId);

        /// <summary>
        /// The host calls this method to notify a resolver that evaluation is finished
        /// </summary>
        void NotifyEvaluationFinished();

        /// <summary>
        /// Log some statistics at the end of the build.
        /// </summary>
        void LogStatistics();
    }
}
