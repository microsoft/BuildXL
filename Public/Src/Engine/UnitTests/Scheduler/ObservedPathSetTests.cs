// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
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

            ContentHash pathSetHashA = await pathSetA.ToContentHash(pathTable, pathExpanderA);

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

            ContentHash pathSetHashB = await pathSetB.ToContentHash(pathTable, pathExpanderB);

            AssertTrue(pathSetHashA == pathSetHashB);
        }

        private static void AddMount(MountPathExpander tokenizer, PathTable pathTable, AbsolutePath path, string name, bool isSystem = false)
        {
            tokenizer.Add(
                pathTable,
                new Configuration.Mutable.Mount() { Name = PathAtom.Create(pathTable.StringTable, name), Path = path, IsSystem = isSystem });
        }

        private static void AssertCompressedSizeExpected(PathTable pathTable, ObservedPathSet pathSet, params string[] uncompressedStrings)
        {
            long compressedSize = GetSizeOfSerializedContent(writer => pathSet.Serialize(pathTable, writer));

            int numberOfUniquePaths = ObservedPathSetTestUtilities.RemoveDuplicates(pathSet).Count;

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
                    original.Serialize(pathTable, writer, pathExpander);
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
