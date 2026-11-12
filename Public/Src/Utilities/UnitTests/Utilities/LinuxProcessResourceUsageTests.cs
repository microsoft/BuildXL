// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Interop.Unix;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using UnixIO = BuildXL.Interop.Unix.IO;

namespace Test.BuildXL.Utilities
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class LinuxProcessResourceUsageTests : TemporaryStorageTestBase
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
            var processDirectory = Path.Combine(TemporaryDirectory, pid.ToString());
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

            var result = Impl_Linux.CreateProcessResourceUsageForPid(pid, TemporaryDirectory, hasIoAccounting);
            Assert.True(result.HasValue);
            Assert.Equal(hasIoAccounting, result.Value.HasIoCounters);
            var usage = result.Value.Usage;
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
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(pid, TemporaryDirectory));
            File.WriteAllText(statPath, string.Join(" ", stat));

            // Missing or inaccessible I/O must not discard valid CPU and memory measurements.
            var ioPath = Path.Combine(processDirectory, "io");
            File.Delete(ioPath);
            var sampleWithoutIo = Impl_Linux.CreateProcessResourceUsageForPid(pid, TemporaryDirectory, true);
            Assert.True(sampleWithoutIo.HasValue);
            Assert.False(sampleWithoutIo.Value.HasIoCounters);
            Assert.Equal(usage.UserTimeMs, sampleWithoutIo.Value.Usage.UserTimeMs);
            Assert.Equal(usage.WorkingSetSize, sampleWithoutIo.Value.Usage.WorkingSetSize);
            Directory.CreateDirectory(ioPath);
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllLines(ioPath));
            var sampleWithInvalidIoPath = Impl_Linux.CreateProcessResourceUsageForPid(pid, TemporaryDirectory, hasIoAccounting);
            Assert.True(sampleWithInvalidIoPath.HasValue);
            Assert.False(sampleWithInvalidIoPath.Value.HasIoCounters);
            Assert.Equal(usage.UserTimeMs, sampleWithInvalidIoPath.Value.Usage.UserTimeMs);
            Assert.Equal(usage.WorkingSetSize, sampleWithInvalidIoPath.Value.Usage.WorkingSetSize);
            Assert.Equal(0UL, sampleWithInvalidIoPath.Value.Usage.DiskReadOps);
            Directory.Delete(ioPath);

            File.Delete(Path.Combine(processDirectory, "status"));
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(pid, TemporaryDirectory));
        }

        [Fact]
        public void ExitingProcessPreservesCpuAndMemorySample()
        {
            const int pid = 123;
            var processDirectory = Path.Combine(TemporaryDirectory, pid.ToString());
            Directory.CreateDirectory(processDirectory);
            var stat = Enumerable.Repeat("0", 52).ToArray();
            stat[13] = "100";
            stat[14] = "50";
            File.WriteAllText(Path.Combine(processDirectory, "stat"), string.Join(" ", stat));
            var statusPath = Path.Combine(processDirectory, "status");
            Assert.Equal(0, UnixIO.MkFifo(statusPath, UnixIO.FilePermissions.S_IRWXU));

            var exitTask = Task.Run(() =>
            {
                // Opening the writer waits until the sampler has read stat and opened status.
                using (var writer = new StreamWriter(statusPath))
                {
                    writer.WriteLine("Name:\ttest");
                    writer.WriteLine("VmRSS:\t    2048 kB");
                    writer.WriteLine("VmHWM:\t    4096 kB");
                    writer.Flush();
                    Directory.Delete(processDirectory, recursive: true);
                    // Closing status lets the sampler continue to the now-missing PID directory.
                }
            });

            var result = Impl_Linux.CreateProcessResourceUsageForPid(pid, TemporaryDirectory, true);
            exitTask.GetAwaiter().GetResult();

            Assert.False(Directory.Exists(processDirectory));
            Assert.Throws<DirectoryNotFoundException>(() => File.ReadAllLines(Path.Combine(processDirectory, "io")));
            Assert.True(result.HasValue);
            Assert.False(result.Value.HasIoCounters);
            Assert.Equal(pid, result.Value.Usage.ProcessId);
            Assert.Equal("test", result.Value.Usage.Name);
            Assert.Equal((ulong)Impl_Linux.TicksToTimeSpan(100).TotalMilliseconds, result.Value.Usage.UserTimeMs);
            Assert.Equal((ulong)Impl_Linux.TicksToTimeSpan(50).TotalMilliseconds, result.Value.Usage.SystemTimeMs);
            Assert.Equal(2048UL * 1024, result.Value.Usage.WorkingSetSize);
            Assert.Equal(4096UL * 1024, result.Value.Usage.PeakWorkingSetSize);
        }

        [Fact]
        public void IoAccountingAvailabilityUsesCurrentProcess()
        {
            var selfDirectory = Path.Combine(TemporaryDirectory, "self");
            Directory.CreateDirectory(selfDirectory);
            Assert.False(Impl_Linux.HasProcessIoAccounting(TemporaryDirectory));

            File.WriteAllText(Path.Combine(selfDirectory, "io"), "syscr: 10");
            Assert.True(Impl_Linux.HasProcessIoAccounting(TemporaryDirectory));

            // A missing target process must not change the current process's capability.
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(123, TemporaryDirectory));
            Assert.True(Impl_Linux.HasProcessIoAccounting(TemporaryDirectory));
        }

        [Fact]
        public void IoAccountingProbeRetriesAfterReadFailure()
        {
            var selfDirectory = Path.Combine(TemporaryDirectory, "self");
            Directory.CreateDirectory(selfDirectory);
            var ioPath = Path.Combine(selfDirectory, "io");
            Directory.CreateDirectory(ioPath);
            var probe = Impl_Linux.CreateProcessIoAccountingProbe(TemporaryDirectory);

            Assert.Throws<UnauthorizedAccessException>(() => probe.Value);
            Assert.False(probe.IsValueCreated);

            Directory.Delete(ioPath);
            File.WriteAllText(ioPath, "syscr: 10");
            Assert.True(probe.Value);
            Assert.True(probe.IsValueCreated);
        }

        [Fact]
        public void ExitedProcessHasNoSample()
        {
            Assert.Null(Impl_Linux.CreateProcessResourceUsageForPid(123, TemporaryDirectory));
        }
    }
}
