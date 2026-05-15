// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides graph introspection functionality.
    /// </summary>
    public sealed class AmbientGraph : AmbientDefinitionBase
    {
        /// <nodoc />
        public AmbientGraph(PrimitiveTypes knownTypes)
            : base("Graph", knownTypes)
        {
        }

        /// <nodoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("Graph"),
                new[]
                {
                    Function("getDirectDependencies", GetDirectDependencies, GetDirectDependenciesSignature),
                    Function("getDependencyClosure", GetDependencyClosure, GetDependencyClosureSignature),
                });
        }

        private CallSignature GetDirectDependenciesSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.ObjectType),
            optional: OptionalParameters(AmbientTypes.ObjectType),
            returnType: new ArrayType(AmbientTypes.ObjectType));

        private CallSignature GetDependencyClosureSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.ObjectType),
            optional: OptionalParameters(AmbientTypes.ObjectType),
            returnType: new ArrayType(AmbientTypes.ObjectType));

        private EvaluationResult GetDirectDependencies(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var obj = Args.AsObjectLiteral(args, 0);
            var processOutputs = GetProcessOutputs(obj, context);
            Process pip = GetProcessPip(context, processOutputs);
            var excludeSources = GetExcludeSources(args, 1, context);

            using (var pooledFiles = Pools.GetFileArtifactSet())
            using (var pooledDirs = Pools.DirectoryArtifactSetPool.GetInstance())
            {
                var seenFiles = pooledFiles.Instance;
                var seenDirectories = pooledDirs.Instance;
                var results = new List<EvaluationResult>();
                CollectProcessInputArtifacts(context.FrontEndHost.PipGraph, pip, results, seenFiles, seenDirectories, excludeSources);

                var entry = context.TopStack;
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(results.ToArray(), entry.InvocationLocation, entry.Path));
            }
        }

        private static Process GetProcessPip(Context context, ProcessOutputs processOutputs)
        {
            if (processOutputs == null || processOutputs.ProcessPipId == PipId.Invalid)
            {
                throw new InputValidationException(
                    "The provided argument must be a 'TransformerExecuteResult' representing a scheduled process pip.",
                    new ErrorContext(pos: 1));
            }

            var pip = context.FrontEndHost.PipGraph.GetPipFromPipId(processOutputs.ProcessPipId);
            if (pip is not Process processPip)
            {
                throw new InputValidationException(
                    "The provided argument does not correspond to a process pip. Only process pips are supported.",
                    new ErrorContext(pos: 1));
            }

            return processPip;
        }

        private EvaluationResult GetDependencyClosure(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var obj = Args.AsObjectLiteral(args, 0);
            var processOutputs = GetProcessOutputs(obj, context);
            var rootPip = GetProcessPip(context, processOutputs);
            var excludeSources = GetExcludeSources(args, 1, context);

            var pipGraph = context.FrontEndHost.PipGraph;

            // BFS over all transitive dependency pips, collecting inputs from each.
            // Since we use a deterministic retrieval of direct dependencies (sorted by pip id),
            // the final closure is deterministic as well.
            using (var pooledFiles = Pools.GetFileArtifactSet())
            using (var pooledDirs = Pools.DirectoryArtifactSetPool.GetInstance())
            using (var pooledQueue = PipPools.PipIdQueuePool.GetInstance())
            {
                var queue = pooledQueue.Instance;
                var allResults = new List<EvaluationResult>();
                var seenFiles = pooledFiles.Instance;
                var seenDirectories = pooledDirs.Instance;

                // Seed with the provided pip: getDependencyClosure returns inputs of the pip itself
                // plus inputs of all transitive dependencies
                using var pooledVisited = PipPools.PipIdSetPool.GetInstance();
                var visited = pooledVisited.Instance;
                visited.Add(rootPip.PipId);
                queue.Enqueue(rootPip.PipId);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var pip = pipGraph.GetPipFromPipId(current);

                    // Collect inputs from this pip
                    CollectPipInputs(pipGraph, pip, allResults, seenFiles, seenDirectories, excludeSources);

                    // Enqueue transitive dependencies
                    foreach (var depId in pipGraph.GetPipDependenciesSorted(current))
                    {
                        if (visited.Add(depId))
                        {
                            queue.Enqueue(depId);
                        }
                    }
                }

                var entry = context.TopStack;
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(allResults.ToArray(), entry.InvocationLocation, entry.Path));
            }
        }

        private static ProcessOutputs GetProcessOutputs(ObjectLiteral obj, Context context)
        {
            var nameId = StringId.Create(context.StringTable, Transformers.AmbientTransformerBase.ProcessOutputsSymbolName);
            var result = obj[nameId];
            return result.Value as ProcessOutputs;
        }

        private static bool GetExcludeSources(EvaluationStackFrame args, int position, Context context)
        {
            var optionsObj = Args.AsObjectLiteralOptional(args, position);
            if (optionsObj == null)
            {
                return false;
            }

            var excludeSourcesId = StringId.Create(context.StringTable, "excludeSources");
            var value = optionsObj[excludeSourcesId];
            if (value.IsUndefined)
            {
                return false;
            }

            return Converter.ExpectBool(value);
        }

        /// <summary>
        /// Collects input artifacts (files and static directories) from a process pip.
        /// </summary>
        private static void CollectProcessInputArtifacts(IMutablePipGraph pipGraph, Process processPip, List<EvaluationResult> results, HashSet<FileArtifact> seenFiles, HashSet<DirectoryArtifact> seenDirectories, bool excludeSources)
        {
            foreach (var fileArtifact in processPip.Dependencies)
            {
                if (excludeSources && fileArtifact.IsSourceFile)
                {
                    continue;
                }

                if (seenFiles.Add(fileArtifact))
                {
                    results.Add(EvaluationResult.Create(fileArtifact));
                }
            }

            foreach (var directoryArtifact in processPip.DirectoryDependencies)
            {
                if (excludeSources && !IsOutputDirectory(pipGraph, directoryArtifact))
                {
                    continue;
                }

                if (seenDirectories.Add(directoryArtifact))
                {
                    var staticDir = CreateStaticDirectory(pipGraph, directoryArtifact);
                    results.Add(EvaluationResult.Create(staticDir));
                }
            }
        }

        /// <summary>
        /// Collects input artifacts from any pip type, deduplicating against already-seen artifacts.
        /// </summary>
        private static void CollectPipInputs(IMutablePipGraph pipGraph, Pip pip, List<EvaluationResult> results, HashSet<FileArtifact> seenFiles, HashSet<DirectoryArtifact> seenDirectories, bool excludeSources)
        {
            switch (pip)
            {
                case Process processPip:
                    CollectProcessInputArtifacts(pipGraph, processPip, results, seenFiles, seenDirectories, excludeSources);
                    break;

                case CopyFile copyFile:
                    if (!excludeSources || copyFile.Source.IsOutputFile)
                    {
                        if (seenFiles.Add(copyFile.Source))
                        {
                            results.Add(EvaluationResult.Create(copyFile.Source));
                        }
                    }
                    break;

                case SealDirectory sealDirectory:
                    foreach (var fileArtifact in sealDirectory.Contents)
                    {
                        if (excludeSources && fileArtifact.IsSourceFile)
                        {
                            continue;
                        }

                        if (seenFiles.Add(fileArtifact))
                        {
                            results.Add(EvaluationResult.Create(fileArtifact));
                        }
                    }
                    foreach (var directoryArtifact in sealDirectory.OutputDirectoryContents)
                    {
                        if (excludeSources && !IsOutputDirectory(pipGraph, directoryArtifact))
                        {
                            continue;
                        }

                        if (seenDirectories.Add(directoryArtifact))
                        {
                            var staticDir = CreateStaticDirectory(pipGraph, directoryArtifact);
                            results.Add(EvaluationResult.Create(staticDir));
                        }
                    }
                    break;

                case IpcPip ipcPip:
                    foreach (var fileArtifact in ipcPip.FileDependencies)
                    {
                        if (excludeSources && fileArtifact.IsSourceFile)
                        {
                            continue;
                        }

                        if (seenFiles.Add(fileArtifact))
                        {
                            results.Add(EvaluationResult.Create(fileArtifact));
                        }
                    }
                    foreach (var directoryArtifact in ipcPip.DirectoryDependencies)
                    {
                        if (excludeSources && !IsOutputDirectory(pipGraph, directoryArtifact))
                        {
                            continue;
                        }

                        if (seenDirectories.Add(directoryArtifact))
                        {
                            var staticDir = CreateStaticDirectory(pipGraph, directoryArtifact);
                            results.Add(EvaluationResult.Create(staticDir));
                        }
                    }
                    break;

                // WriteFile has no file inputs
                // Meta-pips (ModulePip, SpecFilePip, ValuePip) have no inputs
                // HashSourceFile pips are not part of the static dependencies
            }
        }

        /// <summary>
        /// Returns true if the directory artifact is an output directory (opaque or shared opaque).
        /// </summary>
        private static bool IsOutputDirectory(IMutablePipGraph pipGraph, DirectoryArtifact directoryArtifact)
        {
            if (pipGraph.TryGetSealDirectoryKind(directoryArtifact, out var kind))
            {
                return kind.IsDynamicKind();
            }

            return false;
        }

        /// <summary>
        /// Creates a StaticDirectory from a DirectoryArtifact by looking up its seal kind in the pip graph.
        /// </summary>
        private static StaticDirectory CreateStaticDirectory(IMutablePipGraph pipGraph, DirectoryArtifact directoryArtifact)
        {
            if (pipGraph.TryGetSealDirectoryKind(directoryArtifact, out var kind))
            {
                switch (kind)
                {
                    case SealDirectoryKind.Full:
                    case SealDirectoryKind.Partial:
                        // Full and Partial seals have statically known content
                        var content = pipGraph.ListSealedDirectoryContents(directoryArtifact);
                        return new StaticDirectory(directoryArtifact, kind, content.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));
                    case SealDirectoryKind.Opaque:
                    case SealDirectoryKind.SharedOpaque:
                    case SealDirectoryKind.SourceAllDirectories:
                    case SealDirectoryKind.SourceTopDirectoryOnly:
                        // These have no statically known content (determined at runtime), so we can avoid hydrating the pip
                        return new StaticDirectory(directoryArtifact, kind, PipConstructionHelper.EmptyStaticDirContents);
                    default:
                        Contract.Assert(false, $"Unexpected SealDirectoryKind: {kind}");
                        return null;
                }
            }

            Contract.Assert(false, "The directory artifact should have been added to the graph already");
            return null;
        }
    }
}
