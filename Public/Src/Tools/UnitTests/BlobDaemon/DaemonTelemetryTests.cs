// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Tool.BlobDaemon;
using Xunit;

// The test namespace ends in 'BlobDaemon' too, so the bare type name would bind to the namespace.
using BlobDaemonTool = Tool.BlobDaemon.BlobDaemon;

namespace Test.Tool.BlobDaemon
{
    public sealed class DaemonTelemetryTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public DaemonTelemetryTests(ITestOutputHelper output)
           : base(output) { }

        /// <summary>
        /// A target must reduce to its account and container only. The blob's own path is part of the upload
        /// location and is rewritten per file, so keeping it would produce one telemetry target per uploaded file.
        /// </summary>
        [Theory]
        // container-based: the account is an account URL, and the per-file relative path must not affect the target
        [InlineData("#container#https://acct.blob.core.windows.net#cont#a/b/file1.js#", "ContainerBased", "acct", "cont", "acct.blob.core.windows.net")]
        [InlineData("#container#https://acct.blob.core.windows.net#cont#totally/different/file2.js#", "ContainerBased", "acct", "cont", "acct.blob.core.windows.net")]
        // uri-based: account and container come from the host and the first path segment
        [InlineData("#uri#https://acct.blob.core.windows.net/cont/a/b/file1.js#", "UriBased", "acct", "cont", "acct.blob.core.windows.net")]
        [InlineData("#uri#https://acct.blob.core.windows.net/cont/totally/different/file2.js#", "UriBased", "acct", "cont", "acct.blob.core.windows.net")]
        // uri-based pointing at a container root
        [InlineData("#uri#https://acct.blob.core.windows.net/cont#", "UriBased", "acct", "cont", "acct.blob.core.windows.net")]
        public void TargetContainerKeepsOnlyAccountAndContainer(string serializedLocation, string expectedKind, string expectedAccount, string expectedContainer, string expectedHost)
        {
            var location = UploadLocation.TryParse(serializedLocation);
            XAssert.IsTrue(location.Succeeded, $"Could not parse '{serializedLocation}'");

            var target = BlobDaemonTool.CreateTargetContainer(location.Result);

            XAssert.AreEqual(expectedKind, target.Kind);
            XAssert.AreEqual(expectedAccount, target.Account);
            XAssert.AreEqual(expectedContainer, target.Container);
            XAssert.AreEqual(expectedHost, target.Host);
        }

        /// <summary>
        /// Targets are deduplicated by value: files in the same container must collapse to one entry, and a
        /// different container must remain its own.
        /// </summary>
        [Fact]
        public void TargetsCompareByContainerNotByFilePath()
        {
            var first = BlobDaemonTool.CreateTargetContainer(UploadLocation.TryParse("#container#https://acct.blob.core.windows.net#cont#a/file1.js#").Result);
            var second = BlobDaemonTool.CreateTargetContainer(UploadLocation.TryParse("#container#https://acct.blob.core.windows.net#cont#b/file2.js#").Result);
            var third = BlobDaemonTool.CreateTargetContainer(UploadLocation.TryParse("#container#https://acct.blob.core.windows.net#othercont#a/file1.js#").Result);

            XAssert.AreEqual(first, second);
            XAssert.AreEqual(first.GetHashCode(), second.GetHashCode());

            XAssert.AreNotEqual(first, third);
            XAssert.AreNotEqual(second, third);
            XAssert.AreNotEqual(first.GetHashCode(), third.GetHashCode());
        }
    }
}
