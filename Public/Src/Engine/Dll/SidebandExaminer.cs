// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Sideband;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
namespace BuildXL.Engine
{
    /// <summary>
    /// A helper class responsible for examining the state of the sideband files on disk
    /// and determining if it is safe to postpone deletion of shared opaque outputs.
    /// </summary>
    public sealed class SidebandExaminer
    {
        private LoggingContext LoggingContext { get; }
        private Scheduler.Scheduler Scheduler { get; }
        private IConfiguration Configuration { get; }
        private RootFilter RootFilter { get; }

        private AbsolutePath SidebandRootDir => Configuration.Layout.SharedOpaqueSidebandDirectory;
        private PipExecutionContext Context => Scheduler.Context;

        /// <nodoc />
        public SidebandExaminer(LoggingContext loggingContext, Scheduler.Scheduler scheduler, IConfiguration configuration, RootFilter filter)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(scheduler != null);
            Contract.Requires(configuration != null);

            LoggingContext = loggingContext;
            Scheduler = scheduler;
            Configuration = configuration;
            RootFilter = filter;
        }

        /// <summary>
        /// Reasons why <see cref="Examine"/> may decide that it is not safe to postpone shared opaque output deletion
        /// </summary>
        public enum SidebandIntegrityCheckFailReason
        { 
            /// <summary>
            /// Corresponding sideband file was not found.
            /// </summary>
            FileNotFound, 
            
            /// <summary>
            /// Corresponding sideband file was found but its FileEnvelope checksum didn't match 
            /// </summary>
            ChecksumMismatch,

            /// <summary>
            /// Corresponding sideband file was found and its FileEnvelope checksum matched but 
            /// the pip metadata (semistable hash + static fingerprint) didn't match.
            /// </summary>
            MetadataMismatch
        }

        /// <summary>
        /// Examines the state of the sideband files and returns all relevant information packaged up in a <see cref="SidebandState"/> object.
        /// </summary>
        public SidebandState Examine(bool computeExtraneousSidebandFiles)
        {
            try
            {
                if (!Configuration.Schedule.UnsafeLazySODeletion || !SidebandRootDir.IsValid)
                {
                    return SidebandState.CreateForEagerDeletion();
                }

                // find relevant process pips (i.e., those with SOD outputs and not filtered out by RootFilter
                var processesWithSharedOpaqueDirectoryOutputs = GetFilteredNodes()
                    .ToArray()
                    .AsParallel(Context)
                    .Select(nodeId => (Process)Scheduler.PipGraph.GetPipFromPipId(nodeId.ToPipId()))
                    .Where(process => process.HasSharedOpaqueDirectoryOutputs);

                // check validity of their sideband files
                if (!TryGetSidebandEntries(processesWithSharedOpaqueDirectoryOutputs, out var sidebandEntries))
                {
                    return SidebandState.CreateForEagerDeletion();
                }

                // find extraneous sideband files and return
                string[] extraneousSidebandFiles = null;
                if (computeExtraneousSidebandFiles)
                {
                    var allSidebandFiles = SidebandWriter.FindAllProcessPipSidebandFiles(SidebandRootDir.ToString(Context.PathTable));
                    extraneousSidebandFiles = allSidebandFiles
                        .Except(
                            processesWithSharedOpaqueDirectoryOutputs.Select(GetSidebandFile),
                            OperatingSystemHelper.PathComparer)
                        .ToArray();
                }

                return SidebandState.CreateForLazyDeletion(sidebandEntries, extraneousSidebandFiles);
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException)
            {
                Logger.Log.SidebandFileIntegrityCheckThrewException(LoggingContext, ex.ToString());
                return SidebandState.CreateForEagerDeletion();
            }
        }

        private bool TryGetSidebandEntries(IEnumerable<Process> processes, out IReadOnlyDictionary<long, IReadOnlyCollection<AbsolutePath>> sidebandState)
        {
            var mutableSidebandState = new Dictionary<long, IReadOnlyCollection<AbsolutePath>>();
            foreach (var process in processes)
            {
                if (!TryGetAndValidateSidebandStateForProcess(process, out var state))
                {
                    sidebandState = null;
                    return false;
                }

                mutableSidebandState[process.SemiStableHash] = state;
            }

            sidebandState = mutableSidebandState;
            return true;
        }

        private string GetSidebandFile(Process process)
            => SidebandWriter.GetSidebandFileForProcess(Context.PathTable, SidebandRootDir, process);

