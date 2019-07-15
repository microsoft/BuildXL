// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;

namespace BuildXL.Cache.Tests
{
    public static class TestExtensions
    {
        /// <summary>
        /// Create a fingerprint from a content hash.
        /// </summary>
        public static Hash ToFingerprint(this CasHash casHash)
        {
            var hex = casHash.ToString();
            var fingerprint = FingerprintUtilities.Hash(hex);
            return new Hash(fingerprint);
        }
    }

    /// <summary>
    /// This is an abstract class that provides core cache testing support
    /// for any class that implements ICache.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class TestCacheCore : IDisposable
    {
        /// <summary>
        /// Helper method to call cache factory with the given config for testing.
        /// </summary>
        /// <param name="config">Cache construction configuration</param>
        /// <returns>possible ICache or failure</returns>
        protected Task<Possible<ICache, Failure>> InitializeCacheAsync(string config)
        {
            return CacheFactory.InitializeCacheAsync(config, default(Guid));
        }

        /// <summary>
        /// The path to where temporary files and directories may be created
        /// </summary>
        protected string ScratchPath => Path.Combine(Path.GetTempPath(), GetType().ToString());

        /// <summary>
        /// Subclasses may specify that they do not support the ICache concepts of supporting/recording multiple named sessions.
        /// </summary>
        protected virtual bool ImplementsTrackedSessions => true;

        /// <summary>
        /// Subclasses may set this to specify the name of the dummy session that they implement (representing all known strong fingerprints).
        /// </summary>
        protected virtual string DummySessionName => null;

        /// <summary>
        /// Subclasses may specify that they return an extra sentinel when their enumeration is complete
        /// so that their emptiness can be discovered in an async context.
        /// </summary>
        public virtual bool ReturnsSentinelWhenEmpty => false;

        /// <summary>
        /// Enumeration of EventSournces that should be monitored during the test.
        /// </summary>
        /// <remarks>
        /// Any EventSources returned are attached to by a custom listener and monitored for malformed self describing events, or events
        /// from <see cref="CacheActivity"/> that indicate the use of Dispose instead of Stop to end the activity.
        /// </remarks>
        protected abstract IEnumerable<EventSource> EventSources { get; }

        private readonly TestEventListener m_testListener;

        protected TestCacheCore()
        {
            m_testListener = new TestEventListener();

            foreach (EventSource oneSource in EventSources)
            {
                m_testListener.EnableEvents(oneSource, EventLevel.Verbose);
            }
        }

        /// <summary>
        ///  Subclasses must provide a way to create their cache
        ///  instance with the given cache ID.  The cache should
        ///  be empty when constructed this way.
        /// </summary>
        /// <param name="cacheId">The CacheId to be used</param>
        /// <param name="strictMetadataCasCoupling">If possible, use this strictness setting</param>
        /// <param name="authoritative">Whether or not a cache is authoritative: Implementations that don't support this mode can ignore this flag.</param>
        /// <returns>
        ///  JSON config for an ICache for us to use
        ///
        ///  If the cache requires or does not implement strict metadata to CAS coupling
        ///  then the value will be ignored.
        /// </returns>
        public abstract string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false);

        /// <summary>
        /// Calls NewCache to get a cache with the given ID
        /// and validates that it got the ID it expected.
        /// </summary>
        /// <param name="cacheId">Desired cache ID</param>
        /// <param name="strictMetadataCasCoupling">If possible, use this strictness setting</param>
        /// <returns>
        /// An empty ICache instance for use
        ///
        /// If the cache requires strict metadata to CAS coupling then
        /// passing false for strictMetadataCasCoupling will be ignored.
        /// </returns>
        public virtual async Task<ICache> CreateCacheAsync(string cacheId, bool strictMetadataCasCoupling = true)
        {
            string cacheConfigData = NewCache(cacheId, strictMetadataCasCoupling);

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(cacheConfigData);

            ICache cache = cachePossible.Success();
            XAssert.AreEqual(cacheId, cache.CacheId, "Cache Id's are not equal");

            return cache;
        }

