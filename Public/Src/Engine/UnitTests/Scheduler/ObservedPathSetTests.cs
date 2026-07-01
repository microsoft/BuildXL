// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Configuration = BuildXL.Utilities.Configuration;

namespace Test.BuildXL.Scheduler
{
    public class ObservedPathSetTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ObservedPathSetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void RoundTripSerializationRemovesDuplicates()
        {
            var pathTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/a/b/c"),
                X("/X/d/e"),
                X("/X/a/b/c/d"),
                X("/X/a/b/c"));

            var roundtrip = SerializeRoundTripAndAssertEquivalent(pathTable, pathSet);

            ObservedPathSetTestUtilities.AssertPathSetContainsDuplicates(pathSet);
            ObservedPathSetTestUtilities.AssertPathSetDoesNotContainDuplicates(roundtrip);
        }

        [Fact]
        public void RoundTripSerializationRemovesDuplicatesWithUnrelatedPaths()
        {
            var pathTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/a/b/c"),
                X("/X/a/b/c"),
                X("/Y/d/e/f"),
                X("/Y/d/e/f"));

            var roundtrip = SerializeRoundTripAndAssertEquivalent(pathTable, pathSet);

            ObservedPathSetTestUtilities.AssertPathSetContainsDuplicates(pathSet);
            ObservedPathSetTestUtilities.AssertPathSetDoesNotContainDuplicates(roundtrip);
        }

        [Fact]
        public void RoundTripSerializationRemovesDuplicatesInObservedAccessedFileNames()
        {                       
            var pathTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                observedAccessedFileNames: new string[] { "d", "d", "f" },
                X("/X/a/b/c"));

            var roundtrip = SerializeRoundTripAndAssertEquivalent(pathTable, pathSet);

            ObservedPathSetTestUtilities.AssertPathSetContainsDuplicates(pathSet);
            ObservedPathSetTestUtilities.AssertPathSetDoesNotContainDuplicates(roundtrip);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Paths are case-sensitive on Linux-based systems.
        public void RoundTripSerializationNormalizesCasingAndRemovesDuplicatesInObservedAccessedFileNames()
        {
            var pathTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                observedAccessedFileNames: new string[] { "d", "D", "f" },
                X("/X/a/b/c"));

            var roundtrip = SerializeRoundTripAndAssertEquivalent(pathTable, pathSet);

            ObservedPathSetTestUtilities.AssertPathSetContainsDuplicates(pathSet);
            ObservedPathSetTestUtilities.AssertPathSetDoesNotContainDuplicates(roundtrip);
        }

        [Fact]
        public void ObservedFileNamesNormalizedTheSameWayInPathsetAndJsonFingerprinter()
        {
            // This test is guarding codesync between JsonFingerprinter.cs and ObservedPathSet.cs
            var fileNames = new string[] { "a", "b", "C" };
            var pathTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                observedAccessedFileNames: fileNames,
                X("/X/a/b/c"));

            var roundtrip = SerializeRoundTripAndAssertEquivalent(pathTable, pathSet);

            var sb = new StringBuilder();
            using (var writer = new global::BuildXL.Engine.Cache.JsonFingerprinter(sb, pathTable: pathTable))
            {
                writer.AddCollection<StringId, StringId[]>(
                    "fileNames",
                    fileNames.Select(fn => StringId.Create(pathTable.StringTable, fn)).ToArray(),
                    (w, e) => w.AddFileName(e));
            }

            var fpOutput = sb.ToString();
            XAssert.IsTrue(roundtrip.ObservedAccessedFileNames.All(fileName => fpOutput.Contains($"\"{fileName.ToString(pathTable.StringTable)}\"")));
        }

        [Fact]
        public void RoundTripSerializationEmpty()
        {
            var pathTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(pathTable, new string[0]);

            var roundtrip = SerializeRoundTripAndAssertEquivalent(pathTable, pathSet);
            XAssert.AreEqual(0, roundtrip.Paths.Length);
        }

        [Fact]
        public void RoundTripSerializationOfPathsWithUnicodeChars()
        {
            var pathTable = new PathTable();

            var mpe = new global::BuildXL.Engine.MountPathExpander(pathTable);
            mpe.Add(
                pathTable,
                new global::BuildXL.Pips.SemanticPathInfo(
                    PathAtom.Create(pathTable.StringTable, "xcode"),
                    AbsolutePath.Create(pathTable, X("/c/Applications")),
                    false, true, true, false, false, false));

            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/c/Applications/Xcode.app/Contents/Developer/Platforms/iPhoneOS.platform/Developer/Library/CoreSimulator/Profiles/DeviceTypes/iPhone Xʀ.simdevicetype"),
                X("/c/Applications/Xcode.app/Contents/Developer/Platforms/iPhoneOS.platform/Developer/Library/CoreSimulator/Profiles/DeviceTypes/iPhone Xʀ.simdevicetype/Contents"));

            SerializeRoundTripAndAssertEquivalent(pathTable, pathSet, mpe);
        }

        [Fact]
        public void NoCompression()
        {
            var pathTable = new PathTable();

            // No compression since no prefix is shared.
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/a/b"),
                X("/Y/a/b"));

            AssertCompressedSizeExpected(
                pathTable,
                pathSet,
                X("/X/a/b"),
                X("/Y/a/b"));
        }

        [Fact]
        public void PrefixCompressionTrivial()
        {
            var pathTable = new PathTable();

            // No compression since no prefix is shared.
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/a"),
                X("/X/a/b"),
                X("/X/a/b/c"),
                X("/X/a/b/c/d"));

            AssertCompressedSizeExpected(
                pathTable,
                pathSet,
                X("/X/a"),
                $"{Path.DirectorySeparatorChar}b",
                $"{Path.DirectorySeparatorChar}c",
                $"{Path.DirectorySeparatorChar}d");
        }

        [Fact]
        public void PrefixCompressionWithinComponents()
        {
            var pathTable = new PathTable();

            // No compression since no prefix is shared.
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/abcdef"),
                X("/X/abcxyz/123"),
                X("/X/abcxyz/123456"));

            AssertCompressedSizeExpected(
                pathTable,
                pathSet,
                X("/X/abcdef"),
                $"xyz{Path.DirectorySeparatorChar}123",
                "456");
        }

        [Fact]
        public void PrefixCompressionWithReset()
        {
            var pathTable = new PathTable();

            // No compression since no prefix is shared.
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/X/abc"),
                X("/X/abc/def"),
                X("/Y/abc"),
                X("/Y/abc/def"));

            AssertCompressedSizeExpected(
                pathTable,
                pathSet,
                X("/X/abc"),
                $"{Path.DirectorySeparatorChar}def",
                X("/Y/abc"),
                $"{Path.DirectorySeparatorChar}def");
        }

        [Fact]
        public async Task TokenizedPathSet()
        {
            var pathTable = new PathTable();

            var pathExpanderA = new MountPathExpander(pathTable);
            AddMount(pathExpanderA, pathTable, AbsolutePath.Create(pathTable, X("/x/users/AUser")), "UserProfile", isSystem: true);
            AddMount(pathExpanderA, pathTable, AbsolutePath.Create(pathTable, X("/x/windows")), "Windows", isSystem: true);
            AddMount(pathExpanderA, pathTable, AbsolutePath.Create(pathTable, X("/x/test")), "TestRoot", isSystem: false);

            var pathSetA = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/x/abc"),
                X("/x/users/AUser/def"),
                X("/x/windows"),
                X("/x/test/abc"));

            ObservedPathSet roundtripA = SerializeRoundTripAndAssertEquivalent(pathTable, pathSetA);
            XAssert.AreEqual(4, roundtripA.Paths.Length);

            ContentHash pathSetHashA = await pathSetA.ToContentHash(pathTable, pathExpanderA, preservePathCasing: false);

            var pathExpanderB = new MountPathExpander(pathTable);
            AddMount(pathExpanderB, pathTable, AbsolutePath.Create(pathTable, X("/y/users/BUser")), "UserProfile", isSystem: true);
            AddMount(pathExpanderB, pathTable, AbsolutePath.Create(pathTable, X("/y/windows")), "Windows", isSystem: true);
            AddMount(pathExpanderB, pathTable, AbsolutePath.Create(pathTable, X("/y/abc/test")), "TestRoot", isSystem: false);

            var pathSetB = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/x/abc"),
                X("/y/users/BUser/def"),
                X("/y/windows"),
                X("/y/abc/test/abc"));

            ObservedPathSet roundtripB = SerializeRoundTripAndAssertEquivalent(pathTable, pathSetB);
            XAssert.AreEqual(4, roundtripB.Paths.Length);

            ContentHash pathSetHashB = await pathSetB.ToContentHash(pathTable, pathExpanderB, preservePathCasing: false);

            AssertTrue(pathSetHashA == pathSetHashB);
        }

        #region Fast-path AbsolutePath construction tests

        // ObservedPathSet.TryDeserialize includes a fast path that constructs each AbsolutePath by
        // ascending the previous path to a common ancestor and combining new PathAtoms, rather than
        // re-parsing the full string into the PathTable. These tests cover scenarios where the
        // string-prefix reuse-count falls in tricky places (mid-component, at component boundary,
        // very short shared prefix, etc.) and verify that the deserialized paths are bit-identical to
        // what AbsolutePath.TryCreate would have produced. We force the fast path's correctness by
        // deserializing into a fresh PathTable so any wrong AbsolutePath construction would surface
        // as a string mismatch.

        [Theory]
        // Sibling leaves whose common prefix falls mid-component. Original 1JS regression case
        // (.BROWSERSLISTRC -> .YARNRC): shared "/X/." is part of a single leaf, not a directory separator.
        [InlineData("/X/.BROWSERSLISTRC", "/X/.YARNRC")]
        // Sibling leaves in the same directory. reuseCount lands exactly at the last separator.
        [InlineData("/X/a/b/file1.txt", "/X/a/b/file2.txt")]
        // New file in a sibling directory. Ascend one level, then combine two new components.
        [InlineData("/X/a/b/file1", "/X/a/c/file2")]
        // Multi-level ascent then descent. Ascend three levels then combine three new components.
        [InlineData("/X/a/b/c/d/leaf1", "/X/a/e/f/g/leaf2")]
        // Descent extends previous path. reuseCount equals the previous path's full length.
        [InlineData("/X/a/b", "/X/a/b/c")]
        // Growing path chain - walks deeper at each step; common case for directory-tree enumerations.
        [InlineData("/X", "/X/a", "/X/a/b", "/X/a/b/c", "/X/a/b/c/d/e")]
        // Short shared prefix has no separator inside it. Fast path should bail; slow path must still
        // produce correct paths.
        [InlineData("/X/abc", "/X/def")]
        // Alternating deep and shallow paths. Stresses lastPath/lastStr book-keeping.
        [InlineData("/X/aaaaa/bbbbb/ccccc/ddddd/file",
            "/X/aaaaa/bbbbb/ccccc/different",
            "/X/aaaaa/zzzzz/yyyyy/wwwww/another",
            "/X/aaaaa/zzzzz/yyyyy/wwwww/another",
            "/Y/totally/unrelated/path")]
        // Repeated identical paths. Duplicates are de-duplicated by the serializer; ensure the fast
        // path still produces a correct AbsolutePath when the first non-dup entry equals its predecessor.
        [InlineData("/X/a/b/c", "/X/a/b/c", "/X/a/b/d")]
        // Siblings at the filesystem root. Common-ancestor is the root itself.
        [InlineData("/X/aaa", "/X/bbb")]
        // Mid-component shared prefix on the leaf, then a longer suffix.
        [InlineData("/X/a/prefix-something", "/X/a/prefix-other")]
        public void FastPath_RoundTrip(params string[] paths)
        {
            AssertFreshTableRoundTrip(paths.Select(p => X(p)).ToArray());
        }

        [Fact]
        public void FastPath_TokenizedPathsAreHandled()
        {
            // Tokenized paths produced by MountPathExpander serialize as "%TOKEN%\...". The fast path
            // bails on entries beginning with '%' and the slow path must produce correct AbsolutePaths.
            var pathTable = new PathTable();
            var expander = new MountPathExpander(pathTable);
            AddMount(expander, pathTable, AbsolutePath.Create(pathTable, X("/x/users/AUser")), "UserProfile", isSystem: true);
            AddMount(expander, pathTable, AbsolutePath.Create(pathTable, X("/x/windows")), "Windows", isSystem: true);

            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(
                pathTable,
                X("/x/users/AUser/a"),
                X("/x/users/AUser/b/c"),
                X("/x/windows/system32/d"),
                X("/x/users/AUser/b/d/e"));

            AssertFreshTableRoundTripCore(pathTable, pathSet, expander);
        }

        /// <summary>
        /// Serializes the given paths and deserializes them into a FRESH PathTable, then asserts that
        /// every expanded path string matches the original. Using a fresh PathTable on the deserialize
        /// side forces the fast path to actually construct each AbsolutePath from scratch (via the
        /// PathTable-mutating GetParent/Combine path), so any incorrect component splitting in the
        /// fast path manifests as a string mismatch.
        /// </summary>
        private static void AssertFreshTableRoundTrip(params string[] paths)
        {
            var sourceTable = new PathTable();
            var pathSet = ObservedPathSetTestUtilities.CreatePathSet(sourceTable, paths);
            AssertFreshTableRoundTripCore(sourceTable, pathSet, pathExpander: null);
        }

        private static void AssertFreshTableRoundTripCore(PathTable sourceTable, ObservedPathSet pathSet, PathExpander pathExpander)
        {
            byte[] blob;
            using (var mem = new MemoryStream())
            using (var writer = new BuildXLWriter(stream: mem, debug: false, leaveOpen: true, logStats: false))
            {
                pathSet.Serialize(sourceTable, writer, preserveCasing: true, pathExpander);
                blob = mem.ToArray();
            }

            var freshTable = new PathTable();
            ObservedPathSet roundtrip;
            using (var mem = new MemoryStream(blob, writable: false))
            using (var reader = new BuildXLReader(stream: mem, debug: false, leaveOpen: false))
            {
                var maybeRoundtrip = ObservedPathSet.TryDeserialize(freshTable, reader, pathExpander);
                XAssert.IsTrue(maybeRoundtrip.Succeeded, "Failed to deserialize: " + (maybeRoundtrip.Succeeded ? "" : maybeRoundtrip.Failure.Describe()));
                roundtrip = maybeRoundtrip.Result;
            }

            // The serializer de-duplicates, so compare against the de-duplicated source path list.
            var expectedPaths = ObservedPathSetTestUtilities.RemoveDuplicates(pathSet.Paths);
            XAssert.AreEqual(expectedPaths.Count, roundtrip.Paths.Length, "Path count mismatch after round-trip");
            for (int i = 0; i < expectedPaths.Count; i++)
            {
                string expected = expectedPaths[i].ToString(sourceTable);
                string actual = roundtrip.Paths[i].Path.ToString(freshTable);
                XAssert.AreEqual(expected, actual, $"Path at index {i} differs after fresh-table round-trip");
            }
        }

        #endregion Fast-path AbsolutePath construction tests

        private static void AddMount(MountPathExpander tokenizer, PathTable pathTable, AbsolutePath path, string name, bool isSystem = false)
        {
            tokenizer.Add(
                pathTable,
                new Configuration.Mutable.Mount() { Name = PathAtom.Create(pathTable.StringTable, name), Path = path, IsSystem = isSystem });
        }

        private static void AssertCompressedSizeExpected(PathTable pathTable, ObservedPathSet pathSet, params string[] uncompressedStrings)
        {
            long compressedSize = GetSizeOfSerializedContent(writer => pathSet.Serialize(pathTable, writer, preserveCasing: false));

            int numberOfUniquePaths = ObservedPathSetTestUtilities.RemoveDuplicates(pathSet.Paths).Count;

            // This is correct assuming the following:
            // - Each string can be represented with a one byte length prefix, and a one byte reuse-count.
            // - The number of strings can be represented in one byte.
            // - Each character takes one byte when UTF8 encoded.
            long expectedCompressedSize =
                GetSizeOfSerializedContent(writer => pathSet.UnsafeOptions.Serialize(writer)) +
                1 + // The number of observed accesses file names (0)
                1 + // String count
                (3 * numberOfUniquePaths) + // Length isSearchPath, isDirectoryPath, and reuse
                uncompressedStrings.Sum(s => s.Length);

            XAssert.AreEqual(expectedCompressedSize, compressedSize, "Wrong size for compressed path-set");
        }

        private static long GetSizeOfSerializedContent(Action<BuildXLWriter> serializer)
        {
            using (var mem = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(stream: mem, debug: true, leaveOpen: true, logStats: true))
                {
                    serializer(writer);
                }

                return mem.Length;
            }
        }

        private static ObservedPathSet SerializeRoundTripAndAssertEquivalent(PathTable pathTable, ObservedPathSet original, PathExpander pathExpander = null)
        {
            using (var mem = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(stream: mem, debug: true, leaveOpen: true, logStats: true))
                {
                    original.Serialize(pathTable, writer, preserveCasing: false, pathExpander);
                }

                mem.Position = 0;

                ObservedPathSet roundtrip;
                using (var reader = new BuildXLReader(stream: mem, debug: true, leaveOpen: true))
                {
                    var maybeRoundtrip = ObservedPathSet.TryDeserialize(pathTable, reader, pathExpander);
                    XAssert.IsTrue(maybeRoundtrip.Succeeded, "Failed to deserialize a path set unexpectedly");
                    roundtrip = maybeRoundtrip.Result;
                }

                ObservedPathSetTestUtilities.AssertPathSetsEquivalent(original, roundtrip);
                return roundtrip;
            }
        }
    }
}
