// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using DirectoryMembershipFingerprinterRule = BuildXL.Scheduler.DirectoryMembershipFingerprinterRule;
using SortedFileArtifacts =
    BuildXL.Utilities.Collections.SortedReadOnlyArray<BuildXL.Utilities.FileArtifact, BuildXL.Utilities.OrdinalFileArtifactComparer>;

namespace Test.BuildXL.Scheduler
{
    public class ObservedInputProcessorTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ObservedInputProcessorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static DirectoryArtifact[] AllowAccessTo(params DirectoryArtifact[] d)
        {
            return d;
        }

        private static ContentHash CreateFakeContentHash(byte seed)
        {
            byte[] b = new byte[ContentHashingUtilities.HashInfo.ByteLength];
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = seed;
            }

            return ContentHashingUtilities.CreateFrom(b);
        }

        /// <summary>
        /// Creates a dummy <see cref="Process" />. This is overkill, but an <see cref="ObservedInputProcessor" /> needs one presently
        /// (mostly for logging).
        /// </summary>
        private static Process CreateDummyProcess(
            PipExecutionContext context,
            ReadOnlyArray<DirectoryArtifact> directoryDependencies,
            AbsolutePath[] fileDependencies)
        {
            var exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, X("/X/exe")));
            List<FileArtifact> dependencies = new List<FileArtifact> { exe };
            dependencies.AddRange(fileDependencies.Select(FileArtifact.CreateSourceFile));

            var p = new Process(
                directoryDependencies: directoryDependencies,
                executable: exe,
                workingDirectory: AbsolutePath.Create(context.PathTable, X("/X")),
                arguments: new PipDataBuilder(context.StringTable).ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: AbsolutePath.Create(context.PathTable, X("/X/std")),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.From(dependencies),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty)
            { PipId = new PipId(123) };

            return p;
        }

        public enum Expectation
        {
            AccessCheckFailure,
            FileContentRead,
            AbsentPathProbe,
            DirectoryEnumeration,
            ExistingDirectoryProbe,
        }

        private class TestObservation
        {
            public Expectation Expectation { get; private set; }
            public readonly ContentHash ExpectedHash;
            public readonly AbsolutePath Path;
            public bool IsSearchPathEnumeration;
            public bool IsDirectoryEnumeration;
            public bool IsNestedUnderTopOnlySourceSealedDirectory;
            public bool IsDirectoryLocation;
            public string EnumeratePatternRegex;

            private TestObservation(AbsolutePath path, Expectation expectation, ContentHash hash, bool isDirectoryEnumeration = false, bool isDirectoryLocation = false)
            {
                Path = path;
                Expectation = expectation;
                ExpectedHash = hash;
                IsDirectoryEnumeration = isDirectoryEnumeration;
                IsDirectoryLocation = isDirectoryLocation;
            }

            public TestObservation ExpectAccessCheckFailure()
            {
                Expectation = Expectation.AccessCheckFailure;
                return this;
            }

            public static TestObservation ExpectAccessCheckFailure(AbsolutePath path)
            {
                return new TestObservation(path, Expectation.AccessCheckFailure, ContentHashingUtilities.ZeroHash);
            }

            public static TestObservation ExpectAccessCheckFailure(AbsolutePath path, bool isNestedUnderTopOnlySourceSealedDirectory = false)
            {
                return new TestObservation(path, Expectation.AccessCheckFailure, ContentHashingUtilities.ZeroHash)
                {
                    IsNestedUnderTopOnlySourceSealedDirectory = isNestedUnderTopOnlySourceSealedDirectory
                };
            }

            public static TestObservation ExpectAbsenceProbe(AbsolutePath path, bool isDirectoryLocation = false)
            {
                return new TestObservation(path, Expectation.AbsentPathProbe, ContentHashingUtilities.ZeroHash, false, isDirectoryLocation);
            }

            public static TestObservation ExpectEmptyDirectoryEnumerationAbsenceProbe(AbsolutePath path)
            {
                return new TestObservation(path, Expectation.AbsentPathProbe, ContentHashingUtilities.ZeroHash, true);
            }

            public static TestObservation ExpectFileContentRead(AbsolutePath path, ContentHash hash)
            {
                return new TestObservation(path, Expectation.FileContentRead, hash, false);
            }

            public static TestObservation ExpectDirectoryEnumeration(AbsolutePath path, ContentHash hash, string enumeratePatternRegex = null)
            {
                // TODO: Right now we always return DirectoryFingerprint.Zero in the harness environment.

                return new TestObservation(path, Expectation.DirectoryEnumeration, hash, true)
                {
                    EnumeratePatternRegex = enumeratePatternRegex
                };
            }

            public static TestObservation ExpectExistingDirectoryProbe(AbsolutePath path, ContentHash hash)
            {
                // TODO: Right now we always return DirectoryFingerprint.Zero in the harness environment.
                return new TestObservation(path, Expectation.ExistingDirectoryProbe, hash);
            }

            public ObservedInputType GetObservedInputType()
            {
                Contract.Requires(Expectation != Expectation.AccessCheckFailure);

                switch (Expectation)
                {
                    case Expectation.FileContentRead:
                        return ObservedInputType.FileContentRead;
                    case Expectation.AbsentPathProbe:
                        return ObservedInputType.AbsentPathProbe;
                    case Expectation.DirectoryEnumeration:
                        return ObservedInputType.DirectoryEnumeration;
                    case Expectation.ExistingDirectoryProbe:
                        return ObservedInputType.ExistingDirectoryProbe;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private sealed class TestObservationComparer : IComparer<TestObservation>
        {
            private readonly PathTable.ExpandedAbsolutePathComparer m_comparer;

            public TestObservationComparer(PathTable.ExpandedAbsolutePathComparer comparer)
            {
                m_comparer = comparer;
            }

            public int Compare(TestObservation x, TestObservation y)
            {
                return m_comparer.Compare(x.Path, y.Path);
            }
        }

        /// <summary>
        /// Trivial processing target which checks expected <see cref="TestObservation" />s against those observed inputs produced by processing
        /// their respective paths.
        /// If a different input is proposed than expected (or if an access check should have / should not have occurred), the test should fail.
        /// </summary>
        private readonly struct AssertingTarget : IObservedInputProcessingTarget<TestObservation>
        {
            public readonly PipExecutionContext Context;
            private readonly Harness m_harness;
            private readonly Action<TestObservation, bool> m_onObservationHandled;

            public AssertingTarget(Harness harness, PipExecutionContext context, Action<TestObservation, bool> onHandled)
            {
                m_harness = harness;
                Context = context;
                m_onObservationHandled = onHandled;
                Description = null;
            }

            public string Description { get; }

            public AbsolutePath GetPathOfObservation(TestObservation observation)
            {
                return observation.Path;
            }

            public ObservationFlags GetObservationFlags(TestObservation observation)
            {
                var str = observation.Path.ToString(Context.PathTable);
                if (observation.IsDirectoryEnumeration)
                {
                    return ObservationFlags.Enumeration;
                }

                if (observation.IsDirectoryLocation)
                {
                    return ObservationFlags.DirectoryLocation;
                }

                return ObservationFlags.None;
            }

            public ObservedInputAccessCheckFailureAction OnAccessCheckFailure(TestObservation observation, bool fromTopLevelDirectory)
            {
                if (observation.Expectation != Expectation.AccessCheckFailure)
                {
                    XAssert.Fail("Unexpected access check failure for path {0}", observation.Path.ToString(Context.PathTable));
                }

                m_onObservationHandled(observation, fromTopLevelDirectory);
                return ObservedInputAccessCheckFailureAction.Fail;
            }

            public void CheckProposedObservedInput(TestObservation observation, ObservedInput proposedObservedInput)
            {
                switch (observation.Expectation)
                {
                    case Expectation.AccessCheckFailure:
                        XAssert.Fail("Expected access check failure for path {0}", observation.Path.ToString(Context.PathTable));
                        break;
                    case Expectation.AbsentPathProbe:
                    case Expectation.DirectoryEnumeration:
                    case Expectation.FileContentRead:
                    case Expectation.ExistingDirectoryProbe:
                        XAssert.AreEqual(
                            observation.GetObservedInputType(),
                            proposedObservedInput.Type,
                            "Wrong type for path {0}",
                            observation.Path.ToString(Context.PathTable));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (proposedObservedInput.Type == ObservedInputType.FileContentRead ||
                    proposedObservedInput.Type == ObservedInputType.DirectoryEnumeration ||
                    proposedObservedInput.Type == ObservedInputType.ExistingDirectoryProbe)
                {
                    XAssert.AreEqual(
                        observation.ExpectedHash,
                        proposedObservedInput.Hash,
                        "Wrong hash for path {0}",
                        observation.Path.ToString(Context.PathTable));
                }

                m_onObservationHandled(observation, false);
                return;
            }

            public bool IsSearchPathEnumeration(TestObservation directoryEnumeration)
            {
                return directoryEnumeration.IsSearchPathEnumeration;
            }

            public string GetEnumeratePatternRegex(TestObservation directoryEnumeration)
            {
                return directoryEnumeration.EnumeratePatternRegex;
            }

            public void ReportUnexpectedAccess(TestObservation observation, ObservedInputType observedInputType)
            {
                m_harness.ReportedUnexpectedAccesses[observation.Path] = observedInputType;
            }

            public bool IsReportableUnexpectedAccess(AbsolutePath path)
            {
                return true;
            }
        }

        private sealed class Harness
        {
            public readonly PipExecutionContext Context;
            private readonly HashSet<AbsolutePath> m_directories = new HashSet<AbsolutePath>();
            private readonly List<DirectoryArtifact> m_directoryDependencies = new List<DirectoryArtifact>();

            private readonly Dictionary<AbsolutePath, DirectoryFingerprint> m_enumerableDirectories =
                new Dictionary<AbsolutePath, DirectoryFingerprint>();

            private readonly Dictionary<AbsolutePath, FileContentInfo> m_fileContent = new Dictionary<AbsolutePath, FileContentInfo>();

            private readonly List<AbsolutePath> m_fileDependencies = new List<AbsolutePath>();

            public readonly Dictionary<AbsolutePath, ObservedInputType> ReportedUnexpectedAccesses = new Dictionary<AbsolutePath, ObservedInputType>();

            private readonly List<TestObservation> m_observations = new List<TestObservation>();

            private readonly Dictionary<DirectoryArtifact, SortedFileArtifacts> m_sealedDirectories =
                new Dictionary<DirectoryArtifact, SortedFileArtifacts>();

            private readonly Dictionary<AbsolutePath, FileContentInfo> m_sealedPathContentHashes = new Dictionary<AbsolutePath, FileContentInfo>();
            private readonly Dictionary<AbsolutePath, bool> m_sealedSourceDirectories = new Dictionary<AbsolutePath, bool>();

            private readonly HarnessVFS m_virtualFileSystem;

            public readonly Dictionary<AbsolutePath, DirectoryMembershipFilter> TrackedDirectoryFilters
                = new Dictionary<AbsolutePath, DirectoryMembershipFilter>();

            private uint m_nextDirectorySealId;

            public Harness()
            {
                Context = BuildXLContext.CreateInstanceForTesting();
                m_virtualFileSystem = new HarnessVFS(this);
            }

            public void AddFileDependency(string path)
            {
                m_fileDependencies.Add(Path(path));
            }

            public TestObservation AddReadObservation(string path, ContentHash? hash = null)
            {
                var absPath = Path(path);
                ContentHash expectedHash;
                FileContentInfo fileContentInfo;
                if (hash != null)
                {
                    expectedHash = hash.Value;
                }
                else if (m_fileContent.TryGetValue(absPath, out fileContentInfo))
                {
                    expectedHash = fileContentInfo.Hash;
                }
                else
                {
                    expectedHash = CreateFakeContentHash((byte)(m_observations.Count + 1));
                    AddFile(absPath, expectedHash);
                }

                var observation = TestObservation.ExpectFileContentRead(absPath, expectedHash);
                m_observations.Add(observation);
                return observation;
            }

            public TestObservation AddAbsentProbeObservation(string path)
            {
                var observation = TestObservation.ExpectAbsenceProbe(Path(path));
                AddAbsentPath(observation.Path);
                m_observations.Add(observation);
                return observation;
            }

            public TestObservation AddDirectoryEnumeration(string path, bool isSearchPath = false, string[] members = null, bool addDependency = true, string[] enumeratePatterns = null)
            {
                members = members ?? new[] { "test.txt" };
                var root = Path(path);
                var contents = members
                    .Select(m => root.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, m)))
                    .ToArray();

                foreach (var file in contents)
                {
                    AddFile(file, CreateFakeContentHash(1));
                }

                var directory = SealDirectory(root, contents);

                if (addDependency)
                {
                    m_directoryDependencies.Add(directory);
                }

                var observation = TestObservation.ExpectDirectoryEnumeration(Path(path), CreateFakeContentHash(2));
                observation.EnumeratePatternRegex = RegexDirectoryMembershipFilter.ConvertWildcardsToRegex(enumeratePatterns ?? new string[] { });
                observation.IsSearchPathEnumeration = isSearchPath;
                AddEnumerableDirectory(observation.Path, new DirectoryFingerprint(observation.ExpectedHash));
                m_observations.Add(observation);
                return observation;
            }

            public TestObservation AddEmptyDirectoryEnumeration(string path)
            {
                var observation = TestObservation.ExpectEmptyDirectoryEnumerationAbsenceProbe(Path(path));
                AddEnumerableDirectory(observation.Path, new DirectoryFingerprint(observation.ExpectedHash));
                m_observations.Add(observation);
                return observation;
            }

            public TestObservation AddExistingDirectoryProbe(string path, bool isSearchPath = false, string[] members = null)
            {
                members = members ?? new[] { "test.txt" };
                var root = Path(path);
                var contents = members
                    .Select(m => root.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, m)))
                    .ToArray();

                foreach (var file in contents)
                {
                    AddFile(file, CreateFakeContentHash(1));
                }

                var directory = SealDirectory(root, contents);
                m_directoryDependencies.Add(directory);

                var observation = TestObservation.ExpectExistingDirectoryProbe(Path(path), CreateFakeContentHash(0));
                observation.IsSearchPathEnumeration = isSearchPath;
                AddEnumerableDirectory(observation.Path, new DirectoryFingerprint(observation.ExpectedHash));
                m_observations.Add(observation);
                return observation;
            }

            public ObservedInputProcessingResult Process(ObservedInputProcessingStatus expectedStatus, bool allowCollapsingObservations)
            {
                return Process(
                    expectedStatus,
                    m_directoryDependencies.ToArray(),
                    allowCollapsingObservations,
                    m_observations.ToArray());
            }

            public ObservedInputProcessingResult Process(
                ObservedInputProcessingStatus expectedStatus,
                DirectoryArtifact[] directoryDependencies,
                bool allowCollapsingObservations,
                params TestObservation[] observations)
            {
                var loggingContext = new LoggingContext("Test");

                var expectedObservations = observations

                    // Access check failures are first, otherwise sorted by path
                    .OrderBy(o => o.Expectation == Expectation.AccessCheckFailure ? 0 : 1)
                    .ThenBy(o => o.Path, Context.PathTable.ExpandedPathComparer).ToArray();

                // ProcessInternal requires sorted input by path.
                Array.Sort(observations, new TestObservationComparer(Context.PathTable.ExpandedPathComparer));

                // Let's make sure all the observations we pass in are actually handled.
                List<Tuple<TestObservation, bool>> handled = new List<Tuple<TestObservation, bool>>();

                var process = CreateDummyProcess(
                    Context,
                    ReadOnlyArray<DirectoryArtifact>.From(directoryDependencies),
                    m_fileDependencies.ToArray());

                var result = ObservedInputProcessor.ProcessInternalAsync(
                    OperationContext.CreateUntracked(loggingContext),
                    new Environment(this),
                    new AssertingTarget(this, Context, (target, topOnly) => handled.Add(new Tuple<TestObservation, bool>(target, topOnly))),
                    CacheableProcess.GetProcessCacheInfo(process, Context),
                    ReadOnlyArray<TestObservation>.From(observations),
                    default(SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>),
                    isCacheLookup: false).Result;

                XAssert.AreEqual(expectedStatus, result.Status);

                if (!allowCollapsingObservations)
                {
                    XAssert.AreEqual(observations.Length, handled.Count, "Each observation should be processed exactly once in the target.");

                    for (int i = 0; i < expectedObservations.Length; i++)
                    {
                        XAssert.AreSame(expectedObservations[i], handled[i].Item1);
                        XAssert.AreEqual(expectedObservations[i].IsNestedUnderTopOnlySourceSealedDirectory, handled[i].Item2,
                            "IsNestedUnderTopOnlySourceSealedDirectory did not match for observation");
                    }
                }

                return result;
            }

            public AbsolutePath Path(string p)
            {
                return AbsolutePath.Create(Context.PathTable, p);
            }

            public void AddAbsentPath(AbsolutePath path)
            {
                m_fileContent.Add(path, new FileContentInfo(WellKnownContentHashes.AbsentFile, 1));
            }

            public void AddFile(AbsolutePath path, ContentHash hash)
            {
                XAssert.IsFalse(m_directories.Contains(path), "File to be added has been added as a directory");
                m_fileContent.Add(path, new FileContentInfo(hash, 1));
            }

            public void AddEnumerableDirectory(AbsolutePath path, DirectoryFingerprint fingerprint)
            {
                m_enumerableDirectories.Add(path, fingerprint);
                AddDirectory(path);
            }

            public void AddExistingDirectoryProbe(AbsolutePath path, DirectoryFingerprint fingerprint)
            {
                AddDirectory(path);
            }

            public void AddDirectory(AbsolutePath path)
            {
                while (path.IsValid)
                {
                    if (m_directories.Contains(path))
                    {
                        return;
                    }

                    XAssert.IsFalse(m_fileContent.ContainsKey(path), "Directory to be added, or its acestor, has been added as a file");
                    m_directories.Add(path);
                    path = path.GetParent(Context.PathTable);
                }
            }

            /// <summary>
            /// Creates a directory artifact which may be queried with <see cref="IObservedInputProcessingEnvironment.ListSealedDirectoryContents" />,
            /// and whose members may be queried with <see cref="IObservedInputProcessingEnvironment.TryQuerySealedOrUndeclaredInputContent" />.
            /// Each mentioned path must have been added explicitly with <see cref="AddAbsentPath" /> or <see cref="AddFile" />
            /// </summary>
            public DirectoryArtifact SealDirectory(AbsolutePath root, params AbsolutePath[] contents)
            {
                AddDirectory(root);
                FileArtifact[] artifacts = new FileArtifact[contents.Length];

                for (int i = 0; i < contents.Length; i++)
                {
                    if (!contents[i].IsWithin(Context.PathTable, root))
                    {
                        XAssert.Fail("Root {0} does not contain {1}", root.ToString(Context.PathTable), contents[i].ToString(Context.PathTable));
                    }

                    if (!m_fileContent.ContainsKey(contents[i]))
                    {
                        XAssert.Fail("Add the path {0} first before sealing", contents[i].ToString(Context.PathTable));
                    }

                    artifacts[i] = FileArtifact.CreateSourceFile(contents[i]);
                    m_sealedPathContentHashes.Add(contents[i], m_fileContent[contents[i]]);
                }

                SortedFileArtifacts files = SortedFileArtifacts.SortUnsafe(artifacts, OrdinalFileArtifactComparer.Instance);
                DirectoryArtifact sealedDirectory = DirectoryArtifact.CreateDirectoryArtifactForTesting(root, m_nextDirectorySealId++);
                m_sealedDirectories[sealedDirectory] = files;

                return sealedDirectory;
            }

            public DirectoryArtifact SealSourceDirectory(AbsolutePath root, bool allDirectories)
            {
                AddDirectory(root);
                DirectoryArtifact sealedSourceDirectory = DirectoryArtifact.CreateDirectoryArtifactForTesting(root, m_nextDirectorySealId++);
                m_sealedSourceDirectories.Add(root, allDirectories);
                m_sealedDirectories.Add(
                    sealedSourceDirectory,
                    SortedFileArtifacts.FromSortedArrayUnsafe(ReadOnlyArray<FileArtifact>.Empty, OrdinalFileArtifactComparer.Instance));

                return sealedSourceDirectory;
            }

            // ReSharper disable once InconsistentNaming
            private sealed class HarnessVFS
            {
                private readonly Harness m_harness;

                public HarnessVFS(Harness harness)
                {
                    m_harness = harness;
                }

                public Possible<PathExistence> TryProbeForExistence(AbsolutePath path)
                {
                    if (m_harness.m_directories.Contains(path))
                    {
                        return PathExistence.ExistsAsDirectory;
                    }

                    FileContentInfo fileContentInfo;
                    if (!m_harness.m_fileContent.TryGetValue(path, out fileContentInfo))
                    {
                        XAssert.Fail("Probed for unexpected path {0}", path.ToString(m_harness.Context.PathTable));
                    }

                    return fileContentInfo.Hash == WellKnownContentHashes.AbsentFile ? PathExistence.Nonexistent : PathExistence.ExistsAsFile;
                }

                public Possible<PathExistence> TryEnumerateDirectory(AbsolutePath path, Process pip, Action<AbsolutePath, PathExistence> handleEntry)
                {
                    throw new NotImplementedException("Implement this when it is actually used by the directory fingerprinter");
                }
            }

            private class Environment : IObservedInputProcessingEnvironment
            {
                private readonly Harness m_harness;

                public Environment(Harness harness)
                {
                    m_harness = harness;
                    Counters = new CounterCollection<PipExecutorCounter>();

                    // No use of state in this struct, so it is safe to set it to null.
                    State = null;

                    var context = m_harness.Context;
                    MountPathExpander mountExpander = new MountPathExpander(context.PathTable);
                    mountExpander.Add(
                        context.PathTable,
                        new
                            SemanticPathInfo(
                            PathAtom.Create(context.StringTable, "NotHashable"),
                            AbsolutePath.Create(context.PathTable, X("/z/notHashable")),
                            allowHashing: false,
                            readable: true,
                            writable: false));
                    mountExpander.Add(
                        context.PathTable,
                        new
                            SemanticPathInfo(
                            PathAtom.Create(context.StringTable, "readOnly"),
                            AbsolutePath.Create(context.PathTable, X("/z/readOnly")),
                            allowHashing: true,
                            readable: true,
                            writable: false));
                    mountExpander.Add(
                        context.PathTable,
                        new
                            SemanticPathInfo(
                            PathAtom.Create(context.StringTable, "filesystemDisabled"),
                            AbsolutePath.Create(context.PathTable, X("/z/filesystemDisabled")),
                            allowHashing: true,
                            readable: true,
                            writable: false));
                    mountExpander.Add(
                        context.PathTable,
                        new
                            SemanticPathInfo(
                            PathAtom.Create(context.StringTable, "writeable"),
                            AbsolutePath.Create(context.PathTable, X("/z/writeable")),
                            allowHashing: true,
                            readable: true,
                            writable: true));

                    PathExpander = mountExpander;
                }

                public PipExecutionContext Context => m_harness.Context;

                public CounterCollection<PipExecutorCounter> Counters { get; }

                public PipExecutionState.PipScopeState State { get; }

                public SemanticPathExpander PathExpander { get; }

                public DirectoryFingerprint? TryQueryDirectoryFingerprint(
                    AbsolutePath directoryPath,
                    CacheablePipInfo process,
                    DirectoryMembershipFilter filter,
                    bool isReadOnlyDirectory,
                    DirectoryMembershipHashedEventData eventData,
                    out DirectoryEnumerationMode mode,
                    bool trackPathExistence = false)
                {
                    m_harness.TrackedDirectoryFilters.Add(directoryPath, filter);
                    mode = DirectoryEnumerationMode.RealFilesystem;
                    return m_harness.m_enumerableDirectories[directoryPath];
                }

                public Task<FileContentInfo?> TryQuerySealedOrUndeclaredInputContent(AbsolutePath sealedPath, string consumerDescription = null, bool allowUndeclaredSourceReads = false)
                {
                    // We only allow queries of files actually named by the test.
                    if (!m_harness.m_fileContent.ContainsKey(sealedPath) && !m_harness.m_directories.Contains(sealedPath))
                    {
                        XAssert.Fail("Attempted to query content of path {0} which was not added", sealedPath.ToString(Context.PathTable));
                    }

                    FileContentInfo versionedHash;
                    if (m_harness.m_sealedPathContentHashes.TryGetValue(sealedPath, out versionedHash))
                    {
                        return Task.FromResult<FileContentInfo?>(m_harness.m_sealedPathContentHashes[sealedPath]);
                    }

                    var pathTable = Context.PathTable;
                    var initialDirectory = sealedPath.GetParent(pathTable);
                    var currentPath = initialDirectory;
                    while (currentPath.IsValid)
                    {
                        bool allDirectories;
                        if (m_harness.m_sealedSourceDirectories.TryGetValue(currentPath, out allDirectories))
                        {
                            if (m_harness.m_directories.Contains(sealedPath))
                            {
                                return Task.FromResult<FileContentInfo?>(null);
                            }

                            FileContentInfo fileContentInfo;
                            return m_harness.m_fileContent.TryGetValue(sealedPath, out fileContentInfo)
                                ? Task.FromResult<FileContentInfo?>(fileContentInfo)
                                : Task.FromResult<FileContentInfo?>(null);
                        }

                        currentPath = currentPath.GetParent(pathTable);
                    }

                    return Task.FromResult<FileContentInfo?>(null);
                }

                public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact)
                {
                    if (!m_harness.m_sealedDirectories.ContainsKey(directoryArtifact))
                    {
                        XAssert.Fail("Attempted to query directory {0} which was not sealed", directoryArtifact.Path.ToString(Context.PathTable));
                    }

                    return m_harness.m_sealedDirectories[directoryArtifact];
                }

                public bool IsSourceSealedDirectory(DirectoryArtifact directoryArtifact, out bool allDirectories, out ReadOnlyArray<StringId> patterns)
                {
                    patterns = ReadOnlyArray<StringId>.Empty;
                    return m_harness.m_sealedSourceDirectories.TryGetValue(directoryArtifact, out allDirectories);
                }

                public Possible<PathExistence> TryProbeAndTrackForExistence(AbsolutePath path, CacheablePipInfo pipInfo, bool isReadOnly, bool trackPathExistence = false)
                {
                    return m_harness.m_virtualFileSystem.TryProbeForExistence(path);
                }

                public bool IsPathUnderOutputDirectory(AbsolutePath path)
                {
                    return false;
                }
            }
        }

        [Fact]
        public void AllowedFileContentReads()
        {
            var harness = new Harness();

            AbsolutePath a = harness.Path(A("X", "a"));
            AbsolutePath b = harness.Path(A("X", "b"));

            harness.AddFile(a, CreateFakeContentHash(1));
            harness.AddFile(b, CreateFakeContentHash(2));

            DirectoryArtifact d = harness.SealDirectory(harness.Path(A("X", "")), a, b);

            harness.Process(
                ObservedInputProcessingStatus.Success,
                AllowAccessTo(d),
                false,
                TestObservation.ExpectFileContentRead(a, CreateFakeContentHash(1)),
                TestObservation.ExpectFileContentRead(b, CreateFakeContentHash(2)));
        }

        [Fact]
        public void EmptyDirectoryEnumeration()
        {
            var harness = new Harness();

            // Add a non-empty directory. non-empty directories must have a non-default DirectoryFingerprint
            AbsolutePath a = harness.Path(A("X", "a"));
            ContentHash fakeFingerprint = CreateFakeContentHash(10);
            harness.AddEnumerableDirectory(a, new DirectoryFingerprint(fakeFingerprint));

            harness.Process(
                ObservedInputProcessingStatus.Success,
                AllowAccessTo(),
                false,
                TestObservation.ExpectDirectoryEnumeration(a, fakeFingerprint));
        }

        [Fact]
        public void DirectoryLocationExistAsFile()
        {
            var harness = new Harness();

            var insideSealPath = harness.Path(A("X", "Dir2", "a.h"));

            harness.AddFile(insideSealPath, CreateFakeContentHash(1));
            DirectoryArtifact seal = harness.SealDirectory(harness.Path(A("X", "Dir2")), insideSealPath);

            // Even though this file exists, it has the directoryLocation flag so BuildXL will not hash it.
            var result = harness.Process(
                ObservedInputProcessingStatus.Success,
                AllowAccessTo(seal),
                false,
                TestObservation.ExpectAbsenceProbe(insideSealPath, isDirectoryLocation: true));

            XAssert.AreEqual(1, result.ObservedInputs.Length);
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, result.ObservedInputs[0].Type);
        }

        [Fact]
        public void PartialSealsEnforced()
        {
            var harness = new Harness();

            AbsolutePath a = harness.Path(A("X", "d", "a"));
            AbsolutePath b = harness.Path(A("X", "d", "b"));

            harness.AddFile(a, CreateFakeContentHash(1));
            harness.AddFile(b, CreateFakeContentHash(2));

            DirectoryArtifact da = harness.SealDirectory(harness.Path(A("X", "d")), a);
            DirectoryArtifact db = harness.SealDirectory(harness.Path(A("X", "d")), b);

            harness.Process(
                ObservedInputProcessingStatus.Mismatched,
                AllowAccessTo(db),
                false,

                // But not da
                TestObservation.ExpectAccessCheckFailure(a),
                TestObservation.ExpectFileContentRead(b, CreateFakeContentHash(2)));
        }

        [Fact]
        public void UnexpectedAccessesReported()
        {
            var harness = new Harness();

            // Create absent path probes both before and after the enumeration to make sure the order doesn't matter
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: false, members: new[] { "a.h", "c.h" }, addDependency: false);
            var o2 = harness.AddAbsentProbeObservation(A("X", "Dir1", "b.h"));

            // Directory enumerations for empty directories get translated to absent paths and should be handled the same
            var o4 = harness.AddEmptyDirectoryEnumeration(A("X", "Dir1", "nested"));

            var unexpectedObservations = new[] { o1, o2, o4 };

            var result = harness.Process(ObservedInputProcessingStatus.Success, true);
            foreach (var unexpectedObservation in unexpectedObservations)
            {
                XAssert.IsTrue(harness.ReportedUnexpectedAccesses.ContainsKey(unexpectedObservation.Path),
                    "Missing unexpected observation: {0}", unexpectedObservation.Path.ToString(harness.Context.PathTable));
            }

            XAssert.AreEqual(unexpectedObservations.Length, harness.ReportedUnexpectedAccesses.Count);
        }

        [Fact]
        public void ExcludeAbsentProbesUnderEnumeratedDirectories()
        {
            var harness = new Harness();

            // Create absent path probes both before and after the enumeration to make sure the order doesn't matter
            harness.AddAbsentProbeObservation(A("X", "Dir1", "a.h"));
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: false);
            harness.AddAbsentProbeObservation(A("X", "Dir1", "b.h"));

            // Directory enumerations for empty directories get translated to absent paths and should be handled the same
            harness.AddEmptyDirectoryEnumeration(A("X", "Dir1", "nested"));

            // The probe outside the directory should be considered
            harness.AddAbsentProbeObservation(A("X", "Dir2", "c.h"));

            var result = harness.Process(ObservedInputProcessingStatus.Success, true);
            XAssert.AreEqual(3, result.ObservedInputs.Length);
            XAssert.AreEqual(ObservedInputType.DirectoryEnumeration, result.ObservedInputs[0].Type);
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, result.ObservedInputs[1].Type);
            XAssert.AreEqual(AbsolutePath.Create(harness.Context.PathTable, X("/X/Dir1/nested", OperatingSystemHelper.IsUnixOS)), result.ObservedInputs[1].Path);
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, result.ObservedInputs[2].Type);
            XAssert.AreEqual(AbsolutePath.Create(harness.Context.PathTable, X("/X/Dir2/c.h", OperatingSystemHelper.IsUnixOS)), result.ObservedInputs[2].Path);
        }

        [Fact]
        public void ExcludeAbsentProbesUnderEnumeratedDirectoriesWithExistingDirectoryProbes()
        {
            var harness = new Harness();

            // Create absent path probes both before and after the enumeration to make sure the order doesn't matter
            harness.AddAbsentProbeObservation(A("X", "Dir1", "a.h"));
            var o1 = harness.AddExistingDirectoryProbe(A("X", "Dir1", ""), isSearchPath: false);
            harness.AddAbsentProbeObservation(A("X", "Dir1", "b.h"));

            // The probe outside the directory should be considered
            harness.AddAbsentProbeObservation(A("X", "Dir2", "c.h"));

            var result = harness.Process(ObservedInputProcessingStatus.Success, true);

            XAssert.AreEqual(4, result.ObservedInputs.Length);
            XAssert.AreEqual(ObservedInputType.ExistingDirectoryProbe, result.ObservedInputs[0].Type);
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, result.ObservedInputs[1].Type);
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, result.ObservedInputs[2].Type);
            XAssert.AreEqual(AbsolutePath.Create(harness.Context.PathTable, X("/X/Dir2/c.h", OperatingSystemHelper.IsUnixOS)), result.ObservedInputs[3].Path);
        }

        [Trait(Test.BuildXL.TestUtilities.Features.Feature, Test.BuildXL.TestUtilities.Features.SearchPath)]
        [Fact]
        public void ExcludeAbsentProbesUnderEnumeratedDirectoriesConsideringSearchPath()
        {
            var harness = new Harness();
            harness.AddAbsentProbeObservation(A("X", "Dir1", "b.h"));
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: true);
            harness.AddAbsentProbeObservation(A("X", "Dir1", "a.h"));

            // Making the path searchable doesn't change the observed inputs since it causes the 2 filenames to be included
            // in the search path filter
            var result = harness.Process(ObservedInputProcessingStatus.Success, true);
            var filter = harness.TrackedDirectoryFilters[o1.Path] as SearchPathDirectoryMembershipFilter;
            XAssert.IsNotNull(filter);
            XAssert.IsTrue(filter.Include(AbsolutePath.Create(harness.Context.PathTable, X("/X/Dir1/a.h"))));
            XAssert.IsTrue(filter.Include(AbsolutePath.Create(harness.Context.PathTable, X("/X/Dir1/b.h"))));
            XAssert.AreEqual(1, result.ObservedInputs.Length);
            XAssert.AreEqual(ObservedInputType.DirectoryEnumeration, result.ObservedInputs[0].Type);
            XAssert.AreEqual(2, result.ObservedAccessFileNames.Length);
            XAssert.AreEqual("a", result.ObservedAccessFileNames[0].ToString(harness.Context.StringTable));
            XAssert.AreEqual("b", result.ObservedAccessFileNames[1].ToString(harness.Context.StringTable));
        }

        [Trait(Test.BuildXL.TestUtilities.Features.Feature, Test.BuildXL.TestUtilities.Features.SearchPath)]
        [Fact]
        public void SearchPathDirectoryEnumerations()
        {
            var harness = new Harness();

            // Enumerations
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: true, members: new[] { "foo.hpp", @"Dir7\D.exe" });
            var o2 = harness.AddDirectoryEnumeration(A("X", "Dir2", ""), isSearchPath: true, members: new[] { "a.h", "b.h", "c.h", "e.h" });
            var o3 = harness.AddDirectoryEnumeration(A("X", "Dir3", ""), isSearchPath: true, members: new[] { "C.hpp", "f.lib" });
            var o4 = harness.AddDirectoryEnumeration(A("X", "Dir4", ""));
            var o56 = harness.AddDirectoryEnumeration(A("X", "Dir5", "Dir6"), isSearchPath: true);
            var o5 = harness.AddDirectoryEnumeration(A("X", "Dir5", ""), isSearchPath: true);

            // Observed accesses/ probes
            harness.AddAbsentProbeObservation(A("X", "Dir1", "a.h"));
            harness.AddReadObservation(A("X", "Dir2", "a.h"));
            harness.AddReadObservation(A("X", "Dir3", "C.hpp"));
            harness.AddReadObservation(A("X", "Dir1", "Dir7", "D.exe"));

            // Declared dependencies
            harness.AddFileDependency(A("X", "dir2", "e.cpp"));

            harness.Process(ObservedInputProcessingStatus.Success, true);

            var filter4 = harness.TrackedDirectoryFilters[o4.Path];
            Assert.Same(DirectoryMembershipFilter.AllowAllFilter, filter4);

            var filter1 = harness.TrackedDirectoryFilters[o1.Path];
            Assert.IsType(typeof(SearchPathDirectoryMembershipFilter), filter1);
            var searchPathFilter = (SearchPathDirectoryMembershipFilter)filter1;

            // Verify that accessed file names are in the accessed file name set.
            var expectedFileNames = new[] { "Dir7", "Dir6", "a", "c", "e" };
            foreach (var expectedFileName in expectedFileNames)
            {
                bool contains =
                    searchPathFilter.AccessedFileNamesWithoutExtension.Contains(StringId.Create(harness.Context.StringTable, expectedFileName));
                Assert.True(contains);
            }

            Assert.Equal(expectedFileNames.Length, searchPathFilter.AccessedFileNamesWithoutExtension.Count);

            // Go through enumeration observations and verify that they all
            // use the search path enumeration filter.
            foreach (var enumeration in new[] { o2, o3, o56, o5 })
            {
                var enumerationFilter = harness.TrackedDirectoryFilters[enumeration.Path];
                Assert.Same(searchPathFilter, enumerationFilter);
            }

            Assert.True(searchPathFilter.Include(o3.Path.Combine(harness.Context.PathTable, "c.exe")));
            Assert.True(searchPathFilter.Include(o3.Path.Combine(harness.Context.PathTable, "Dir7.testext")));
            Assert.True(searchPathFilter.Include(o56.Path.Combine(harness.Context.PathTable, "c.exe")));
            Assert.False(searchPathFilter.Include(o56.Path.Combine(harness.Context.PathTable, "unaccessedfilename.h")));
        }

        [Trait(Test.BuildXL.TestUtilities.Features.Feature, Test.BuildXL.TestUtilities.Features.SearchPath)]
        [Fact]
        public void SearchPathDirectoryEnumerationsWithFilters()
        {
            var harness = new Harness();

            // Enumerations
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: true, members: new[] { "foo.hpp", @"Dir7\D.exe" }, enumeratePatterns: new[] { "*.cpp" });
            var o2 = harness.AddDirectoryEnumeration(A("X", "Dir2", ""), isSearchPath: true, members: new[] { "a.h", "b.h", "c.h", "e.h" });
            var o3 = harness.AddDirectoryEnumeration(A("X", "Dir3", ""), isSearchPath: true, members: new[] { "C.hpp", "f.lib" });
            var o4 = harness.AddDirectoryEnumeration(A("X", "Dir4", ""));
            var o56 = harness.AddDirectoryEnumeration(A("X", "Dir5", "Dir6"), isSearchPath: true);
            var o5 = harness.AddDirectoryEnumeration(A("X", "Dir5", ""), isSearchPath: true);

            // Observed accesses/ probes
            harness.AddAbsentProbeObservation(A("X", "Dir1", "a.h"));
            harness.AddReadObservation(A("X", "Dir2", "a.h"));
            harness.AddReadObservation(A("X", "Dir3", "C.hpp"));
            harness.AddReadObservation(A("X", "Dir1", "Dir7", "D.exe"));

            // Declared dependencies
            harness.AddFileDependency(A("X", "dir2", "e.cpp"));

            harness.Process(ObservedInputProcessingStatus.Success, true);

            var filter4 = harness.TrackedDirectoryFilters[o4.Path];
            Assert.Same(DirectoryMembershipFilter.AllowAllFilter, filter4);

            var filter1 = harness.TrackedDirectoryFilters[o1.Path];
            Assert.IsType(typeof(UnionDirectoryMembershipFilter), filter1);
            var unionRegexAndSearchFilter = (UnionDirectoryMembershipFilter)filter1;

            var stringTable = harness.Context.StringTable;

            // Verify file names passing filter
            var expectedFileNames = new[] { "Dir7", "Dir6", "a", "c", "e", "c.exe", "Dir7.testext", "unaccessedfilename.cpp" };
            foreach (var fileName in expectedFileNames)
            {
                Assert.True(unionRegexAndSearchFilter.Include(PathAtom.Create(stringTable, fileName), fileName));
            }

            // Verify file names not passing filter
            var unexpectedFileNames = new[] { "unaccessedfilename.h" };
            foreach (var fileName in unexpectedFileNames)
            {
                Assert.False(unionRegexAndSearchFilter.Include(PathAtom.Create(stringTable, fileName), fileName));
            }
        }

        [Fact]
        public void DirectoryEnumerationsWithFilters()
        {
            var harness = new Harness();

            // Enumerations
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: false, members: new[] { "foo.hpp" }, enumeratePatterns: new[] { "*.cpp", "rc*.*" });

            harness.Process(ObservedInputProcessingStatus.Success, true);

            var filter1 = harness.TrackedDirectoryFilters[o1.Path];
            Assert.IsType(typeof(RegexDirectoryMembershipFilter), filter1);
            var stringTable = harness.Context.StringTable;

            // Verify file names passing filter
            var expectedFileNames = new[] { "rc.exe", "rca.exe", ".cpp", "a.CPP", "A.CPP", "RC.EXE" };
            foreach (var fileName in expectedFileNames)
            {
                Assert.True(filter1.Include(PathAtom.Create(stringTable, fileName), fileName));
            }

            // Verify file names not passing filter
            var unexpectedFileNames = new[] { "foo.hpp", "arc.exe", "cpp", "rc", "rca" };
            foreach (var fileName in unexpectedFileNames)
            {
                Assert.False(filter1.Include(PathAtom.Create(stringTable, fileName), fileName));
            }
        }

        [Fact]
        public void DirectoryEnumerationsWithWhitespaceFilename()
        {
            var harness = new Harness();

            // File/Directory with a whitespace filename is legit on some platforms. Need to make sure it is handled appropriately
            string[] directoryMembers = new[] { "foo.hpp", "  " };
            var o1 = harness.AddDirectoryEnumeration(A("X", "Dir1", ""), isSearchPath: false, members: directoryMembers);
            var o2 = harness.AddDirectoryEnumeration(A("X", "Dir2", ""), isSearchPath: false, members: directoryMembers, enumeratePatterns: new[] { "*.cpp", "rc*.*" });
            harness.Process(ObservedInputProcessingStatus.Success, true);

            // Make sure the generated filters are of the appropriate types
            var filter1 = harness.TrackedDirectoryFilters[o1.Path];
            Assert.Same(filter1, DirectoryMembershipFilter.AllowAllFilter);

            var filter2 = harness.TrackedDirectoryFilters[o2.Path];
            Assert.IsType(typeof(RegexDirectoryMembershipFilter), filter2);

            // Verify that the filter can handle the filenames
            foreach (var fileName in directoryMembers)
            {
                // Everything is included
                Assert.True(filter1.Include(PathAtom.Create(harness.Context.StringTable, fileName), fileName));

                // Nothing should be included
                Assert.False(filter2.Include(PathAtom.Create(harness.Context.StringTable, fileName), fileName));
            }
        }

        [Fact]
        public void TestEnumerationModeRules()
        {
            // Setup the environment for the tests
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var config = ConfigurationHelpers.GetDefaultForTesting(
                context.PathTable,
                AbsolutePath.Create(context.PathTable, Path.Combine(TestOutputDirectory, "config.dc")));

            DummyPipExecutionEnvironment dummy = new DummyPipExecutionEnvironment(LoggingContext, context, config, sandboxedKextConnection: GetSandboxedKextConnection());
            DirectoryMembershipFingerprinter fingerprinter = new DirectoryMembershipFingerprinter(LoggingContext, context);

            DirectoryMembershipFingerprinterRule excludeFiles =
                new DirectoryMembershipFingerprinterRule(
                    "TestRule",
                    AbsolutePath.Create(context.PathTable, X("/z/filesystemDisabled")),
                    disableFilesystemEnumeration: true,
                    fileIgnoreWildcards: new string[] { });

            ModuleId testModule = new ModuleId(2, "test");
            ModuleConfiguration moduleConfig = new ModuleConfiguration { ModuleId = testModule };

            DirectoryMembershipFingerprinterRuleSet parentRuleSet = new DirectoryMembershipFingerprinterRuleSet(
                new RootModuleConfiguration(),
                context.StringTable);
            DirectoryMembershipFingerprinterRuleSet childRuleSet = new DirectoryMembershipFingerprinterRuleSet(new[] { excludeFiles }, parentRuleSet);
            parentRuleSet.AddModuleRuleSet(testModule, childRuleSet);

            MountPathExpander mountExpander = new MountPathExpander(context.PathTable);
            mountExpander.Add(
                context.PathTable,
                new
                    SemanticPathInfo(
                    PathAtom.Create(context.StringTable, "NotHashable"),
                    AbsolutePath.Create(context.PathTable, X("/z/notHashable")),
                    allowHashing: false,
                    readable: true,
                    writable: false));
            mountExpander.Add(
                context.PathTable,
                new
                    SemanticPathInfo(
                    PathAtom.Create(context.StringTable, "readOnly"),
                    AbsolutePath.Create(context.PathTable, X("/z/readOnly")),
                    allowHashing: true,
                    readable: true,
                    writable: false));
            mountExpander.Add(
                context.PathTable,
                new
                    SemanticPathInfo(
                    PathAtom.Create(context.StringTable, "filesystemDisabled"),
                    AbsolutePath.Create(context.PathTable, X("/z/filesystemDisabled")),
                    allowHashing: true,
                    readable: true,
                    writable: false));
            mountExpander.Add(
                context.PathTable,
                new
                    SemanticPathInfo(
                    PathAtom.Create(context.StringTable, "writeable"),
                    AbsolutePath.Create(context.PathTable, X("/z/writeable")),
                    allowHashing: true,
                    readable: true,
                    writable: true));

            PipExecutionState pes = new PipExecutionState(
                config,
                cache: null,
                fileAccessWhitelist: null,
                directoryMembershipFingerprinter: fingerprinter,
                pathExpander: mountExpander,
                fileSystemView: null,
                executionLog: null,
                unsafeConfiguration: config.Sandbox.UnsafeSandboxConfiguration,
                preserveOutputsSalt: ContentHashingUtilities.CreateRandom(),
                fileContentManager: new FileContentManager(dummy, new NullOperationTracker()),
                directoryMembershipFinterprinterRuleSet: parentRuleSet);
            PipExecutionState.PipScopeState state = new PipExecutionState.PipScopeState(pes, testModule, allowPreserveOutputs: false);

            var adapter = new ObservedInputProcessingEnvironmentAdapter(dummy, state);
            DirectoryMembershipFingerprinterRule rule;
            DirectoryEnumerationMode mode;

            // End of test setup. Now query various directories under different situations to make sure the appropriate enumeration method is used

            // A file outside of any mount should get the default fingerprint
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/outsideAnyMount")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.DefaultFingerprint, mode);
            XAssert.IsNull(rule);

            // A file in an unhashable mount also gets the default fingerprint
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/notHashable")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.DefaultFingerprint, mode);
            XAssert.IsNull(rule);

            // Readonly mounts use the filesystem
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/readOnly")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.RealFilesystem, mode);
            XAssert.IsNull(rule);

            // Readonly directories use the filesystem
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/writeable")), isReadOnlyDirectory: true, rule: out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.RealFilesystem, mode);
            XAssert.IsNull(rule);

            // Writeable mounts use the graph
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/writeable")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.MinimalGraph, mode);
            XAssert.IsNull(rule);

            // Check that a rule impacts the enumeration method
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/filesystemDisabled")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.MinimalGraph, mode);
            XAssert.IsNotNull(rule);

            // Change the configuration to always use the minimal graph. This should override the readonly path that use to use the real filesystem
            config.Sandbox.FileSystemMode = FileSystemMode.AlwaysMinimalGraph;
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/readOnly")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.MinimalGraph, mode);
            XAssert.IsNull(rule);

            // Set the configuration to use the full graph
            config.Sandbox.FileSystemMode = FileSystemMode.RealAndPipGraph;
            mode = adapter.DetermineEnumerationModeAndRule(AbsolutePath.Create(context.PathTable, X("/z/writeable")), out rule);
            XAssert.AreEqual(DirectoryEnumerationMode.FullGraph, mode);
            XAssert.IsNull(rule);
        }

        [Fact]
        public void UnsealedAntiDependenciesAreAllowed()
        {
            var harness = new Harness();

            AbsolutePath absent = harness.Path(A("X", "c", "absent"));
            AbsolutePath present = harness.Path(A("X", "d", "present"));

            harness.AddAbsentPath(absent);
            harness.AddFile(present, CreateFakeContentHash(1));

            // Note that this does not contain 'absent'
            DirectoryArtifact d = harness.SealDirectory(harness.Path(A("X", "d")), present);

            harness.Process(
                ObservedInputProcessingStatus.Success,
                AllowAccessTo(d),
                false,
                TestObservation.ExpectAbsenceProbe(absent));
        }

        [Fact]
        public void UnsealedDirectoryEnumerationsAreAllowed()
        {
            var harness = new Harness();

            // Add a non-empty directory. non-empty directories must have a non-default DirectoryFingerprint
            AbsolutePath a = harness.Path(A("X", "a"));
            ContentHash fakeFingerprint = CreateFakeContentHash(10);
            harness.AddEnumerableDirectory(a, new DirectoryFingerprint(fakeFingerprint));

            harness.Process(
                ObservedInputProcessingStatus.Success,
                AllowAccessTo(),
                false,
                TestObservation.ExpectDirectoryEnumeration(a, fakeFingerprint));
        }

        [Fact]
        public void UnsealedFileReadsAreDisallowed()
        {
            var harness = new Harness();

            AbsolutePath absent = harness.Path(A("X", "c", "absent"));
            AbsolutePath present = harness.Path(A("X", "d", "present"));

            harness.AddAbsentPath(absent);
            harness.AddFile(present, CreateFakeContentHash(1));

            // Note that this does not contain 'present'
            DirectoryArtifact d = harness.SealDirectory(harness.Path(A("X", "c")), absent);

            harness.Process(
                ObservedInputProcessingStatus.Mismatched,
                AllowAccessTo(d),
                false,
                TestObservation.ExpectAccessCheckFailure(present),
                TestObservation.ExpectAbsenceProbe(absent));
        }

        [Fact]
        public void SealSourceDirectoryRecursivelyShouldObserveNestedDirectory()
        {
            var harness = new Harness();
            AbsolutePath sourceDirectory = harness.Path(A("X", "sourceDir"));
            AbsolutePath nestedDirectory = harness.Path(A("X", "sourceDir", "nested"));
            AbsolutePath fileInsideNestedDirectory = harness.Path(A("X", "sourceDir", "nested", "file.txt"));

            DirectoryArtifact sealedSourceDirectory = harness.SealSourceDirectory(sourceDirectory, true);

            harness.AddEnumerableDirectory(nestedDirectory, new DirectoryFingerprint(CreateFakeContentHash(11)));
            harness.AddFile(fileInsideNestedDirectory, CreateFakeContentHash(1));

            harness.Process(
                ObservedInputProcessingStatus.Success,
                AllowAccessTo(sealedSourceDirectory),
                false,
                TestObservation.ExpectDirectoryEnumeration(nestedDirectory, CreateFakeContentHash(11)),
                TestObservation.ExpectFileContentRead(fileInsideNestedDirectory, CreateFakeContentHash(1)));
        }

        [Fact]
        public void SealSourceDirectoryTopLevelShouldObserveNestedArtifacts()
        {
            var harness = new Harness();
            AbsolutePath sourceDirectory = harness.Path(A("X", "sourceDir"));
            AbsolutePath nestedDirectory = harness.Path(A("X", "sourceDir", "nested"));
            AbsolutePath fileTopLevelDirectory = harness.Path(A("X", "sourceDir", "file.txt"));
            AbsolutePath fileInsideNestedDirectory = harness.Path(A("X", "sourceDir", "nested", "file.txt"));

            DirectoryArtifact sealedSourceDirectory = harness.SealSourceDirectory(sourceDirectory, false);

            harness.AddEnumerableDirectory(nestedDirectory, new DirectoryFingerprint(CreateFakeContentHash(11)));
            harness.AddFile(fileInsideNestedDirectory, CreateFakeContentHash(1));
            harness.AddFile(fileTopLevelDirectory, CreateFakeContentHash(2));

            harness.Process(
                ObservedInputProcessingStatus.Mismatched,
                AllowAccessTo(sealedSourceDirectory),
                false,
                TestObservation.ExpectFileContentRead(fileTopLevelDirectory, CreateFakeContentHash(2)),
                TestObservation.ExpectDirectoryEnumeration(nestedDirectory, CreateFakeContentHash(11)),
                TestObservation.ExpectAccessCheckFailure(fileInsideNestedDirectory, isNestedUnderTopOnlySourceSealedDirectory: true));
        }

        [Fact]
        public void SealSourceDirectoryDeeplyNestedFileDoesNotMaskAsSealDirectoryError()
        {
            var harness = new Harness();
            AbsolutePath sourceDirectory = harness.Path(A("X", "sourceDir"));
            AbsolutePath fileInsideNestedDirectory = harness.Path(A("X", "sourceDir", "nested", "file.txt"));

            DirectoryArtifact topOnlySourceSealedDirectory = harness.SealSourceDirectory(sourceDirectory, allDirectories: false);
            harness.AddFile(fileInsideNestedDirectory, CreateFakeContentHash(1));

            var result = harness.Process(
                ObservedInputProcessingStatus.Mismatched,
                AllowAccessTo(topOnlySourceSealedDirectory),
                false,
                TestObservation.ExpectAccessCheckFailure(fileInsideNestedDirectory, isNestedUnderTopOnlySourceSealedDirectory: true));
        }
    }
}
