// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// File dependency provider that is based on the <see cref="Workspace"/> and <see cref="ISemanticModel"/>.
    /// </summary>
    internal sealed class WorkspaceBasedSpecDependencyProvider : ISpecDependencyProvider
    {
        private readonly Workspace m_workspace;
        private readonly PathTable m_pathTable;
        private readonly ISemanticModel m_semanticModel;

        /// <nodoc />
        public WorkspaceBasedSpecDependencyProvider(Workspace workspace, PathTable pathTable)
        {
            m_workspace = workspace;
            m_pathTable = pathTable;
            m_semanticModel = workspace.GetSemanticModel();

            MaterializeDependencies();
        }

        private void MaterializeDependencies()
        {
            var sources = m_workspace.GetAllSourceFiles();

            Parallel.ForEach(
                sources,
                source =>
                {
                    source.FileDependents.MaterializeSetIfNeeded(sources, (files, idx) => files[idx].GetAbsolutePath(m_pathTable));
                    source.FileDependencies.MaterializeSetIfNeeded(sources, (files, idx) => files[idx].GetAbsolutePath(m_pathTable));
                });
        }

        /// <inheritdoc />
        public HashSet<AbsolutePath> GetFileDependenciesOf(AbsolutePath specPath)
        {
            var sourceFile = m_workspace.GetSourceFile(specPath);
            return m_semanticModel.GetFileDependenciesOf(sourceFile).MaterializedSetOfPaths;
        }

        /// <inheritdoc />
        public HashSet<AbsolutePath> GetFileDependentsOf(AbsolutePath specPath)
        {
            var sourceFile = m_workspace.GetSourceFile(specPath);
            return m_semanticModel.GetFileDependentFilesOf(sourceFile).MaterializedSetOfPaths;
        }

        /// <inheritdoc />
        public HashSet<string> GetModuleDependenciesOf(AbsolutePath specPath)
        {
            var sourceFile = m_workspace.GetSourceFile(specPath);
            return m_semanticModel.GetModuleDependentsOf(sourceFile);
        }
    }
}
