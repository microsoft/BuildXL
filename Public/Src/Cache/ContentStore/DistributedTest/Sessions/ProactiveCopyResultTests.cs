// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
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

            result.ToString().Should().Contain("Success Count=[0]");
            result.ToString().Should().Contain("InsideRing=[Not Applicable]");
            result.ToString().Should().Contain("OutsideRing=[Not Applicable]");
        }

        [Fact]
        public void Success_Success_Is_Succeeded()
        {
            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();

            ringCopyResult.Succeeded.Should().BeTrue();
            outsideRingCopyResult.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[2]");
            result.ToString().Should().Contain("InsideRing=[Success]");
            result.ToString().Should().Contain("OutsideRing=[Success]");
        }

        [Fact]
        public void Diabled_Succeeded_Is_Succeeded()
        {
            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Disabled(), 0);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();

            ringCopyResult.Succeeded.Should().BeFalse();
            outsideRingCopyResult.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[1]");
            result.ToString().Should().Contain("OutsideRing=[Success]");
            result.ToString().Should().Contain("InsideRing=[Disabled]");
        }

        [Fact]
        public void Error_Succeeded_Is_Succeeded()
        {
            var myDiagnostics = "My diagnostics";
            var myErrorMessage = "My error message";
            var ringCopyResult = ProactivePushResult.FromPushFileResult(new PushFileResult(myErrorMessage, myDiagnostics), 2);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);

            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            // Even if one of the operations is successful, the overall operation is successful.
            result.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[1]");
            result.ToString().Should().NotContain(myDiagnostics, "The final error should not have an error message. Only error status");
            result.ToString().Should().NotContain(myErrorMessage, "The final error should not have an error message. Only error status");
            result.ToString().Should().Contain(ringCopyResult.Status);
        }

        [Fact]
        public void Unavailable_Success_Is_Succeeded()
        {
            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.ServerUnavailable(), 3);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();
            ringCopyResult.Succeeded.Should().BeFalse();
            outsideRingCopyResult.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[1]");
            result.ToString().Should().Contain("OutsideRing=[Success]");
            result.ToString().Should().Contain("InsideRing=[ServerUnavailable]");
        }

        [Fact]
        public void Unavailable_Error_Is_Error()
        {
            var error = "my error";
            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.ServerUnavailable(), 1);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(new PushFileResult(error),1);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeFalse();
            result.ToString().Should().Contain("Success Count=[0]");
            result.ToString().Should().Contain(ringCopyResult.Status);
            result.ToString().Should().Contain(outsideRingCopyResult.Status);
        }

        [Fact]
        public void Reject_Succeeded_Is_Succeeded()
        {
            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent), 2);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 1);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 3);

            // Even if one of the operations is successful, the overall operation is successful.
            result.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[1]");
            result.ToString().Should().Contain(ringCopyResult.Status);
        }

        [Fact]
        public void Reject_Reject_Is_Rejected()
        {
            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent), 2);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent),2);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeFalse();
            result.ToString().Should().Contain("Success Count=[0]");
            ringCopyResult.Succeeded.Should().BeFalse();
            result.ToString().Should().Contain(ringCopyResult.Status);
        }

        [Fact]
        public void Error_Reject_Is_Rejected()
        {
            string error = "my error";
            var ringCopyResult = ProactivePushResult.FromPushFileResult(new PushFileResult(error),2);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Rejected(RejectionReason.OlderThanLastEvictedContent), 2);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeFalse();

            result.ToString().Should().Contain("Success Count=[0]");
            result.ToString().Should().Contain(error);
            result.ToString().Should().Contain(ringCopyResult.Status);
            result.ToString().Should().Contain($"InsideRingError=[{error}]");
        }

        [Fact]
        public void BuildIdNotSpecified_Success_Is_Success()
        {
            var ringCopyResult = ProactivePushResult.FromStatus(ProactivePushStatus.BuildIdNotSpecified, 0);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[1]");
            result.ToString().Should().Contain("OutsideRing=[Success]");
            result.ToString().Should().Contain("InsideRing=[BuildIdNotSpecified]");
        }

        [Fact]
        public void MachineAlreadyHasCopyTest()
        {
            var ringCopyResult = ProactivePushResult.FromStatus(ProactivePushStatus.MachineAlreadyHasCopy, 0);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);

            result.Succeeded.Should().BeTrue();
            result.ToString().Should().Contain("Success Count=[2]");
            result.ToString().Should().Contain("InsideRing=[MachineAlreadyHasCopy]");
            result.ToString().Should().Contain("OutsideRing=[Success]");
        }

        [Fact]
        public void SuccessCountWhenSkippedTest()
        {

            var ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Disabled(), 0);
            var outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            var result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);
            result.Succeeded.Should().BeTrue();
            result.Skipped.Should().BeFalse();
            result.SuccessCount.Should().Be(1);
            result.ToString().Should().Contain("Success Count=[1]");
            result.ToString().Should().Contain("InsideRing=[Disabled]");
            result.ToString().Should().Contain("OutsideRing=[Success]");


            ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Disabled(), 0);
            outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.Disabled(), 0);
            result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);
            result.Succeeded.Should().BeTrue();
            result.Skipped.Should().BeTrue();
            result.SuccessCount.Should().Be(0);
            result.ToString().Should().Contain("Success Count=[0]");
            result.ToString().Should().Contain("InsideRing=[Disabled]");
            result.ToString().Should().Contain("OutsideRing=[Disabled]");

            ringCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            outsideRingCopyResult = ProactivePushResult.FromPushFileResult(PushFileResult.PushSucceeded(size: null), 0);
            result = new ProactiveCopyResult(ringCopyResult, outsideRingCopyResult, retries: 0);
            result.Succeeded.Should().BeTrue();
            result.Skipped.Should().BeFalse();
            result.SuccessCount.Should().Be(2);
            result.ToString().Should().Contain("Success Count=[2]");
            result.ToString().Should().Contain("InsideRing=[Success]");
            result.ToString().Should().Contain("OutsideRing=[Success]");

            var copyNotRequiredResult= ProactiveCopyResult.CopyNotRequiredResult;
            copyNotRequiredResult.Skipped.Should().BeTrue();
            copyNotRequiredResult.SuccessCount.Should().Be(0);
            copyNotRequiredResult.ToString().Should().Contain("Success Count=[0]");
            copyNotRequiredResult.ToString().Should().Contain("InsideRing=[Not Applicable]");
            copyNotRequiredResult.ToString().Should().Contain("OutsideRing=[Not Applicable]");
        }

        [Fact]
        public void NoRingCopyIncludesCorrectErrorStringTest()
        {
            string error = "my error";
            var result = new ProactiveCopyResult(new PushFileResult(error));

            result.Succeeded.Should().BeFalse();

            result.ToString().Should().Contain("Success Count=[0]");
            result.ToString().Should().Contain("InsideRing=[Not Applicable]");
            result.ToString().Should().Contain("OutsideRing=[Not Applicable]");
        }
    }
}
