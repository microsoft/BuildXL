// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using EnvVar = System.Collections.Generic.KeyValuePair<string, (string, bool)>;

namespace BuildXL.Execution.Analyzer.Analyzers.CacheMiss
{
    /// <summary>
    /// Pip caching information
    /// </summary>
    internal sealed class PipCachingInfo
    {
        public PipCachingInfo(PipId pipId, AnalysisModel model)
        {
            PipId = pipId;
            Model = model;
            OriginalPipId = pipId;
        }

        public uint WorkerId { get; private set; }
        public AnalysisModel Model;
        public PipId PipId;
        public PipId OriginalPipId;
        public ProcessFingerprintComputationEventData FingerprintComputation { get; private set; }
        public CacheablePipInfo CacheablePipInfo;
        public PipContentFingerprinter Fingerprinter => Model.GetFingerprinter(WorkerId);

        public ProcessStrongFingerprintComputationData StrongFingerprintComputation;

        public void SetFingerprintComputation(ProcessFingerprintComputationEventData fingerprintComputation, uint workerId)
        {
            if (!FingerprintComputation.PipId.IsValid || fingerprintComputation.Kind == FingerprintComputationKind.Execution)
            {
                // Execution takes precedence over cache check if set. This is needed because the events may come out of order
                FingerprintComputation = fingerprintComputation;
                WorkerId = workerId;
            }
        }

        public Process GetOriginalProcess()
        {
            return (Process)Model.CachedGraph.PipGraph.GetPipFromPipId(OriginalPipId);
        }

        public IReadOnlyList<EnvVar> GetEnvironmentVariables()
        {
            var context = Model.CachedGraph.Context;
            return GetOriginalProcess().EnvironmentVariables.Select(env => new EnvVar(env.Name.ToString(context.StringTable), (env.Value.IsValid ? "{unset}" : env.Value.ToString(context.PathTable), env.IsPassThrough))).ToList();
        }

        public IReadOnlyList<FileData> DependencyData => CacheablePipInfo.Dependencies.ToFileDataList(WorkerId, Model);

        public bool HasValidFingerprintComputation => FingerprintComputation.PipId.IsValid;

        public DirectoryMembershipHashedEventData? GetDirectoryMembershipData(AbsolutePath path, string enumeratePatternRegex)
        {
            return Model.GetDirectoryMembershipData(WorkerId, PipId, path, enumeratePatternRegex);
        }
    }
}
