// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Native.IO;
using System.IO;

namespace BuildXL.Pips
{
    /// <summary>
    /// General performance information for running any pip.
    /// </summary>
    public class PipExecutionPerformance
    {
        /// <summary>
        /// Indicates the manner in which a pip executed.
        /// </summary>
        public readonly PipExecutionLevel ExecutionLevel;

        /// <summary>
        /// Start time in UTC.
        /// </summary>
        public readonly DateTime ExecutionStart;

        /// <summary>
        /// Stop time in UTC.
        /// </summary>
        public readonly DateTime ExecutionStop;

        /// <summary>
        /// Identifier for worker which executed pip
        /// </summary>
        // TODO: This doesn't seem to be the right place for WorkerId. Move it to a more appropriate class (PipResult?)
        // The member is not readonly because it is not initialized properly and only ProcessExecutionResultSerializer can make it right.
        // We have an option to either remove the readonly flag or to make yet another copy during the deserialization. Given the fact
        // that this is probably not the right place, removing the flag seems to cause less harm.
        public uint WorkerId;

        /// <nodoc />
        protected PipExecutionPerformance(PipExecutionLevel level, DateTime executionStart, DateTime executionStop, uint workerId)
        {
            Contract.Requires(executionStart.Kind == DateTimeKind.Utc);
            Contract.Requires(executionStop.Kind == DateTimeKind.Utc);

            // Since these don't use the high precision clock, very occasionally the start & stop as seen with DateTime
            // are out of order. This is a quick fix until 453683 is fully addressed
            if (executionStart > executionStop)
            {
                executionStop = executionStart;
            }

            ExecutionLevel = level;
            ExecutionStart = executionStart;
            ExecutionStop = executionStop;
            WorkerId = workerId;
        }

        /// <summary>
        /// Creates perf info given a specific pip status (it is mapped to a <see cref="PipExecutionLevel"/>) and a start time
        /// (the end time is inferred as the current time).
        /// </summary>
        public static PipExecutionPerformance Create(PipResultStatus status, DateTime executionStart)
        {
            Contract.Requires(executionStart.Kind == DateTimeKind.Utc);

            return new PipExecutionPerformance(status.ToExecutionLevel(), executionStart, DateTime.UtcNow, workerId: 0);
        }

        /// <summary>
        /// Creates perf info given a specific pip status (it is mapped to a <see cref="PipExecutionLevel"/>),
        /// and with start-stop markers at the current instant (zero duration).
        /// </summary>
        public static PipExecutionPerformance CreatePoint(PipResultStatus status)
        {
            DateTime point = DateTime.UtcNow;
            return new PipExecutionPerformance(status.ToExecutionLevel(), point, point, workerId: 0);
        }

        /// <summary>
        /// Serialize the performace data to a binary format
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            ProcessPipExecutionPerformance processPipExecutionPerformance = this as ProcessPipExecutionPerformance;
            bool isProcessExecutionPerformance = processPipExecutionPerformance != null;
            writer.Write(isProcessExecutionPerformance);

            SerializeFields(writer);
            if (isProcessExecutionPerformance)
            {
                processPipExecutionPerformance.SerializeExtraFields(writer);
            }
        }

        /// <summary>
        /// Deserialize performance data
        /// </summary>
        public static PipExecutionPerformance Deserialize(BuildXLReader reader)
        {
            bool isProcessExecutionPerformance = reader.ReadBoolean();

            PipExecutionLevel level;
            DateTime executionStart;
            DateTime executionStop;
            uint workerId;
            DeserializeFields(reader, out level, out executionStart, out executionStop, out workerId);

            if (isProcessExecutionPerformance)
            {
                return ProcessPipExecutionPerformance.Deserialize(reader, level, executionStart, executionStop, workerId);
            }
            else
            {
                return new PipExecutionPerformance(level, executionStart, executionStop, workerId);
            }
        }

        internal void SerializeFields(BuildXLWriter writer)
        {
            writer.Write((byte)ExecutionLevel);
            writer.Write(ExecutionStart);
            writer.Write(ExecutionStop);
            writer.WriteCompact(WorkerId);
        }

