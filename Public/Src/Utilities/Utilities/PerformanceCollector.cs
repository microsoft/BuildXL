// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Interop;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Dispatch;
using static BuildXL.Interop.Unix.Memory;
using static BuildXL.Interop.Unix.Processor;
using static BuildXL.Interop.Windows.IO;
using static BuildXL.Interop.Windows.Memory;
using static BuildXL.Interop.Windows.Processor;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Collects system performance data
    /// </summary>
    public sealed class PerformanceCollector : IDisposable
    {
        private readonly int m_processorCount;

        // The thread that collects performance info
        private Timer m_collectionTimer;
        private readonly object m_collectionTimerLock = new object();
        private readonly object m_collectLock = new object();
        private readonly TimeSpan m_collectionFrequency;
        private readonly bool m_collectHeldBytesFromGC;
        private readonly TestHooks m_testHooks;

        // Objects that aggregate performance info during their lifetime
        private readonly HashSet<Aggregator> m_aggregators = new HashSet<Aggregator>();

        #region State needed for collecting various metrics

        // Used for calculating the Process CPU time
        private DateTime m_processTimeLastCollectedAt = DateTime.MinValue;
        private TimeSpan m_processTimeLastValue;

        // Used for calculating the Machine CPU time
        private DateTime m_machineTimeLastCollectedAt = DateTime.MinValue;
        private long m_machineTimeLastVale;
        private CpuLoadInfo m_lastCpuLoadInfo;

        // Used for collecting disk activity
        private readonly (DriveInfo driveInfo, SafeFileHandle safeFileHandle, DISK_PERFORMANCE diskPerformance)[] m_drives;

        // NetworkMonitor to measure network bandwidth of the computer.
        private NetworkMonitor m_networkMonitor;

        // Used for calculating the network sample time
        private DateTime m_networkTimeLastCollectedAt = DateTime.MinValue;
        #endregion

        /// <summary>
        /// Gets the drives registered with the <see cref="PerformanceCollector"/>
        /// </summary>
        public IEnumerable<string> GetDrives()
        {
            return OperatingSystemHelper.IsUnixOS ? // Drive names are processed differently depending on OS
                m_drives.Select(t => t.driveInfo.Name) : 
                m_drives.Where(t => !t.safeFileHandle.IsInvalid && !t.safeFileHandle.IsClosed).Select(t => t.driveInfo.Name.TrimStart('\\').TrimEnd('\\').TrimEnd(':'));
        }

        /// <summary>
        /// Test hooks for PerformanceCollector
        /// </summary>
        public class TestHooks
        {
            /// <summary>
            /// Return a specific AvailableDiskSpace
            /// </summary>
            public int AvailableDiskSpace;
        }

        /// <summary>
        /// Creates a new PerformanceCollector with the specified collection frequency.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope",
            Justification = "Handle is owned by PerformanceCollector and is disposed on its disposal")]
        public PerformanceCollector(TimeSpan collectionFrequency, bool collectBytesHeld = false, TestHooks testHooks = null)
        {
            m_collectionFrequency = collectionFrequency;
            m_processorCount = Environment.ProcessorCount;
            m_collectHeldBytesFromGC = collectBytesHeld;
            m_testHooks = testHooks;

            // Figure out which drives we want to get counters for
            List<(DriveInfo, SafeFileHandle, DISK_PERFORMANCE)> drives = new List<(DriveInfo, SafeFileHandle, DISK_PERFORMANCE)>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        drives.Add((drive, null, default));
                    }
                    else if (drive.Name.Length == 3 && drive.Name.EndsWith(@":\", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = @"\\.\" + drive.Name[0] + ":";
                        SafeFileHandle handle = CreateFileW(path, FileDesiredAccess.None, FileShare.Read, IntPtr.Zero, FileMode.Open, FileFlagsAndAttributes.FileAttributeNormal, IntPtr.Zero);
                        if (!handle.IsClosed && !handle.IsInvalid)
                        {
                            drives.Add((drive, handle, default));
                        }
                        else
                        {
                            handle.Dispose();
                        }
                    }
                }
            }

            m_drives = drives.ToArray();

            if (!OperatingSystemHelper.IsUnixOS)
            {
                // Initialize network telemetry objects
                InitializeNetworkMonitor();

                InitializeWMI();
            }

            // Perform all initialization before starting the timer
            m_collectionTimer = new Timer(Collect, null, 0, 0);
        }

        private ManagementScope m_scope;
        private ManagementObjectSearcher m_querySearcher;

        private void InitializeWMI()
        {
            try
            {
                m_scope = new ManagementScope(String.Format("\\\\{0}\\root\\CIMV2", "."), null);
                m_scope.Connect();

                ObjectQuery query = new ObjectQuery("SELECT AvailableBytes, ModifiedPageListBytes, FreeAndZeroPageListBytes, StandbyCacheCoreBytes, StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes FROM Win32_PerfFormattedData_PerfOS_Memory");
                m_querySearcher = new ManagementObjectSearcher(m_scope, query);
            }
#pragma warning disable ERP022
            catch (Exception)
            {
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Collects a sample and sends the data to any aggregators
        /// </summary>
        private void Collect(object state)
        {
            lock (m_aggregators)
            {
                if (m_aggregators.Count == 0)
                {
                    // Nobody's listening. No-op
                    ReschedulerTimer();
                    return;
                }
            }

            CollectOnce();

            ReschedulerTimer();
        }

        private void CollectOnce()
        {
            lock (m_collectLock)
            {
                // This must be reacquired for every collection and may not be cached because some of the fields like memory
                // usage are only set in the Process() constructor
                Process currentProcess = Process.GetCurrentProcess();

                // Compute the performance data
                double? machineCpu = 0.0;
                double? processCpu = GetProcessCpu(currentProcess);
                double processThreads = currentProcess.Threads.Count;
                double processPrivateBytes = currentProcess.PrivateMemorySize64;
                double processWorkingSetBytes = currentProcess.WorkingSet64;
                double processHeldBytes = m_collectHeldBytesFromGC ? GC.GetTotalMemory(forceFullCollection: true) : 0;

                double? machineAvailablePhysicalBytes = null;
                double? machineTotalPhysicalBytes = null;
                double? commitUsedBytes = null;
                double? commitLimitBytes = null;

                double? modifiedPagelistBytes = null;
                double? freePagelistBytes = null;
                double? standbyPagelistBytes = null;

                DiskStats[] diskStats = null;

                if (!OperatingSystemHelper.IsUnixOS)
                {
                    machineCpu = GetMachineCpu();

                    MEMORYSTATUSEX memoryStatusEx = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memoryStatusEx))
                    {
                        machineAvailablePhysicalBytes = memoryStatusEx.ullAvailPhys;
                        machineTotalPhysicalBytes = memoryStatusEx.ullTotalPhys;
                    }

                    PERFORMANCE_INFORMATION performanceInfo = PERFORMANCE_INFORMATION.CreatePerfInfo();
                    if (GetPerformanceInfo(out performanceInfo, performanceInfo.cb))
                    {
                        commitUsedBytes = performanceInfo.CommitUsed.ToInt64() * performanceInfo.PageSize.ToInt64();
                        commitLimitBytes = performanceInfo.CommitLimit.ToInt64() * performanceInfo.PageSize.ToInt64();
                    }

                    diskStats = GetDiskCountersWindows();

                    TryGetRAMDetails(ref modifiedPagelistBytes, ref freePagelistBytes, ref standbyPagelistBytes);
                }
                else
                {
                    diskStats = GetDiskCountersMacOS();
                    machineCpu = GetMachineCpuMacOS();

                    RamUsageInfo ramUsageInfo = new RamUsageInfo();
                    if (GetRamUsageInfo(ref ramUsageInfo) == MACOS_INTEROP_SUCCESS)
                    {
                        machineTotalPhysicalBytes = ramUsageInfo.TotalBytes;
                        machineAvailablePhysicalBytes = ramUsageInfo.FreeBytes;
                    }
                }

                // stop network monitor measurement and gather data
                m_networkMonitor?.StopMeasurement();

                DateTime temp = DateTime.UtcNow;
                TimeSpan duration = temp - m_networkTimeLastCollectedAt;
                m_networkTimeLastCollectedAt = temp;

                double? machineKbitsPerSecSent = null;
                double? machineKbitsPerSecReceived = null;

                if (m_networkMonitor != null)
                {
                    machineKbitsPerSecSent = Math.Round(1000 * BytesToKbits(m_networkMonitor.TotalSentBytes) / Math.Max(duration.TotalMilliseconds, 1.0), 3);
                    machineKbitsPerSecReceived = Math.Round(1000 * BytesToKbits(m_networkMonitor.TotalReceivedBytes) / Math.Max(duration.TotalMilliseconds, 1.0), 3);
                }

                // Update the aggregators
                lock (m_aggregators)
                {
                    foreach (var aggregator in m_aggregators)
                    {
                        aggregator.RegisterSample(
                            processCpu: processCpu,
                            processPrivateBytes: processPrivateBytes,
                            processWorkingSetBytes: processWorkingSetBytes,
                            threads: processThreads,
                            machineCpu: machineCpu,
                            machineTotalPhysicalBytes: machineTotalPhysicalBytes,
                            machineAvailablePhysicalBytes: machineAvailablePhysicalBytes,
                            commitUsedBytes: commitUsedBytes,
                            commitLimitBytes: commitLimitBytes,
                            machineBandwidth: m_networkMonitor?.Bandwidth,
                            machineKbitsPerSecSent: machineKbitsPerSecSent,
                            machineKbitsPerSecReceived: machineKbitsPerSecReceived,
                            diskStats: diskStats,
                            gcHeldBytes: processHeldBytes,
                            modifiedPagelistBytes: modifiedPagelistBytes,
                            freePagelistBytes: freePagelistBytes,
                            standbyPagelistBytes: standbyPagelistBytes);
                    }
                }

                // restart network monitor to start new measurement
                m_networkMonitor?.StartMeasurement();
            }
        }

        private void TryGetRAMDetails(ref double? modifiedPagelistBytes, ref double? freePagelistBytes, ref double? standbyPagelistBytes)
        {
            try
            {
                if (m_querySearcher != null)
                {
                    foreach (ManagementObject WmiObject in m_querySearcher.Get())
                    {
                        modifiedPagelistBytes = (UInt64)WmiObject["ModifiedPageListBytes"];
                        freePagelistBytes = (UInt64)WmiObject["FreeAndZeroPageListBytes"];

                        standbyPagelistBytes = (UInt64)WmiObject["StandbyCacheCoreBytes"];
                        standbyPagelistBytes += (UInt64)WmiObject["StandbyCacheNormalPriorityBytes"];
                        standbyPagelistBytes += (UInt64)WmiObject["StandbyCacheReserveBytes"];
                    }
                }
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch (Exception)
            {
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private static double BytesToKbits(long bytes)
        {
            // Convert to Kbits
            return (bytes / 1024.0) * 8;
        }

        /// <summary>
        /// Converts Bytes to GigaBytes
        /// </summary>
        public static double BytesToGigaBytes(long bytes)
        {
            // Convert to Gigabytes
            return ((double)bytes / (1024 * 1024 * 1024));
        }

        private void ReschedulerTimer()
        {
            lock (m_collectionTimerLock)
            {
                m_collectionTimer?.Change((int)m_collectionFrequency.TotalMilliseconds, 0);
            }
        }

        /// <summary>
        /// Creates an Aggregator to recieve performance data over the lifetime of the Aggregator
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope",
            Justification = "Disposing is the responsibility of the caller")]
        public Aggregator CreateAggregator()
        {
            Aggregator result = new Aggregator(this);
            lock (m_aggregators)
            {
                m_aggregators.Add(result);
            }

            return result;
        }

        /// <summary>
        /// Removes the aggregator and prevents it from receiving future updates
        /// </summary>
        private void RemoveAggregator(Aggregator aggregator)
        {
            lock (m_aggregators)
            {
                m_aggregators.Remove(aggregator);
            }
        }

        /// <nodoc/>
        public void Dispose()
        {
            lock (m_aggregators)
            {
                foreach (var aggregator in m_aggregators)
                {
                    aggregator.Dispose();
                }
            }

            lock (m_collectionTimerLock)
            {
                if (m_collectionTimer != null)
                {
                    m_collectionTimer.Dispose();
                    m_collectionTimer = null;
                }
            }

            if (m_drives != null)
            {
                foreach (var item in m_drives)
                {
                    item.safeFileHandle?.Dispose();
                }
            }
        }

        #region Perf data collection implementations

        /// <summary>
        /// Initializes network performance counters.
        /// </summary>
        private void InitializeNetworkMonitor()
        {
            try
            {
                // initialize NetworkMonitor and start measurement
                m_networkMonitor = new NetworkMonitor();
                m_networkTimeLastCollectedAt = DateTime.UtcNow;
                m_networkMonitor.StartMeasurement();
            }
#pragma warning disable ERP022
            catch
            {
                // NetworkMonitor is not working so set to null
                m_networkMonitor = null;
            }
#pragma warning restore ERP022
        }

        private DiskStats[] GetDiskCountersWindows()
        {
            DiskStats[] diskStats = new DiskStats[m_drives.Length];
            for (int i = 0; i < m_drives.Length; i++)
            {
                if (m_testHooks != null)
                {
                    // Various tests may need to inject artificial results for validation of scenarios
                    diskStats[i] = new DiskStats(availableDiskSpace: m_testHooks.AvailableDiskSpace);
                    continue;
                }

                var drive = m_drives[i];
                if (!drive.safeFileHandle.IsClosed && !drive.safeFileHandle.IsInvalid)
                {
                    uint bytesReturned;

                    try
                    {
                        DISK_PERFORMANCE perf = default(DISK_PERFORMANCE);
                        bool result = DeviceIoControl(drive.safeFileHandle, IOCTL_DISK_PERFORMANCE,
                            inputBuffer: IntPtr.Zero,
                            inputBufferSize: 0,
                            outputBuffer: out perf,
                            outputBufferSize: Marshal.SizeOf(typeof(DISK_PERFORMANCE)),
                            bytesReturned: out bytesReturned,
                            overlapped: IntPtr.Zero);
                        if (result && drive.driveInfo.TotalSize != 0)
                        {
                            diskStats[i] = new DiskStats(
                                availableDiskSpace: BytesToGigaBytes(drive.driveInfo.AvailableFreeSpace),
                                diskPerformance: perf);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Occasionally the handle is disposed even though it's checked against being closed and valid
                        // above. In those cases, just catch the failure and continue on to avoid crashes.
                    }
                }
            }

            return diskStats;
        }

        private DiskStats[] GetDiskCountersMacOS()
        {
            DiskStats[] stats = new DiskStats[m_drives.Length];
            for (int i = 0; i < m_drives.Length; i++)
            {
                try
                {
                    stats[i] = new DiskStats(availableDiskSpace: BytesToGigaBytes(m_drives[i].driveInfo.AvailableFreeSpace));
                }
                catch (IOException)
                {
                    // No stats for DriveNotFoundException. Leave the struct as uninitialized and it will be marked as invalid.
                }
            }
            return stats;
        }

        private double? GetProcessCpu(Process currentProcess)
        {
            // Processor time consumed by this process
            TimeSpan processTimeCurrentValue = Dispatch.TotalProcessorTime(currentProcess);
            DateTime processTimeCurrentCollectedAt = DateTime.UtcNow;

            if (m_processTimeLastCollectedAt == DateTime.MinValue)
            {
                m_processTimeLastCollectedAt = processTimeCurrentCollectedAt;
                m_processTimeLastValue = processTimeCurrentValue;
                return null;
            }
            else
            {
                var procUsage = (100.0 * (processTimeCurrentValue - m_processTimeLastValue).Ticks) /
                    ((processTimeCurrentCollectedAt - m_processTimeLastCollectedAt).Ticks * m_processorCount);

                m_processTimeLastCollectedAt = processTimeCurrentCollectedAt;
                m_processTimeLastValue = processTimeCurrentValue;
                return procUsage;
            }
        }

        private double? GetMachineCpu()
        {
            double? machineCpu = null;

            if (m_machineTimeLastCollectedAt == DateTime.MinValue)
            {
                m_machineTimeLastCollectedAt = DateTime.UtcNow;
                long idleTime, kernelTime, userTime;
                if (GetSystemTimes(out idleTime, out kernelTime, out userTime))
                {
                    m_machineTimeLastVale = kernelTime + userTime - idleTime;
                }
            }
            else
            {
                long idleTime, kernelTime, userTime;
                if (GetSystemTimes(out idleTime, out kernelTime, out userTime))
                {
                    DateTime machineTimeCurrentCollectedAt = DateTime.UtcNow;
                    long machineTimeCurrentValue = kernelTime + userTime - idleTime;

                    long availProcessorTime = ((machineTimeCurrentCollectedAt.ToFileTime() - m_machineTimeLastCollectedAt.ToFileTime()) * m_processorCount);
                    if (availProcessorTime > 0)
                    {
                        machineCpu = (100.0 * (machineTimeCurrentValue - m_machineTimeLastVale)) / availProcessorTime;

                        // Windows will create a new Processor Group for every 64 logical processors. Technically we should
                        // call GetSystemTimes() multiple times, targeting each processor group for correct data. Instead,
                        // this code assumes each processor group is eavenly loaded and just uses the number of predicted
                        // groups as a factor to scale the system times observed on the default processor group for this thread.
                        // https://msdn.microsoft.com/en-us/library/windows/desktop/dd405503(v=vs.85).aspx
                        machineCpu = machineCpu * Math.Ceiling((double)m_processorCount / 64);

                        // Sometimes the calculated value pops up above 100.
                        machineCpu = Math.Min(100, machineCpu.Value);

                        m_machineTimeLastCollectedAt = machineTimeCurrentCollectedAt;
                        m_machineTimeLastVale = machineTimeCurrentValue;
                    }
                }
            }

            return machineCpu;
        }

        private double? GetMachineCpuMacOS()
        {
            double? machineCpu = null;

            var buffer = new CpuLoadInfo();

            // Initialize the CPU load info
            if (m_lastCpuLoadInfo.SystemTime == 0 && m_lastCpuLoadInfo.UserTime == 0 && m_lastCpuLoadInfo.IdleTime == 0)
            {
                GetCpuLoadInfo(ref buffer);
                m_lastCpuLoadInfo = buffer;
            }

            buffer = new CpuLoadInfo();
            if (GetCpuLoadInfo(ref buffer) == MACOS_INTEROP_SUCCESS)
            {
                double systemTicks = buffer.SystemTime - m_lastCpuLoadInfo.SystemTime;
                double userTicks = buffer.UserTime - m_lastCpuLoadInfo.UserTime;
                double idleTicks = buffer.IdleTime - m_lastCpuLoadInfo.IdleTime;
                double totalTicks = systemTicks + userTicks + idleTicks;

                machineCpu = 100.0 * ((systemTicks + userTicks) / totalTicks);
            }

            m_lastCpuLoadInfo = buffer;

            return machineCpu;
        }

        #endregion

        /// <summary>
        /// Summary of the aggregator (more human-readable)
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct MachinePerfInfo
        {
            /// <summary>
            /// CPU usage percentage
            /// </summary>
            public int CpuUsagePercentage;

            /// <summary>
            /// Disk usage percentages for each disk
            /// </summary>
            public int[] DiskUsagePercentages;

            /// <summary>
            /// Queue depths for each disk
            /// </summary>
            public int[] DiskQueueDepths;

            /// <summary>
            /// Available Disk Space in GigaBytes for each disk
            /// </summary>
            public int[] DiskAvailableSpaceGb;

            /// <summary>
            /// Resource summary to show on the console
            /// </summary>
            public string ConsoleResourceSummary;

            /// <summary>
            /// Resource summary to show in the text log
            /// </summary>
            public string LogResourceSummary;

            /// <summary>
            /// RAM usage percentage
            /// </summary>
            public int? RamUsagePercentage;

            /// <summary>
            /// Total ram in MB
            /// </summary>
            public int? TotalRamMb;

            /// <summary>
            /// Available ram in MB
            /// </summary>
            public int? AvailableRamMb;

            /// <summary>
            /// Commit usage percentage
            /// </summary>
            public int? CommitUsagePercentage;

            /// <summary>
            /// Total committed memory in MB. This is not an indication of physical memory usage.
            /// </summary>
            public int? CommitUsedMb;

            /// <summary>
            /// Maximum memory that can be committed in MB. If the page file can be extended, this is a soft limit.
            /// </summary>
            public int? CommitLimitMb;

            /// <summary>
            /// Network bandwidth available on the machine
            /// </summary>
            public long MachineBandwidth;

            /// <summary>
            /// Kbits/sec sent on all network interfaces
            /// </summary>
            public double MachineKbitsPerSecSent;

            /// <summary>
            /// Kbits/sec received on all network interfaces
            /// </summary>
            public double MachineKbitsPerSecReceived;

            /// <summary>
            /// CPU utilization percent of just this process
            /// </summary>
            public int ProcessCpuPercentage;

            /// <summary>
            /// Working Set for just this process
            /// </summary>
            public int ProcessWorkingSetMB;

            /// <summary>
            /// Modified pagelist that is a part of used RAM
            /// </summary>
            /// <remarks>
            /// Pages in the modified pagelist are dirty and waiting to be written to the pagefile.
            /// </remarks>
            internal int? ModifiedPagelistMb;

            /// <summary>
            /// Free pagelist that is a part of available RAM
            /// </summary>
            internal int? FreePagelistMb;

            /// <summary>
            /// Standby pagelist that is a part of used RAM
            /// </summary>
            /// <remarks>
            /// Pages in the standby pagelist are clean and they can be reused for the same process or used as free pages for other processes.
            /// </remarks>
            internal int? StandbyPagelistMb;

            /// <summary>
            /// Effective Available RAM = Modified pagelist + Available RAM
            /// </summary>
            /// <remarks>
            /// When modified pagelist is not available, EffectiveAvailableRAM equals to AvailableRAM.
            /// </remarks>
            public int? EffectiveAvailableRamMb;

            /// <summary>
            /// Effective RAM usage percentage = (TotalRam - Effective Available RAM) / TotalRAM
            /// </summary>
            public int? EffectiveRamUsagePercentage;
        }

        /// <summary>
        /// Aggregates performance data
        /// </summary>
        public sealed class Aggregator : IDisposable
        {
            // Parent PerformanceCollector. Used during Dispose()
            private readonly PerformanceCollector m_parent;

            private int m_sampleCount = 0;

            /// <summary>
            /// The percent of CPU time consumed by the currently running process
            /// </summary>
            public readonly Aggregation ProcessCpu;

            /// <summary>
            /// The private megabytes consumed by the currently running process
            /// </summary>
            public readonly Aggregation ProcessPrivateMB;

            /// <summary>
            /// The working set in megabytes consumed by the currently running process. This is the number shown in TaskManager.
            /// </summary>
            public readonly Aggregation ProcessWorkingSetMB;

            /// <summary>
            /// Count of threads associated with the current process. Based on System.Diagnostics.Processes.Threads
            /// </summary>
            public readonly Aggregation ProcessThreadCount;

            /// <summary>
            /// The percent of CPU time used by the machine
            /// </summary>
            public readonly Aggregation MachineCpu;

            /// <summary>
            /// The available megabytes of physical memory available on the machine
            /// </summary>
            public readonly Aggregation MachineAvailablePhysicalMB;

            /// <summary>
            /// The total megabytes of physical memory on the machine
            /// </summary>
            public readonly Aggregation MachineTotalPhysicalMB;

            /// <summary>
            /// The total megabytes of memory current committed by the system
            /// </summary>
            public readonly Aggregation CommitUsedMB;

            /// <summary>
            /// The total megabytes of memory current that can be committed by the system without extending the page
            /// file. If the page file can be extended, this is a soft limit.
            /// </summary>
            public readonly Aggregation CommitLimitMB;

            /// <summary>
            /// The total megabytes of memory held as reported by the GC
            /// </summary>
            public readonly Aggregation ProcessHeldMB;

            /// <summary>
            /// Total Network bandwidth available on the machine
            /// </summary>
            public readonly Aggregation MachineBandwidth;

            /// <summary>
            /// Kbits/sec sent on all network interfaces on the machine
            /// </summary>
            public readonly Aggregation MachineKbitsPerSecSent;

            /// <summary>
            /// Kbits/sec received on all network interfaces on the machine
            /// </summary>
            public readonly Aggregation MachineKbitsPerSecReceived;

            /// <nodoc />
            public readonly Aggregation ModifiedPagelistMB;

            /// <nodoc />
            public readonly Aggregation FreePagelistMB;

            /// <nodoc />
            public readonly Aggregation StandbyPagelistMB;

            /// <summary>
            /// Stats about disk usage. This is guarenteed to be in the same order as <see cref="GetDrives"/>
            /// </summary>
            public IReadOnlyCollection<DiskStatistics> DiskStats => m_diskStats;

            private readonly DiskStatistics[] m_diskStats;

            /// <nodoc/>
            public sealed class DiskStatistics
            {
                /// <summary>
                /// The drive name. If this is a standard drive it will be a single character like 'C'
                /// </summary>
                public string Drive { get; set; }

                /// <nodoc/>
                public Aggregation QueueDepth = new Aggregation();

                /// <nodoc/>
                public Aggregation IdleTime = new Aggregation();

                /// <nodoc/>
                public Aggregation ReadTime = new Aggregation();

                /// <nodoc/>
                public Aggregation WriteTime = new Aggregation();

                /// <nodoc/>
                public Aggregation AvailableSpaceGb = new Aggregation();

                /// <summary>
                /// Calculates the disk active time
                /// </summary>
                /// <param name="lastOnly">Whether the calculation should only apply to the sample taken at the last time window</param>
                public int CalculateActiveTime(bool lastOnly)
                {
                    double percentage = 0;
                    if (lastOnly)
                    {
                        var denom = ReadTime.Difference + WriteTime.Difference + IdleTime.Difference;
                        if (denom > 0)
                        {
                            percentage = (ReadTime.Difference + WriteTime.Difference) / denom;
                        }
                    }
                    else
                    {
                        var denom = ReadTime.Range + WriteTime.Range + IdleTime.Range;
                        if (denom > 0)
                        {
                            percentage = (ReadTime.Range + WriteTime.Range) / denom;
                        }
                    }

                    return (int)(percentage * 100.0);
                }
            }

            /// <nodoc/>
            public Aggregator(PerformanceCollector collector)
            {
                m_parent = collector;
                ProcessCpu = new Aggregation();
                ProcessPrivateMB = new Aggregation();
                ProcessWorkingSetMB = new Aggregation();
                ProcessThreadCount = new Aggregation();
                ProcessHeldMB = new Aggregation();
                MachineCpu = new Aggregation();
                MachineAvailablePhysicalMB = new Aggregation();
                MachineTotalPhysicalMB = new Aggregation();
                CommitLimitMB = new Aggregation();
                CommitUsedMB = new Aggregation();

                MachineBandwidth = new Aggregation();
                MachineKbitsPerSecSent = new Aggregation();
                MachineKbitsPerSecReceived = new Aggregation();
                ModifiedPagelistMB = new Aggregation();
                StandbyPagelistMB = new Aggregation();
                FreePagelistMB = new Aggregation();

                List<Tuple<string, Aggregation>> aggs = new List<Tuple<string, Aggregation>>();
                List<DiskStatistics> diskStats = new List<DiskStatistics>();

                foreach (var drive in collector.GetDrives())
                {
                    aggs.Add(new Tuple<string, Aggregation>(drive, new Aggregation()));
                    diskStats.Add(new DiskStatistics() { Drive = drive });
                }

                m_diskStats = diskStats.ToArray();
            }

            /// <summary>
            /// Compute machine perf info to get more human-readable resource usage info
            /// </summary>
            /// <param name="ensureSample">when true and no performance measurement samples are registered, immediately forces a collection of a performance
            /// measurement sample</param>
            public MachinePerfInfo ComputeMachinePerfInfo(bool ensureSample = false)
            {
                if (ensureSample && Volatile.Read(ref m_sampleCount) == 0)
                {
                    m_parent.CollectOnce();
                }

                MachinePerfInfo perfInfo = default(MachinePerfInfo);
                unchecked
                {
                    
                    using (var sbPool = Pools.GetStringBuilder())
                    using (var sbPool2 = Pools.GetStringBuilder())
                    {
                        StringBuilder consoleSummary = sbPool.Instance;
                        StringBuilder logFileSummary = sbPool2.Instance;

                        perfInfo.CpuUsagePercentage = SafeConvert.ToInt32(MachineCpu.Latest);
                        consoleSummary.AppendFormat("CPU:{0}%", perfInfo.CpuUsagePercentage);
                        logFileSummary.AppendFormat("CPU:{0}%", perfInfo.CpuUsagePercentage);
                        if (MachineTotalPhysicalMB.Latest > 0)
                        {
                            var availableRam = SafeConvert.ToInt32(MachineAvailablePhysicalMB.Latest);
                            var totalRam = SafeConvert.ToInt32(MachineTotalPhysicalMB.Latest);

                            var ramUsagePercentage = SafeConvert.ToInt32(((100.0 * (totalRam - availableRam)) / totalRam));
                            Contract.Assert(ramUsagePercentage >= 0 && ramUsagePercentage <= 100);

                            perfInfo.RamUsagePercentage = ramUsagePercentage;
                            perfInfo.TotalRamMb = totalRam;
                            perfInfo.AvailableRamMb = availableRam;
                            consoleSummary.AppendFormat(" RAM:{0}%", ramUsagePercentage);
                            logFileSummary.AppendFormat(" RAM:{0}%", ramUsagePercentage);
                        }

                        if (ModifiedPagelistMB.Latest > 0)
                        {
                            perfInfo.ModifiedPagelistMb = SafeConvert.ToInt32(ModifiedPagelistMB.Latest);
                        }

                        if (FreePagelistMB.Latest > 0)
                        {
                            perfInfo.FreePagelistMb = SafeConvert.ToInt32(FreePagelistMB.Latest);
                        }

                        if (StandbyPagelistMB.Latest > 0)
                        {
                            perfInfo.StandbyPagelistMb = SafeConvert.ToInt32(StandbyPagelistMB.Latest);
                        }

                        if (perfInfo.TotalRamMb.HasValue)
                        {
                            perfInfo.EffectiveAvailableRamMb = SafeConvert.ToInt32(perfInfo.AvailableRamMb.Value + (perfInfo.ModifiedPagelistMb ?? 0));
                            perfInfo.EffectiveRamUsagePercentage = SafeConvert.ToInt32(100.0 * (perfInfo.TotalRamMb.Value - perfInfo.EffectiveAvailableRamMb.Value) / perfInfo.TotalRamMb.Value);
                        }

                        if (CommitLimitMB.Latest > 0)
                        {
                            var commitUsed = SafeConvert.ToInt32(CommitUsedMB.Latest);
                            var commitLimit = SafeConvert.ToInt32(CommitLimitMB.Latest);
                            var commitUsagePercentage = SafeConvert.ToInt32(((100.0 * commitUsed) / commitLimit));

                            perfInfo.CommitUsagePercentage = commitUsagePercentage;
                            perfInfo.CommitUsedMb = commitUsed;
                            perfInfo.CommitLimitMb = commitLimit;
                        }

                        if (MachineBandwidth.Latest > 0)
                        {
                            perfInfo.MachineBandwidth = SafeConvert.ToLong(MachineBandwidth.Latest);
                            perfInfo.MachineKbitsPerSecSent = MachineKbitsPerSecSent.Latest;
                            perfInfo.MachineKbitsPerSecReceived = MachineKbitsPerSecReceived.Latest;
                        }

                        int diskIndex = 0;
                        perfInfo.DiskAvailableSpaceGb = new int[DiskStats.Count];
                        foreach (var disk in DiskStats)
                        {
                            var availableSpaceGb = SafeConvert.ToInt32(disk.AvailableSpaceGb.Latest);
                            perfInfo.DiskAvailableSpaceGb[diskIndex] = availableSpaceGb;
                            diskIndex++;
                        }

                        if (!OperatingSystemHelper.IsUnixOS)
                        {
                            perfInfo.DiskUsagePercentages = new int[DiskStats.Count];
                            perfInfo.DiskQueueDepths = new int[DiskStats.Count];

                            string worstDrive = "N/A";
                            int highestActiveTime = -1;
                            int highestQueueDepth = 0;

                            // Loop through and find the worst looking disk
                            diskIndex = 0;
                            foreach (var disk in DiskStats)
                            {
                                if (disk.ReadTime.Maximum == 0)
                                {
                                    perfInfo.DiskUsagePercentages[diskIndex] = 0;
                                    perfInfo.DiskQueueDepths[diskIndex] = 0;
                                    diskIndex++;
                                    // Don't consider samples unless some activity has been registered
                                    continue;
                                }

                                var activeTime = disk.CalculateActiveTime(lastOnly: true);
                                var queueDepth = SafeConvert.ToInt32(disk.QueueDepth.Latest);
                                perfInfo.DiskUsagePercentages[diskIndex] = activeTime;
                                perfInfo.DiskQueueDepths[diskIndex] = queueDepth;
                                diskIndex++;

                                logFileSummary.Append(FormatDiskUtilization(disk.Drive, activeTime));

                                if (activeTime > highestActiveTime)
                                {
                                    worstDrive = disk.Drive;
                                    highestActiveTime = activeTime;
                                    highestQueueDepth = queueDepth;
                                }
                            }

                            if (highestActiveTime != -1)
                            {
                                consoleSummary.Append(FormatDiskUtilization(worstDrive, highestActiveTime));
                            }
                        }

                        perfInfo.ProcessCpuPercentage = SafeConvert.ToInt32(ProcessCpu.Latest);
                        logFileSummary.AppendFormat(" DominoCPU:{0}%", perfInfo.ProcessCpuPercentage);

                        perfInfo.ProcessWorkingSetMB = SafeConvert.ToInt32(ProcessWorkingSetMB.Latest);
                        logFileSummary.AppendFormat(" DominoRAM:{0}MB", perfInfo.ProcessWorkingSetMB);

                        perfInfo.ConsoleResourceSummary = consoleSummary.ToString();
                        perfInfo.LogResourceSummary = logFileSummary.ToString();
                    }

                    return perfInfo;
                }
            }

            private static string FormatDiskUtilization(string drive, int activeTime)
            {
                return string.Format(CultureInfo.InvariantCulture, " {0}:{1}%", drive, activeTime);
            }

            /// <summary>
            /// Registers a sample of the performance data
            /// </summary>
            internal void RegisterSample(
                double? processCpu,
                double? processPrivateBytes,
                double? processWorkingSetBytes,
                double? threads,
                double? machineCpu,
                double? machineAvailablePhysicalBytes,
                double? machineTotalPhysicalBytes,
                double? commitUsedBytes,
                double? commitLimitBytes,
                long? machineBandwidth,
                double? machineKbitsPerSecSent,
                double? machineKbitsPerSecReceived,
                DiskStats[] diskStats,
                double? gcHeldBytes,
                double? modifiedPagelistBytes,
                double? freePagelistBytes,
                double? standbyPagelistBytes)
            {
                Interlocked.Increment(ref m_sampleCount);

                ProcessCpu.RegisterSample(processCpu);
                ProcessPrivateMB.RegisterSample(BytesToMB(processPrivateBytes));
                ProcessWorkingSetMB.RegisterSample(BytesToMB(processWorkingSetBytes));
                ProcessThreadCount.RegisterSample(threads);
                MachineCpu.RegisterSample(machineCpu);
                MachineAvailablePhysicalMB.RegisterSample(BytesToMB(machineAvailablePhysicalBytes));
                MachineTotalPhysicalMB.RegisterSample(BytesToMB(machineTotalPhysicalBytes));
                CommitUsedMB.RegisterSample(BytesToMB(commitUsedBytes));
                CommitLimitMB.RegisterSample(BytesToMB(commitLimitBytes));
                ProcessHeldMB.RegisterSample(BytesToMB(gcHeldBytes));
                MachineBandwidth.RegisterSample(machineBandwidth);
                MachineKbitsPerSecSent.RegisterSample(machineKbitsPerSecSent);
                MachineKbitsPerSecReceived.RegisterSample(machineKbitsPerSecReceived);
                ModifiedPagelistMB.RegisterSample(BytesToMB(modifiedPagelistBytes));
                FreePagelistMB.RegisterSample(BytesToMB(freePagelistBytes));
                StandbyPagelistMB.RegisterSample(BytesToMB(standbyPagelistBytes));

                Contract.Assert(m_diskStats.Length == diskStats.Length);
                for (int i = 0; i < diskStats.Length; i++)
                {
                    if (m_diskStats[i] != null && diskStats[i].IsValid)
                    {
                        m_diskStats[i].AvailableSpaceGb.RegisterSample(diskStats[i].AvailableSpaceGb);
                        m_diskStats[i].ReadTime.RegisterSample(diskStats[i].DiskPerformance.ReadTime);
                        m_diskStats[i].WriteTime.RegisterSample(diskStats[i].DiskPerformance.WriteTime);
                        m_diskStats[i].IdleTime.RegisterSample(diskStats[i].DiskPerformance.IdleTime);
                        m_diskStats[i].QueueDepth.RegisterSample(diskStats[i].DiskPerformance.QueueDepth);
                    }
                }
            }

            private static double? BytesToMB(double? bytes)
            {
                if (bytes.HasValue)
                {
                    // Technically calculations based on 1024 are MiB, but our codebase calls that MB so let's be consistent
                    return bytes.Value / (1024 * 1024);
                }

                return bytes;
            }

            /// <nodoc/>
            public void Dispose()
            {
                m_parent.RemoveAggregator(this);
            }
        }

        /// <summary>
        /// An aggregation of performance data
        /// </summary>
        public sealed class Aggregation
        {
            /// <summary>
            /// Delegate type for the <see cref="OnChange"/> event.
            /// </summary>
            public delegate void OnChangeHandler(Aggregation source);

            /// <summary>
            /// Event that is fired every time this aggregation is updated.
            /// </summary>
            public event OnChangeHandler OnChange;

            /// <summary>
            /// The value of the first sample
            /// </summary>
            public double First { get; private set; }

            /// <summary>
            /// The value of the latest sample
            /// </summary>
            public double Latest { get; private set; }

            /// <summary>
            /// The value of the minimum sample
            /// </summary>
            public double Minimum { get; private set; }

            /// <summary>
            /// The value of the maximum sample
            /// </summary>
            public double Maximum { get; private set; }

            /// <summary>
            /// The difference between the currrent value and the previous value
            /// </summary>
            public double Difference { get; private set; }

            /// <summary>
            /// The difference between the first value and the last value
            /// </summary>
            public double Range => Latest - First;

            /// <summary>
            /// The value of all samples
            /// </summary>
            public double Total { get; private set; }

            /// <summary>
            /// The number of samples collected
            /// </summary>
            public int Count { get; private set; }

            /// <summary>
            /// The time in UTC when the first sample was collected
            /// </summary>
            public DateTime FirstSampleTime = DateTime.MinValue;

            /// <summary>
            /// The time in UTC when the latest sample was collected
            /// </summary>
            public DateTime LatestSampleTime = DateTime.MinValue;

            /// <summary>
            /// The average value of all samples collected
            /// </summary>
            public double Average => Count == 0 ? 0 : Total / Count;

            /// <summary>
            /// Registers a new sample
            /// </summary>
            public void RegisterSample(double? sample)
            {
                if (sample.HasValue && !double.IsNaN(sample.Value))
                {
                    Difference = sample.Value - Latest;
                    Latest = sample.Value;
                    LatestSampleTime = DateTime.UtcNow;

                    if (Count == 0)
                    {
                        First = Latest;
                        FirstSampleTime = DateTime.UtcNow;
                        Maximum = Latest;
                        Minimum = Latest;
                    }

                    if (Maximum < Latest)
                    {
                        Maximum = Latest;
                    }

                    if (Minimum > Latest)
                    {
                        Minimum = Latest;
                    }

                    Total += Latest;
                    Count++;

                    OnChange?.Invoke(this);
                }
            }
        }
    }
}