        /// <summary>
        /// Returns nodes accepted by the filter, i.e.,
        ///   - when no filter is specified, returns all process nodes
        ///   - otherwise, asks the scheduler to apply the filter
        /// </summary>
        private IEnumerable<NodeId> GetFilteredNodes()
        {
            if (RootFilter == null || RootFilter.IsEmpty)
            {
                return Scheduler.PipGraph
                    .RetrievePipReferencesOfType(PipType.Process)
                    .Select(pipRef => pipRef.PipId.ToNodeId());
            }
            // TODO(#1657322): FilterNodesToBuild can be pretty expensive, so we should avoid doing this twice (here and in Scheduler.InitForOrchestrator)
            else if (Scheduler.PipGraph.FilterNodesToBuild(LoggingContext, RootFilter, out var nodesRangeSet))
            {
                // Need to check not only explicitly filtered nodes but also their dependencies
                var buildSetCalculator = new Scheduler.Scheduler.SchedulerBuildSetCalculator(LoggingContext, Scheduler);
                var scheduledNodesResult = buildSetCalculator.GetNodesToSchedule(
                    scheduleDependents: false,
                    explicitlyScheduledNodes: nodesRangeSet,
                    forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                    scheduleMetaPips: false);

                return scheduledNodesResult.ScheduledNodes.Where(nodeId => Scheduler.GetPipType(nodeId.ToPipId()) == PipType.Process);
            }
            else
            {
                return CollectionUtilities.EmptyArray<NodeId>();
            }
        }

        /// <summary>
        /// Checks that
        ///   - a sideband file for a given process exists
        ///   - the sideband file is not corrupt (its checksum checks out)
        ///   - the process metadata recorded in the sideband file matches the metadata expected for this process
        /// Returns true on success and the sideband state in the out parameter
        /// </summary>
        private bool TryGetAndValidateSidebandStateForProcess(Process process, out IReadOnlyCollection<AbsolutePath> paths)
        {
            var sidebandFile = GetSidebandFile(process);
            paths = null;
            if (!FileUtilities.FileExistsNoFollow(sidebandFile))
            {
                return failed(SidebandIntegrityCheckFailReason.FileNotFound);
            }

            using (var reader = new SidebandReader(sidebandFile))
            {
                if (!reader.ReadHeader(ignoreChecksum: false))
                {
                    return failed(SidebandIntegrityCheckFailReason.ChecksumMismatch);
                }

                var metadata = reader.ReadMetadata();
                var expected = PipExecutor.CreateSidebandMetadata(Scheduler, process);
                if (!metadata.Equals(expected))
                {
                    return failed(SidebandIntegrityCheckFailReason.MetadataMismatch, $"Expected: {expected}.  Actual: {metadata}");
                }

                paths = reader.ReadRecordedPaths().Select(p => AbsolutePath.Create(Context.PathTable, p)).ToHashSet();
                return true;
            }

            bool failed(SidebandIntegrityCheckFailReason reason, string details = "")
            {
                Logger.Log.SidebandIntegrityCheckForProcessFailed(LoggingContext, process.FormattedSemiStableHash, sidebandFile, reason.ToString(), details);
                return false;
            }
        }

        /// <summary>
        /// Reads in parallel all writes recorded in given sideband files (<paramref name="sidebandFiles"/>).
        /// The task of reading paths from a single sideband file is delegated to <see cref="SidebandReader.ReadSidebandFile"/>.
        /// Exceptions of type <see cref="IOException"/> and <see cref="BuildXLException"/> are caught, logged, and ignored.
        /// </summary>
        internal string[] TryReadAllRecordedWrites(IReadOnlyList<string> sidebandFiles)
        {
            try
            {
                return sidebandFiles
                    .AsParallel(Context)
                    .SelectMany(f => 
                    {
                        if (!TryReadSidebandFile(f, out _, out var paths))
                        {
                            return CollectionUtilities.EmptyArray<string>();
                        }

                        return paths;
                    })
                    .ToArray();
            }
            catch (OperationCanceledException)
            {
                // No specific handling needed for cancellations. Build session will terminate
                return CollectionUtilities.EmptyArray<string>();
            }
        }

        private bool TryReadSidebandFile(string filename, out SidebandMetadata metadata, out IEnumerable<string> paths)
        {
            try
            {
                // We ignore the checksum because even when the sideband file is compromised, 
                // it is possible to call <see cref="ReadRecordedPaths"/> which will then try to recover 
                // as many recorded paths as possible. 
                (paths, metadata) = SidebandReader.ReadSidebandFile(filename, ignoreChecksum: true);
                return true;
            }
            catch (Exception e) when (e is BuildXLException || e is IOException || e is OperationCanceledException)
            {
                Processes.Tracing.Logger.Log.CannotReadSidebandFileWarning(LoggingContext, filename, e.Message);
                metadata = null;
                paths = null;
                return false;
            }
        }
    }

    /// <nodoc />
    internal static class Extensions
    {
        /// <summary>
        /// Sets up <see cref="ParallelQuery"/> for a given array and a given configuration.
        /// </summary>
        /// <exception cref="OperationCanceledException">When the CancellationToken in the <see cref="PipExecutionContext"/> is triggered.</exception>
        public static IEnumerable<T> AsParallel<T>(this IReadOnlyList<T> @this, PipExecutionContext context)
        {
            return @this
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .WithCancellation(context.CancellationToken);
        }
    }
}
