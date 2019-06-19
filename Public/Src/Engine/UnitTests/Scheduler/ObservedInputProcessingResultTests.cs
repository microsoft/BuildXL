// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
// using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class ObservedProcessingResultTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ObservedProcessingResultTests(ITestOutputHelper output)
            : base(output)
        {
        }

#if false
        [Fact]
        public void ProjectPathSet()
        {
            var pathTable = new PathTable();

            AbsolutePath firstPath = AbsolutePath.Create(pathTable, X("/X/bar"));
            AbsolutePath secondPath = AbsolutePath.Create(pathTable, X("/X/foo"));

            var p = CreateResult(
                pathTable,
                ObservedInput.CreateFileContentRead(firstPath, ContentHashingUtilities.EmptyHash),
                ObservedInput.CreateFileContentRead(secondPath, ContentHashingUtilities.EmptyHash));

            ObservedPathSet projected = p.GetPathSet(unsafeOptions: null);
            ObservedPathSetTestUtilities.AssertPathSetsEquivalent(
                projected,
                ObservedPathSetTestUtilities.CreatePathSet(pathTable, firstPath, secondPath));
        }

        [Fact]
        public void StrongFingerprintVariations()
        {
            PathTable pathTable = new PathTable();

            AbsolutePath pathA = AbsolutePath.Create(pathTable, X("/X/bar"));
            AbsolutePath pathB = AbsolutePath.Create(pathTable, X("/X/foo"));

            WeakContentFingerprint wfp1 = new WeakContentFingerprint(CreateFakeFingerprint(1));
            WeakContentFingerprint wfp2 = new WeakContentFingerprint(CreateFakeFingerprint(2));

            ContentHash content1 = CreateFakeContentHash(3);
            ContentHash content2 = CreateFakeContentHash(4);

            FingerprintingHarness harness = new FingerprintingHarness(pathTable);

            // Initial
            harness.AssertFingerprintIsNew(
                wfp1,
                ObservedInput.CreateFileContentRead(pathA, content1));

            // Change WFP of Initial
            harness.AssertFingerprintIsNew(
                wfp2, // Changed
                ObservedInput.CreateFileContentRead(pathA, content1));

            // Change observation type of Initial
            harness.AssertFingerprintIsNew(
                wfp1,
                ObservedInput.CreateAbsentPathProbe(pathA)); // Changed
            harness.AssertFingerprintIsNew(
                wfp1,
                ObservedInput.CreateDirectoryEnumeration(pathA, new DirectoryFingerprint(content1))); // Changed

            // Change file content of Initial
            harness.AssertFingerprintIsNew(
                wfp1,
                ObservedInput.CreateFileContentRead(pathA, content2)); // Changed

            // Add a second file to Initial
            harness.AssertFingerprintIsNew(
                wfp1,
                ObservedInput.CreateFileContentRead(pathA, content1),
                ObservedInput.CreateFileContentRead(pathB, content1)); // New

            // Change WFP versus previous
            harness.AssertFingerprintIsNew(
                wfp2, // Changed
                ObservedInput.CreateFileContentRead(pathA, content1),
                ObservedInput.CreateFileContentRead(pathB, content1));

            harness.AssertFingerprintIsNew(
                wfp1,
                ObservedInput.CreateExistingDirectoryProbe(pathA)); // Changed
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

        private static Fingerprint CreateFakeFingerprint(byte seed)
        {
            byte[] b = new byte[FingerprintUtilities.FingerprintLength];
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = seed;
            }

            return FingerprintUtilities.CreateFrom(b);
        }

        private static ObservedInputProcessingResult CreateResult(PathTable pathTable, params ObservedInput[] inputs)
        {
            var sorted = SortedReadOnlyArray<ObservedInput, ObservedInputExpandedPathComparer>.SortUnsafe(
                inputs,
                new ObservedInputExpandedPathComparer(pathTable.ExpandedPathComparer));

            var emptyObservedAccessFileNames = SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.FromSortedArrayUnsafe(
                ReadOnlyArray<StringId>.Empty,
                new CaseInsensitiveStringIdComparer(pathTable.StringTable));
            return ObservedInputProcessingResult.CreateForSuccess(
                sorted, 
                emptyObservedAccessFileNames, 
                dynamicallyObservedFiles: ReadOnlyArray<AbsolutePath>.Empty, 
                dynamicallyObservedEnumerations: ReadOnlyArray<AbsolutePath>.Empty, 
                allowedUndeclaredSourceReads: CollectionUtilities.EmptySet<AbsolutePath>(), 
                absentPathProbesUnderNonDependenceOutputDirectories: CollectionUtilities.EmptySet<AbsolutePath>());
        }

        private class FingerprintingHarness
        {
            public readonly PathTable PathTable;
            public readonly HashSet<StrongContentFingerprint> Fingerprints = new HashSet<StrongContentFingerprint>();

            public FingerprintingHarness(PathTable pathTable)
            {
                PathTable = pathTable;
            }

            public void AssertFingerprintIsNew(WeakContentFingerprint weakFingerprint, params ObservedInput[] inputs)
            {
                var result = CreateResult(PathTable, inputs);
                StrongContentFingerprint fp = result.ComputeStrongFingerprint(PathTable, weakFingerprint, ContentHashingUtilities.ZeroHash);
                XAssert.IsFalse(Fingerprints.Contains(fp), "Duplicate strong fingerprint");
                Fingerprints.Add(fp);
            }
        }
#endif
    }
}
