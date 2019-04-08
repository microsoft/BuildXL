// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces.Test;
using BuildXL.Cache.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Tests
{
    [ExcludeFromCodeCoverage]
    public class TestBasicFilesystemStats
    {
        // Local TextWriter wrapper class over the ITestOutputHelper that XUnit provides
        private class MyTextOutput
        {
            private readonly ITestOutputHelper m_output;

            public MyTextOutput(ITestOutputHelper output)
            {
                m_output = output;
            }

            public void WriteLine(string data)
            {
                m_output.WriteLine(data);
            }

            public void LogStats(CacheSessionStatistics[] stats)
            {
                foreach (var stat in stats)
                {
                    m_output.WriteLine("CacheId = {0}", stat.CacheId);
                    foreach (var kv in stat.Statistics.OrderBy(v => v.Key))
                    {
                        m_output.WriteLine("  {0} = {1}", kv.Key, kv.Value);
                    }
                }
            }
        }

        private readonly MyTextOutput m_output;

        public TestBasicFilesystemStats(ITestOutputHelper output)
        {
            m_output = new MyTextOutput(output);
            TestType = new TestBasicFilesystem();
        }

        protected virtual TestCacheCore TestType { get; }

        private async Task<long> PinAndGetStreamSize(ICacheReadOnlySession session, CasHash hash)
        {
            long result;

            await session.PinToCasAsync(hash).SuccessAsync();

            using (var stream = await session.GetStreamAsync(hash).SuccessAsync())
            {
                result = stream.Length;
            }

            return result;
        }

        // We don't pin here as we do this after having pinned everything in the PinAndGetStreamSize
        private async Task<long> GetAsFile(ICacheReadOnlySession session, CasHash hash)
        {
            long result;

            var tmpFile = Path.GetTempFileName();
            try
            {
                await session.ProduceFileAsync(hash, tmpFile, FileState.ReadOnly).SuccessAsync();
                result = new FileInfo(tmpFile).Length;
            }
            finally
            {
                File.Delete(tmpFile);
            }

            return result;
        }

        // A quick test of the telemetry for the cache
        [Fact]
        public async Task TestCacheStats()
        {
            string testName = nameof(TestCacheStats);

            ICache firstInvocation = await TestType.CreateCacheAsync(testName, true);
            Guid originalGuid = firstInvocation.CacheGuid;

            ICacheSession session = (await firstInvocation.CreateSessionAsync("firstSession")).Success();

            PipDefinition[] pips =
            {
                new PipDefinition("PipA", pipSize: 4, hashIndex: 2),
                new PipDefinition("PipB", pipSize: 5, hashIndex: 3),
                new PipDefinition("PipC", pipSize: 5, hashIndex: 4),

                // Note that this second "PipC" just has a different 3rd hash in the
                // strong fingerprint but has the same weak fingerprint (and content)
                // This is specifically to check on some other counter cases in the cache
                new PipDefinition("PipC", pipSize: 5, hashIndex: 3)
            };

            await pips.BuildAsync(session);

            (await session.CloseAsync()).Success();

            var stats = await session.GetStatisticsAsync().SuccessAsync();

            // We output the stats to the test harness such that we can debug failures
            m_output.WriteLine("\nFirst Fake Build:");
            m_output.LogStats(stats);

            XAssert.AreEqual(4, stats[0].Statistics["AddOrGet_New_Count"]);

            // Sum of the number of CAS entries in the 4 fingerprints. (4 + 5 + 5 + 5)
            XAssert.AreEqual(19, stats[0].Statistics["AddOrGet_New_Number_Sum"]);

            // Sum-squared of CAS entries in the 4 fingerprints (16 + 25 + 25 + 25)
            XAssert.AreEqual(91, stats[0].Statistics["AddOrGet_New_Number_Sum2"]);

            // Time should be non-zero (since we added some) [and sum-squared]
            // NOTE: It seems some build machines have a slow clock (not high resolution)
            //       and are fast enough that no time passes when doing the test AddOrGet
            //       calls (even though they create a directory and create/write a file)
            //       So, to remove flaky, these are not checked as being non-zero but
            //       just that they exist.  (Which does not actually test much, unfortunately)
            // XAssert.AreNotEqual(0, stats[0].Statistics["AddOrGet_New_Time_Sum"]);
            // XAssert.AreNotEqual(0, stats[0].Statistics["AddOrGet_New_Time_Sum2"]);
            XAssert.IsTrue(stats[0].Statistics.ContainsKey("AddOrGet_New_Time_Sum"));
            XAssert.IsTrue(stats[0].Statistics.ContainsKey("AddOrGet_New_Time_Sum2"));

            // Only the first 3 pips have unique CAS content (4 + 5 + 5) and input lists (1 + 1 + 1)
            // that makes for 17 unique new CAS entries added
            XAssert.AreEqual(17, stats[0].Statistics["AddToCas_Stream_New_Count"]);

            // ... and 5 + 1 (aka 6) that are duplicated content
            XAssert.AreEqual(6, stats[0].Statistics["AddToCas_Stream_Dup_Count"]);

            // Bytes of new content  (arrived at by adding up the bytes in all of the cas files generated by the
            // first 3 pips
            XAssert.AreEqual(182, stats[0].Statistics["AddToCas_Stream_New_Bytes_Sum"]);
            XAssert.AreEqual(3738, stats[0].Statistics["AddToCas_Stream_New_Bytes_Sum2"]);

            // And 65 bytes of duplicate
            XAssert.AreEqual(65, stats[0].Statistics["AddToCas_Stream_Dup_Bytes_Sum"]);
            XAssert.AreEqual(1405, stats[0].Statistics["AddToCas_Stream_Dup_Bytes_Sum2"]);

            // And that at close time we had 4 fingerprints in our session
            XAssert.AreEqual(1, stats[0].Statistics["Close_Count"]);
            XAssert.AreEqual(4, stats[0].Statistics["Close_Fingerprints"]);

            // There should be no disconnects
            XAssert.AreEqual(0, stats[0].Statistics["CacheDisconnected"]);

            // Now to check for the second session (all cache hits)
            session = (await firstInvocation.CreateSessionAsync("secondSession")).Success();

            // This time we will also read all of the CAS items and get the total size
            // of all of the data we read (and there will be some duplicates)
            double totalCas = 0;
            double totalCasSize = 0;
            foreach (var record in await pips.BuildAsync(session))
            {
                totalCas++;
                totalCasSize += await PinAndGetStreamSize(session, record.StrongFingerprint.CasElement);

                foreach (var casItem in record.CasEntries)
                {
                    totalCas++;
                    totalCasSize += await PinAndGetStreamSize(session, casItem);
                }
            }

            // Silly, but we do this again, just to get some bigger counts
            // and to do file based produce (different counts) - this should
            // then match the counts above.
            double totalCasFileSize = 0;
            foreach (var record in await pips.BuildAsync(session))
            {
                totalCasFileSize += await GetAsFile(session, record.StrongFingerprint.CasElement);
                foreach (var casItem in record.CasEntries)
                {
                    totalCasFileSize += await GetAsFile(session, casItem);
                }
            }

            (await session.CloseAsync()).Success();

            stats = await session.GetStatisticsAsync().SuccessAsync();

            // We output the stats to the test harness such that we can debug failures
            m_output.WriteLine("\nSecond Fake Build:");
            m_output.LogStats(stats);

            // The files produced and the streams produced should match
            XAssert.AreEqual(totalCasSize, totalCasFileSize, "Files and Streams should match in size!");

            // And that at close time we had 4 fingerprints in our session
            XAssert.AreEqual(1, stats[0].Statistics["Close_Count"]);
            XAssert.AreEqual(4, stats[0].Statistics["Close_Fingerprints"]);

            XAssert.AreEqual(0, stats[0].Statistics["AddToCas_Stream_New_Count"]);
            XAssert.AreEqual(0, stats[0].Statistics["AddToCas_Stream_Dup_Count"]);

            // We have 8 pips and enumerate the fingerprints 8 times
            // from weak but we know that there are 2 of them that share so...
            XAssert.AreEqual(8, stats[0].Statistics["EnumerateStrongFingerprints_Count"]);
            XAssert.AreEqual(10, stats[0].Statistics["EnumerateStrongFingerprints_Number_Sum"]);
            XAssert.AreEqual(14, stats[0].Statistics["EnumerateStrongFingerprints_Number_Sum2"]);

            // And we then get the entry from the cache (hashes will match)
            XAssert.AreEqual(8, stats[0].Statistics["GetCacheEntry_Hit_Count"]);

            // There are 17 unique CAS entries and 6 total duplicates
            XAssert.AreEqual(17, stats[0].Statistics["PinToCas_Hit_Count"]);
            XAssert.AreEqual(6, stats[0].Statistics["PinToCas_Dup_Count"]);

            // But we blindly get each and every one of them (all 23) and their total size
            XAssert.AreEqual(23, stats[0].Statistics["GetStream_Hit_Count"]);
            XAssert.AreEqual(247, stats[0].Statistics["GetStream_Hit_Bytes_Sum"]);
            XAssert.AreEqual(5143, stats[0].Statistics["GetStream_Hit_Bytes_Sum2"]);

            // But we blindly get each and every one of them (all 23) and their total size (as files)
            XAssert.AreEqual(247, stats[0].Statistics["ProduceFile_Hit_Bytes_Sum"]);
            XAssert.AreEqual(5143, stats[0].Statistics["ProduceFile_Hit_Bytes_Sum2"]);
            XAssert.AreEqual(23, stats[0].Statistics["ProduceFile_Hit_Count"]);

            // Validate that these are the values we observed
            XAssert.AreEqual(totalCas, stats[0].Statistics["GetStream_Hit_Count"]);
            XAssert.AreEqual(totalCasSize, stats[0].Statistics["GetStream_Hit_Bytes_Sum"]);

            // There should be no disconnects
            XAssert.AreEqual(0, stats[0].Statistics["CacheDisconnected"]);

            await TestType.ShutdownCacheAsync(firstInvocation, testName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TestCacheDisconnectedStat(bool namedSession)
        {
            string testName = nameof(TestCacheDisconnectedStat);

            var tmpFileName = Path.GetTempFileName();
            FileStream tempFile = null;
            try
            {
                tempFile = File.Open(tmpFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                ICache cache = await TestType.CreateCacheAsync(testName, true);
                PipDefinition[] pips =
                {
                    new PipDefinition("PipA", pipSize: 4, hashIndex: 2),
                };

                ICacheSession session;

                if (namedSession)
                {
                    session = (await cache.CreateSessionAsync("NamedSession")).Success();
                }
                else
                {
                    session = (await cache.CreateSessionAsync()).Success();
                }

                CallbackCacheSessionWrapper sessionWrapper = new CallbackCacheSessionWrapper(session);

                // We have to generate a cache disconnect. To do that we will override one of the ICacheSession calls that the BuildAsync method will issue
                // and force it to attempt to open tmpFileName (which is locked). This will generate an IO exception when it tries to open it and the cache will disconnect.
                // Unfortunately, BuildAsync does not seem to call AddToCasAsync with a file name, only with a stream. The only way to force an IO error is to override
                // the AddToCasAsync method that takes a Stream and make it call AddToCasAsync with a filename and pass in tmpFileName.
                // It is ugly, but this was the only way to generate a fake cache disconnect.
                sessionWrapper.AddToCasAsyncCallback = (Stream filestream, CasHash? casHash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
                {
                    return wrappedSession.AddToCasAsync(tmpFileName, FileState.ReadOnly, casHash, urgencyHint, activityId);
                };

                try
                {
                    await pips.BuildAsync(sessionWrapper);
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // the session wrapper is forcing a disconnect and that will end up throwing an exception that BuildAsync does not handle.
                    // We can disregard this exception. The goal was to put the cache into a disconnected state.
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                (await session.CloseAsync()).Success();

                var stats = await session.GetStatisticsAsync().SuccessAsync();

                // We output the stats to the test harness such that we can debug failures
                m_output.WriteLine("\nFirst Fake Build:");
                m_output.LogStats(stats);

                // There should be 1 disconnect and the cache should be in Disconnected state.
                XAssert.AreEqual(1, stats[0].Statistics["CacheDisconnected"]);
                XAssert.IsTrue(cache.IsDisconnected);

                await TestType.ShutdownCacheAsync(cache, testName);
            }
            finally
            {
                tempFile.Close();
                File.Delete(tmpFileName);
            }
        }
    }
}
