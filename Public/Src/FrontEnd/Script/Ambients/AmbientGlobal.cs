// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for global functions.
    /// </summary>
    public sealed class AmbientGlobal : AmbientDefinitionBase
    {
        private readonly ConcurrentDictionary<AbsolutePath, bool> m_alreadyTrackedDirectories = new ConcurrentDictionary<AbsolutePath, bool>();
        private readonly ConcurrentDictionary<EnumeratePathKey, EvaluationResult[]> m_alreadyEnumeratedPaths = new ConcurrentDictionary<EnumeratePathKey, EvaluationResult[]>();

        /// <nodoc />
        public AmbientGlobal(PrimitiveTypes knownTypes)
            : base(null, knownTypes)
        {
        }

        /// <inheritdoc />
        protected override void Register(GlobalModuleLiteral globalModuleLiteral)
        {
            // Need to override Register method because global functions are a bit different from namespace level functions:
            // they should be registered directly in the global module, but not in the nested one.
            RegisterFunctionDefinitions(globalModuleLiteral, GetGlobalFunctionDefinitions());
        }

        private List<NamespaceFunctionDefinition> GetGlobalFunctionDefinitions()
        {
            var globRecursivelyBinding = ModuleBinding.CreateFun(Symbol(Constants.Names.GlobRFunction), GlobRecursively, GlobSignature, StringTable);

            return new List<NamespaceFunctionDefinition>
            {
                Function(Constants.Names.GlobFunction, Glob, GlobSignature),
                Function(Constants.Names.GlobRFunction, globRecursivelyBinding),
                Function(Constants.Names.GlobRecursivelyFunction, globRecursivelyBinding),
                Function(Constants.Names.GlobFoldersFunction, GlobFolders, GlobFoldersSignature),
                Function("addIf", AddIf, AddIfSignature),
                Function("addIfLazy", AddIfLazy, AddIfLazySignature),
                Function("sign", Sign, SignSignature)
            };
        }

        private EvaluationResult AddIf(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var condition = Args.AsBool(args, 0);
            
            if (!condition)
            {
                // Return empty Array
                var entry = context.TopStack;
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(new EvaluationResult[0], entry.InvocationLocation, entry.Path));
            }

            var items = Args.AsArrayLiteral(args, 1);
            return EvaluationResult.Create(items);
        }

        private EvaluationResult AddIfLazy(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var condition = Args.AsBool(args, 0);

            if (!condition)
            {
                // Return empty Array
                var entry = context.TopStack;
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(new EvaluationResult[0], entry.InvocationLocation, entry.Path));
            }

            // Call the function passed in and return that result
            var closure = Args.AsClosure(args, 1);
            var result = context.InvokeClosure(closure, closure.Frame);

            if (result.IsErrorValue)
            {
                return EvaluationResult.Error;
            }

            return result;
        }

        private EvaluationResult Glob(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return GlobImpl(context, args, SearchOption.TopDirectoryOnly);
        }

        private EvaluationResult GlobRecursively(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return GlobImpl(context, args, SearchOption.AllDirectories);
        }

        private EvaluationResult GlobFolders(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var dirPath = Args.AsPath(args, 0, false);
            var pathTable = context.FrontEndContext.PathTable;
            var path = dirPath.ToString(pathTable);

            // TODO:ST: add different set of function that will distinguish optional from required arguments!
            string pattern = Args.AsStringOptional(args, 1) ?? "*";
            bool recursive = Args.AsBoolOptional(args, 2);

            var resultPaths = EnumerateFilesOrDirectories(context, path, pattern, directoriesToSkipRecursively: 0, isRecursive: recursive, enumerateDirectory: true);

            var entry = context.TopStack;

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(resultPaths, entry.InvocationLocation, entry.Path));
        }

        private EvaluationResult GlobImpl(Context context, EvaluationStackFrame args, SearchOption searchOption)
        {
            AbsolutePath dirPath = Args.AsPath(args, 0, false);
            var pathTable = context.FrontEndContext.PathTable;
            var path = dirPath.ToString(pathTable);

            string pattern = Args.AsStringOptional(args, 1) ?? "*";

            uint directoriesToSkipRecursively = 0;
            if (pattern.Length > 2)
            {
                if (pattern[0] == '*' &&
                    pattern[1] == '/' || pattern[1] == '\\')
                {
                    pattern = pattern.Substring(2);
                    directoriesToSkipRecursively = 1;
                }
            }

            var sw = Stopwatch.StartNew();

            var resultPaths = EnumerateFilesOrDirectories(
                context,
                path,
                pattern,
                directoriesToSkipRecursively,
                searchOption == SearchOption.AllDirectories,         
                enumerateDirectory: false);

            sw.Stop();
            Interlocked.Add(ref context.Statistics.TotalGlobTimeInTicks, sw.Elapsed.Ticks);

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(resultPaths, entry.InvocationLocation, entry.Path));
        }

        private EvaluationResult[] EnumerateFilesOrDirectories(
            Context context,
            string directoryPath,
            string searchPattern,
            bool isRecursive,
            bool enumerateDirectory)
        {
            var fileSystem = context.FrontEndContext.FileSystem;
            if (enumerateDirectory)
            {
                return fileSystem
                    .EnumerateDirectories(AbsolutePath.Create(context.PathTable, directoryPath), searchPattern, isRecursive)
                    .Select(ap => EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(ap)))
                    .ToArray();
            }

            return fileSystem
                .EnumerateFiles(AbsolutePath.Create(context.PathTable, directoryPath), searchPattern, isRecursive)
                .Select(ap => EvaluationResult.Create(FileArtifact.CreateSourceFile(ap)))
                .ToArray();
        }


        private EvaluationResult[] EnumerateFilesOrDirectories(
            Context context,
            string directoryPath,
            string searchPattern,
            uint directoriesToSkipRecursively,
            bool isRecursive,
            bool enumerateDirectory)
        { 
            var enumeratePathKey = new EnumeratePathKey(directoryPath, searchPattern, isRecursive, enumerateDirectory);

            if (m_alreadyEnumeratedPaths.TryGetValue(enumeratePathKey, out var filesOrDirectories))
            {
                return filesOrDirectories;
            }

            var accumulators = new DirectoryEntriesAccumulator(
                context,
                AbsolutePath.Create(context.PathTable, directoryPath),
                m_alreadyTrackedDirectories);

            var result = context.FrontEndContext.FileSystem.EnumerateDirectoryEntries(
                directoryPath,
                enumerateDirectory: enumerateDirectory,
                pattern: searchPattern,
                directoriesToSkipRecursively: directoriesToSkipRecursively,
                recursive: isRecursive,
                accumulators: accumulators);

            // If the result indicates that the enumeration succeeded or the directory does not exist, then the result is considered success.
            // In particular, if the globed directory does not exist, then we want to return the empty file, and track for the anti-dependency.
            if (
                !(result.Status == EnumerateDirectoryStatus.Success ||
                  result.Status == EnumerateDirectoryStatus.SearchDirectoryNotFound))
            {
                throw new DirectoryOperationException(result.Directory, result.CreateExceptionForError());
            }

            accumulators.Done();

            filesOrDirectories = accumulators.GetAndTrackArtifacts(asDirectories: enumerateDirectory);
            m_alreadyEnumeratedPaths.TryAdd(enumeratePathKey, filesOrDirectories);

            return filesOrDirectories;
        }

        /// <summary>
        /// Class for the key of file-system enumeration memoization.
        /// </summary>
        private sealed class EnumeratePathKey
        {
            /// <summary>
            /// Enumerated path.
            /// </summary>
            private string Path { get; }

            /// <summary>
            /// Search pattern.
            /// </summary>
            private string SearchPattern { get; }

            /// <summary>
            /// Flag indicating if the enumeration recursive to all directories or only top level one.
            /// </summary>
            private bool IsRecursive { get; }

            /// <summary>
            /// Flag indicating if the enumeration is only for directory.
            /// </summary>
            private bool EnumerateDirectory { get; }

            private readonly int m_hashCode;

            /// <nodoc />
            public EnumeratePathKey(string path, string searchPattern, bool isRecursive, bool enumerateDirectory)
            {
                Path = path;
                SearchPattern = searchPattern;
                IsRecursive = isRecursive;
                EnumerateDirectory = enumerateDirectory;

                m_hashCode = HashCodeHelper.Combine(Path.GetHashCode(), SearchPattern.GetHashCode(), IsRecursive ? 1 : 0, EnumerateDirectory ? 1 : 0);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(this, obj))
                {
                    return true;
                }

                var other = obj as EnumeratePathKey;

                return other != null && Equals(other);
            }

            /// <nodoc />
            private bool Equals(EnumeratePathKey other)
            {
                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                // Path and search pattern comparisons are case-sensitive for the following reasons:
                // - The path should come from AbsolutePath, and PathTable gives a consisten path in terms of casing.
                // - This is an optimization for generated specs where paths and search patterns have consistent casing.
                // - As an optimization, it is OK to miss just because difference in casing.
                // - Comparison will work for X-platform.
                return other != null
                    && string.Equals(Path, other.Path)
                    && string.Equals(SearchPattern, other.SearchPattern)
                    && IsRecursive == other.IsRecursive
                    && EnumerateDirectory == other.EnumerateDirectory;
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return m_hashCode;
            }
        }

        /// <inheritdoc />
        private sealed class DirectoryEntriesAccumulator : IDirectoryEntriesAccumulator
        {
            public sealed class DirectoryEntryAccumulator : IDirectoryEntryAccumulator
            {
                /// <inheritdoc />
                public AbsolutePath DirectoryPath { get; }

                /// <summary>
                /// Used to know the index of files wrt. all files in the accumulator collection.
                /// </summary>
                public int Index { private get; set; }

                /// <inheritdoc />
                public bool Succeeded { get; set; } = true;

                /// <summary>
                /// Number of files.
                /// </summary>
                public int FileCount => m_files.Count;

                private readonly List<string> m_files = new List<string>();
                private readonly List<(string fileName, FileAttributes fileAttributes)> m_trackedFiles;

                private readonly bool m_isAlreadyTracked;

                /// <nodoc />
                public DirectoryEntryAccumulator(AbsolutePath directoryPath, bool isAlreadyTracked = false)
                {
                    DirectoryPath = directoryPath;
                    m_isAlreadyTracked = isAlreadyTracked;
                    m_trackedFiles = isAlreadyTracked ? null : new List<(string fileName, FileAttributes fileAttributes)>();
                }

                /// <inheritdoc />
                public void AddFile(string fileName)
                {
                    m_files.Add(fileName);
                }

                /// <inheritdoc />
                public void AddTrackFile(string fileName, FileAttributes fileAttributes)
                {
                    m_trackedFiles?.Add((fileName, fileAttributes));
                }

                /// <summary>
                /// Dumps file members into an array of file artifacts.
                /// </summary>
                public void GetArtifacts(Context context, EvaluationResult[] files, bool asDirectory)
                {
                    for (int i = 0; i < m_files.Count; ++i)
                    {
                        var path = DirectoryPath.Combine(context.PathTable, m_files[i]);
                        files[Index + i] = asDirectory
                                ? EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(path))
                                : EvaluationResult.Create(FileArtifact.CreateSourceFile(path));
                    }
                }

                /// <summary>
                /// Tracks directory.
                /// </summary>
                public void TrackDirectory(Context context)
                {
                    if (m_isAlreadyTracked)
                    {
                        return;
                    }

                    var path = DirectoryPath.ToString(context.PathTable);

                    // If enumeration failed, then let the tracker decide how to track the directory.
                    var trackedFiles = Succeeded ? m_trackedFiles : null;

                    if (context.IsGlobalConfigFile)
                    {
                        // We distinguish the evaluation of the primary config file and non-config files
                        // because when the primary config file is evaluated, the engine has not been set up
                        // and thus the input tracker, which is part of the engine, may not be ready for use.
                        context.FrontEndHost.EnumeratedDirectoriesInConfig.TryAdd(path, trackedFiles);
                    }
                    else
                    {
                        context.FrontEndHost.Engine.TrackDirectory(path, trackedFiles);
                    }
                }
            }

            /// <inheritdoc />
            public IDirectoryEntryAccumulator Current => m_accumulators.Count == 0 ? null : m_accumulators[m_accumulators.Count - 1];

            private readonly List<DirectoryEntryAccumulator> m_accumulators = new List<DirectoryEntryAccumulator>();
            private readonly Context m_context;
            private bool m_done;
            private int m_fileCounts;
            private readonly ConcurrentDictionary<AbsolutePath, bool> m_alreadyTrackedDirectories;
            private static readonly EvaluationResult[] s_emptyArray = CollectionUtilities.EmptyArray<EvaluationResult>();

            /// <nodoc />
            public DirectoryEntriesAccumulator(Context context, AbsolutePath directoryPath, ConcurrentDictionary<AbsolutePath, bool> alreadyTrackedDirectories)
            {
                Contract.Requires(context != null);
                Contract.Requires(directoryPath.IsValid);

                m_context = context;
                m_alreadyTrackedDirectories = alreadyTrackedDirectories;
                m_accumulators.Add(new DirectoryEntryAccumulator(directoryPath, IsAlreadyTracked(directoryPath, m_alreadyTrackedDirectories)));
            }

            /// <inheritdoc />
            public void AddNew(IDirectoryEntryAccumulator parent, string directoryName)
            {
                Contract.Assume(!m_done);

                var directoryPath = parent.DirectoryPath.Combine(m_context.PathTable, directoryName);
                m_accumulators.Add(new DirectoryEntryAccumulator(directoryPath, IsAlreadyTracked(directoryPath, m_alreadyTrackedDirectories)));
            }

            private static bool IsAlreadyTracked(AbsolutePath directoryPath, ConcurrentDictionary<AbsolutePath, bool> tracker)
            {
                return !tracker.TryAdd(directoryPath, true);
            }

            /// <inheritdoc />
            public void Done()
            {
                m_done = true;
                foreach (var accumulator in m_accumulators)
                {
                    accumulator.Index = m_fileCounts;
                    m_fileCounts += accumulator.FileCount;
                }
            }

            /// <inheritdoc />
            public EvaluationResult[] GetAndTrackArtifacts(bool asDirectories = false)
            {
                Contract.Assume(m_done);

                var results = m_fileCounts == 0 ? s_emptyArray : new EvaluationResult[m_fileCounts];
                Parallel.Invoke(TrackArtifacts, () => GetArtifacts(results, asDirectories));

                return results;
            }

            private void TrackArtifacts()
            {
                Parallel.ForEach(m_accumulators, accumulator => accumulator.TrackDirectory(m_context));
            }

            private void GetArtifacts(EvaluationResult[] results, bool asDirectory)
            {
                Parallel.ForEach(
                    m_accumulators,
                    accumulator => accumulator.GetArtifacts(m_context, results, asDirectory));
            }
        }

        private static EvaluationResult Sign(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            if (Args.IsUndefined(args, 0))
            {
                return EvaluationResult.Undefined;
            }

            return EvaluationResult.Create(Args.AsBool(args, 0) ? "+" : "-");
        }

        private CallSignature AddIfSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.BooleanType),
            restParameterType: PrimitiveType.AnyType,
            returnType: new ArrayType(PrimitiveType.AnyType));

        private CallSignature AddIfLazySignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.BooleanType, AmbientTypes.ClosureType),
            returnType: new ArrayType(PrimitiveType.AnyType));

        private CallSignature GlobFoldersSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.DirectoryType),
            optional: OptionalParameters(PrimitiveType.StringType, PrimitiveType.BooleanType),
            returnType: new ArrayType(AmbientTypes.DirectoryType));

        private CallSignature GlobSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.DirectoryType),
            optional: OptionalParameters(PrimitiveType.StringType),
            returnType: new ArrayType(AmbientTypes.FileType));

        private static CallSignature SignSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.BooleanType),
            returnType: PrimitiveType.StringType);
    }
}