        /// <summary>
        /// Returns the properly formatted cache Id for a given ID
        /// </summary>
        /// <remarks>
        /// Some caches (Such as the VerticalAggregator) mutate the cache ID to provide
        /// additional information, this allows them to be tested.
        /// </remarks>
        /// <param name="cacheId">initial cache Id</param>
        /// <returns>formatted cache id</returns>
        protected virtual string GetCacheFormattedId(string cacheId)
        {
            return cacheId;
        }

        /// <summary>
        /// Create a session and validate its base constructs
        /// </summary>
        /// <param name="cache">The cache</param>
        /// <param name="sessionId">The session name to create</param>
        /// <returns>A cache session</returns>
        protected async Task<ICacheSession> CreateSessionAsync(ICache cache, string sessionId)
        {
            ICacheSession session = (await cache.CreateSessionAsync(sessionId)).Success();
            XAssert.AreEqual(sessionId, session.CacheSessionId);

            return session;
        }

        /// <summary>
        /// A simple way to make a unique test name based on
        /// the passed in name and the current test implementation type
        /// </summary>
        /// <param name="testName">Base test name</param>
        /// <returns>Unique test name that includes the test implementation type</returns>
        protected string MakeCacheId(string testName)
        {
            return testName;
        }

        /// <summary>
        /// A simple way to make a unique folder name used as a root folder for various caches
        /// </summary>
        /// <param name="testName">Base test name</param>
        /// <param name="jsonCompatible">If true, escape the string to be jsonCompatible</param>
        /// <returns>Unique folder name that does not exist yet</returns>
        protected string GenerateCacheFolderPath(string testName, bool jsonCompatible = false)
        {
            string basePath = Path.GetFullPath(Path.GetTempPath());
            string cacheDir;
            int cacheNum = 0;
            do
            {
                cacheNum++;
                cacheDir = Path.Combine(basePath, testName + cacheNum);
            }
            while (Directory.Exists(cacheDir));

            Directory.CreateDirectory(cacheDir);

            if (jsonCompatible)
            {
                cacheDir = ConvertToJSONCompatibleString(cacheDir);
            }

            return cacheDir;
        }

