// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BuildXL.Engine;
using BuildXL.Execution.Analyzer.JPath;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Execution.Analyzer.JPath.Evaluator;
using static BuildXL.FrontEnd.Script.Debugger.Renderer;
using ProcessMonitoringData = BuildXL.Scheduler.Tracing.ProcessExecutionMonitoringReportedEventData;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Global XLG state (where all pips, artifacts, etc. are displayed)
    /// </summary>
    public class XlgDebuggerState : ThreadState, IExpressionEvaluator
    {
        /// <summary>
        /// Reserved name of the "last value" variable.
        /// </summary>
        private const string LastValueVarName = "$_result";
        private const string EvalDurationVarName = "$_evalDuration";
        private const string ExeLevelNotCompleted = "NotCompleted";
        private const int XlgThreadId = 1;

        private Evaluator Evaluator { get; }

        private DebugLogsAnalyzer Analyzer { get; }

        private CachedGraph CachedGraph => Analyzer.CachedGraph;

        private PipGraph PipGraph => CachedGraph.PipGraph;

        private PathTable PathTable => CachedGraph.Context.PathTable;

        private StringTable StringTable => PathTable.StringTable;

        private Renderer Renderer => Analyzer.Session.Renderer;

        private ObjectInfo PipsByType { get; }

        private ObjectInfo PipsByStatus { get; }
        private ObjectInfo RootObject { get; }

        private Env RootEnv { get; }

        /// <inheritdoc />
        public override IReadOnlyList<DisplayStackTraceEntry> StackTrace { get; }

        /// <nodoc />
        public XlgDebuggerState(DebugLogsAnalyzer analyzer)
            : base(XlgThreadId)
        {
            Analyzer = analyzer;

            var logFile = Path.Combine(Path.GetDirectoryName(Analyzer.Input.ExecutionLogPath), "BuildXL.status.csv");
            StackTrace = new[]
            {
                new DisplayStackTraceEntry(file: logFile, line: 1, position: 1, functionName: "<main>", entry: null)
            };

            PipsByType = Enum
                .GetValues(typeof(PipType))
                .Cast<PipType>()
                .Except(new[] { PipType.Max })
                .Aggregate(new ObjectInfoBuilder(), (obi, pipType) => obi.Prop(pipType.ToString(), () => PipGraph.RetrievePipReferencesOfType(pipType).ToArray()))
                .Build();

            PipsByStatus = Enum
                .GetValues(typeof(PipExecutionLevel))
                .Cast<PipExecutionLevel>()
                .Select(level => level.ToString())
                .Concat(new[] { ExeLevelNotCompleted })
                .Aggregate(new ObjectInfoBuilder(), (obi, levelStr) => obi.Prop(levelStr, () => GetPipsForExecutionLevel(levelStr)))
                .Build();

            RootObject = new ObjectInfoBuilder()
                .Preview("Root")
                .Prop("Pips",          () => PipGraph.RetrieveAllPips())
                .Prop("ProcessPips",   () => PipGraph.RetrievePipReferencesOfType(PipType.Process))
                .Prop("Files",         () => PipGraph.AllFiles)
                .Prop("Directories",   () => PipGraph.AllSealDirectories)
                .Prop("DirMembership", () => Analyzer.GetDirMembershipData())
                //.Prop("CriticalPath",  () => new AnalyzeCriticalPath())
                .Prop("GroupedBy",         new ObjectInfoBuilder()
                    .Prop("Pips",          new PipsScope())
                    .Prop("Files",         () => GroupFiles(PipGraph.AllFiles))
                    .Prop("Directories",   () => GroupDirs(PipGraph.AllSealDirectories))
                    .Build())
                .Build();

            RootEnv = new Env(
                parent: null,
                resolver: (obj) => 
                {
                    return Renderer.GetObjectInfo(context: this, obj).SetValidator(obj => !Renderer.IsInvalid(obj));
                },
                vars: LibraryFunctions.All.ToDictionary(f => '$' + f.Name, f => Result.Scalar(f)),
                current: RootObject);

            Evaluator = new Evaluator(
                new Env(parent: RootEnv, resolver: RootEnv.Resolver, current: RootEnv.Current),
                Analyzer.EnableEvalCaching,
                Analyzer.EnsureOrdering);
        }

        private IEnumerable<T> Concat<T>(params IEnumerable<T>[] list)
        {
            return list.Aggregate((acc, current) => acc != null ? acc.Concat(current) : current);
        }

        private PipReference[] GetPipsForExecutionLevel(string level)
        {
            return PipGraph
                .RetrievePipReferencesOfType(PipType.Process)
                .Where(pipRef => GetPipExecutionLevel(pipRef.PipId) == level)
                .ToArray();
        }

        private string GetPipExecutionLevel(PipId pipId) => GetPipExecutionLevel(Analyzer.TryGetPipExePerf(pipId));

        private string GetPipExecutionLevel(PipExecutionPerformance exePerf)
        {
            return exePerf == null
                ? ExeLevelNotCompleted
                : exePerf.ExecutionLevel.ToString();
        }

        #region Expression Evaluator

        /// <inheritdoc />
        public Possible<ObjectContext, Failure> GetCompletions(ThreadState threadState, int frameIndex, string expr)
        {
            return new Failure<string>("hi");
        }

        /// <inheritdoc />
        public Possible<ObjectContext, Failure> EvaluateExpression(ThreadState threadState, int frameIndex, string expr, bool evaluateForCompletions)
        {
            if (Pip.TryParseSemiStableHash(expr, out var hash) && Analyzer.SemiStableHash2Pip.TryGetValue(hash, out var pipId))
            {
                return new ObjectContext(context: this, Analyzer.AsPipReference(pipId));
            }

            string translatedPath = Analyzer.Session.TranslateUserPath(expr);
            if (AbsolutePath.TryCreate(PathTable, translatedPath, out var path) && path.IsValid)
            {
                return new ObjectContext(context: this, new AnalyzePath(path));
            }

            var start = DateTime.UtcNow;
            var exprToEval = evaluateForCompletions ? $"({expr})[0]" : expr;
            var maybeResult = JPath.JPath.TryEval(Evaluator, exprToEval);
            var evalDuration = DateTime.UtcNow.Subtract(start);
            if (maybeResult.Succeeded)
            {
                if (!evaluateForCompletions)
                {
                    Analysis.IgnoreResult(Evaluator.TopEnv.SetVar(LastValueVarName, maybeResult.Result));
                    Analysis.IgnoreResult(Evaluator.TopEnv.SetVar(EvalDurationVarName, Result.Scalar(evalDuration)));
                }
                return new ObjectContext(this, maybeResult.Result);
            }
            else
            {
                return maybeResult.Failure;
            }
        }

        #endregion

        /// <inheritdoc />
        public override string ThreadName() => "Full XLG";

        /// <inheritdoc />
        public override IEnumerable<ObjectContext> GetSupportedScopes(int frameIndex)
        {
            return new[]
            {
                Renderer.GetObjectInfo(this, Evaluator.TopEnv.Root).WithPreview("Root"),
                new ObjectInfo("Funs", properties: RootEnv.Vars.Select(VarToProp)),
                new ObjectInfo("Vars", properties: Evaluator.TopEnv.Vars.Select(VarToProp))
            }
            .Select(obj => new ObjectContext(this, obj));

            Property VarToProp(KeyValuePair<string, Result> kvp) => new Property(kvp.Key, kvp.Value);
        }

        /// <nodoc />
        public ObjectInfo Render(Renderer renderer, object ctx, object obj)
        {
            switch (obj)
            {
                case AbsolutePath p:               return new ObjectInfo(p.ToString(PathTable));
                case Result result:                return renderer.GetObjectInfo(ctx, result.IsScalar ? result.Value.First() : result.Value.ToArray());
                case Function func:                return new ObjectInfo("fun");
                case PipsScope ps:                 return PipsInfo(ps);
                case FileArtifact f:               return FileArtifactInfo(f);
                case DirectoryArtifact d:          return DirectoryArtifactInfo(d);
                case AnalyzePath ap:               return AnalyzePathInfo(ap);
                case AnalyzeCriticalPath cp:       return AnalyzeCriticalPathInfo();
                case PipId pipId:                  return Render(renderer, ctx, Analyzer.GetPip(pipId)).WithPreview(pipId.ToString());
                case PipReference pipRef:          return Render(renderer, ctx, Analyzer.GetPip(pipRef.PipId));
                case Process proc:                 return ProcessInfo(proc);
                case Pip pip:                      return PipInfo(pip).Build();
                case FileArtifactWithAttributes f: return FileArtifactInfo(f.ToFileArtifact()); // return GenericObjectInfo(f, preview: FileArtifactPreview(f.ToFileArtifact()));
                case ProcessMonitoringData m:      return ProcessMonitoringInfo(m);
                case string s:                     return new ObjectInfo(s);
                default:
                    return null;
            }
        }

        private static readonly PropertyInfo[] s_pipProperties = GetPublicProperties(typeof(Pip));
        private static readonly FieldInfo[] s_pipFields = GetPublicFields(typeof(Pip));

        private ObjectInfoBuilder PipInfo(Pip pip)
        {
            var obi = new ObjectInfoBuilder();
            obi = s_pipProperties.Aggregate(obi, (acc, pi) => acc.Prop(pi.Name, () => pi.GetValue(pip)));
            obi = s_pipFields.Aggregate(obi, (acc, fi) => acc.Prop(fi.Name, () => fi.GetValue(pip)));
            return obi
                .Prop("Description", () => pip.GetDescription(PipGraph.Context))
                .Prop("DownstreamPips", CachedGraph.DirectedGraph.GetOutgoingEdges(pip.PipId.ToNodeId()).Cast<Edge>().Select(e => Analyzer.GetPip(e.OtherNode.ToPipId())))
                .Prop("UpstreamPips", CachedGraph.DirectedGraph.GetIncomingEdges(pip.PipId.ToNodeId()).Cast<Edge>().Select(e => Analyzer.GetPip(e.OtherNode.ToPipId())))
                .Preview(PipPreview(pip));
        }

        private ObjectInfo AnalyzeCriticalPathInfo()
        {
            return GenericObjectInfo(Analyzer.CriticalPath, preview: "").Build();
        }

        private ObjectInfo ProcessMonitoringInfo(ProcessMonitoringData m)
        {
            return GenericObjectInfo(m).Remove("PipId").Remove("Metadata").Build();
        }

        private ObjectInfo AnalyzePathInfo(AnalyzePath ap)
        {
            var props = new List<Property>()
            {
                new Property("Path", ap.Path)
            };

            var latestFileArtifact = PipGraph.TryGetLatestFileArtifactForPath(ap.Path);
            if (latestFileArtifact.IsValid)
            {
                props.Add(new Property("LastestFileArtifact", latestFileArtifact));
            }

            var dirArtifact = PipGraph.TryGetDirectoryArtifactForPath(ap.Path);
            if (dirArtifact.IsValid)
            {
                props.Add(new Property("DirectoryArtifact", dirArtifact));
            }

            return new ObjectInfo(props);
        }

        private string PipPreview(Pip pip)
        {
            var pipType = pip.PipType.ToString().ToUpperInvariant();
            return $"<{pipType}> {pip.GetShortDescription(PipGraph.Context)}";
        }

        private ObjectInfo ProcessInfo(Process proc)
        {
            return PipInfo(proc)
                .Preview(PipPreview(proc))
                .Prop("EXE", () => proc.Executable)
                .Prop("CMD", () => Analyzer.RenderProcessArguments(proc))
                .Prop("Inputs", new ObjectInfoBuilder()
                    .Prop("Files", () => proc.Dependencies.ToArray())
                    .Prop("Directories", () => proc.DirectoryDependencies.ToArray())
                    .Build())
                .Prop("Outputs", new ObjectInfoBuilder()
                    .Prop("Files", () => proc.FileOutputs.ToArray())
                    .Prop("Directories", proc.DirectoryOutputs.ToArray())
                    .Build())
                .Prop("ExecutionPerformance", () => Analyzer.TryGetPipExePerf(proc.PipId))
                .Prop("ExecutionLevel", () => GetPipExecutionLevel(Analyzer.TryGetPipExePerf(proc.PipId)))
                .Prop("MonitoringData", () => Analyzer.TryGetProcessMonitoringData(proc.PipId))
                .Prop("FingerprintData", () => Analyzer.TryGetProcessFingerprintData(proc.PipId))
                .Prop("GenericInfo", () => GenericObjectInfo(proc, preview: "").Build())
                .Build();
        }

        private string FileArtifactPreview(FileArtifact f)
        {
            var name = f.Path.GetName(PathTable).ToString(StringTable);
            var kind = f.IsSourceFile ? "source" : "output";
            return $"{name} [{kind}]";
        }

        private ObjectInfo FileArtifactInfo(FileArtifact f)
        {
            if (!f.IsValid)
            {
                return new ObjectInfo("Invalid");
            }

            return new ObjectInfoBuilder()
                .Preview(FileArtifactPreview(f))
                .Prop("Path", f.Path.ToString(PathTable))
                .Prop("Kind", f.IsSourceFile ? "source" : "output")
                .Prop("RewriteCount", f.RewriteCount)
                .Prop("FileContentInfo", () => Analyzer.TryGetFileContentInfo(f))
                .Prop("Producer", () => f.IsOutputFile ? Analyzer.GetPip(PipGraph.TryGetProducer(f)) : null)
                .Prop("Consumers", () => PipGraph.GetConsumingPips(f.Path))
                .Build();
        }

        private ObjectInfo DirectoryArtifactInfo(DirectoryArtifact d)
        {
            if (!d.IsValid)
            {
                return new ObjectInfo("Invalid");
            }

            var name = d.Path.GetName(PathTable).ToString(StringTable);
            var kind = d.IsSharedOpaque ? "shared opaque" : d.IsOutputDirectory() ? "exclusive opaque" : "source";

            return new ObjectInfoBuilder()
                .Preview($"{name} [{kind}]")
                .Prop("Path", d.Path.ToString(PathTable))
                .Prop("PartialSealId", d.PartialSealId)
                .Prop("Kind", kind)
                .Prop("Producer", () => d.IsOutputDirectory() ? Analyzer.GetPip(PipGraph.TryGetProducer(d)) : null)
                .Prop("Consumers", PipGraph.GetConsumingPips(d))
                .Prop("Members", () => d.IsOutputDirectory()
                    ? (object)Analyzer.GetDirMembers(d)
                    : d.PartialSealId > 0
                        ? PipGraph.ListSealedDirectoryContents(d).Select(f => f.Path)
                        : CollectionUtilities.EmptyArray<AbsolutePath>())
                .Build();
        }

        private ObjectInfo PipsInfo(PipsScope _)
        {
            return new ObjectInfoBuilder()
                .Prop("ByType", PipsByType)
                .Prop("ByStatus", PipsByStatus)
                .Prop("ByExecutionStart", () => PipsOrderedBy(exePerf => exePerf.ExecutionStart))
                .Prop("ByExecutionStop", () => PipsOrderedBy(exePerf => exePerf.ExecutionStop))
                .Build();
        }

        private PipReference[] PipsOrderedBy(Func<PipExecutionPerformance, object> selector)
        {
            return Analyzer.Pip2Perf
                .OrderBy(kvp => selector(kvp.Value))
                .Select(kvp => Analyzer.AsPipReference(kvp.Key))
                .ToArray();
        }

        private ObjectInfo GroupFiles(IEnumerable<FileArtifact> files)
        {
            return new ObjectInfoBuilder()
                .Prop("Source", () => GroupByFirstFileNameLetter(files.Where(f => f.IsSourceFile), f => f.Path))
                .Prop("Output", () => GroupByFirstFileNameLetter(files.Where(f => f.IsOutputFile), f => f.Path))
                .Build();
        }

        private ObjectInfo GroupDirs(IEnumerable<DirectoryArtifact> dirs)
        {
            return new ObjectInfoBuilder()
                .Prop("Source", () => GroupByFirstFileNameLetter(dirs.Where(d => !d.IsOutputDirectory()), d => d.Path))
                .Prop("ExclusiveOpaque", () => GroupByFirstFileNameLetter(dirs.Where(d => d.IsOutputDirectory() && !d.IsSharedOpaque), d => d.Path))
                .Prop("SharedOpaque", () => GroupByFirstFileNameLetter(dirs.Where(d => d.IsSharedOpaque), d => d.Path))
                .Build();
        }

        private ObjectInfo GroupByFirstFileNameLetter<T>(IEnumerable<T> elems, Func<T, AbsolutePath> toPath)
        {
            return new ObjectInfo(elems
                .GroupBy(t => "'" + toPath(t).GetName(PathTable).ToString(StringTable)[0].ToUpperInvariantFast() + "'")
                .OrderBy(grp => grp.Key)
                .Select(grp => new Property(
                    name: grp.Key, 
                    value: grp.OrderBy(t => toPath(t).GetName(PathTable).ToString(StringTable).ToUpperInvariant()).ToArray())));
        }

        private class PipsScope { }
        private class FilesScope { }
        private class AnalyzeCriticalPath { }
        private class AnalyzePath
        {
            internal AbsolutePath Path { get; }
            internal AnalyzePath(AbsolutePath path)
            {
                Path = path;
            }
        }
    }
}
