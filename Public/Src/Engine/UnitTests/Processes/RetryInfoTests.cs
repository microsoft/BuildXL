// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class RetryInfoTests : XunitBuildXLTest
    {
        private ITestOutputHelper TestOutput { get; }

        public RetryInfoTests(ITestOutputHelper output)
            : base(output) => TestOutput = output;

        [Theory]
        [InlineData(RetryReason.ResourceExhaustion, false, true)]
        [InlineData(RetryReason.UserSpecifiedExitCode, true, false)]
        [InlineData(RetryReason.VmExecutionError, true, true)]
        [InlineData(RetryReason.RemoteFallback, false, true)]
        public void BasicTest(RetryReason reason, bool canBeRetriedInline, bool canBeRetriedByReschedule)
        {
            RetryInfo retryInfo = RetryInfo.GetDefault(reason);
            Verify(retryInfo);

            using var ms = new MemoryStream();
            retryInfo.Serialize(new BinaryWriter(ms));

            ms.Seek(0, SeekOrigin.Begin);
            retryInfo = RetryInfo.Deserialize(new BinaryReader(ms));

            Verify(retryInfo);

            void Verify(RetryInfo ri)
            {
                if (canBeRetriedInline)
                {
                    XAssert.IsTrue(ri.CanBeRetriedInline());
                }

                if (canBeRetriedByReschedule)
                {
                    XAssert.IsTrue(ri.CanBeRetriedByReschedule());
                }
            }
        }
    }
}
