// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Service class that builds <see cref="SemanticWorkspace"/>.
    /// </summary>
    public sealed class SemanticWorkspaceProvider
    {
        private readonly IWorkspaceStatistics m_statistics;
        private readonly WorkspaceConfiguration m_workspaceConfiguration;

        /// <nodoc/>
        public SemanticWorkspaceProvider(IWorkspaceStatistics statistics, WorkspaceConfiguration workspaceConfiguration)
        {
            Contract.Requires(statistics != null);
            Contract.Requires(workspaceConfiguration != null);

            m_statistics = statistics;
            m_workspaceConfiguration = workspaceConfiguration;
        }

        /// <summary>
        /// Computes semantic workspace from parsed workspace.
        /// </summary>
        public static Task<Workspace> ComputeSemanticWorkspace(PathTable pathTable, Workspace workspace, WorkspaceConfiguration configuration)
        {
            var provider = new SemanticWorkspaceProvider(new WorkspaceStatistics(), configuration);
            return provider.ComputeSemanticWorkspaceAsync(pathTable, workspace);
        }

        /// <summary>
        /// Computes semantic workspace from parsed workspace.
        /// </summary>
        /// <remarks>
        /// If this is not the first time specs in this workspace are type checked, the semantic model that resulted in a previous check is expected to be passed
        /// </remarks>
        public async Task<Workspace> ComputeSemanticWorkspaceAsync(PathTable pathTable, Workspace workspace, [CanBeNull] ISemanticModel originalSemanticModel = null, bool incrementalMode = false)
        {
            Contract.Requires(workspace != null);

            ITypeChecker checker = await MeasureCheckingDurationAsync(
                workspace,
                w => TypeCheckWorkspace(pathTable, workspace, m_workspaceConfiguration.MaxDegreeOfParallelismForTypeChecking, originalSemanticModel, incrementalMode));

            return workspace.WithSemanticModel(new SemanticModel(checker), workspace.FilterWasApplied);
        }

        private async Task<ITypeChecker> MeasureCheckingDurationAsync(Workspace workspace, Func<Workspace, ITypeChecker> func)
        {
            // yield to caller first because the rest of this is blocking
            await Task.Yield();

            using (m_statistics.EndToEndTypeChecking.Start())
            {
                return func(workspace);
            }
        }

        private ITypeChecker TypeCheckWorkspace(PathTable pathTable, Workspace workspace, int degreeOfParallelism, [CanBeNull]ISemanticModel originalSemanticModel, bool interactiveMode)
        {
            // TODO: This is temporary and should be removed!!!
            // The checker needs to be split into symbol binding and type checking. Only symbol binding should happen here!
            // Furthermore, type checking needs to work on a per-resolver basis, so each resolver can decide how to type check. This is not possible today either.
            // In the meantime, this happens here as a single monolithic step if the managed type checker is requested. This allows to exercise the
            // managed symbol binding and type checker, measure perf, etc.

            // There is no original semantic model, this is the first time we are type checking this
            if (interactiveMode)
            {
                // TODO: Observer that these numbers will monotonically increase! Consider resetting and type check from scratch when they get too big
                // Some of the specs were already type checked. So we create a checker that start with the following ids so we avoid id clashes
                var checker = originalSemanticModel?.TypeChecker;
                var nextMergeId = checker?.GetCurrentMergeId() ?? 0;
                var nextNodeId = checker?.GetCurrentNodeId() ?? 0;
                var nextSymbolId = checker?.GetCurrentSymbolId() ?? 0;

                // This is an interactive mode for IDE, no need to type check everything.
                // The IDE language service will type check per file bases.
                return WorkspaceChecker.Create(pathTable, workspace, m_statistics, degreeOfParallelism, nextMergeId, nextNodeId, nextSymbolId, interactiveMode: true);
            }

            return WorkspaceChecker.Check(pathTable, workspace, m_statistics, degreeOfParallelism);
        }
    }
}
