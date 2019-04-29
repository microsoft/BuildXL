// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script
{
    /// <nodoc />
    internal class NullFrontEndStatistics : IFrontEndStatistics
    {
        /// <inheritdoc />
        public Counter SpecAstConversion => new Counter();

        /// <inheritdoc />
        public Counter SpecAstDeserialization => new Counter();

        /// <inheritdoc />
        public Counter SpecAstSerialization => new Counter();

        /// <inheritdoc />
        public Counter PublicFacadeComputation => new Counter();

        /// <inheritdoc />
        public CounterWithRootCause CounterWithRootCause => new CounterWithRootCause();

        /// <inheritdoc />
        public EventHandler<WorkspaceProgressEventArgs> WorkspaceProgress => (e, sender) => { };

        /// <inheritdoc />
        public Counter ConfigurationProcessing => new Counter();

        /// <inheritdoc />
        public Counter PreludeProcessing => new Counter();

        /// <inheritdoc />
        public INugetStatistics NugetStatistics => null;

        /// <inheritdoc />
        public ILoadConfigStatistics LoadConfigStatistics => new NullLoadConfigStatistics();

        /// <inheritdoc />
        public Counter SpecParsing => new Counter();

        /// <inheritdoc />
        public Counter SpecBinding => new Counter();

        /// <inheritdoc />
        public Counter SpecTypeChecking => new Counter();

        /// <inheritdoc />
        public Counter SpecComputeFingerprint => new Counter();

        /// <inheritdoc />
        public Counter SpecConversion => new Counter();

        /// <inheritdoc />
        public Counter SpecEvaluation => new Counter();

        /// <inheritdoc />
        public Counter EndToEndParsing => new Counter();

        /// <inheritdoc />
        public Counter EndToEndBinding => new Counter();

        /// <inheritdoc />
        public Counter EndToEndTypeChecking => new Counter();

        /// <inheritdoc />
        public TimeSpan? FrontEndSnapshotSavingDuration { get => null; set { } }

        /// <inheritdoc />
        public TimeSpan? FrontEndSnapshotLoadingDuration { get => null; set {} }

        /// <inheritdoc />
        public Counter PublicFacadeHits => new Counter();

        /// <inheritdoc />
        public Counter SerializedAstHits => new Counter();

        /// <inheritdoc />
        public Counter PublicFacadeGenerationFailures => new Counter();

        /// <inheritdoc />
        public Counter PublicFacadeSaves => new Counter();

        /// <inheritdoc />
        public Counter AstSerializationSaves => new Counter();

        /// <inheritdoc />
        public CounterValue AstSerializationBlobSize => new CounterValue();

        /// <inheritdoc />
        public CounterValue PublicFacadeSerializationBlobSize => new CounterValue();

        /// <inheritdoc />
        public WeightedCounter SourceFileNodes => new WeightedCounter();

        /// <inheritdoc />
        public WeightedCounter SourceFileIdentifiers => new WeightedCounter();

        /// <inheritdoc />
        public WeightedCounter SourceFileLines => new WeightedCounter();

        /// <inheritdoc />
        public WeightedCounter SourceFileChars => new WeightedCounter();

        /// <inheritdoc />
        public WeightedCounter SourceFileSymbols => new WeightedCounter();

        /// <inheritdoc />
        public void AnalysisCompleted(AbsolutePath path, TimeSpan duration)
        {
        }

        /// <inheritdoc />
        public TimeSpan GetOverallAnalysisDuration()
        {
            return TimeSpan.Zero;
        }
    }
}
