// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class UnixBpfUtilsTests
    {
        [Theory]
        [InlineData("36 25 0:32 / /sys/fs/bpf rw,nosuid,nodev,noexec,relatime - bpf bpf rw", true)]
        [InlineData("36 25 0:32 / /sys/fs/bpf rw shared:1 master:2 - bpf bpf rw", true)]
        [InlineData("36 25 0:32 / /sys/fs/bpf rw - tmpfs bpf rw", false)]
        [InlineData("36 25 0:32 / /other/bpf rw - bpf bpf rw", false)]
        [InlineData("36 25 0:32 / /sys/fs/bpf/child rw - bpf bpf rw", false)]
        [InlineData("", false)]
        [InlineData("36 25 0:32 / /sys/fs/bpf rw bpf bpf rw", false)]
        [InlineData("36 25 0:32 / /sys/fs/bpf rw - bpf", false)]
        [InlineData("36 25 0:32 / /sys/fs/bpf rw - bpf bpf", false)]
        public void DetectsBpfFileSystemAtExpectedMountPoint(string entry, bool expected)
        {
            XAssert.AreEqual(expected, UnixBpfUtils.IsBpfFileSystemMounted(new[] { entry }));
        }

        [Fact]
        public void DoesNotTreatSysfsAsBpfFileSystem()
        {
            XAssert.IsFalse(UnixBpfUtils.IsBpfFileSystemMounted(new[]
            {
                "25 20 0:22 / /sys rw,nosuid,nodev,noexec,relatime - sysfs sysfs rw",
                "36 25 0:32 / /sys/fs/cgroup rw - cgroup2 cgroup rw",
            }));
        }
    }
}
