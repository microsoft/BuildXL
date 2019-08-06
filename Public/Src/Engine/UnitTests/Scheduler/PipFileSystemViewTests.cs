// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using SortedFileArtifacts =
    BuildXL.Utilities.Collections.SortedReadOnlyArray<BuildXL.Utilities.FileArtifact, BuildXL.Utilities.OrdinalFileArtifactComparer>;
using System.Text.RegularExpressions;

namespace Test.BuildXL.Scheduler
{
    public class PipFileSystemViewTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public PipFileSystemViewTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static string a = A("c", "a");
        private static string b = A("c", "a", "b");
        private static string c = A("c", "a", "b", "c");
        private static string d = A("c", "a", "b", "d");
        private static string e = A("c", "a", "b", "e");
        private static string f = A("c", "a", "b", "f");
        private static string g = A("c", "a", "b", "g");
        private static string h = A("c", "a", "b", "g", "h");
        private static string i = A("c", "a", "i");
        private static string j = A("c", "a", "j");
        private static string k = A("c", "a", "j", "k");
        private static string l = A("c", "a", "j", "k", "l");
        private static HashSet<string> s_emptySet = new HashSet<string>();

        [Fact]
        public void Basic()
        {            
            // PipOne tests
            var harness1 = new Harness(TestOutputDirectory);
            harness1.AddPath(c);
            harness1.AddPath(d);
            harness1.AddPath(e);
            harness1.AddPath(f);
            harness1.AddPath(h);
            harness1.EnumerateDirAndAssert(b, expectedPaths: new HashSet<string> { c, d, e, f, g });
            harness1.EnumerateDirAndAssert(a, expectedPaths: new HashSet<string> { b });

            // PipTwo tests
            var harness2 = new Harness(TestOutputDirectory);
            harness2.AddPath(h);
            harness2.EnumerateDirAndAssert(b, expectedPaths: new HashSet<string> { g });

            // PipThree tests
            var harness3 = new Harness(TestOutputDirectory);
            harness3.AddPath(b);
            harness3.EnumerateDirAndAssert(b, expectedPaths: s_emptySet);
            harness3.EnumerateDirAndAssert(a, expectedPaths: new HashSet<string> { b });

            // PipFour tests
            var harness4 = new Harness(TestOutputDirectory);
            harness4.AddPath(l);
            harness4.EnumerateDirAndAssert(a, expectedPaths: new HashSet<string> { j });
        }

        [Fact]
        public void SealDirectories()
        {
            var harness1 = new Harness(TestOutputDirectory);
            harness1.AddSealDir(harness1.SealDir(b, c, d));

            harness1.EnumerateDirAndAssert(a, expectedPaths: new HashSet<string> { b });
            harness1.EnumerateDirAndAssert(b, expectedPaths: new HashSet<string> { c, d });
            harness1.EnumerateDirAndAssert(c, expectedPaths: s_emptySet);
            harness1.EnumerateDirAndAssert(k, expectedPaths: s_emptySet);

            var harness2 = new Harness(TestOutputDirectory);
            harness2.AddSealDir(harness2.SealDir(j, l));
            harness2.EnumerateDirAndAssert(j, expectedPaths: new HashSet<string> { k });

            var harness3 = new Harness(TestOutputDirectory);
            harness3.AddSealDir(harness3.SealDir(a, h));
            harness3.EnumerateDirAndAssert(a, expectedPaths: new HashSet<string> { b });
            harness3.EnumerateDirAndAssert(b, expectedPaths: new HashSet<string> { g });
            harness3.EnumerateDirAndAssert(g, expectedPaths: new HashSet<string> { h });
            harness3.EnumerateDirAndAssert(c, expectedPaths: s_emptySet);
        }

        [Fact]
        public void NonExistentPath()
        {
            var harness = new Harness(TestOutputDirectory);

            harness.EnumerateDirAndAssert(d, expectedPaths: s_emptySet);

            // Use a path which is not created before we create the CachedFileSystemView.
            var x = A("x");

            // Enumerate a path whose index is equal to or greater than CachedFileSystemView's size.
            // Because we create the path after we construct the CachedFileSystemView withs
            harness.EnumerateDirAndAssert(x, expectedPaths: s_emptySet);
        }

        private sealed class Harness
        {
            public readonly PipExecutionContext Context;

            public readonly PathTable Table;

            private readonly Dictionary<DirectoryArtifact, SortedFileArtifacts> m_directories =
                new Dictionary<DirectoryArtifact, SortedFileArtifacts>();

            private uint m_nextDirectorySealId;

            private PipFileSystemView m_fileSystem;

            private DummyPipExecutionEnvironment m_env;

            public Harness(string testOutputDirectory)
            {
                Context = BuildXLContext.CreateInstanceForTesting();
                var config = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, AbsolutePath.Create(Context.PathTable, System.IO.Path.Combine(testOutputDirectory, "config.dc")));
                m_env = new DummyPipExecutionEnvironment(CreateLoggingContextForTest(), Context, config, sandboxConnection: GetSandboxConnection());
                var sealContentsCache = new ConcurrentBigMap<DirectoryArtifact, int[]>();

                Table = Context.PathTable;

                // Create the paths in the PathTable before creating the CachedFileSystemView
                Path(a);
                Path(b);
                Path(c);
                Path(d);
                Path(e);
                Path(f);
                Path(g);
                Path(h);
                Path(i);
                Path(j);
                Path(k);
                Path(l);

                m_fileSystem = new PipFileSystemView();
                m_fileSystem.Initialize(Table);
            }

            public AbsolutePath Path(string p)
            {
                return AbsolutePath.Create(Table, p);
            }

            public void AddPath(string strPath)
            {
                m_fileSystem.AddPath(Table, Path(strPath));
            }

            public void AddSealDir(DirectoryArtifact sealDir)
            {
                m_fileSystem.AddSealDirectoryContents(m_env, sealDir);
            }

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
            public DirectoryArtifact SealDir(string root, params string[] contents)
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

                    artifacts[i] = FileArtifact.CreateSourceFile(path);
                }

                DirectoryArtifact newArtifact = DirectoryArtifact.CreateDirectoryArtifactForTesting(rootPath, m_nextDirectorySealId++);
                m_env.SetSealedDirectoryContents(newArtifact, artifacts);
                return newArtifact;
            }
        }
    }
}
