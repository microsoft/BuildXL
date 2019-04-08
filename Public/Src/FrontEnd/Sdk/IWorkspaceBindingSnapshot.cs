// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Incrementality;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Snapshot of the workspace used by incremental scenarios.
    /// </summary>
    public interface IWorkspaceBindingSnapshot
    {
        /// <summary>
        /// Number of sources in the snapshot.
        /// </summary>
        int SourcesCount { get; }

        /// <summary>
        /// Binding information for all specs in the snapshot.
        /// </summary>
        IEnumerable<ISourceFileBindingState> Sources { get; }

        /// <summary>
        /// Forces spec-2-spec map materialization.
        /// </summary>
        void MaterializeDependencies();

        /// <nodoc/>
        void UpdateBindingFingerprint(AbsolutePath fullPath, [NotNull]string referencedSymbolsFingerprint, [NotNull]string declaredSymbolsFingerprint);

        /// <nodoc/>
        ISpecBindingState TryGetSpecState(AbsolutePath fullPath);
    }
}
