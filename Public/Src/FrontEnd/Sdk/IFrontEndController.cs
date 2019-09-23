// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// The interface the BuildXL engine uses to talk to the front end.
    /// </summary>
    /// <remarks>
    /// This is not the interface that FrontEnds like DScript, NuGet etc use as a
    /// communication with the host.
    /// That is abstract class <see cref="FrontEndHost" />
    /// </remarks>
    public interface IFrontEndController : IDisposable
    {
        /// <summary>
        /// Initialize the host
        /// </summary>
        void InitializeHost([NotNull]FrontEndContext context, [NotNull]IConfiguration configuration);

        /// <summary>
        /// Parses the configuration
        /// </summary>
        [CanBeNull]
        IConfiguration ParseConfig([NotNull]ICommandLineConfiguration configuration);

        /// <summary>
        /// Asks the frontEndController to parse and evaluate any specs, definition to construct the pipGraph.
        /// </summary>
        /// <remarks>
        /// The graph passed in 'can' be null. In that case the graph doesn't need to be populated, but the specs are still expected to be evaluated and checked for errors.
        /// the cache is ridiculously a function because we only want to block on the cache being initialized if we need it since it is slow sometimes.
        /// </remarks>
        bool PopulateGraph(
            [NotNull]Task<Possible<EngineCache>> cache,
            [CanBeNull]IPipGraph graph,
            [NotNull]FrontEndEngineAbstraction engineAbstraction,
            [NotNull]EvaluationFilter evaluationFilter,
            [NotNull]IConfiguration configuration,
            [NotNull]IStartupConfiguration startupConfiguration);

        /// <summary>
        /// Log some statistics with an option to show statistcs about the slowest procceses. The defaul is false.
        /// </summary>
        FrontEndControllerStatistics LogStatistics(bool showSlowestElementsStatistics, bool showLargestFilesStatistics);

        /// <summary>
        /// Returns the list of paths that shouldn't be scrubbed by the engine
        /// </summary>
        /// <remarks>This method is assumed to be called after InitializeHost and ParseConfig</remarks>
        [NotNull]
        IReadOnlyList<string> GetNonScrubbablePaths();
    }

    /// <summary>
    /// Statistics relating to the FrontEnd
    /// </summary>
    public class FrontEndControllerStatistics
    {
        /// <summary>
        /// The weight of IO time to computation time during FrontEnd processing. A value of 2 would mean twice as
        /// much time was spent in IO compared to CPU
        /// </summary>
        public double IOWeight;
    }
}
