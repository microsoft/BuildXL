// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Pips
{
    public sealed class PipFragmentTests : XunitBuildXLTest
    {
        public PipFragmentTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void PipFragmentEquality()
        {
            var pathTable = new PathTable();
            StructTester.TestEquality(
                baseValue: PipFragment.FromString("mystring", pathTable.StringTable),
                equalValue: PipFragment.FromString("mystring", pathTable.StringTable),
                notEqualValues: new[]
                                {
                                    PipFragment.FromString("MyString", pathTable.StringTable),
                                    PipFragment.FromAbsolutePathForTesting(
                                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, A("t", "file1.txt")))),
                                    PipFragment.FromAbsolutePathForTesting(
                                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, A("t", "file1.txt"))).CreateNextWrittenVersion()),
                                    PipFragment.CreateNestedFragment(
                                        PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules))
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }

        [Fact]
        public void TestRendererWithoutHashLookup()
        {
            var pathTable = new PathTable();
            var renderer = new PipFragmentRenderer(pathTable);
            DoTestRenderer(pathTable, renderer, FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.ZeroHash).Render());

            var moniker = new StringMoniker("123");
            XAssert.AreEqual(moniker.Id, renderer.Render(PipFragment.CreateIpcMonikerForTesting(moniker, pathTable.StringTable)));
        }

        [Fact]
        public void TestRendererWithHashLookup()
        {
            var pathTable = new PathTable();
            var fakeContentInfo = FileContentInfo.CreateWithUnknownLength(ContentHash.Random());
            var expectedHash = fakeContentInfo.Render();
            var renderer = new PipFragmentRenderer(pathTable, (mId) => "XYZ:" + mId, (f) => fakeContentInfo);
            DoTestRenderer(pathTable, renderer, expectedHash);

            var moniker = new StringMoniker("123");
            XAssert.AreEqual("XYZ:123", renderer.Render(PipFragment.CreateIpcMonikerForTesting(moniker, pathTable.StringTable)));
        }

        private void DoTestRenderer(PathTable pathTable, PipFragmentRenderer renderer, string expectedHash)
        {
            // StringId
            var strValue = "my string";
            XAssert.AreEqual(strValue, renderer.Render(PipFragment.FromString(strValue, pathTable.StringTable)));

            var pathStr = A("t", "file1.txt");
            var path = AbsolutePath.Create(pathTable, pathStr);
            var srcFile = FileArtifact.CreateSourceFile(path);
            var outFile = FileArtifact.CreateOutputFile(srcFile);
            var rwFile = outFile.CreateNextWrittenVersion();
            var rw2File = rwFile.CreateNextWrittenVersion();
            var opaqueDir = DirectoryArtifact.CreateDirectoryArtifactForTesting(path, 0);
            var sharedDir = new DirectoryArtifact(path, 1, isSharedOpaque: true);

            // AbsolutePath
            XAssert.AreEqual(pathStr, renderer.Render(PipFragment.FromAbsolutePathForTesting(path)));
            XAssert.AreEqual(pathStr, renderer.Render(PipFragment.FromAbsolutePathForTesting(srcFile)));
            XAssert.AreEqual(pathStr, renderer.Render(PipFragment.FromAbsolutePathForTesting(outFile)));
            XAssert.AreEqual(pathStr, renderer.Render(PipFragment.FromAbsolutePathForTesting(rwFile)));
            XAssert.AreEqual(pathStr, renderer.Render(PipFragment.FromAbsolutePathForTesting(rw2File)));

            // VsoHash
            XAssert.AreEqual(expectedHash, renderer.Render(PipFragment.VsoHashFromFileForTesting(srcFile)));
            XAssert.AreEqual(expectedHash, renderer.Render(PipFragment.VsoHashFromFileForTesting(outFile)));
            XAssert.AreEqual(expectedHash, renderer.Render(PipFragment.VsoHashFromFileForTesting(rwFile)));
            XAssert.AreEqual(expectedHash, renderer.Render(PipFragment.VsoHashFromFileForTesting(rw2File)));

            XAssert.AreEqual(DirectoryId.ToString(opaqueDir), renderer.Render(PipFragment.DirectoryIdForTesting(opaqueDir)));
            XAssert.AreEqual(DirectoryId.ToString(sharedDir), renderer.Render(PipFragment.DirectoryIdForTesting(sharedDir)));

        }
    }
}
