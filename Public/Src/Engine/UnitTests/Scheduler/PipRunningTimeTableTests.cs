// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public class PipRunningTimeTableTests
    {
        [Fact]
        public void PipHistoricPerfDataConstructorDoesntCrash()
        {
            // TODO: Use IntelliTest/Pex for this.
            var times = new[]
                        {
                            new DateTime(DateTime.MinValue.Year + 1, 1, 1).ToUniversalTime(), 
                            new DateTime(2015, 1, 1).ToUniversalTime(),
                            new DateTime(DateTime.MaxValue.Year, 1, 1).ToUniversalTime()
                        };
            var spans = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.MaxValue };
            var ints = new[] {0, int.MaxValue};
            var ulongs = new ulong[] {0, 1, ulong.MaxValue};
            var uints = new uint[] {0, 1, uint.MaxValue};

            foreach (var executionStart in times)
            {
                foreach (var executionStop in times)
                {
                    foreach (var processExecutionTime in spans)
                    {
                        foreach (var fileMonitoringWarnings in ints)
                        {
                            foreach (var ioCounters in new[]
                                                       {
                                                           new IOCounters(
                                                               readCounters: new IOTypeCounters(operationCount: 1, transferCount: ulong.MaxValue),
                                                               writeCounters: new IOTypeCounters(operationCount: 0, transferCount: 0),
                                                               otherCounters: new IOTypeCounters(operationCount: 0, transferCount: 0)
                                                               ),
                                                           new IOCounters(
                                                               readCounters: new IOTypeCounters(operationCount: 0, transferCount: 0),
                                                               writeCounters: new IOTypeCounters(operationCount: 0, transferCount: 0),
                                                               otherCounters: new IOTypeCounters(operationCount: 0, transferCount: 0)
                                                               )
                                                       })
                            {
                                foreach (var userTime in spans)
                                {
                                    foreach (var kernelTime in spans)
                                    {
                                        foreach (var peakMemoryUsage in ulongs)
                                        {
                                            foreach (var numberOfProcesses in uints)
                                            {
                                                foreach (var workerId in uints)
                                                {
                                                    if (executionStart > executionStop)
                                                    {
                                                        continue;
                                                    }

                                                    var performance = new ProcessPipExecutionPerformance(
                                                        PipExecutionLevel.Executed,
                                                        executionStart,
                                                        executionStop,
                                                        FingerprintUtilities.ZeroFingerprint,
                                                        processExecutionTime,
                                                        new FileMonitoringViolationCounters(fileMonitoringWarnings, fileMonitoringWarnings, fileMonitoringWarnings), 
                                                        ioCounters,
                                                        userTime,
                                                        kernelTime,
                                                        peakMemoryUsage,
                                                        peakMemoryUsage,
                                                        peakMemoryUsage,
                                                        numberOfProcesses,
                                                        workerId);
                                                    var data = new PipHistoricPerfData(performance);
                                                    data = data.Merge(data);
                                                    Analysis.IgnoreResult(data);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void EverythingAsync()
        {
            const int MaxExecTime = 24 * 3600 * 1000;

            var stream = new MemoryStream();
            var r = new Random(0);
            for (int i = 0; i < 10; i++)
            {
                int seed = r.Next(100 * 1000);
                PipRuntimeTimeTable table = new PipRuntimeTimeTable();
                XAssert.IsTrue(table.Count == 0);

                var s = new Random(seed);
                var buffer = new byte[sizeof(long)];
                for (int j = 0; j < 10; j++)
                {
                    s.NextBytes(buffer);
                    long semiStableHash = BitConverter.ToInt64(buffer, 0);

                    int execTime = s.Next(MaxExecTime);
                    var processPipExecutionPerformance = new ProcessPipExecutionPerformance(
                        PipExecutionLevel.Executed,
                        DateTime.UtcNow,
                        DateTime.UtcNow.AddMilliseconds(execTime),
                        FingerprintUtilities.ZeroFingerprint,
                        TimeSpan.FromMilliseconds(execTime),
                        default(FileMonitoringViolationCounters),
                        default(IOCounters),
                        TimeSpan.FromMilliseconds(execTime),
                        TimeSpan.FromMilliseconds(execTime / 2),
                        1024 * 1024,
                        1024 * 1024,
                        1024 * 1024,
                        1,
                        workerId: 0);

                    PipHistoricPerfData runTimeData = new PipHistoricPerfData(processPipExecutionPerformance);
                    table[semiStableHash] = runTimeData;
                }

                XAssert.IsTrue(table.Count == 10);

                stream.Position = 0;
                table.Save(stream);
                stream.Position = 0;
                table = PipRuntimeTimeTable.Load(stream);
                XAssert.IsTrue(table.Count == 10);

                s = new Random(seed);
                for (int j = 0; j < 10; j++)
                {
                    s.NextBytes(buffer);
                    long semiStableHash = BitConverter.ToInt64(buffer, 0);
                    XAssert.IsTrue(table[semiStableHash].DurationInMs >= (uint) s.Next(MaxExecTime));
                }
            }
        }

        [Fact]
        public void TimeToLive()
        {
            int execTime = 1;
            var processPipExecutionPerformance = new ProcessPipExecutionPerformance(
                PipExecutionLevel.Executed,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMilliseconds(execTime),
                FingerprintUtilities.ZeroFingerprint,
                TimeSpan.FromMilliseconds(execTime),
                default(FileMonitoringViolationCounters),
                default(IOCounters),
                TimeSpan.FromMilliseconds(execTime),
                TimeSpan.FromMilliseconds(execTime / 2),
                1024 * 1024,
                1024 * 1024,
                1024 * 1024,
                1,
                workerId: 0);

            PipHistoricPerfData runTimeData = new PipHistoricPerfData(processPipExecutionPerformance);
            PipRuntimeTimeTable table = new PipRuntimeTimeTable();
            var semiStableHashToKeep = 0;
            table[semiStableHashToKeep] = runTimeData;
            var semiStableHashToDrop = 1;
            table[semiStableHashToDrop] = runTimeData;
            var stream = new MemoryStream();
            for (int i = 0; i < PipHistoricPerfData.DefaultTimeToLive; i++)
            {
                stream.Position = 0;
                table.Save(stream);
                stream.Position = 0;
                table = PipRuntimeTimeTable.Load(stream);
                Analysis.IgnoreResult(table[semiStableHashToKeep]);
            }

            stream.Position = 0;
            table = PipRuntimeTimeTable.Load(stream);
            XAssert.AreEqual(1u, table[semiStableHashToKeep].DurationInMs);
            XAssert.AreEqual(0u, table[semiStableHashToDrop].DurationInMs);
        }
    }
}
