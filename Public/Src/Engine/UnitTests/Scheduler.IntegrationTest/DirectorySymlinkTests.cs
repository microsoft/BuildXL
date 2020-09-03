// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Unix;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using EngineLogEventId=BuildXL.Engine.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Feature(Features.Symlink)]
    [Trait("Category", "ReparsePointTests")]
    public class DirectorySymlinkTests : SchedulerIntegrationTestBase
    {
        public readonly struct LookupSpec
        {
            /// <summary>Arbitrary description</summary>
            internal string Desc { get; }

            /// <summary>Lookup path</summary>
            internal string Lookup { get; }

            /// <summary>Target expanded path</summary>
            internal string Target { get; }

            /// <remarks>Expected format of <paramref name="expectedObservations"/> is "[+-] path-relative-to-roodDir"</remarks>
            internal string[] Observations { get; }

            internal LookupSpec(string desc, string lookup, string target, string[] observations)
            {
                Desc = desc;
                Lookup = lookup;
                Target = target;
                Observations = observations;
            }

            internal enum TransalteAction
            { 
                ShouldContain,
                ShouldNotContain,
                ShouldNotExistInAnyPath
            }

            internal (TransalteAction action, string fullPath)[] TranslateObservations(string rootDir)
            {
                return Observations
                    .Select(spec =>
                    {
                        var splits = spec.Split(' ');
                        XAssert.AreEqual(2, splits.Length);
                        var plusMinusBang = splits[0];
                        var relPath = splits[1];
                        TransalteAction action = TransalteAction.ShouldContain;
                        switch (plusMinusBang)
                        {
                            case "+":
                                action = TransalteAction.ShouldContain;
                                break;
                            case "-":
                                action = TransalteAction.ShouldNotContain;
                                break;
                            case "!":
                                action = TransalteAction.ShouldNotExistInAnyPath;
                                break;
                            default:
                                XAssert.IsFalse(true, "Unknown TranslateAction encoutnered!");
                                break;
                        }
                        var fullPath = X($"{rootDir}/{relPath}");
                        return (action, fullPath);
                    })
                    .ToArray();
            }

            internal bool IncludesObservation(string rootDir, string fullPath) => FilterObservations(rootDir, fullPath, action: TransalteAction.ShouldContain).Any();
            internal bool ExcludesObservation(string rootDir, string fullPath) => FilterObservations(rootDir, fullPath, action: TransalteAction.ShouldNotContain).Any();

            internal IEnumerable<(TransalteAction action, string fullPath)> FilterObservations(string rootDir, string fullPath, TransalteAction action)
                => TranslateObservations(rootDir).Where(o => o.fullPath == fullPath && o.action == action);

            /// <nodoc />
            public override string ToString() => Desc;
        }

        public DirectorySymlinkTests(ITestOutputHelper output) : base(output)
        {
            // Enable full symbolic link resolving for testing 
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;
        }

        protected override void Dispose(bool disposing)
        {
            AssertWarningEventLogged(EngineLogEventId.ScrubbingExternalFileOrDirectoryFailed, count: 0, allowMore: true);
            base.Dispose(disposing);
        }

        /*
         * Operations which when executed produce the following layout

            {rootDir}
            ├── sym-Versions_A_file -> Versions/A/file
            ├── sym-Versions_sym-A_file -> Versions/sym-A/file
            └── Versions
                ├── A
                │   ├── file
                │   └── sym-loop -> ../sym-A
                ├── sym-A -> A
                └── sym-sym-A -> sym-A
        */
     
        private const string GeneralDirectoryLayout = @"
sym-Versions_A_file -> Versions/A/file
sym-Versions_sym-A_file -> Versions/sym-A/file
Versions/
Versions/A/
Versions/A/file
Versions/A/sym-loop -> ../sym-A/
Versions/sym-A -> A/
Versions/sym-sym-A -> sym-A/
";

        /// <summary>
        /// Different lookups (via different symlinks) and specifications for expected observed accesses.
        /// </summary>
        /// <remarks>
        /// Expected observed paths:
        ///   - paths going through any directory symlinks should never be observed
        ///   - full path to the actual file must be observed
        ///   - full paths to any symbolic links that were resolved while looking up the actual file must be observed
        ///
        /// <see cref="LookupSpec.Observations"/> for more details on the format of strings specified as observations.
        /// In a nutshell, prefix is "+" means that the path must be observed, and "-" means that the path must not be observed.
        /// </remarks>
        private static LookupSpec[] LookupSpecs { get; } = new[]
        {
            new LookupSpec(
                "readDirectly",
                lookup: "Versions/A/file",
                target: "Versions/A/file",
                observations: new[]
                {
                    "+ Versions/A/file",
                    "- Versions/A/sym-loop",
                    "- Versions/sym-A",
                    "- Versions/sym-A/file",
                    "- Versions/sym-sym-A",
                    "- Versions/sym-sym-A/file"
                }
            ),

            new LookupSpec(
                "readViaDirSymlink",
                lookup: "Versions/sym-A/file",
                target: "Versions/A/file",
                observations: new[]
                {
                    "+ Versions/sym-A",
                    "+ Versions/A/file",
                    "- Versions/A/sym-loop",
                    "- Versions/sym-A/file",
                    "- Versions/sym-sym-A",
                    "- Versions/sym-sym-A/file"
                }
            ),

            new LookupSpec(
                "readViaDirDirSymlink",
                lookup: "Versions/sym-sym-A/file",
                target: "Versions/A/file",
                observations: new[]
                {
                    "+ Versions/sym-sym-A",
                    "+ Versions/sym-A",
                    "+ Versions/A/file",
                    "- Versions/A/sym-loop",
                    "- Versions/sym-A/file",
                    "- Versions/sym-sym-A/file"
                }
            ),

            new LookupSpec(
                "readViaFileSymlink",
                lookup: "sym-Versions_A_file",
                target: "Versions/A/file",
                observations: new[]
                {
                    "+ sym-Versions_A_file",
                    "+ Versions/A/file",
                    "- Versions/A/sym-loop",
                    "- Versions/sym-A",
                    "- Versions/sym-A/file",
                    "- Versions/sym-sym-A",
                    "- Versions/sym-sym-A/file"
                }
            ),

            new LookupSpec(
                "readViaFileDirSymlink",
                lookup: "sym-Versions_sym-A_file",
                target: "Versions/A/file",
                observations: new[]
                {
                    "+ sym-Versions_sym-A_file",
                    "+ Versions/sym-A",
                    "+ Versions/A/file",
                    "- Versions/A/sym-loop",
                    "- Versions/sym-A/file",
                    "- Versions/sym-sym-A",
                    "- Versions/sym-sym-A/file"
                }
            ),
            
            new LookupSpec(
                "readViaSymLoop",
                lookup: "Versions/A/sym-loop/file",
                target: "Versions/A/file",
                observations: new[]
                {
                    "+ Versions/A/sym-loop",
                    "+ Versions/sym-A",
                    "+ Versions/A/file",
                    "- Versions/A/sym-loop/file",
                    "- Versions/sym-A/file",
                    "- Versions/sym-sym-A",
                    "- Versions/sym-sym-A/file"
                }
            ),
        };

        private static LookupSpec[] AbsentProbeSpecs { get; } = new[]
        {
            new LookupSpec(
                "absentProbeViaDirSymlink",
                lookup: "Versions/sym-A/absent1",
                target: "Versions/A/absent1",
                observations: new[]
                {
                    "+ Versions/sym-A",
                    "+ Versions/A/absent1",
                    "- Versions/sym-A/absent1",
                }
            ),

            new LookupSpec(
                "absentProbeViaDirDirSymlink",
                lookup: "Versions/sym-sym-A/absent2",
                target: "Versions/A/absent2",
                observations: new[]
                {
                    "+ Versions/sym-sym-A",
                    "+ Versions/sym-A",
                    "+ Versions/A/absent2",
                    "- Versions/sym-A/absent2",
                    "- Versions/sym-sym-A/absent2"
                }
            ),
        };

        private IEnumerable<Operation> GetLayoutProducingOperations(string rootDir, string layout)
        {
            return layout
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(spec => $"{rootDir}/{spec.Trim()}")
                .Select(spec =>
                    spec.Contains("->") ? OpCreateSym(X(spec), spec.EndsWith("/") ? Operation.SymbolicLinkFlag.DIRECTORY : Operation.SymbolicLinkFlag.FILE) :
                    spec.EndsWith("/")  ? OpCreateDir(X(spec)) :
                                          OpWriteFile(X(spec)));
        }

        private IEnumerable<Operation> GetLayoutProducingOperationsWithDummyReadFile(string rootDir, string layout, string dummyFileDescription)
            => GetLayoutProducingOperations(rootDir, layout).Concat(new[]
            {
                OpReadDummySourceFile(dummyFileDescription)
            });

        public static IEnumerable<object[]> LookupTestsData()
        {
            foreach (var spec in LookupSpecs.Concat(AbsentProbeSpecs))
            {
                yield return new object[] { spec };
            }
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [MemberData(nameof(LookupTestsData))]
        public void LookupTests(LookupSpec spec)
        {
            AbsolutePath rootDirAbsPath = CreateUniqueObjPath("LookupTest");
            string rootDir = rootDirAbsPath.ToString(Context.PathTable);
            var files = CreateLayoutOnDisk(rootDir, GeneralDirectoryLayout);

            var sealDirArtifact = SealDirectory(rootDirAbsPath, files, SealDirectoryKind.Full);

            var pip = CreateAndScheduleConsumer(sealDirArtifact, spec.Desc, spec.Lookup);

            var result = RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);

            ValidateObservations(result, rootDir, new[] { (pip, spec) });
        }

        [Feature(Features.Symlink)]
        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestEnumerateDirectory()
        {
            AbsolutePath dirAbsPath = CreateUniqueObjPath($"TestDirEnum.framework");
            string rootDir = dirAbsPath.ToString(Context.PathTable);

            // create layout on disk
            List<FileArtifact> dirContents = CreateLayoutOnDisk(rootDir, GeneralDirectoryLayout);

            // enumerate directory
            var reportedFilePaths = new HashSet<string>();
            FileUtilities.EnumerateDirectoryEntries(
                rootDir,
                recursive: true,
                handleEntry: (dir, name, attr) =>
                {
                    if (!FileUtilities.IsDirectoryNoFollow(attr))
                    {
                        reportedFilePaths.Add(Path.Combine(dir, name));
                    }
                });

            // assert the files are the same
            XAssert.AreSetsEqual(
                dirContents.Select(f => ArtifactToString(f)),
                reportedFilePaths,
                comparer: StringComparer.InvariantCultureIgnoreCase,
                expectedResult: true);
        }

        [Feature(Features.Symlink)]
        [Feature(Features.OpaqueDirectory)]
        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(SealDirectoryKind.Full)]
        [InlineData(SealDirectoryKind.Partial)]
        [InlineData(SealDirectoryKind.Opaque)]
        [InlineData(SealDirectoryKind.SharedOpaque)]
        public void DirectorySymlinksInOutputDirectory(SealDirectoryKind dirKind)
        {
            XAssert.IsFalse(dirKind.IsSourceSeal());

            AbsolutePath rootDirAbsPath = CreateUniqueObjPath($"{dirKind}.framework");
            string rootDir = rootDirAbsPath.ToString(Context.PathTable);

            // schedule producer pip: produce opaque directory with a bunch of symlinks
            var operations = GetLayoutProducingOperationsWithDummyReadFile(rootDir, GeneralDirectoryLayout, "producer");
            var producerPipBuilder = CreatePipBuilder(operations);
            producerPipBuilder.ToolDescription = StringId.Create(Context.StringTable, "producer");

            // if dirKind is an opaque output, then just declare the root directory as an opaque output;
            // otherwise, add all outputs explicitly and schedule a seal directory pip to seal them.
            Process producerPip;
            DirectoryArtifact outputDirArtifact;
            if (dirKind.IsOpaqueOutput())
            {
                producerPipBuilder.AddOutputDirectory(rootDirAbsPath, kind: dirKind);
                producerPip = SchedulePipBuilder(producerPipBuilder).Process;
                outputDirArtifact = producerPip.DirectoryOutputs.First();
            }
            else
            {
                var dao = InferIOFromOperations(operations, force: true);
                foreach (var output in dao.Outputs) producerPipBuilder.AddOutputFile(output.Path);
                foreach (var input in dao.Dependencies) producerPipBuilder.AddInputFile(input);
                producerPip = SchedulePipBuilder(producerPipBuilder).Process;
                outputDirArtifact = SealDirectory(rootDirAbsPath, dirKind, dao.Outputs.ToArray());
            }

            // schedule consumer pips: read Version/A/file directly and via various symlinks
            var readConsumers = GetConsumers(outputDirArtifact, LookupSpecs);
            var absentProbeConsumers = GetConsumers(outputDirArtifact, AbsentProbeSpecs);
            var allConsumers = readConsumers.Concat(absentProbeConsumers).ToArray();

            var allPipIds = new[] { producerPip.PipId }.Concat(allConsumers.Select(p => p.pip.PipId)).ToArray();

            // first run, expect all cache misses
            ScheduleRunResult firstRunResult = RunSchedulerAndValidateProducedLayout().AssertCacheMiss(allPipIds);

            // assert observations for each consumer pip:
            ValidateObservations(firstRunResult, rootDir, allConsumers);

            // rerun, expect all cache hits
            RunSchedulerAndValidateProducedLayout().AssertCacheHit(allPipIds);

            // delete out dir, rerun, expect all cache hits
            FileUtilities.DeleteDirectoryContents(rootDir, deleteRootDirectory: true);
            RunSchedulerAndValidateProducedLayout().AssertCacheHit(allPipIds);

            // invalidate each consumer pip, rerun, expect only that pip to be cache miss
            foreach (var consumer in allConsumers)
            {
                InvalidatePip(consumer.pip);
                RunSchedulerAndValidateProducedLayout()
                    .AssertCacheMiss(consumer.pip.PipId)
                    .AssertCacheHit(allPipIds.Except(new[] { consumer.pip.PipId }).ToArray());
            }

            // invalidate producer pip, rerun, expect reads to be cache misses and absent probes to be cache hits
            InvalidatePip(producerPip);
            RunSchedulerAndValidateProducedLayout()
                .AssertCacheMiss(readConsumers.Select(t => t.pip.PipId).ToArray())
                .AssertCacheHit(absentProbeConsumers.Select(t => t.pip.PipId).ToArray());

            // -------------------------------- local functions ---------------------------------

            ScheduleRunResult RunSchedulerAndValidateProducedLayout()
            {
                var r = RunScheduler().AssertSuccess();
                ValidateProducedLayout();
                return r;
            }

            void ValidateProducedLayout()
            {
                foreach (var op in operations)
                {
                    switch (op.OpType)
                    {
                        case Operation.Type.CreateDir:
                            ValidateDirectoryExists(ArtifactToString(op.Path));
                            break;
                        case Operation.Type.CreateSymlink:
                            ValidateSymlinkExists(ArtifactToString(op.LinkPath), op.SymLinkFlag);
                            break;
                        case Operation.Type.WriteFile:
                            ValidateNonSymlinkFileExists(ArtifactToString(op.Path));
                            break;
                        case Operation.Type.ReadFile:
                            break;
                        default:
                            XAssert.Fail("Unexpected operation type: " + op.OpType);
                            break;
                    }
                }
            }

            IgnoreWarnings();
        }

        [Feature(Features.Symlink)]
        [Feature(Features.SealedSourceDirectory)]
        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(SealDirectoryKind.SourceAllDirectories)]
        [InlineData(SealDirectoryKind.SourceTopDirectoryOnly)]
        public void DirectorySymlinksInSealSourceDirectory(SealDirectoryKind sealKind)
        {
            XAssert.IsTrue(sealKind.IsSourceSeal());

            AbsolutePath sealDirAbsPath = CreateUniqueObjPath($"{sealKind}.framework");
            string sealDir = sealDirAbsPath.ToString(Context.PathTable);

            // create layout on disk
            List<FileArtifact> dirContents = CreateLayoutOnDisk(sealDir, GeneralDirectoryLayout);

            // schedule seal dir
            var sealDirArtifact = SealDirectory(sealDirAbsPath, sealKind, dirContents.ToArray());

            // schedule consumer pips
            var readConsumers = GetConsumers(sealDirArtifact, LookupSpecs);
            var absentProbeConsumers = AbsentProbeSpecs
                .Select(spec => {
                    var pipBuilder = CreateConsumer(sealDirArtifact, spec.Desc, spec.Lookup);
                    pipBuilder.AddInputFile(InFile(Path.Combine(sealDir, spec.Target)));
                    return (pip: SchedulePipBuilder(pipBuilder).Process, spec: spec);
                })
                .ToArray();

            var allConsumers = readConsumers.Concat(absentProbeConsumers).ToArray();

            var allPipIds = allConsumers.Select(p => p.pip.PipId).ToArray();

            // first run, all cache misses
            ScheduleRunResult firstRun = RunScheduler().AssertSuccess().AssertCacheMiss(allPipIds);

            // assert observations for each consumer pip:
            ValidateObservations(firstRun, sealDir, allConsumers);

            // run again, expect all cache hits
            RunScheduler().AssertSuccess().AssertCacheHit(allPipIds);

            // invalidate each consumer pip (by rewriting the file it read), rerun, expect only that pip to be cache miss
            foreach (var consumer in allConsumers)
            {
                InvalidatePip(consumer.pip);
                RunScheduler()
                    .AssertSuccess()
                    .AssertCacheMiss(consumer.pip.PipId)
                    .AssertCacheHit(allPipIds.Except(new[] { consumer.pip.PipId }).ToArray());
            }

            // pick the first FileArtifact from produced content, delete it, and assert that the right pips got invalidated
            string fileToDelete = ArtifactToString(dirContents[0]);
            File.Delete(fileToDelete);
            var expectedCacheMisses = allConsumers.Where(c => c.spec.IncludesObservation(sealDir, fileToDelete)).ToArray();
            var expectedCacheHits = allConsumers.Except(expectedCacheMisses).ToArray();
            Console.WriteLine("deleted: " + fileToDelete);
            Console.WriteLine("expected hits: " + string.Join(", ", expectedCacheHits.Select(pip => pip.pip.ToolDescription.ToString(Context.StringTable))));
            Console.WriteLine("expected misses: " + string.Join(", ", expectedCacheMisses.Select(pip => pip.pip.ToolDescription.ToString(Context.StringTable))));
            RunScheduler()
                .AssertCacheHit(expectedCacheHits.Select(s => s.pip.PipId).ToArray())
                .AssertCacheMiss(expectedCacheMisses.Select(s => s.pip.PipId).ToArray());

            // assert that all absent lookup paths are still absent
            XAssert.All(AbsentProbeSpecs, spec => !File.Exists(X($"{sealDir}/{spec.Lookup}")));

            // create all absent lookup paths
            foreach (var spec in AbsentProbeSpecs)
            {
                var path = X($"{sealDir}/{spec.Lookup}");
                File.WriteAllText(path, "text");
            }

            // assert that all lookup paths that were absent are now present
            XAssert.All(AbsentProbeSpecs, spec => File.Exists(X($"{sealDir}/{spec.Lookup}")));

            // run again and assert that all absent probe consumers were invalidated
            var result = RunScheduler()
                .AssertSuccess()
                .AssertCacheHit(readConsumers.Select(t => t.pip.PipId).ToArray())
                .AssertCacheMiss(absentProbeConsumers.Select(t => t.pip.PipId).ToArray());
        }

        private (Process pip, LookupSpec spec)[] GetConsumers(DirectoryArtifact dir, LookupSpec[] specs)
        {
            return specs
                .Select(spec => (pip: CreateAndScheduleConsumer(dir, spec.Desc, spec.Lookup), spec: spec))
                .ToArray();
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void UserSpecifiesOutputPathContainingDirectorySymlinksFails()
        {
            AbsolutePath rootDirAbsPath = CreateUniqueObjPath("ExplicitSymDirOutPath.framework");
            string rootDir = rootDirAbsPath.ToString(Context.PathTable);

            // schedule producer pip
            var operations = GetLayoutProducingOperationsWithDummyReadFile(rootDir, GeneralDirectoryLayout, "producer");
            var producerPipBuilder = CreatePipBuilder(operations);
            producerPipBuilder.ToolDescription = StringId.Create(Context.StringTable, "producer");

            // infer dependencies
            var dao = InferIOFromOperations(operations, force: true);

            // add (correctly) inferred inputs and outputs
            foreach (var input in dao.Dependencies) producerPipBuilder.AddInputFile(input);
            foreach (var output in dao.Outputs) producerPipBuilder.AddOutputFile(output.Path);

            // additionally declare Versions/sym-A/file as output
            var outViaSymDir = X($"{rootDir}/Versions/sym-A/file");
            producerPipBuilder.AddOutputFile(Context.PathTable, outViaSymDir);

            // schedule pip and run
            var producerPip = SchedulePipBuilder(producerPipBuilder).Process;

            // assert failure with error "Pip produced outputs
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.FailPipOutputWithNoAccessed);
            AssertLogContains(caseSensitive: false, $"No file access for output: '{outViaSymDir}'");
        }

        private const int ReadCount = 50;

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void ConcurrentAccessToHardlinksPointingToSameFile()
        {
            CreateHardLinks(out var hardlink1, out var hardlink2);
            CreateAndSchedulePipBuilder(
                description: "pip1",
                processOperations: Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip1out")),
                    Operation.ReadFile(hardlink1) * ReadCount));
            CreateAndSchedulePipBuilder(
                description: "pip2",
                processOperations: Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip2out")),
                    Operation.ReadFile(hardlink2) * ReadCount));
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void ConcurrentCreationOfHardlinksPointingToSameFile()
        {
            // Tests generally use an InMemoryArtifactContentCache and running this test causes share violations when concurrency is high
            // and page flushing is enabled on Windows systems - this seems to be a bug in either the CLR or the kernel code
            Configuration.Sandbox.FlushPageCacheToFileSystemOnStoringOutputsToCache = false;

            var srcFile = CreateSourceFileWithPrefix(prefix: "hardlink-source.txt");
            for (int i = 0; i < 666; i++)
            {
                CreateAndSchedulePipBuilder(
                    description: "create-hardlink-" + i,
                    processOperations: new[]
                    {
                        Operation.CreateHardlink(srcFile, CreateOutputFileArtifact(prefix: "hardlink-" + i))
                    });
            }
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void ConcurrentAccessToHardlinksPointingToSameFileViaSymlink()
        {
            CreateHardLinks(out var hardlink1, out var hardlink2);
            AbsolutePath symPath = CreateUniqueSourcePath(prefix: "sym-hardlink1");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ToString(symPath), ToString(hardlink1), isTargetFile: true));
            FileArtifact symFile = FileArtifact.CreateSourceFile(symPath);

            CreateAndSchedulePipBuilder(
                description: "pip1",
                processOperations: Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip1out")),
                    Operation.ReadFile(hardlink1) * 1,
                    Operation.ReadFile(symFile) * ReadCount));
            CreateAndSchedulePipBuilder(
                description: "pip2",
                processOperations: Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip2out")),
                    Operation.ReadFile(hardlink2) * ReadCount));
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void ConcurrentAccessToHardlinksPointingToSameFileViaSymlinkDirectory()
        {
            CreateHardLinks(out var hardlink1, out var hardlink2);
            AbsolutePath symDir = CreateUniqueSourcePath(prefix: "sym-dir-hardlink1");
            string symDirPath = ToString(symDir);
            string hardlink1Path = ToString(hardlink1);
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symDirPath, Path.GetDirectoryName(hardlink1Path), isTargetFile: false));
            FileArtifact hardlink1ViaSymDir = InFile(X($"{symDirPath}/{Path.GetFileName(hardlink1Path)}"));

            var pip1builder = CreatePipBuilder(
                description: "pip1",
                processOperations: Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip1out")),
                    Operation.ReadFile(hardlink1) * 1,
                    Operation.ReadFile(hardlink1ViaSymDir) * ReadCount));
            pip1builder.AddInputFile(InFile(symDirPath));
            SchedulePipBuilder(pip1builder);

            CreateAndSchedulePipBuilder(
                description: "pip2",
                processOperations: Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip2out")),
                    Operation.ReadFile(hardlink2) * ReadCount));
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void ConcurrentAccessToHardlinksPointingToSameFileIntertwinedWithOtherReads()
        {
            CreateHardLinks(out var hardlink1, out var hardlink2);
            var file1 = CreateSourceFileWithPrefix(prefix: "file1");
            var file2 = CreateSourceFileWithPrefix(prefix: "file2");

            CreateAndSchedulePipBuilder(
                description: "pip1",
                processOperations: Shuffle(Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip1out")),
                    Operation.ReadFile(hardlink1) * ReadCount,
                    Operation.ReadFile(file1) * ReadCount)));
            CreateAndSchedulePipBuilder(
                description: "pip2",
                processOperations: Shuffle(Concat(
                    Operation.WriteFile(CreateOutputFileArtifact(prefix: "pip2out")),
                    Operation.ReadFile(hardlink2) * ReadCount,
                    Operation.ReadFile(file2) * ReadCount)));
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void EnumerateDirectoryViaDirectorySymlinkShouldBeObservedAsDirectoryEnumeration()
        {
            AbsolutePath targetDirectory = CreateUniqueDirectory(ReadonlyRoot, "Target");
            CreateSourceFile(targetDirectory, "file1");
            CreateSourceFile(targetDirectory, "file2");

            AbsolutePath directorySymlink = CreateUniquePath("Symlink", ReadonlyRoot);
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(
                directorySymlink.ToString(Context.PathTable),
                targetDirectory.ToString(Context.PathTable),
                isTargetFile: false).Succeeded);

            var pipBuilder = CreatePipBuilder(new[]
            {
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(directorySymlink)),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            pipBuilder.AddInputFile(directorySymlink);

            ProcessWithOutputs processWithOutputs = SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(processWithOutputs.Process.PipId);

            CreateSourceFile(targetDirectory, "file3");
            RunScheduler().AssertCacheMiss(processWithOutputs.Process.PipId);
        }
        
        [Fact(Skip = "Skip until new LKG with correct symlink loop enumeration / deletion is deployed")]
        public void EnumeratingAndDeletingDirectoriesWithDirectorySymlinks()
        {
            AbsolutePath rootDirAbsPath = CreateUniqueObjPath("layout");
            string rootDir = rootDirAbsPath.ToString(Context.PathTable);

            string testDirectoryLayout = @"
                target/
                target/file
                target/file2
                sym_dir -> target/
                target/sym_dir_loop -> ../target/
            ";
            
            var files = CreateLayoutOnDisk(rootDir, testDirectoryLayout);
            var filePaths = files.Select(fa => fa.Path.ToString(Context.PathTable)).ToList();

            FileUtilities.EnumerateDirectoryEntries(rootDir, true /* recursive */, (dir, fileName, attributes) =>
            {
                if (attributes.HasFlag(FileAttributes.Archive) ||
                    attributes.HasFlag(FileAttributes.Normal) ||
                    attributes.HasFlag(FileAttributes.Directory | FileAttributes.ReparsePoint))
                {
                    var entry = Path.Combine(dir, fileName);
                    XAssert.Contains(filePaths, new[] { entry });
                    filePaths.Remove(entry);
                }
            });

            // Make sure file and any kind of symlink was reported as file / reparse point with directory type
            XAssert.IsTrue(filePaths.Count == 0);

            FileUtilities.DeleteDirectoryContents(rootDir, deleteRootDirectory: true);

            // Make sure deletion worked, because sym_dir_loop creates a symbolic directory link loop
            XAssert.IsFalse(Directory.Exists(rootDir));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void AbsoluteDirectorySymlinksRecursivelyResolveCorrectly()
        {
            AbsolutePath rootDirAbsPath = CreateUniqueObjPath("layout");
            string rootDir = rootDirAbsPath.ToString(Context.PathTable);

            string testDirectoryLayout = string.Format(@"
                first_root/
                first_root/target/
                first_root/target/file
                first_root/sym_dir -> target/
                first_root/nested_sym_dir -> sym_dir/
                second_root/
                second_root/target/
                second_root/target/file -> {0}/first_root/nested_sym_dir/file
                second_root/sym-dir -> target/
            ", rootDir);

            var spec = new LookupSpec(
                "readThroughSymlinkFileOverRelativeAndAbsoluteDirectorySymlinks",
                lookup: "second_root/sym-dir/file",
                target: "first_root/target/file",
                observations: new[]
                {
                    "+ first_root/target/file",
                    "+ first_root/sym_dir",
                    "+ first_root/nested_sym_dir",
                    "+ second_root/sym-dir",
                    "+ second_root/target/file"
                }
            );

            var files = CreateLayoutOnDisk(rootDir, testDirectoryLayout);
            var sealDirArtifact = SealDirectory(rootDirAbsPath, files, SealDirectoryKind.Full);
            var pip = CreateAndScheduleConsumer(sealDirArtifact, spec.Desc, spec.Lookup);

            var result = RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            ValidateObservations(result, rootDir, new[] { (pip, spec) });
        }

        [FactIfSupported(requiresSymlinkPermission: true, requiresWindowsBasedOperatingSystem: true)]
        public void JunctionsInPathDontInfluenceDirectorySymlinkResolving()
        {
            AbsolutePath junctionDirectory = CreateUniqueDirectory(prefix: "junction");
            AbsolutePath rootDirectory = CreateUniqueDirectory(prefix: "root");
            FileUtilities.CreateJunction(junctionDirectory.ToString(Context.PathTable), rootDirectory.ToString(Context.PathTable));
            string rootDir = junctionDirectory.ToString(Context.PathTable);

            string testDirectoryLayout = string.Format(@"
                base/
                base/target/
                base/target/file
                base/sym_dir -> target/
            ", rootDir);

            var spec = new LookupSpec(
                "readThroughPathWithJunctionAndResolveOnlySymlinks",
                lookup: "base/sym_dir/file",
                target: "base/target/file",
                observations: new[]
                {
                    "+ base/target/file",
                    "+ base/sym_dir",
                    "! root"
                }
            );

            var files = CreateLayoutOnDisk(rootDir, testDirectoryLayout);
            var sealDirArtifact = SealDirectory(junctionDirectory, files, SealDirectoryKind.Full);
            var pip = CreateAndScheduleConsumer(sealDirArtifact, spec.Desc, spec.Lookup);

            var result = RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            ValidateObservations(result, rootDir, new[] { (pip, spec) });
        }

        private static IEnumerable<T> Multiply<T>(int count, T elem) => Enumerable.Range(1, count).Select(_ => elem);
        private static IEnumerable<T> Concat<T>(T elem, params IEnumerable<T>[] rest) => new[] { elem }.Concat(rest.SelectMany(e => e));
        private static IEnumerable<T> Shuffle<T>(IEnumerable<T> col) => col.OrderBy(e => new Random().Next());

        private void CreateHardLinks(out FileArtifact hardLink1, out FileArtifact hardLink2)
        {
            FileArtifact file1 = CreateSourceFileWithPrefix(prefix: "hard-link-1.txt");
            AbsolutePath file2 = CreateUniqueSourcePath("hard-link-2.txt");
            var status = FileUtilities.TryCreateHardLink(ToString(file2), ToString(file1));
            XAssert.AreEqual(CreateHardLinkStatus.Success, status, "failed to create hard link");

            // sanity check
            XAssert.AreEqual(File.ReadAllText(ToString(file1)), File.ReadAllText(ToString(file2)));
            if (OperatingSystemHelper.IsUnixOS)
            {
                var unixFileUtils = new FileUtilitiesUnix();
                XAssert.AreEqual(0, unixFileUtils.GetDeviceAndInodeNumbers(ToString(file1), followSymlink: false, out var file1dev, out var file1inode));
                XAssert.AreEqual(0, unixFileUtils.GetDeviceAndInodeNumbers(ToString(file2), followSymlink: false, out var file2dev, out var file2inode));
                XAssert.AreEqual(file1dev, file2dev);
                XAssert.AreEqual(file1inode, file2inode);
            }

            hardLink1 = file1;
            hardLink2 = FileArtifact.CreateSourceFile(file2);
        }

        protected string ToString(AbsolutePath path)
        {
            return path.ToString(Context.PathTable);
        }

        private List<FileArtifact> CreateLayoutOnDisk(string rootDir, string layout)
        {
            FileUtilities.CreateDirectory(rootDir);
            var operations = GetLayoutProducingOperations(rootDir, layout);
            foreach (var op in operations)
            {
                op.PathTable = Context.PathTable;
                op.Run();
            }

            // infer and translate output file artifacts to source files, because we just created this layout before the build
            return InferIOFromOperations(operations, force: true)
                .Outputs
                .Select(fileArtifact => InFile(ArtifactToString(fileArtifact)))
                .ToList();
        }

        private ProcessBuilder CreateConsumer(DirectoryArtifact inputDir, string description, string pathRelativeToOpaqueDir)
        {
            var filePath = X($"{ArtifactToString(inputDir)}/{pathRelativeToOpaqueDir}");
            var pipBuilder = CreatePipBuilder(new Operation[]
            {
                OpReadDummySourceFile(description), // dummy source file dependency that can be easily invalidated
                Operation.WriteFile(CreateOutputFileArtifact(prefix: OutPrefix + description)), // dummy output file
                Operation.ReadFile(InFile(filePath), doNotInfer: true) // filePath may contain symlinks
            });
            pipBuilder.AddInputDirectory(inputDir);
            pipBuilder.ToolDescription = StringId.Create(Context.StringTable, description);
            return pipBuilder;
        }

        private Process CreateAndScheduleConsumer(DirectoryArtifact inputDir, string description, string pathRelativeToOpaqueDir)
        {
            return SchedulePipBuilder(CreateConsumer(inputDir, description, pathRelativeToOpaqueDir)).Process;
        }

        private void ValidateObservations(ScheduleRunResult result, string rootDir, (Process pip, LookupSpec spec)[] allConsumers)
        {
            foreach (var consumer in allConsumers)
            {
                ValidateObservations(
                    result,
                    consumer.pip,
                    consumer.spec
                        .TranslateObservations(rootDir)
                        // exclude paths that are explicitly declared as inputs because those don't have to be reported
                        .Where(t => !(t.action == LookupSpec.TransalteAction.ShouldContain && consumer.pip.Dependencies.Any(fileDep => ArtifactToString(fileDep) == t.fullPath)))
                        .ToArray());
            }
        }

        private void ValidateObservations(ScheduleRunResult result, Process pip, (LookupSpec.TransalteAction action, string fullPath)[] observationSpecs)
        {
            XAssert.IsTrue(result.PathSets[pip.PipId].HasValue);
            var observedPaths = new HashSet<AbsolutePath>(result.PathSets[pip.PipId].Value.Paths.Select(e => e.Path));
            var observedPathsStr = string.Join(
                string.Empty,
                observedPaths.Select(p => Environment.NewLine + "  " + p.ToString(Context.PathTable)).OrderBy(s => s));
            var pipDesc = pip.ToolDescription.ToString(Context.StringTable);

            foreach (var (action, path) in observationSpecs)
            {
                var absPath = AbsolutePath.Create(Context.PathTable, path);
                switch (action)
                {
                    case LookupSpec.TransalteAction.ShouldContain:
                    {
                        XAssert.IsTrue(
                            observedPaths.Contains(absPath),
                            $"Pip '{pipDesc}' was expected to contain '{path}' but it doesn't.  Observed paths: {observedPathsStr}");
                        break;
                    }
                    case LookupSpec.TransalteAction.ShouldNotContain:
                    {
                        XAssert.IsFalse(
                            observedPaths.Contains(absPath),
                            $"Pip '{pipDesc}' was expected to NOT contain '{path}' but it does.  Observed paths: {observedPathsStr}");
                        break;
                    }
                    case LookupSpec.TransalteAction.ShouldNotExistInAnyPath:
                    {
                        XAssert.IsFalse(
                            observedPaths.Select(p => p.ToString(Context.PathTable)).Where(p => p.Contains(path)).Any(),
                            $"Pip '{pipDesc}' was expected to NOT contain '{path}' in any observed path but it does.  Observed paths: {observedPathsStr}");
                        break;
                    }
                }
            }
        }

        private const string InPrefix = "__in__";
        private const string OutPrefix = "__out__";

        private void InvalidatePip(Process pip)
        {
            var inFile = pip.Dependencies.First(f => ArtifactToString(f).Contains(InPrefix));
            WriteSourceFile(inFile);
        }

        private FileArtifact InFile(string path)
            => FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, path));

        private FileArtifact OutFile(string path)
            => FileArtifact.CreateOutputFile(AbsolutePath.Create(Context.PathTable, path));

        private DirectoryArtifact OutDir(string path)
            => DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, path));

        private Operation OpReadDummySourceFile(string desc)
            => Operation.ReadFile(CreateSourceFileWithPrefix(prefix: InPrefix + desc));

        private Operation OpCreateDir(string dirPath)
            => Operation.CreateDir(OutDir(dirPath), doNotInfer: true);

        private Operation OpWriteFile(string filePath)
            => Operation.WriteFile(OutFile(filePath), doNotInfer: true);

        private Operation OpCreateSym(string spec, Operation.SymbolicLinkFlag flag)
        {
            var splits = spec
                .Split(new string[] { "->" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToArray();
            XAssert.AreEqual(2, splits.Length);
            string path = splits[0];
            string reparsePoint = splits[1];
            return Operation.CreateSymlink(OutFile(path), reparsePoint, flag, doNotInfer: true);
        }

        private void ValidateDirectoryExists(string dir)
            => ValidateFileExists(dir, PathExistence.ExistsAsDirectory);

        private void ValidateSymlinkExists(string file, Operation.SymbolicLinkFlag flag)
            => ValidateFileExists(file, PathExistence.ExistsAsFile, isSymlink: true, isDirectorySymlink: flag == Operation.SymbolicLinkFlag.DIRECTORY);

        private void ValidateNonSymlinkFileExists(string file)
            => ValidateFileExists(file, PathExistence.ExistsAsFile, isSymlink: false);

        private void ValidateFileExists(string file, PathExistence expected, bool? isSymlink = null, bool isDirectorySymlink = false)
        {
            var maybePathExistence = FileUtilities.TryProbePathExistence(file, followSymlink: false);
            XAssert.IsTrue(maybePathExistence.Succeeded, $"Failed to determine path existence for '{file}'");
            XAssert.AreEqual(expected, maybePathExistence.Result, $"Wrong file existence for file '{file}'");
            if (isSymlink.HasValue)
            {
                XAssert.AreEqual(isSymlink.Value, ReparsePointTests.IsSymlink(file, isDirectorySymlink), $"Wrong symlink attribute for file '{file}'");
            }
        }
    }
}
