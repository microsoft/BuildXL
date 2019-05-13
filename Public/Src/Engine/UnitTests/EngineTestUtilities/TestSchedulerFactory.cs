// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Factory functions for common uses of <see cref="Scheduler" /> in tests.
    /// </summary>
    public static class TestSchedulerFactory
    {
        /// <summary>
        /// Creates an empty, mutable pip graph. Add pips to it, then create a scheduler.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public static PipGraph.Builder CreateEmptyPipGraph(
            EngineContext context,
            IConfiguration configuration,
            SemanticPathExpander semanticPathExpander)
        {
            var pipTable = new PipTable(context.PathTable, context.SymbolTable, initialBufferSize: 16, maxDegreeOfParallelism: 0, debug: true);

            return new PipGraph.Builder(
                pipTable,
                context,
                global::BuildXL.Scheduler.Tracing.Logger.Log,
                BuildXLTestBase.CreateLoggingContextForTest(),
                configuration,
                semanticPathExpander);
        }

        /// <summary>
        /// Creates a scheduler that runs non-incrementally (no cache).
        /// Artifact content is managed with an <see cref="InMemoryArtifactContentCache"/>, so the total artifact footprint must be small.
        /// The provided pip graph is marked immutable if it isn't already.
        /// Both the scheduler and <see cref="EngineCache"/> must be disposed by the caller.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Caller owns the returned disposables")]
        public static Tuple<Scheduler, EngineCache> Create(
            PipExecutionContext context,
            IConfiguration configuration,
            PipGraph.Builder graphBuilder,
            IPipQueue queue)
        {
            Contract.Requires(graphBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(queue != null);
            Contract.Requires(configuration != null);

            var cacheLayer = new EngineCache(
                new InMemoryArtifactContentCache(),
                new EmptyTwoPhaseFingerprintStore());

            Scheduler scheduler = CreateInternal(
                context,
                graphBuilder.Build(),
                queue,
                cache: cacheLayer,
                configuration: configuration);

            return Tuple.Create(scheduler, cacheLayer);
        }

        /// <summary>
        /// Creates a scheduler that runs with a fully capable cache for storing artifact content and cache descriptors.
        /// Pips may complete from cache.
        /// Both the scheduler and <see cref="EngineCache"/> must be disposed by the caller.
        /// The provided pip graph is marked immutable if it isn't already.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Caller owns the returned disposables")]
        public static Tuple<Scheduler, EngineCache> CreateWithCaching(
            PipExecutionContext context,
            IConfiguration configuration,
            PipGraph.Builder graphBuilder,
            IPipQueue queue)
        {
            Contract.Requires(graphBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(queue != null);

            var cacheLayer = new EngineCache(
                new InMemoryArtifactContentCache(),
                new InMemoryTwoPhaseFingerprintStore());

            Scheduler scheduler = CreateInternal(
                context,
                graphBuilder.Build(),
                queue,
                cacheLayer,
                configuration);

            return Tuple.Create(scheduler, cacheLayer);
        }

        private static Scheduler CreateInternal(
            PipExecutionContext context,
            PipGraph pipGraph,
            IPipQueue queue,
            EngineCache cache,
            IConfiguration configuration)
        {
            Contract.Requires(context != null);
            Contract.Requires(queue != null);
            Contract.Requires(cache != null);
            Contract.Requires(configuration != null);

            var fileContentTable = FileContentTable.CreateNew();

            var fileAccessWhiteList = new FileAccessWhitelist(context);

            var testHooks = new SchedulerTestHooks();

            return new Scheduler(
                pipGraph,
                queue,
                context,
                fileContentTable,
                cache: cache,
                loggingContext: Events.StaticContext,
                configuration: configuration,
                fileAccessWhitelist: fileAccessWhiteList,
                testHooks: testHooks,
                buildEngineFingerprint: null);
        }
    }
}
