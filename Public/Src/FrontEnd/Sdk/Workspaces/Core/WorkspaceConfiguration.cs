// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.DScript;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A workspace configuration specifies the settings of a collection of resolvers.
    /// </summary>
    /// <remarks>
    /// The order in the collection is important, since resolvers are initialized in that order and module lookup
    /// respects resolver ordering.
    /// </remarks>
    public sealed class WorkspaceConfiguration
    {
        /// <summary>
        /// Creates a configuration.
        /// </summary>
        /// <remarks>
        /// A prelude module name can be optionally specified. In that case, a prelude with that name is required to be known
        /// by some of the provided resolvers, and it is guaranteed to be part of a successfully built workspace.
        /// </remarks>
        public WorkspaceConfiguration(
            IReadOnlyCollection<IResolverSettings> resolverSettings,
            bool constructFingerprintDuringParsing,
            int maxDegreeOfParallelismForParsing,
            int maxDegreeOfParallelismForTypeChecking,
            ParsingOptions parsingOptions,
            bool cancelOnFirstFailure,
            string includePreludeWithName,
            bool trackFileToFileDepedendencies = true,
            CancellationToken? cancellationToken = null)
        {
            Contract.Requires(resolverSettings != null);
            Contract.Requires(parsingOptions != null);

            ResolverSettings = resolverSettings;
            ConstructFingerprintDuringParsing = constructFingerprintDuringParsing;
            PreludeName = includePreludeWithName;
            MaxDegreeOfParallelismForParsing = maxDegreeOfParallelismForParsing;
            MaxDegreeOfParallelismForTypeChecking = maxDegreeOfParallelismForTypeChecking;
            ParsingOptions = parsingOptions;
            CancelOnFirstFailure = cancelOnFirstFailure;
            CancellationToken = cancellationToken ?? CancellationToken.None;
            TrackFileToFileDependencies = trackFileToFileDepedendencies;
        }

        /// <summary>
        /// A workspace configuration with some default settings for testing purposes
        /// </summary>
        public static WorkspaceConfiguration CreateForTesting()
        {
            return new WorkspaceConfiguration(
                CollectionUtilities.EmptyArray<IResolverSettings>(),
                constructFingerprintDuringParsing: false,
                maxDegreeOfParallelismForParsing: DataflowBlockOptions.Unbounded,
                parsingOptions: TypeScript.Net.DScript.ParsingOptions.DefaultParsingOptions,
                maxDegreeOfParallelismForTypeChecking: 1,
                cancelOnFirstFailure: true,
                includePreludeWithName: null);
        }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public IReadOnlyCollection<IResolverSettings> ResolverSettings { get; }

        /// <summary>
        /// If true, then the spec fingerprint would be constructed during the parsing phase.
        /// </summary>
        public bool ConstructFingerprintDuringParsing { get; }

        /// <nodoc/>
        public bool ShouldIncludePrelude => !string.IsNullOrEmpty(PreludeName);

        /// <nodoc/>
        [CanBeNull]
        public string PreludeName { get; }

        /// <nodoc/>
        public int MaxDegreeOfParallelismForParsing { get; }

        /// <nodoc/>
        public int MaxDegreeOfParallelismForTypeChecking { get; }

        /// <summary>
        /// If true, then file-2-file map information would be computed and preserved by the checker.
        /// </summary>
        public bool TrackFileToFileDependencies { get; }

        /// <nodoc/>
        [CanBeNull]
        public ParsingOptions ParsingOptions { get; }

        /// <summary>
        /// Used by the parser queue to break after the first failure.
        /// </summary>
        public bool CancelOnFirstFailure { get; }

        /// <summary>
        /// Cancellation token.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Minimal number of files inside one module to trigger parallel execution for module updates.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public int ThreasholdForParallelUpdate => 1000;

        /// <nodoc />
        public WorkspaceConfiguration WithComputeBindingFingerprint(bool computeBindingFingerprint)
        {
            return new WorkspaceConfiguration(
                ResolverSettings,
                computeBindingFingerprint,
                MaxDegreeOfParallelismForParsing,
                MaxDegreeOfParallelismForTypeChecking,
                ParsingOptions,
                CancelOnFirstFailure,
                PreludeName,
                TrackFileToFileDependencies,
                CancellationToken);
        }

        /// <nodoc />
        public WorkspaceConfiguration WithPreludeName([CanBeNull] string preludeName)
        {
            return new WorkspaceConfiguration(
                ResolverSettings,
                ConstructFingerprintDuringParsing,
                MaxDegreeOfParallelismForParsing,
                MaxDegreeOfParallelismForTypeChecking,
                ParsingOptions,
                CancelOnFirstFailure,
                preludeName,
                TrackFileToFileDependencies,
                CancellationToken);
        }

        /// <nodoc />
        public WorkspaceConfiguration WithTrackFileToFilemap(bool trackFileToFileMap)
        {
            return new WorkspaceConfiguration(
                ResolverSettings,
                ConstructFingerprintDuringParsing,
                MaxDegreeOfParallelismForParsing,
                MaxDegreeOfParallelismForTypeChecking,
                ParsingOptions,
                CancelOnFirstFailure,
                PreludeName,
                trackFileToFileMap,
                CancellationToken);
        }
    }
}
