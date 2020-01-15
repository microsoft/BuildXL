// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Execution.Analyzer
{
    public enum AnalysisMode
    {
        None,
        PipFilter,
        FileImpact,
        FilterLog,
        FingerprintText,
        FailedPipInput,
        ExportGraph,
        DirMembership,
        DumpProcess,
        CriticalPath,
        DumpPip,
        ProcessRunScript,
        ExportDgml,
        ObservedInput,
        ObservedInputSummary,
        ObservedAccess,
        PipExecutionPerformance,
        ProcessDetouringStatus,
        ToolEnumeration,
        Whitelist,
        LogCompare,
        IdeGenerator,
        BuildStatus,
        WinIdeDependency,
        PerfSummary,
        CacheMissLegacy,
        CacheDump,
        EventStats,
        Simulate,
        Dev,
        SpecClosure,
        IncrementalSchedulingState,
        FileChangeTracker,
        InputTracker,
        CacheMiss,
        ScheduledInputsOutputs,
        RequiredDependencies,
#if FEATURE_VSTS_ARTIFACTSERVICES
        CacheHitPredictor,
#endif
        Codex,
        CosineJson,
        CosineDumpPip,
        FileConsumption,
        DependencyAnalyzer,
        GraphDiffAnalyzer,
        DumpMounts,
        FailedPipsDump,
        PipFingerprint,
        CopyFile, 
        XlgToDb,
        DebugLogs,
        ContentPlacement
    }
}