        /// <summary>
        /// Converts a string to a format that can be embedded into Json data strings
        /// </summary>
        /// <param name="strToConvert">The string to convert to Json compatible format</param>
        /// <returns>Json compatible string</returns>
        protected string ConvertToJSONCompatibleString(string strToConvert)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return strToConvert;
            }
            // we just have to escape the \ chars
            return strToConvert.Replace("\\", "\\\\");
        }

        /// <summary>
        /// Close a session and validate that it really did close
        /// </summary>
        /// <param name="session">Session to close</param>
        /// <param name="testSessionId">SessionId of the closed session</param>
        /// <returns>void - fails tests if it fails to close as needed</returns>
        protected async Task CloseSessionAsync(ICacheSession session, string testSessionId)
        {
            XAssert.IsFalse(session.IsClosed, "Should not be closed yet!");
            XAssert.AreEqual(testSessionId, (await session.CloseAsync()).Success());
            XAssert.IsTrue(session.IsClosed, "Should be closed now!");
        }

        /// <summary>
        /// Validate that the cache shutdown as requested
        /// </summary>
        /// <param name="cache">Cache to shutdown</param>
        /// <param name="testCacheId">The cache ID</param>
        /// <returns>void - fails tests if it fails to shutdown as needed</returns>
        public async Task ShutdownCacheAsync(ICache cache, string testCacheId)
        {
            XAssert.IsFalse(cache.IsShutdown, "Should not be shutdown yet");
            XAssert.AreEqual(GetCacheFormattedId(testCacheId), (await cache.ShutdownAsync()).Success());
            XAssert.IsTrue(cache.IsShutdown, "Should be shutdown now!");
        }

        private async Task ValidateSessionAsync(HashSet<FullCacheRecord> expectedRecords, ICache cache, string sessionId, FakeBuild.CasAccessMethod accessMethod)
        {
            if (!ImplementsTrackedSessions || DummySessionName != null)
            {
                return;
            }

            ICacheReadOnlySession readOnlySession = (await cache.CreateReadOnlySessionAsync()).Success();

            // Check that the content is fine
            HashSet<FullCacheRecord> foundRecords = new HashSet<FullCacheRecord>();
            foreach (var strongFingerprintTask in cache.EnumerateSessionStrongFingerprints(DummySessionName ?? sessionId).Success().OutOfOrderTasks())
            {
                StrongFingerprint strongFingerprint = await strongFingerprintTask;
                CasEntries casEntries = (await readOnlySession.GetCacheEntryAsync(strongFingerprint)).Success();
                FullCacheRecord record = new FullCacheRecord(strongFingerprint, casEntries);
                XAssert.IsTrue(expectedRecords.Contains(record), "Found record that was not expected!");
                foundRecords.Add(record);
            }

            (await readOnlySession.CloseAsync()).Success();

            await FakeBuild.CheckContentsAsync(cache, foundRecords, accessMethod);

            XAssert.AreEqual(expectedRecords.Count, foundRecords.Count);
        }

        /// <summary>
        /// Helper method to run a set of pip definitions through the cache
        /// </summary>
        /// <param name="testName">Name of the test</param>
        /// <param name="pips">Pip definitions to run</param>
        /// <param name="accessMethod">Cas access method (stream or file)</param>
        /// <returns>async task only - no value</returns>
        protected async Task TestMultiplePipsAsync(string testName, PipDefinition[] pips, FakeBuild.CasAccessMethod accessMethod)
        {
            string testCacheId = MakeCacheId(testName + accessMethod);
            ICache cache = await CreateCacheAsync(testCacheId);

            // Now for the session (which we base on the cache ID)
            string testSessionId = "Session1-" + cache.CacheId;

            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            HashSet<FullCacheRecord> records = await pips.BuildAsync(session);

            XAssert.AreEqual(pips.Length, records.Count, "Should have had {0} cache records generated!", pips.Length);
            foreach (var record in records)
            {
                XAssert.AreEqual(FakeBuild.NewRecordCacheId, record.CacheId);
            }

            // Now we see if we can get back the items we think we should
            await CloseSessionAsync(session, testSessionId);

            // Check that the content is fine
            await ValidateSessionAsync(records, cache, testSessionId, accessMethod);

            // Cache hits test...
            testSessionId = "Session2-" + cache.CacheId;

            session = await CreateSessionAsync(cache, testSessionId);

            foreach (var record in await pips.BuildAsync(session))
            {
                XAssert.AreNotEqual(FakeBuild.NewRecordCacheId, record.CacheId);
                XAssert.IsTrue(records.Contains(record));
            }

            await CloseSessionAsync(session, testSessionId);

            // Check that the content is fine
            await ValidateSessionAsync(records, cache, testSessionId, accessMethod);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        public void Dispose()
        {
             // Do not change this code. Put cleanup code in Dispose(bool disposing) below.
             Dispose(true);
        }

        protected bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                XAssert.IsTrue(disposing, "Dispose was not called from Dispose, but from a finaializer.");
                disposedValue = true; // Set true here, as the following XAssert may result in early exit

                if (!OperatingSystemHelper.IsUnixOS)
                {
                    XAssert.IsFalse(m_testListener.HasSeenErrors, "Test traced incorrect events to ETW." + string.Join(Environment.NewLine, m_testListener.FailedEventStackTraces.ToArray().Select((stack) => stack.ToString()).ToArray()));
                }
            }
        }
    }
}
