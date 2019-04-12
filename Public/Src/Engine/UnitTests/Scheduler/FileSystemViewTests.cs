// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    using StructTuple = StructTuple;
    using PathTuple = ValueTuple<AbsolutePath, AbsolutePath>;
    using System.Collections.Concurrent;
    using global::BuildXL.Scheduler.Graph;
    using global::BuildXL.Scheduler.FileSystem;

    [Trait("Category", "FileContentManagerTests")]
    public sealed partial class FileSystemViewTests : PipTestBase
    {
        public FileSystemViewTests(ITestOutputHelper output) : base(output)
        {
        }
        [Fact]
        public void TestExistenceInferenceOfRealFileSystem()
        {
            var harness = new Harness(Context.PathTable);

            harness.AddRealPath(A("c", "1", "a", "foo.txt"), PathExistence.ExistsAsFile);
            harness.AddRealPath(A("c", "2", "a", "foo.txt"), PathExistence.ExistsAsFile);
            harness.AddRealPath(A("c", "3", "a", "foo.txt"), PathExistence.ExistsAsFile);
            harness.AddRealPath(A("c", "4"), PathExistence.ExistsAsFile);

            harness.VerifyExistence(A("c", "1", "a", "foo.txt"), FileSystemViewMode.Real, PathExistence.ExistsAsFile, shouldBeProbed: true);

            // Parent paths of existent path are inferred as directories so no probe is necessary
            harness.VerifyExistence(A("c", "1", "a"), FileSystemViewMode.Real, PathExistence.ExistsAsDirectory, shouldBeProbed: false);
            harness.VerifyExistence(A("c", "1", ""), FileSystemViewMode.Real, PathExistence.ExistsAsDirectory, shouldBeProbed: false);
            harness.VerifyExistence(A("c", ""), FileSystemViewMode.Real, PathExistence.ExistsAsDirectory, shouldBeProbed: false);

            // Path in same directory is probed
            harness.VerifyExistence(A("c", "3"), FileSystemViewMode.Real, PathExistence.ExistsAsDirectory, shouldBeProbed: true);

            // Enumerate the c:\ directory and expect members
            harness.ExpectEnumerationMembers(A("c", ""), FileSystemViewMode.Real, Dir(A("c", "1")), Dir(A("c", "2")), Dir(A("c", "3")), File(A("c", "4")));

            // Existent paths in enumerated directories do not need to be probed
            harness.VerifyExistence(A("c", "2"), FileSystemViewMode.Real, PathExistence.ExistsAsDirectory, shouldBeProbed: false);
            harness.VerifyExistence(A("c", "4"), FileSystemViewMode.Real, PathExistence.ExistsAsFile, shouldBeProbed: false);

            // Absent paths in enumerated directories do not need to be probed
            harness.VerifyExistence(A("c", "5"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: false);
            harness.VerifyExistence(A("c", "nonexistent"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: false);

            // Child paths of absent paths in enumerated directories do not need to be probed
            harness.VerifyExistence(A("c", "missing", "subpath", "deep", "chain"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: false);
            harness.VerifyExistence(A("c", "missing", "subpath", "deep"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: false);
            harness.VerifyExistence(A("c", "missing", "subpath"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: false);
            harness.VerifyExistence(A("c", "missing"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: false);
        }

        [Fact]
        public void TestGraphFileSystem()
        {
            var harness = new Harness(Context.PathTable);

            // Rewritten file
            harness.AddArtifact(A("b", "dir", "out", "bin", "a2.dll"), 0);
            harness.AddArtifact(A("b", "dir", "out", "bin", "a2.dll"), 1);

            harness.AddArtifact(A("b", "dir", "out", "bin", "a3.dll"), 3);
            harness.AddArtifact(A("b", "dir", "out", "gen1.cs"), 1);
            harness.AddArtifact(A("b", "dir", "out", "gen2.cs"), 2);
            harness.AddArtifact(A("b", "dir", "a.cs"), 0);
            harness.AddArtifact(A("b", "dir", "b.cs"), 0);
            harness.AddArtifact(A("b", "dir", "output.dll"), 1);
            harness.AddArtifact(A("b", "src", "foo.txt"), 0);
            harness.AddArtifact(A("b", "src", "jab.txt"), 0);

            harness.AddRealPath(A("b", "src", "real.ds"), PathExistence.ExistsAsFile);

            harness.VerifyGraphExistence(A("b", "dir"), FileSystemViewMode.FullGraph, PathExistence.ExistsAsDirectory);
            harness.VerifyGraphExistence(A("b", "dir", "out"), FileSystemViewMode.FullGraph, PathExistence.ExistsAsDirectory);
            harness.VerifyGraphExistence(A("b", "dir", "out", "bin", "a2.dll"), FileSystemViewMode.FullGraph, PathExistence.ExistsAsFile);
            harness.VerifyGraphExistence(A("b", "dir", "out", "bin", "a3.dll"), FileSystemViewMode.FullGraph, PathExistence.ExistsAsFile);

            harness.VerifyGraphExistence(A("b", "dir"), FileSystemViewMode.Output, PathExistence.ExistsAsDirectory);
            harness.VerifyGraphExistence(A("b", "dir", "out"), FileSystemViewMode.Output, PathExistence.ExistsAsDirectory);
            harness.VerifyGraphExistence(A("b", "dir", "out", "bin", "a2.dll"), FileSystemViewMode.Output, PathExistence.ExistsAsFile);
            harness.VerifyGraphExistence(A("b", "dir", "out", "bin", "a3.dll"), FileSystemViewMode.Output, PathExistence.ExistsAsFile);

            // Paths which are non-existent in output graph but existent in full graph
            // Also verify they are non-existent in real fs
            harness.VerifyExistence(A("b", "src", "foo.txt"), FileSystemViewMode.Real, PathExistence.Nonexistent, shouldBeProbed: true);
            harness.VerifyGraphExistence(A("b", "src", "foo.txt"), FileSystemViewMode.FullGraph, PathExistence.ExistsAsFile);
            harness.VerifyGraphExistence(A("b", "src", "foo.txt"), FileSystemViewMode.Output, PathExistence.Nonexistent);

            harness.VerifyExistence(A("b", "src"), FileSystemViewMode.Real, PathExistence.ExistsAsDirectory, shouldBeProbed: true);
            harness.VerifyGraphExistence(A("b", "src"), FileSystemViewMode.FullGraph, PathExistence.ExistsAsDirectory);
            harness.VerifyGraphExistence(A("b", "src"), FileSystemViewMode.Output, PathExistence.Nonexistent);

            // Enumerate same directories in output graph and full graph
            harness.ExpectEnumerationMembers(A("b", ""), FileSystemViewMode.Output, Dir(A("b", "dir")));
            harness.ExpectEnumerationMembers(A("b", ""), FileSystemViewMode.FullGraph, Dir(A("b", "dir")), Dir(A("b", "src")));

            harness.ExpectEnumerationMembers(A("b", "dir"), FileSystemViewMode.Output, Dir(A("b", "dir", "out")), File(A("b", "dir", "output.dll")));
            harness.ExpectEnumerationMembers(A("b", "dir"), FileSystemViewMode.FullGraph, Dir(A("b", "dir", "out")), File(A("b", "dir", "output.dll")), File(A("b", "dir", "a.cs")), File(A("b", "dir", "b.cs")));

            // Query the same path in all three file systems which only exists on real file system
            harness.VerifyExistence(A("b", "src", "real.ds"), FileSystemViewMode.Real, PathExistence.ExistsAsFile, shouldBeProbed: true);
            harness.ResetProbes();

            harness.VerifyGraphExistence(A("b", "src", "real.ds"), FileSystemViewMode.FullGraph, PathExistence.Nonexistent);
            harness.ResetProbes();

            harness.VerifyGraphExistence(A("b", "src", "real.ds"), FileSystemViewMode.Output, PathExistence.Nonexistent);
            harness.ResetProbes();
        }

        private FileOrDirectoryArtifact File(string path)
        {
            return FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, path));
        }

        private FileOrDirectoryArtifact Dir(string path)
        {
            return DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, path));
        }

        public AbsolutePath GetPath(string path)
        {
            return AbsolutePath.Create(Context.PathTable, path);
        }

        private class Harness : ILocalDiskFileSystemView, IPipGraphFileSystemView
        {
            private readonly PathTable m_pathTable;

            private readonly HashSet<AbsolutePath> m_files = new HashSet<AbsolutePath>();

            private readonly ConcurrentDictionary<AbsolutePath, HashSet<FileOrDirectoryArtifact>> m_directories = new ConcurrentDictionary<AbsolutePath, HashSet<FileOrDirectoryArtifact>>();

            private readonly ConcurrentDictionary<AbsolutePath, int> m_latestWriteCountByPath = new ConcurrentDictionary<AbsolutePath, int>();

            public readonly HashSet<PathTuple> TrackedAbsentPairs = new HashSet<PathTuple>();
            public readonly HashSet<AbsolutePath> EnumeratedDirectories = new HashSet<AbsolutePath>();
            public readonly HashSet<AbsolutePath> AllProbePaths = new HashSet<AbsolutePath>();
            public readonly HashSet<AbsolutePath> ProbePaths = new HashSet<AbsolutePath>();
            public readonly HashSet<AbsolutePath> TryGetLatestFileArtifactPaths = new HashSet<AbsolutePath>();

            public readonly FileSystemView FileSystemView;

            public Harness(PathTable pathTable)
            {
                m_pathTable = pathTable;
                FileSystemView = new FileSystemView(pathTable, this, this);
            }

            public void ResetProbes()
            {
                AllProbePaths.Clear();
            }

            public void VerifyGraphExistence(string fullPath, FileSystemViewMode mode, PathExistence existence)
            {
                Contract.Assert(mode != FileSystemViewMode.Real);

                // Graph queries should never probe the file system
                VerifyExistence(fullPath, mode, existence, shouldBeProbed: false);

                var path = GetPath(fullPath);

                if (existence == PathExistence.ExistsAsFile)
                {
                    // File should be queried from underlying pip graph file system
                    Assert.True(TryGetLatestFileArtifactPaths.Contains(path) && m_latestWriteCountByPath.ContainsKey(path));
                }
                else
                {
                    // Directory and non-existent paths will query underlying pip graph file system but will not be found
                    Assert.True(TryGetLatestFileArtifactPaths.Contains(path));
                    if (mode == FileSystemViewMode.FullGraph)
                    {
                        Assert.False(m_latestWriteCountByPath.ContainsKey(path));
                    }
                    else
                    {
                        Assert.True(!m_latestWriteCountByPath.ContainsKey(path) || m_latestWriteCountByPath[path] == 0);
                    }
                }
            }

            public void VerifyExistence(string fullPath, FileSystemViewMode mode, PathExistence existence, bool shouldBeProbed)
            {
                ProbePaths.Clear();
                var path = GetPath(fullPath);
                var actualExistence = FileSystemView.GetExistence(path, mode);
                Assert.Equal(existence, actualExistence.Result);
                Assert.Equal(shouldBeProbed, ProbePaths.Contains(path));
            }

            public void ExpectEnumerationMembers(string path, FileSystemViewMode mode, params FileOrDirectoryArtifact[] expectedMembers)
            {
                Dictionary<AbsolutePath, PathExistence> expectations = expectedMembers.ToDictionary(a => a.Path, a => a.IsFile ? PathExistence.ExistsAsFile : PathExistence.ExistsAsDirectory);

                AssertSuccess(
                    FileSystemView.TryEnumerateDirectory(
                        GetPath(path),
                        mode,
                        (memberName, memberPath, existence) =>
                        {
                            Assert.True(expectations.Remove(memberPath), "Path not found in expected member paths");
                        })
                );

                Assert.True(expectations.Count == 0, "Expected path not encountered during enumeration");
            }

            public void AddArtifact(string path, int rewriteCount)
            {
                AddArtifact(new FileArtifact(GetPath(path), rewriteCount));
            }

            public AbsolutePath GetPath(string path)
            {
                return AbsolutePath.Create(m_pathTable, path);
            }

            private void AddArtifact(FileArtifact artifact)
            {
                FileSystemView.AddArtifact(artifact);
                m_latestWriteCountByPath.AddOrUpdate(artifact.Path, artifact.RewriteCount, (k, v) => Math.Max(artifact.RewriteCount, v));
            }

            public void AddRealPath(string fullPath, PathExistence existence)
            {
                if (existence == PathExistence.Nonexistent)
                {
                    return;
                }

                var path = AbsolutePath.Create(m_pathTable, fullPath);

                FileOrDirectoryArtifact member = FileOrDirectoryArtifact.Invalid;
                while (path.IsValid)
                {
                    if (existence == PathExistence.ExistsAsFile)
                    {
                        m_files.Add(path);
                        member = new FileArtifact(path);
                    }
                    else
                    {
                        var members = m_directories.GetOrAdd(path, p => new HashSet<FileOrDirectoryArtifact>());
                        if (member.IsValid)
                        {
                            if (!members.Add(member))
                            {
                                return;
                            }
                        }

                        member = DirectoryArtifact.CreateWithZeroPartialSealId(path);
                    }

                    existence = PathExistence.ExistsAsDirectory;
                    path = path.GetParent(m_pathTable);
                }
            }

            public Possible<PathExistence> TryEnumerateDirectoryAndTrackMembership(AbsolutePath path, Action<string, FileAttributes> handleEntry)
            {
                AssertAdd(EnumeratedDirectories, path);

                HashSet<FileOrDirectoryArtifact> members;
                if (m_directories.TryGetValue(path, out members))
                {
                    foreach (var member in members)
                    {
                        handleEntry(member.Path.GetName(m_pathTable).ToString(m_pathTable.StringTable), member.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal);
                    }

                    return PathExistence.ExistsAsDirectory;
                }

                return m_files.Contains(path) ? PathExistence.ExistsAsFile : PathExistence.Nonexistent;
            }

            public Possible<PathExistence, Failure> TryProbeAndTrackPathForExistence(ExpandedAbsolutePath path, bool? isReadOnly = default)
            {
                AssertAdd(ProbePaths, path.Path);
                AssertAdd(AllProbePaths, path.Path);

                if (m_files.Contains(path.Path))
                {
                    return PathExistence.ExistsAsFile;
                }

                if (m_directories.ContainsKey(path.Path))
                {
                    return PathExistence.ExistsAsDirectory;
                }

                return PathExistence.Nonexistent;
            }

            public bool TrackAbsentPath(AbsolutePath trackedParentPath, AbsolutePath absentChildPath)
            {
                Assert.True(absentChildPath.IsWithin(m_pathTable, trackedParentPath));
                Assert.True(absentChildPath.IsWithin(m_pathTable, trackedParentPath));
                TrackedAbsentPairs.Add((trackedParentPath, absentChildPath));
                return true;
            }

            private void AssertAdd<T>(HashSet<T> set, T value)
            {
                Assert.True(set.Add(value));
            }

            FileArtifact IPipGraphFileSystemView.TryGetLatestFileArtifactForPath(AbsolutePath path)
            {
                TryGetLatestFileArtifactPaths.Add(path);

                int rewriteCount;
                if (m_latestWriteCountByPath.TryGetValue(path, out rewriteCount))
                {
                    return new FileArtifact(path, rewriteCount);
                }

                return FileArtifact.Invalid;
            }

            public bool IsPathUnderOutputDirectory(AbsolutePath path, out bool isItUnderSharedOpaque)
            {
                isItUnderSharedOpaque = false;
                return false;
            }
        }

        private static void AssertSuccess<T>(Possible<T> possible)
        {
            Assert.True(possible.Succeeded);
        }
    }
}
