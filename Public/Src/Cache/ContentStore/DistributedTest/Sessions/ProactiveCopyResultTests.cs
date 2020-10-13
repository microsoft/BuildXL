// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service.Grpc;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Sessions
{
    public class ProactiveCopyResultTests
    {
        [Fact]
        public void CopyNotRequired()
        {
            var result = ProactiveCopyResult.CopyNotRequiredResult;
            result.Succeeded.Should().BeTrue();

            result.ToString().Should().Contain("Success");
        }

        [Fact]
        public void Diabled_Succeeded_Is_Succeeded()
        {
            var ringCopyResult = PushFileResult.Disabled();
            var outsideRingCopyResult = PushFileResult.PushSucceeded(size: null);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();

            ringCopyResult.Succeeded.Should().BeFalse();
            outsideRingCopyResult.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success");
            result.ToString().Should().Contain("Disabled");
        }

        [Fact]
        public void Error_Succeeded_Is_Succeeded()
        {
            var myDiagnostics = "My diagnostics";
            var myErrorMessage = "My error message";
            var ringCopyResult = new PushFileResult(myErrorMessage, myDiagnostics);
            var outsideRingCopyResult = PushFileResult.PushSucceeded(size: null);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            // Even if one of the operations is successful, the overall operation is successful.
            result.Succeeded.Should().BeTrue();
            result.ToString().Should().NotContain(myDiagnostics, "The final error should not have an error message. Only error status");
            result.ToString().Should().NotContain(myErrorMessage, "The final error should not have an error message. Only error status");
            result.ToString().Should().Contain(ringCopyResult.Status.ToString());
        }

        [Fact]
        public void Unavailable_Success_Is_Succeeded()
        {
            var ringCopyResult = PushFileResult.ServerUnavailable();
            var outsideRingCopyResult = PushFileResult.PushSucceeded(size: null);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();
            ringCopyResult.Succeeded.Should().BeFalse();
            outsideRingCopyResult.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success");
            result.ToString().Should().Contain("Unavailable");
        }

        [Fact]
        public void Unavailable_Error_Is_Error()
        {
            var ringCopyResult = PushFileResult.ServerUnavailable();
            var error = "my error";
            var outsideRingCopyResult = new PushFileResult(error);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeFalse();
            result.Status.Should().Be(ProactiveCopyStatus.Error);

            result.ToString().Should().Contain(ringCopyResult.Status.ToString());
            result.ToString().Should().Contain(outsideRingCopyResult.Status.ToString());
        }

        [Fact]
        public void Reject_Succeeded_Is_Succeeded()
        {
            var ringCopyResult = PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent);
            var outsideRingCopyResult = PushFileResult.PushSucceeded(size: null);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            // Even if one of the operations is successful, the overall operation is successful.
            result.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain(ringCopyResult.Status.ToString());
        }

        [Fact]
        public void Reject_Reject_Is_Rejected()
        {
            var ringCopyResult = PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent);
            var outsideRingCopyResult = PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeFalse();
            result.Status.Should().Be(ProactiveCopyStatus.Rejected);

            ringCopyResult.Succeeded.Should().BeFalse();
            result.ToString().Should().Contain(ringCopyResult.Status.ToString());
        }

        [Fact]
        public void Error_Reject_Is_Rejected()
        {
            string error = "my error";
            var ringCopyResult = new PushFileResult(error);
            var outsideRingCopyResult = PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeFalse();
            result.Status.Should().Be(ProactiveCopyStatus.Rejected);

            result.ToString().Should().NotContain("Success");
            result.ToString().Should().NotContain(error);
            result.ToString().Should().Contain(ringCopyResult.Status.ToString());
        }
    }
}
