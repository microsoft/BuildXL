// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Interop.Unix;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class LinuxProcessResourceUsageTests : XunitBuildXLTest
    {
        public LinuxProcessResourceUsageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CpuAndMemoryDoNotRequireIoAccounting(bool hasIoAccounting)
        {
            const int pid = 123;
            var processDirectory = Path.Combine(TestOutputDirectory, pid.ToString());
            Directory.CreateDirectory(processDirectory);
            var stat = Enumerable.Repeat("0", 52).ToArray();
            stat[0] = pid.ToString();
            stat[1] = "(test)";
            stat[2] = "R";
            stat[13] = "100";
            stat[14] = "50";
            File.WriteAllText(Path.Combine(processDirectory, "stat"), string.Join(" ", stat));
            File.WriteAllLines(Path.Combine(processDirectory, "status"), new[]
            {
                "Name:\ttest",
                "VmRSS:\t    2048 kB",
                "VmHWM:\t    4096 kB",
            });
            if (hasIoAccounting)
            {
                File.WriteAllLines(Path.Combine(processDirectory, "io"), new[]
                {
                    "syscr: 10",
                    "read_bytes: 4096",
                    "syscw: 20",
                    "write_bytes: 8192",
                });
            }

            var result = Impl_Linux.CreateProcessResourceUsageForPid(pid, TestOutputDirectory, hasIoAccounting);
            Assert.True(result.HasValue);
            var usage = result.Value;
            Assert.Equal(pid, usage.ProcessId);
            Assert.Equal("test", usage.Name);
            Assert.Equal((ulong)Impl_Linux.TicksToTimeSpan(100).TotalMilliseconds, usage.UserTimeMs);
            Assert.Equal((ulong)Impl_Linux.TicksToTimeSpan(50).TotalMilliseconds, usage.SystemTimeMs);
            Assert.Equal(2048UL * 1024, usage.WorkingSetSize);
            Assert.Equal(4096UL * 1024, usage.PeakWorkingSetSize);
            Assert.Equal(hasIoAccounting ? 10UL : 0UL, usage.DiskReadOps);
            Assert.Equal(hasIoAccounting ? 4096UL : 0UL, usage.DiskBytesRead);
            Assert.Equal(hasIoAccounting ? 20UL : 0UL, usage.DiskWriteOps);
            Assert.Equal(hasIoAccounting ? 8192UL : 0UL, usage.DiskBytesWritten);

            var statPath = Path.Combine(processDirectory, "stat");
            File.Delete(statPath);
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(pid, TestOutputDirectory));
            File.WriteAllText(statPath, string.Join(" ", stat));

            // An unavailable kernel must skip the I/O path entirely, including errors at that path.
            var ioPath = Path.Combine(processDirectory, "io");
            File.Delete(ioPath);
            Assert.True(Impl_Linux.CreateProcessResourceUsageForPid(pid, TestOutputDirectory, true).HasValue);
            Directory.CreateDirectory(ioPath);
            var sampleWithInvalidIoPath = Impl_Linux.CreateProcessResourceUsageForPid(pid, TestOutputDirectory, hasIoAccounting);
            if (hasIoAccounting)
            {
                Assert.Null(sampleWithInvalidIoPath);
            }
            else
            {
                Assert.True(sampleWithInvalidIoPath.HasValue);
                Assert.Equal(usage.UserTimeMs, sampleWithInvalidIoPath.Value.UserTimeMs);
                Assert.Equal(usage.WorkingSetSize, sampleWithInvalidIoPath.Value.WorkingSetSize);
                Assert.Equal(0UL, sampleWithInvalidIoPath.Value.DiskReadOps);
            }
            Directory.Delete(ioPath);

            File.Delete(Path.Combine(processDirectory, "status"));
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(pid, TestOutputDirectory));
        }

        [Fact]
        public void IoAccountingAvailabilityUsesCurrentProcess()
        {
            var selfDirectory = Path.Combine(TestOutputDirectory, "self");
            Directory.CreateDirectory(selfDirectory);
            Assert.False(Impl_Linux.HasProcessIoAccounting(TestOutputDirectory));

            File.WriteAllText(Path.Combine(selfDirectory, "io"), "syscr: 10");
            Assert.True(Impl_Linux.HasProcessIoAccounting(TestOutputDirectory));

            // A missing target process must not change the current process's capability.
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(123, TestOutputDirectory));
            Assert.True(Impl_Linux.HasProcessIoAccounting(TestOutputDirectory));
        }

        [Fact]
        public void ExitedProcessHasNoSample()
        {
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(123, TestOutputDirectory));
        }
    }
}
