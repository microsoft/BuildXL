// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Execution.Analyzer
{
    public enum AnalysisMode
    {
        None,
        Allowlist,
        BuildStatus,
        CacheDump,
#if FEATURE_VSTS_ARTIFACTSERVICES
        CacheHitPredictor,
#endif
        CacheMiss,
        CacheMissLegacy,
        Codex,
        CopyFile,
        CosineJson,
        CosineDumpPip,
        CriticalPath,
        DebugLogs,
        DependencyAnalyzer,
        Dev,
        DirMembership,
        DumpProcess,
        DumpPip,
        DumpMounts,
        EventStats,
        ExportDgml,
        ExportGraph,
        FailedPipInput,
        FailedPipsDump,
        FileChangeTracker,
        FileConsumption,
        FileImpact,
        FilterLog,
        FingerprintText,
        GraphDiffAnalyzer,
        IdeGenerator,
        IncrementalSchedulingState,
        InputTracker,
        JavaScriptDependencyFixer,
        LogCompare,
        ObservedInput,
        ObservedInputSummary,
        ObservedAccess,
        PackedExecutionExporter,
        PerfSummary,
        PipFilter,
        PipFingerprint,
        PipExecutionPerformance,
        ProcessRunScript,
        ProcessDetouringStatus,
        RequiredDependencies,
        ScheduledInputsOutputs,
        Simulate,
        SpecClosure,
        ToolEnumeration,
        Whitelist, // compatibility
        WinIdeDependency,
        XlgToDb,
    }
}
