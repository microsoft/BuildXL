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
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;

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
        /// The result of the <see cref="Examine"/> call.
        /// </summary>
        public sealed class Result
        {
            /// <summary>
            /// Final decision about whether or not to postpone deletion of shared opaque outputs.
            /// </summary>
            public bool ShouldPostponeDeletion { get; }

            /// <summary>
            /// List of sideband files that are present on disk but whose corresponding pips are not found in the pip graph.
            /// This value is only set when <see cref="ShouldPostponeDeletion"/> is true.
            /// </summary>
            public IReadOnlyList<string> ExtraneousSidebandFiles { get; }

            /// <nodoc />
            internal Result(bool shouldPostponeDeletion, [CanBeNull] IReadOnlyList<string> extraneousSidebandFiles)
            {
                Contract.Requires(shouldPostponeDeletion == (extraneousSidebandFiles != null));
                Contract.Ensures(ExtraneousSidebandFiles != null);
                Contract.Ensures(ShouldPostponeDeletion || ExtraneousSidebandFiles.Count == 0);

                ShouldPostponeDeletion = shouldPostponeDeletion;
                ExtraneousSidebandFiles = extraneousSidebandFiles ?? CollectionUtilities.EmptyArray<string>();
            }

            /// <nodoc />
            internal static Result CreateForEagerDeletion() 
                => new Result(shouldPostponeDeletion: false, extraneousSidebandFiles: null);

            /// <nodoc />
            internal static Result CreateForLazyDeletion(IReadOnlyList<string> extraneousSidebandFiles)
                => new Result(shouldPostponeDeletion: true, extraneousSidebandFiles);
        }

        /// <summary>
        /// Examines the state of the sideband files and returns all relevant information packaged up in a <see cref="Result"/> object.
        /// </summary>
        public Result Examine(bool computeExtraneousSidebandFiles)
        {
            try
            {
                if (!Configuration.Schedule.UnsafeLazySODeletion || !SidebandRootDir.IsValid)
                {
                    return Result.CreateForEagerDeletion();
                }

                // find relevant process pips (i.e., those with SOD outputs and not filtered out by RootFilter
                var processesWithSharedOpaqueDirectoryOutputs = GetFilteredNodes()
                    .ToArray()
                    .AsParallel(Context)
                    .Select(nodeId => (Process)Scheduler.PipGraph.GetPipFromPipId(nodeId.ToPipId()))
                    .Where(process => process.HasSharedOpaqueDirectoryOutputs)
                    .ToArray();

                // check validity of their sideband files
                if (!processesWithSharedOpaqueDirectoryOutputs.All(process => ValidateSidebandFileForProcess(process)))
                {
                    return Result.CreateForEagerDeletion();
                }

                // find extraneous sideband files and return
                string[] extraneousSidebandFiles = null;
                if (computeExtraneousSidebandFiles)
                {
                    var allSidebandFiles = SidebandWriter.FindAllProcessPipSidebandFiles(SidebandRootDir.ToString(Context.PathTable));
                    extraneousSidebandFiles = allSidebandFiles
                        .Except(
                            processesWithSharedOpaqueDirectoryOutputs.Select(GetSidebandFile),
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                return Result.CreateForLazyDeletion(extraneousSidebandFiles);
            }
            catch (IOException ex)
            {
                Logger.Log.SidebandFileIntegrityCheckThrewException(LoggingContext, ex.ToString());
                return Result.CreateForEagerDeletion();
            }
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
            // TODO(#1657322): FilterNodesToBuild can be pretty expensive, so we should avoid doing this twice (here and in Scheduler.InitForMaster)
            else if (Scheduler.PipGraph.FilterNodesToBuild(LoggingContext, RootFilter, out var nodesRangeSet))
            {
                return nodesRangeSet.Where(nodeId => Scheduler.GetPipType(nodeId.ToPipId()) == PipType.Process);
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
        /// </summary>
        private bool ValidateSidebandFileForProcess(Process process)
        {
            var sidebandFile = GetSidebandFile(process);
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
            return sidebandFiles
                .AsParallel(Context)
                .SelectMany(tryReadSidebandFile)
                .ToArray();

            IEnumerable<string> tryReadSidebandFile(string filename)
            {
                try
                {
                    return SidebandReader.ReadSidebandFile(filename, ignoreChecksum: true);
                }
                catch (Exception e) when (e is BuildXLException || e is IOException || e is OperationCanceledException)
                {
                    Processes.Tracing.Logger.Log.CannotReadSidebandFileWarning(LoggingContext, filename, e.Message);
                    return CollectionUtilities.EmptyArray<string>();
                }
            }
        }
    }

    /// <nodoc />
    internal static class Extensions
    {
        /// <summary>
        /// Sets up <see cref="ParallelQuery"/> for a given array and a given configuration.
        /// </summary>
        public static IEnumerable<T> AsParallel<T>(this IReadOnlyList<T> @this, PipExecutionContext context)
        {
            return @this
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .WithCancellation(context.CancellationToken);
        }
    }
}
