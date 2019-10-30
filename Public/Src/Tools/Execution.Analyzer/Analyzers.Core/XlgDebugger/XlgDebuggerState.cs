// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Execution.Analyzer.JPath;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.Pips;
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

            PipsByType = new ObjectInfo(Enum
                .GetValues(typeof(PipType))
                .Cast<PipType>()
                .Except(new[] { PipType.Max })
                .Select(pipType => new Property(pipType.ToString(), () => PipGraph.RetrievePipReferencesOfType(pipType).ToArray())));

            PipsByStatus = new ObjectInfo(Enum
                .GetValues(typeof(PipExecutionLevel))
                .Cast<PipExecutionLevel>()
                .Select(level => level.ToString())
                .Concat(new[] { ExeLevelNotCompleted })
                .Select(levelStr => new Property(levelStr, () => GetPipsForExecutionLevel(levelStr))));

            RootObject = new ObjectInfo(preview: "Root", properties: new[]
            {
                new Property("Pips",          () => PipGraph.RetrieveAllPips()),
                new Property("Files",         () => PipGraph.AllFiles),
                new Property("Directories",   () => PipGraph.AllSealDirectories),
                new Property("DirMembership", () => Analyzer.GetDirMembershipData()),
                //new Property("CriticalPath",  new AnalyzeCricialPath()),
                new Property("GroupedBy",     new ObjectInfo(new[]
                {
                    new Property("Pips",          new PipsScope()),
                    new Property("Files",         () => GroupFiles(PipGraph.AllFiles)),
                    new Property("Directories",   () => GroupDirs(PipGraph.AllSealDirectories))
                }))
            });

            RootEnv = new Env(
                parent: null,
                resolver: (obj) => 
                {
                    var result = Renderer.GetObjectInfo(context: this, obj);
                    return new ObjectInfo(
                        result.Preview,
                        properties: result.Properties
                            .Select(p => new Property(p.Name, value: Renderer.IsInvalid(p.Value) ? new object[0] : p.Value))
                            .ToArray());
                },
                vars: LibraryFunctions.All.ToDictionary(f => '$' + f.Name, f => Result.Scalar(f)),
                current: RootObject);

            Evaluator = new Evaluator(
                new Env(parent: RootEnv, resolver: RootEnv.Resolver, current: RootEnv.Current),
                Analyzer.EnableEvalCaching);
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

            var exprToEval = evaluateForCompletions ? $"({expr})[0]" : expr;
            var maybeResult = JPath.JPath.TryEval(Evaluator, exprToEval);
            if (maybeResult.Succeeded)
            {
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
                case AnalyzeCricialPath cp:        return AnalyzeCricialPathInfo();
                case PipId pipId:                  return renderer.GetObjectInfo(ctx, Analyzer.GetPip(pipId)).WithPreview(pipId.ToString());
                case PipReference pipRef:          return renderer.GetObjectInfo(ctx, Analyzer.GetPip(pipRef.PipId));
                case Process proc:                 return ProcessInfo(proc);
                case Pip pip:                      return PipInfo(pip);
                case FileArtifactWithAttributes f: return FileArtifactInfo(f.ToFileArtifact()); // return GenericObjectInfo(f, preview: FileArtifactPreview(f.ToFileArtifact()));
                case ProcessMonitoringData m:      return ProcessMonitoringInfo(m);
                case string s:                     return new ObjectInfo(s);
                default:
                    return null;
            }
        }

        private ObjectInfo PipInfo(Pip pip)
        {
            var props = ExtractObjectProperties(pip)
                .Concat(ExtractObjectFields(pip))
                .Concat(GetPipDownAndUpStreamProperties(pip));
            return new ObjectInfo(preview: PipPreview(pip), properties: props);
        }

        private ObjectInfo AnalyzeCricialPathInfo()
        {
            return GenericObjectInfo(Analyzer.CriticalPath, preview: "");
        }

        private ObjectInfo ProcessMonitoringInfo(ProcessMonitoringData m)
        {
            return GenericObjectInfo(m, excludeProperties: new[] { "PipId", "Metadata" });
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
            var pipExePerf = Analyzer.TryGetPipExePerf(proc.PipId);
            return new ObjectInfo(preview: PipPreview(proc), properties: new[]
            {
                new Property("PipId", proc.PipId.Value),
                new Property("PipType", proc.PipType),
                new Property("SemiStableHash", proc.SemiStableHash),
                new Property("Description", proc.GetDescription(PipGraph.Context)),
                new Property("ExecutionLevel", GetPipExecutionLevel(pipExePerf)),
                new Property("EXE", proc.Executable),
                new Property("CMD", Analyzer.RenderProcessArguments(proc)),
                new Property("Inputs", new ObjectInfo(properties: new[]
                {
                    new Property("Files", proc.Dependencies.ToArray()),
                    new Property("Directories", proc.DirectoryDependencies.ToArray())
                })),
                new Property("Outputs", new ObjectInfo(properties: new[]
                {
                    new Property("Files", proc.FileOutputs.ToArray()),
                    new Property("Directories", proc.DirectoryOutputs.ToArray())
                })),
                new Property("ExecutionPerformance", pipExePerf),
                new Property("MonitoringData", () => Analyzer.TryGetProcessMonitoringData(proc.PipId)),
                new Property("GenericInfo", () => GenericObjectInfo(proc, preview: ""))
            }.Concat(GetPipDownAndUpStreamProperties(proc)));
        }

        private IEnumerable<Property> GetPipDownAndUpStreamProperties(Pip pip)
        {
            return new[]
            {
                new Property("DownstreamPips", CachedGraph.DataflowGraph.GetOutgoingEdges(pip.PipId.ToNodeId()).Cast<Edge>().Select(e => Analyzer.GetPip(e.OtherNode.ToPipId()))),
                new Property("UpstreamPips", CachedGraph.DataflowGraph.GetIncomingEdges(pip.PipId.ToNodeId()).Cast<Edge>().Select(e => Analyzer.GetPip(e.OtherNode.ToPipId()))),
            };
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

            return new ObjectInfo(
                preview: FileArtifactPreview(f), 
                properties: new[]
                {
                    new Property("Path", f.Path.ToString(PathTable)),
                    new Property("Kind", f.IsSourceFile ? "source" : "output"),
                    new Property("RewriteCount", f.RewriteCount),
                    new Property("FileContentInfo", () => Analyzer.TryGetFileContentInfo(f)),
                    f.IsOutputFile ? new Property("Producer", () => Analyzer.GetPip(PipGraph.GetProducer(f))) : null,
                    new Property("Consumers", () => PipGraph.GetConsumingPips(f.Path))
                }
                .Where(p => p != null));
        }

        private ObjectInfo DirectoryArtifactInfo(DirectoryArtifact d)
        {
            if (!d.IsValid)
            {
                return new ObjectInfo("Invalid");
            }

            var name = d.Path.GetName(PathTable).ToString(StringTable);
            var kind = d.IsSharedOpaque ? "shared opaque" : d.IsOutputDirectory() ? "exclusive opaque" : "source";
            var members = d.IsOutputDirectory()
                ? Analyzer.GetDirMembers(d, PipGraph.GetProducer(d))
                : d.PartialSealId > 0
                    ? PipGraph.ListSealedDirectoryContents(d).Select(f => f.Path)
                    : CollectionUtilities.EmptyArray<AbsolutePath>();
            return new ObjectInfo(
                preview: $"{name} [{kind}]",
                properties: new[]
                {
                    new Property("Path", d.Path.ToString(PathTable)),
                    new Property("PartialSealId", d.PartialSealId),
                    new Property("Kind", kind),
                    d.IsOutputDirectory() ? new Property("Producer", () => Analyzer.GetPip(PipGraph.GetProducer(d))) : null,
                    new Property("Consumers", PipGraph.GetConsumingPips(d)),
                    new Property("Members", members)
                }
                .Where(p => p != null));
        }

        private ObjectInfo PipsInfo(PipsScope _)
        {
            return new ObjectInfo(new[]
            {
                new Property("ByType", PipsByType),
                new Property("ByStatus", PipsByStatus),
                new Property("ByExecutionStart", () => PipsOrderedBy(exePerf => exePerf.ExecutionStart)),
                new Property("ByExecutionStop", () => PipsOrderedBy(exePerf => exePerf.ExecutionStop))
            });
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
            return new ObjectInfo(properties: new[]
            {
                new Property("Source", () => GroupByFirstFileNameLetter(files.Where(f => f.IsSourceFile), f => f.Path)),
                new Property("Output", () => GroupByFirstFileNameLetter(files.Where(f => f.IsOutputFile), f => f.Path))
            });
        }

        private ObjectInfo GroupDirs(IEnumerable<DirectoryArtifact> dirs)
        {
            return new ObjectInfo(properties: new[]
            {
                new Property("Source",          () => GroupByFirstFileNameLetter(dirs.Where(d => !d.IsOutputDirectory()), d => d.Path)),
                new Property("ExclusiveOpaque", () => GroupByFirstFileNameLetter(dirs.Where(d => d.IsOutputDirectory() && !d.IsSharedOpaque), d => d.Path)),
                new Property("SharedOpaque",    () => GroupByFirstFileNameLetter(dirs.Where(d => d.IsSharedOpaque), d => d.Path))
            });
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
        private class AnalyzeCricialPath { }
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
