// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Incrementality;
using TypeScript.Net.Types;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// Snapshot of the front-end computed based on the fully parsed files.
    /// </summary>
    public sealed class SourceFileBasedBindingSnapshot : IWorkspaceBindingSnapshot
    {
        // all source files including prelude files and configuration files.
        private readonly ISourceFile[] m_sources;

        private readonly PathTable m_pathTable;

        /// <nodoc />
        public SourceFileBasedBindingSnapshot(ISourceFile[] sources, PathTable pathTable)
        {
            Contract.Requires(sources != null);

            m_sources = sources;
            m_pathTable = pathTable;
        }

        /// <inheritdoc />
        public int SourcesCount => m_sources.Length;

        /// <inheritdoc />
        public IEnumerable<ISourceFileBindingState> Sources => m_sources;

        /// <summary>
        /// Forces spec-2-spec map materialization.
        /// </summary>
        public void MaterializeDependencies()
        {
            // The following code allocates 17Kb per second.
            Parallel.ForEach(
                m_sources,
                source =>
                {
                    source.FileDependents.MaterializeSetIfNeeded(m_sources, (files, idx) => files[idx].GetAbsolutePath(m_pathTable));
                    source.FileDependencies.MaterializeSetIfNeeded(m_sources, (files, idx) => files[idx].GetAbsolutePath(m_pathTable));
                });
        }

        /// <inheritdoc />
        public void UpdateBindingFingerprint(AbsolutePath fullPath, string referencedSymbolsFingerprint, string declaredSymbolsFingerprint)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ISpecBindingState TryGetSpecState(AbsolutePath fullPath)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Snapshot of the front-end that was loaded from the cache.
    /// </summary>
    public sealed class WorkspaceBindingSnapshot : IWorkspaceBindingSnapshot
    {
        private readonly PathTable m_pathTable;
        private readonly Dictionary<AbsolutePath, ISpecBindingState> m_stateByPath;

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [NotNull]
        public ISpecBindingState[] Specs { get; }

        /// <nodoc />
        public WorkspaceBindingSnapshot([NotNull]ISpecBindingState[] specs, PathTable pathTable)
        {
            Contract.Requires(specs != null, "specs != null");

            m_pathTable = pathTable;

            Specs = specs;
            m_stateByPath = specs.ToDictionary(s => s.GetAbsolutePath(m_pathTable));
        }

        /// <summary>
        /// Forces spec-2-spec map materialization.
        /// </summary>
        public void MaterializeDependencies()
        {
            Parallel.ForEach(
                Specs,
                source =>
                {
                    source.FileDependents.MaterializeSetIfNeeded(Specs, (files, idx) => files[idx].GetAbsolutePath(m_pathTable));
                    source.FileDependencies.MaterializeSetIfNeeded(Specs, (files, idx) => files[idx].GetAbsolutePath(m_pathTable));
                });
        }

        /// <nodoc />
        [CanBeNull]
        public ISpecBindingState TryGetSpecState(AbsolutePath fullPath)
        {
            m_stateByPath.TryGetValue(fullPath, out var result);
            return result;
        }

        /// <nodoc />
        public void UpdateBindingFingerprint(AbsolutePath fullPath, string referencedSymbolsFingerprint, string declaredSymbolsFingerprint)
        {
            var specState = TryGetSpecState(fullPath);

            Contract.Assert(specState != null, "specState != null");
            m_stateByPath[fullPath] = specState.WithBindingFingerprint(referencedSymbolsFingerprint, declaredSymbolsFingerprint);
        }

        /// <inheritdoc />
        int IWorkspaceBindingSnapshot.SourcesCount => Specs.Length;

        /// <inheritdoc />
        IEnumerable<ISourceFileBindingState> IWorkspaceBindingSnapshot.Sources => Specs;
    }
}