        internal static void DeserializeFields(BuildXLReader reader, out PipExecutionLevel level, out DateTime executionStart, out DateTime executionStop, out uint workerId)
        {
            level = (PipExecutionLevel)reader.ReadByte();
            executionStart = reader.ReadDateTime();
            executionStop = reader.ReadDateTime();
            workerId = reader.ReadUInt32Compact();
        }
    }

    /// <summary>
    /// Indicates the manner in which a pip executed.
    /// </summary>
    public enum PipExecutionLevel
    {
        /// <summary>
        /// The pip's full work was performed.
        /// </summary>
        Executed,

        /// <summary>
        /// The pip was cached, and some work was performed to deploy it from cache.
        /// </summary>
        Cached,

        /// <summary>
        /// The pip was fully up to date.
        /// </summary>
        UpToDate,

        /// <summary>
        /// The pip failed.
        /// </summary>
        Failed,
    }

    /// <summary>
    /// Performance information for a process pip.
    /// </summary>
    public sealed class ProcessPipExecutionPerformance : PipExecutionPerformance
    {
        /// <summary>
        /// Time spent executing the entry-point process (possibly zero, such as if this execution was cached).
        /// </summary>
        public TimeSpan ProcessExecutionTime { get; }

        /// <summary>
        /// Counters for aggregate IO transfer for the entire process tree.
        /// </summary>
        public IOCounters IO { get; }

        /// <summary>
        /// User-mode execution time. Note that this counter increases as threads in the process tree execute (it is not equivalent to wall clock time).
        /// </summary>
        public TimeSpan UserTime { get; }

        /// <summary>
        /// Kernel-mode execution time. Note that this counter increases as threads in the process tree execute (it is not equivalent to wall clock time).
        /// </summary>
        public TimeSpan KernelTime { get; }

        /// <summary>
        /// Memory counters
        /// </summary>
        public ProcessMemoryCounters MemoryCounters { get; }

        /// <summary>
        /// Number of processes that executed as part of the pip (the entry-point process may start children).
        /// </summary>
        public uint NumberOfProcesses { get; }

        /// <summary>
        /// Counters for the classification of file monitoring violations encountered during process execution.
        /// </summary>
        public FileMonitoringViolationCounters FileMonitoringViolations { get; }

        /// <summary>
        /// Fingerprint identity as used for cache lookup.
        /// </summary>
        public Fingerprint Fingerprint { get; }

        /// <summary>
        /// Unique ID of the cache descriptor (if one was stored or retrieved from cache).
        /// </summary>
        public ulong? CacheDescriptorId { get; }

        /// <nodoc />
        public ProcessPipExecutionPerformance(
            PipExecutionLevel level,
            DateTime executionStart,
            DateTime executionStop,
            Fingerprint fingerprint,
            TimeSpan processExecutionTime,
            FileMonitoringViolationCounters fileMonitoringViolations,
            IOCounters ioCounters,
            TimeSpan userTime,
            TimeSpan kernelTime,
            ProcessMemoryCounters memoryCounters,
            uint numberOfProcesses,
            uint workerId)
            : base(level, executionStart, executionStop, workerId)
        {
            Contract.Requires(executionStart.Kind == DateTimeKind.Utc);
            Contract.Requires(executionStop.Kind == DateTimeKind.Utc);
            Contract.Requires(processExecutionTime >= TimeSpan.Zero);
            Contract.Requires(userTime >= TimeSpan.Zero);
            Contract.Requires(kernelTime >= TimeSpan.Zero);

            ProcessExecutionTime = processExecutionTime;
            Fingerprint = fingerprint;
            FileMonitoringViolations = fileMonitoringViolations;
            IO = ioCounters;
            UserTime = userTime;
            KernelTime = kernelTime;
            MemoryCounters = memoryCounters;
            NumberOfProcesses = numberOfProcesses;
        }

        /// <summary>
        /// Serialize the process performace data to a binary format
        /// </summary>
        public new void Serialize(BuildXLWriter writer)
        {
            SerializeFields(writer);
            SerializeExtraFields(writer);
        }

        /// <summary>
        /// Deserialize process performance data
        /// </summary>
        public static new ProcessPipExecutionPerformance Deserialize(BuildXLReader reader)
        {
            PipExecutionLevel level;
            DateTime executionStart;
            DateTime executionStop;
            uint workerId;
            DeserializeFields(reader, out level, out executionStart, out executionStop, out workerId);

            return Deserialize(reader, level, executionStart, executionStop, workerId);
        }

        internal void SerializeExtraFields(BuildXLWriter writer)
        {
            Fingerprint.WriteTo(writer);

            writer.Write(ProcessExecutionTime);
            WriteFileMonitoringViolationCounters(writer, FileMonitoringViolations);
            IO.Serialize(writer);
            writer.Write(UserTime);
            writer.Write(KernelTime);
            MemoryCounters.Serialize(writer);
            writer.WriteCompact(NumberOfProcesses);
        }

        internal static ProcessPipExecutionPerformance Deserialize(BuildXLReader reader, PipExecutionLevel level, DateTime executionStart, DateTime executionStop, uint workerId)
        {
            var fingerprint = FingerprintUtilities.CreateFrom(reader);

            TimeSpan processExecutionTime = reader.ReadTimeSpan();
            FileMonitoringViolationCounters fileMonitoringViolations = ReadFileMonitoringViolationCounters(reader);
            IOCounters ioCounters = IOCounters.Deserialize(reader);
            TimeSpan userTime = reader.ReadTimeSpan();
            TimeSpan kernelTime = reader.ReadTimeSpan();
            ProcessMemoryCounters memoryCounters = ProcessMemoryCounters.Deserialize(reader);

            uint numberOfProcesses = reader.ReadUInt32Compact();

            return new ProcessPipExecutionPerformance(
                fingerprint: fingerprint,
                level: level,
                executionStart: executionStart,
                executionStop: executionStop,
                processExecutionTime: processExecutionTime,
                fileMonitoringViolations: fileMonitoringViolations,
                ioCounters: ioCounters,
                userTime: userTime,
                kernelTime: kernelTime,
                memoryCounters: memoryCounters,
                numberOfProcesses: numberOfProcesses,
                workerId: workerId);
        }

        private static FileMonitoringViolationCounters ReadFileMonitoringViolationCounters(BuildXLReader reader)
        {
            return new FileMonitoringViolationCounters(
                numFileAccessViolationsNotWhitelisted: reader.ReadInt32Compact(),
                numFileAccessesWhitelistedButNotCacheable: reader.ReadInt32Compact(),
                numFileAccessesWhitelistedAndCacheable: reader.ReadInt32Compact());
        }

        private static void WriteFileMonitoringViolationCounters(BuildXLWriter writer, FileMonitoringViolationCounters counters)
        {
            writer.WriteCompact((int)counters.NumFileAccessViolationsNotWhitelisted);
            writer.WriteCompact((int)counters.NumFileAccessesWhitelistedButNotCacheable);
            writer.WriteCompact((int)counters.NumFileAccessesWhitelistedAndCacheable);
        }
    }

    /// <summary>
    /// Counters for the classification of file monitoring violations encountered during process execution.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct FileMonitoringViolationCounters
    {
        /// <summary>
        /// Count of accesses such that the access was whitelisted, but was not in the cache-friendly part of the whitelist. The pip should not be cached.
        /// </summary>
        public readonly int NumFileAccessesWhitelistedButNotCacheable;

        /// <summary>
        /// Count of accesses such that the access was whitelisted, via the cache-friendly part of the whitelist. The pip may be cached.
        /// </summary>
        public readonly int NumFileAccessesWhitelistedAndCacheable;

        /// <summary>
        /// Count of accesses such that the access was not whitelisted at all, and should be reported as a violation.
        /// </summary>
        public readonly int NumFileAccessViolationsNotWhitelisted;

        /// <nodoc />
        public FileMonitoringViolationCounters(
            int numFileAccessesWhitelistedButNotCacheable,
            int numFileAccessesWhitelistedAndCacheable,
            int numFileAccessViolationsNotWhitelisted)
        {
            NumFileAccessViolationsNotWhitelisted = numFileAccessViolationsNotWhitelisted;
            NumFileAccessesWhitelistedAndCacheable = numFileAccessesWhitelistedAndCacheable;
            NumFileAccessesWhitelistedButNotCacheable = numFileAccessesWhitelistedButNotCacheable;
        }

        /// <nodoc />
        public int Total => NumFileAccessesWhitelistedButNotCacheable + NumFileAccessesWhitelistedAndCacheable + NumFileAccessViolationsNotWhitelisted;

        /// <summary>
        /// Total violations whitelisted. This is the sum of cacheable and non-cacheable violations.
        /// </summary>
        public int TotalWhitelisted => NumFileAccessesWhitelistedAndCacheable + NumFileAccessesWhitelistedButNotCacheable;

        /// <summary>
        /// Indicates if this context has reported accesses which should mark the owning process as cache-ineligible.
        /// </summary>
        public bool HasUncacheableFileAccesses => NumFileAccessesWhitelistedButNotCacheable > 0;
    }
}
