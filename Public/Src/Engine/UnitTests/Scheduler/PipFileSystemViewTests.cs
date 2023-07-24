// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.IO;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.Scheduler
{
    public class PipFileSystemViewTests : XunitBuildXLTest
    {
        public PipFileSystemViewTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static readonly string s_a = A("c", "a");
        private static readonly string s_b = A("c", "a", "b");
        private static readonly string s_c = A("c", "a", "b", "c");
        private static readonly string s_d = A("c", "a", "b", "d");
        private static readonly string s_e = A("c", "a", "b", "e");
        private static readonly string s_f = A("c", "a", "b", "f");
        private static readonly string s_g = A("c", "a", "b", "g");
        private static readonly string s_h = A("c", "a", "b", "g", "h");
        private static readonly string s_i = A("c", "a", "i");
        private static readonly string s_j = A("c", "a", "j");
        private static readonly string s_k = A("c", "a", "j", "k");
        private static readonly string s_l = A("c", "a", "j", "k", "l");
        private static readonly HashSet<string> s_emptySet = new();

        [Fact]
        public void Basic()
        {
            // PipOne tests
            var harness1 = new Harness(TestOutputDirectory);
            harness1.AddPath(s_c);
            harness1.AddPath(s_d);
            harness1.AddPath(s_e);
            harness1.AddPath(s_f);
            harness1.AddPath(s_h);
            harness1.EnumerateDirAndAssert(s_b, expectedPaths: new HashSet<string> { s_c, s_d, s_e, s_f, s_g });
            harness1.EnumerateDirAndAssert(s_a, expectedPaths: new HashSet<string> { s_b });

            // PipTwo tests
            var harness2 = new Harness(TestOutputDirectory);
            harness2.AddPath(s_h);
            harness2.EnumerateDirAndAssert(s_b, expectedPaths: new HashSet<string> { s_g });

            // PipThree tests
            var harness3 = new Harness(TestOutputDirectory);
            harness3.AddPath(s_b);
            harness3.EnumerateDirAndAssert(s_b, expectedPaths: s_emptySet);
            harness3.EnumerateDirAndAssert(s_a, expectedPaths: new HashSet<string> { s_b });

            // PipFour tests
            var harness4 = new Harness(TestOutputDirectory);
            harness4.AddPath(s_l);
            harness4.EnumerateDirAndAssert(s_a, expectedPaths: new HashSet<string> { s_j });
        }

        [Fact]
        public void SealDirectories()
        {
            var harness1 = new Harness(TestOutputDirectory);
            harness1.AddSealDir(harness1.SealDir(root: s_b, outputDir: false, s_c, s_d));

            harness1.EnumerateDirAndAssert(s_a, expectedPaths: new HashSet<string> { s_b });
            harness1.EnumerateDirAndAssert(s_b, expectedPaths: new HashSet<string> { s_c, s_d });
            harness1.EnumerateDirAndAssert(s_c, expectedPaths: s_emptySet);
            harness1.EnumerateDirAndAssert(s_k, expectedPaths: s_emptySet);

            var harness2 = new Harness(TestOutputDirectory);
            harness2.AddSealDir(harness2.SealDir(root: s_j, outputDir: false, s_l));
            harness2.EnumerateDirAndAssert(s_j, expectedPaths: new HashSet<string> { s_k });

            var harness3 = new Harness(TestOutputDirectory);
            harness3.AddSealDir(harness3.SealDir(root: s_a, outputDir: false, s_h));
            harness3.EnumerateDirAndAssert(s_a, expectedPaths: new HashSet<string> { s_b });
            harness3.EnumerateDirAndAssert(s_b, expectedPaths: new HashSet<string> { s_g });
            harness3.EnumerateDirAndAssert(s_g, expectedPaths: new HashSet<string> { s_h });
            harness3.EnumerateDirAndAssert(s_c, expectedPaths: s_emptySet);
        }

        [Fact]
        public void NonExistentPath()
        {
            var harness = new Harness(TestOutputDirectory);

            harness.EnumerateDirAndAssert(s_d, expectedPaths: s_emptySet);

            // Use a path which is not created before we create the CachedFileSystemView.
            var x = A("x");

            // Enumerate a path whose index is equal to or greater than CachedFileSystemView's size.
            // Because we create the path after we construct the CachedFileSystemView withs
            harness.EnumerateDirAndAssert(x, expectedPaths: s_emptySet);
        }

        [Fact]
        public void NonExistingMemberOfDynamicDirectoryExcludedFromEnumeration()
        {
            var harness  = new Harness(TestOutputDirectory);
            var outputDirectory = Path.Combine(TestOutputDirectory, "outputDir");
            var existingMember = Path.Combine(outputDirectory, "existing");
            var nonExistingMember = Path.Combine(outputDirectory, "nonExisting");

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(existingMember, "existing");

            var existingHash = ContentHashingUtilities.HashFile(existingMember);
            var existingInfo = FileMaterializationInfo.CreateWithUnknownLength(existingHash);
            var nonExistingInfo = FileMaterializationInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile);

            var outputDirectoryArtifact = harness.SealDir(root: outputDirectory, outputDir: true, existingMember, nonExistingMember);
            var opContext = OperationContext.CreateUntracked(new LoggingContext("Test"));
            var existingArtifact = FileArtifact.CreateOutputFile(harness.Path(existingMember));
            var nonExistingArtifact = FileArtifact.CreateOutputFile(harness.Path(nonExistingMember));
            harness.Env.State.FileContentManager.ReportOutputContent(opContext, 0, existingArtifact, existingInfo, PipOutputOrigin.Produced);
            harness.Env.State.FileContentManager.ReportOutputContent(opContext, 0, nonExistingArtifact, nonExistingInfo, PipOutputOrigin.Produced);
            harness.Env.RegisterDynamicOutputDirectory(outputDirectoryArtifact);
            harness.Env.State.FileContentManager.ReportDynamicDirectoryContents(
                outputDirectoryArtifact,
                new[]
                {
                    FileArtifactWithAttributes.Create(existingArtifact, FileExistence.Required),
                    FileArtifactWithAttributes.Create(nonExistingArtifact, FileExistence.Temporary)
                },
                PipOutputOrigin.Produced);

            harness.AddSealDir(outputDirectoryArtifact);
            harness.EnumerateDirAndAssert(outputDirectory, new HashSet<string> { existingMember });
        }


        private sealed class Harness
        {
            public readonly PipExecutionContext Context;

            public readonly PathTable Table;

            private uint m_nextDirectorySealId;

            private readonly PipFileSystemView m_fileSystem;

            public DummyPipExecutionEnvironment Env { get; init; }

            public Harness(string testOutputDirectory)
            {
                Context = BuildXLContext.CreateInstanceForTesting();
                var config = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, AbsolutePath.Create(Context.PathTable, System.IO.Path.Combine(testOutputDirectory, "config.dc")));

                var isSubstUsed = FileUtilities.TryGetSubstSourceAndTarget(testOutputDirectory, out var substSource, out var substTarget, out var errorMessage);
                XAssert.IsFalse(!isSubstUsed && errorMessage != null, errorMessage);

                Env = new DummyPipExecutionEnvironment(
                    CreateLoggingContextForTest(),
                    Context,
                    config,
                    subst: isSubstUsed
                        ? (substSource, substTarget) 
                        : default((string, string)?),
                    sandboxConnection: GetSandboxConnection());

                Table = Context.PathTable;

                // Create the paths in the PathTable before creating the CachedFileSystemView
                Path(s_a);
                Path(s_b);
                Path(s_c);
                Path(s_d);
                Path(s_e);
                Path(s_f);
                Path(s_g);
                Path(s_h);
                Path(s_i);
                Path(s_j);
                Path(s_k);
                Path(s_l);

                m_fileSystem = new PipFileSystemView();
                m_fileSystem.Initialize(Table);
            }

            public AbsolutePath Path(string p) => AbsolutePath.Create(Table, p);

            public void AddPath(string strPath) => m_fileSystem.AddPath(Table, Path(strPath));

            public void AddSealDir(DirectoryArtifact sealDir) => m_fileSystem.AddSealDirectoryContents(Env, sealDir);

            public void EnumerateDirAndAssert(string dir, HashSet<string> expectedPaths)
            {
                var expectedExistence = expectedPaths.Count != 0 ? PathExistence.ExistsAsDirectory : PathExistence.Nonexistent;
                HashSet<string> actualPaths = new HashSet<string>();

                var isAnyDuplicate = false;
                var existence = m_fileSystem.EnumerateDirectory(Table, Path(dir), (path, fileName) =>
                {
                    var isAdded = actualPaths.Add(path.ToString(Table));
                    isAnyDuplicate = isAnyDuplicate ? true : !isAdded;
                });

                XAssert.AreEqual(expectedExistence, existence);
                XAssert.IsFalse(isAnyDuplicate, "Directory enumeration should not return duplicate members");
                XAssert.IsTrue(expectedPaths.SetEquals(actualPaths), "Directory contains different paths than expected");
            }

            /// <summary>
            /// Creates a directory artifact which may be queried with <see cref="ListSealedDirectoryContents"/>,
            /// and whose members may be queried with <see cref="TryQuerySealedInputContent"/>.
            /// Each mentioned path must have been added explicitly with <see cref="AddAbsentPath"/> or <see cref="AddFile"/>
            /// </summary>
            public DirectoryArtifact SealDir(string root, bool outputDir, params string[] contents)
            {
                var rootPath = Path(root);

                FileArtifact[] artifacts = new FileArtifact[contents.Length];
                for (int i = 0; i < contents.Length; i++)
                {
                    var path = Path(contents[i]);
                    if (!path.IsWithin(Context.PathTable, rootPath))
                    {
                        XAssert.Fail("Root {0} does not contain {1}", root, contents[i]);
                    }

                    artifacts[i] = outputDir ? FileArtifact.CreateOutputFile(path) : FileArtifact.CreateSourceFile(path);
                }

                DirectoryArtifact newArtifact = outputDir
                    ? DirectoryArtifact.CreateWithZeroPartialSealId(rootPath)
                    : DirectoryArtifact.CreateDirectoryArtifactForTesting(rootPath, m_nextDirectorySealId++);
                Env.SetSealedDirectoryContents(newArtifact, artifacts);
                return newArtifact;
            }
        }
    }
}
