// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedExecution;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PackedExecutionConsistentEnumTests : TemporaryStorageTestBase
    {
        public PackedExecutionConsistentEnumTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void PackedExecution_PipType_enum_matches_BuildXL()
        {
            XAssert.AreEqual((int)PipType.CopyFile, (int)global::BuildXL.Pips.Operations.PipType.CopyFile);
            XAssert.AreEqual((int)PipType.HashSourceFile, (int)global::BuildXL.Pips.Operations.PipType.HashSourceFile);
            XAssert.AreEqual((int)PipType.Ipc, (int)global::BuildXL.Pips.Operations.PipType.Ipc);
            XAssert.AreEqual((int)PipType.Max, (int)global::BuildXL.Pips.Operations.PipType.Max);
            XAssert.AreEqual((int)PipType.Module, (int)global::BuildXL.Pips.Operations.PipType.Module);
            XAssert.AreEqual((int)PipType.Process, (int)global::BuildXL.Pips.Operations.PipType.Process);
            XAssert.AreEqual((int)PipType.SealDirectory, (int)global::BuildXL.Pips.Operations.PipType.SealDirectory);
            XAssert.AreEqual((int)PipType.SpecFile, (int)global::BuildXL.Pips.Operations.PipType.SpecFile);
            XAssert.AreEqual((int)PipType.Value, (int)global::BuildXL.Pips.Operations.PipType.Value);
            XAssert.AreEqual((int)PipType.WriteFile, (int)global::BuildXL.Pips.Operations.PipType.WriteFile);
        }
    }
}
