// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Cache.ContentStore.Utils;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Synchronization
{
    [Trait("Category", "Synch")]
    [Trait("Category", "WindowsOSOnly")]
    public class IpcUtilitiesTests
    {
        [Fact]
        public void IpcDoestNotLastAfterDispose()
        {
            var identifier = $"{nameof(IpcUtilitiesTests)}.{nameof(IpcDoestNotLastAfterDispose)}";
            using (var readyHandle = IpcUtilities.GetReadyWaitHandle(identifier))
            {
                // Ready handle uses ManualReset, so this should leave the handle open for all
                readyHandle.Set();
                readyHandle.WaitOne(100).Should().BeTrue();
            }

            var found = IpcUtilities.TryOpenExistingReadyWaitHandle(identifier, out EventWaitHandle existingReadyHandle);
            found.Should().BeFalse();
            existingReadyHandle.Should().BeNull();
        }

        [Fact]
        public void TwoHandlesWithSameNameAreIdentical()
        {
            var identifier = $"{nameof(IpcUtilitiesTests)}.{nameof(TwoHandlesWithSameNameAreIdentical)}";
            using (var shutdownHandle = IpcUtilities.GetShutdownWaitHandle(identifier))
            {
                // Open the handle for one thread
                shutdownHandle.Set().Should().BeTrue();

                using (var dupShutdownHandle = IpcUtilities.GetShutdownWaitHandle(identifier))
                {
                    // Use handle's signal and reset
                    dupShutdownHandle.WaitOne(100).Should().BeTrue();
                }
            }
        }

        [Fact]
        public void ClosedHandleCannotBeReopened()
        {
            var identifier = $"{nameof(IpcUtilitiesTests)}.{nameof(ClosedHandleCannotBeReopened)}";

            // Set the shutdown handle and immediately close it
            IpcUtilities.SetShutdown(identifier).Should().BeTrue();

            using (var dupShutdownHandle = IpcUtilities.GetShutdownWaitHandle(identifier))
            {
                // The handle should NOT be set
                dupShutdownHandle.WaitOne(100).Should().BeFalse();
            }
        }

        [Fact]
        public void SetAndCloseAnOpenHandle()
        {
            var identifier = $"{nameof(IpcUtilitiesTests)}.{nameof(SetAndCloseAnOpenHandle)}";

            using (var shutdownHandle = IpcUtilities.GetShutdownWaitHandle(identifier))
            {
                // Open the handle, set it, and close it. This should not close the outer handle.
                IpcUtilities.SetShutdown(identifier).Should().BeTrue();

                // Validate the outer handle is set but not closed.
                shutdownHandle.WaitOne(100).Should().BeTrue();
            }
        }
    }
}
