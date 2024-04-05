// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit;
using Xunit.Abstractions;
using Test.BuildXL.TestUtilities.Xunit;

#if NET6_0_OR_GREATER
using BuildXL.Ipc.GrpcBasedIpc;
#endif

namespace Test.BuildXL.Ipc
{
    public sealed class IpcGrpcTests : IpcTestBase
    {
        public IpcGrpcTests(ITestOutputHelper output) : base(output)
        {
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void TestIpcResultStatusToGrpcConversion()
        {
            foreach (var value in Enum.GetValues<global::BuildXL.Ipc.Interfaces.IpcResultStatus>())
            {
                XAssert.AreEqual(value, value.AsGrpc().FromGrpc());
            }
        }
#endif
    }
}
