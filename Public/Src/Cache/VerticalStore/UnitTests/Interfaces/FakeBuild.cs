// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// A fake build structure that has the OutputList and Outputs
    /// as streams generated at construction time.
    /// </summary>
    public readonly struct FakeBuild
    {
        /// <summary>
        /// The stream that represents the input list
        /// </summary>
        public readonly Stream OutputList;

        /// <summary>
        /// The streams that represent the outputs generated.
        /// </summary>
        public readonly Stream[] Outputs;

        /// <summary>
        /// The hash of the contents of the input list
        /// </summary>
        public readonly CasHash OutputListHash;

        /// <summary>
        /// The hashes of the contents of the outputs
        /// </summary>
        public readonly Hash[] OutputHashes;

        /// <summary>
        /// The cache ID of a new cache record (newly added)
        /// </summary>
        public const string NewRecordCacheId = "--New--";

        /// <summary>
        /// The default size of a fake build's outputs
        /// </summary>
        public const int DefaultFakeBuildSize = 3;

        /// <summary>
        /// A fake build with a set number of outputs and
        /// an OutputList that matches the outputs and is
        /// used as the "input list" entry for the tests.
        /// </summary>
        /// <param name="prefix">The prefix text used to make the build unique</param>
        /// <param name="outputCount">Number of "output files"</param>
        /// <param name="forceUniqueOutputs">Determines if the outputs will be unique or not. Unique outputs cannot be verfied by CheckContentsAsync</param>
        public FakeBuild(string prefix, int outputCount, int startIndex = 0, bool forceUniqueOutputs = false)
        {
            Contract.Requires(prefix != null);
            Contract.Requires(outputCount > 0);

            StringBuilder inputList = new StringBuilder();
            Guid unique = Guid.NewGuid();

            Outputs = new Stream[outputCount];
            OutputHashes = new Hash[outputCount];

            for (int i = 0; i < outputCount; i++)
            {
                string contents = I($"{prefix}:{i + startIndex}");
                inputList.Append(contents).Append("\n");

                if (forceUniqueOutputs)
                {
                    contents += ":" + unique.ToString();
                }

                Outputs[i] = contents.AsStream();
                OutputHashes[i] = Outputs[i].AsHash();
            }

            OutputList = inputList.AsStream();
            OutputListHash = new CasHash(OutputList.AsHash());
        }

        // Used to split the items in out output
        private static readonly char[] s_splitLines = { '\n' };

        /// <summary>
        /// Check the fake build via the session given
        /// </summary>
        /// <param name="session">Session to use for the check</param>
        /// <param name="index">The "index" CasHash</param>
        /// <param name="entries">The CasHash entries that should match the index</param>
        /// <param name="accessMethod">Method (File or stream) for how files are materialized from the cache</param>
        /// <returns>An Async Task</returns>
        public static async Task CheckContentsAsync(ICacheReadOnlySession session, CasHash index, CasEntries entries, CasAccessMethod accessMethod = CasAccessMethod.Stream)
        {
            string cacheId = await session.PinToCasAsync(index).SuccessAsync("Cannot pin entry {0} to cache {1}", index.ToString(), session.CacheId);
            string[] expected = (await GetStreamAsync(index, accessMethod, session)).Success().AsString().Split(s_splitLines, StringSplitOptions.RemoveEmptyEntries);

            XAssert.AreEqual(expected.Length, entries.Count, "Counts did not match from cache {0}: {1} != {2}", cacheId, expected.Length, entries.Count);

            for (int i = 0; i < expected.Length; i++)
            {
                string casCacheId = await session.PinToCasAsync(entries[i]).SuccessAsync();
                string entry = (await GetStreamAsync(entries[i], accessMethod, session)).Success().AsString();

                XAssert.AreEqual(expected[i], entry, "CasEntry {0} mismatch from cache {1}:  [{2}] != [{3}]", i, casCacheId, expected[i], entry);
            }
        }

        /// <summary>
        /// Gets a file stream from the CAS either directly or by materlializing a file in the temp path and then opening it for read.
        /// </summary>
        /// <param name="hash">Hash for CAS entry</param>
        /// <param name="method">Method used to access CAS</param>
        /// <param name="session">Cache session</param>
        /// <returns>A stream pointing to the file contents, or a failure.</returns>
        private static async Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, CasAccessMethod method, ICacheReadOnlySession session)
        {
            switch (method)
            {
                case CasAccessMethod.Stream:
                    return await session.GetStreamAsync(hash);
                case CasAccessMethod.FileSystem:

                    string filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

                    string placedFilePath = await session.ProduceFileAsync(hash, filePath, FileState.ReadOnly).SuccessAsync();
                    XAssert.AreEqual(filePath, placedFilePath);

                    FileStream fs = new FileStream(placedFilePath, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read);

                    File.Delete(placedFilePath);

                    return fs;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Check the fake build via the cache given
        /// </summary>
        /// <param name="cache">The cache to read from (uses a read-only session)</param>
        /// <param name="index">The "index" CasHash</param>
        /// <param name="entries">The CasHash entries that should match the index</param>
        /// <param name="accessMethod">Method (File or stream) for how files are materialized from the cache</param>
        /// <returns>Success if all worked</returns>
        /// <remarks>
        /// This is tied to the FakeBuild where the set of results is
        /// made as the index file which we use in the strong fingerprint
        /// and basically describes the set of entries that should be in the CasEntries
        /// </remarks>
        public static async Task CheckContentsAsync(ICache cache, CasHash index, CasEntries entries, CasAccessMethod accessMethod = CasAccessMethod.Stream)
        {
            Contract.Requires(cache != null);
            Contract.Requires(entries.IsValid);

            ICacheReadOnlySession session = await cache.CreateReadOnlySessionAsync().SuccessAsync();
            await CheckContentsAsync(session, index, entries, accessMethod);
            await session.CloseAsync().SuccessAsync();
        }

        /// <summary>
        /// Check that the fake build contents are available and correct
        /// </summary>
        /// <param name="cache">The cache to read from (uses a read-only session)</param>
        /// <param name="record">The full cache record that is to be used to check the cache</param>
        /// <returns>Success if all worked</returns>
        /// <remarks>
        /// This is tied to the FakeBuild where the set of results is
        /// made as the index file which we use in the strong fingerprint
        /// and basically describes the set of entries that should be in the CasEntries
        /// </remarks>
        public static Task CheckContentsAsync(ICache cache, FullCacheRecord record)
        {
            Contract.Requires(cache != null);
            Contract.Requires(record != null);

            return CheckContentsAsync(cache, record.StrongFingerprint.CasElement, record.CasEntries);
        }

        /// <summary>
        /// Check that the fake build contents are available and correct
        /// </summary>
        /// <param name="cache">The cache to read from (uses a read-only session)</param>
        /// <param name="records">The full cache records to check</param>
        /// <param name="accessMethod">Method (File or stream) for how files are materialized from the cache</param>
        /// <returns>Success if all worked</returns>
        /// <remarks>
        /// This is tied to the FakeBuild where the set of results is
        /// made as the index file which we use in the strong fingerprint
        /// and basically describes the set of entries that should be in the CasEntries
        /// </remarks>
        public static async Task CheckContentsAsync(ICache cache, IEnumerable<FullCacheRecord> records, CasAccessMethod accessMethod = CasAccessMethod.Stream)
        {
            Contract.Requires(cache != null);
            Contract.Requires(records != null);

            ICacheReadOnlySession session = await cache.CreateReadOnlySessionAsync().SuccessAsync();

            foreach (FullCacheRecord record in records)
            {
                await CheckContentsAsync(session, record.StrongFingerprint.CasElement, record.CasEntries, accessMethod);
            }

            await session.CloseAsync().SuccessAsync();
        }

        /// <summary>
        /// This will do a build into the session with a given name
        /// and output count.
        /// </summary>
        /// <param name="session">The cache session to work with</param>
        /// <param name="pipName">Some text that acts as a base element in the output</param>
        /// <param name="pipSize">Number of elements in the output.  Must be enough to cover the variants</param>
        /// <param name="weakIndex">Variant with different weak index - defaults to 1</param>
        /// <param name="hashIndex">Variant with different hash index - defaults to 0</param>
        /// <param name="accessMethod">Method (File or stream) for how files are materialized from the cache</param>
        /// <param name="determinism">Determinism to provide for new build records</param>
        /// <returns>The FullCacheRecord of the build</returns>
        /// <remarks>
        /// This will do a "fake build" including a cache lookup via weak fingerprints
        /// and then return either the existing FullCacheRecord or add the build as a new
        /// one.  A new FullCacheRecord will have the StrongFingerprint.CacheId set to NewRecordCacheId
        /// </remarks>
        public static Task<FullCacheRecord> DoPipAsync(
            ICacheSession session,
                                                             string pipName,
                                                             int pipSize = DefaultFakeBuildSize,
                                                             int weakIndex = 1,
                                                             int hashIndex = 0,
                                                             CacheDeterminism determinism = default(CacheDeterminism),
                                                             CasAccessMethod accessMethod = CasAccessMethod.Stream)
        {
            Contract.Requires(session != null);
            Contract.Requires(pipName != null);
            Contract.Requires(pipSize > 0);
            Contract.Requires(weakIndex >= 0 && weakIndex < pipSize);
            Contract.Requires(hashIndex >= 0 && hashIndex < pipSize);

            FakeBuild fake = new FakeBuild(pipName, pipSize);

            WeakFingerprintHash weak = new WeakFingerprintHash(FingerprintUtilities.Hash(fake.OutputHashes[weakIndex].ToString()).ToByteArray());
            Hash simpleHash = new Hash(FingerprintUtilities.Hash(fake.OutputHashes[hashIndex].ToString()));

            return DoPipAsyncImpl(session, weak, simpleHash, fake, determinism, accessMethod);
        }

        private static async Task<FullCacheRecord> DoPipAsyncImpl(ICacheSession session,
                                                                  WeakFingerprintHash weak,
                                                                  Hash simpleHash,
                                                                  FakeBuild fake,
                                                                  CacheDeterminism determinism,
                                                                  CasAccessMethod accessMethod)
        {
            foreach (var strongTask in session.EnumerateStrongFingerprints(weak))
            {
                StrongFingerprint possibleHit = await strongTask.SuccessAsync();

                if (fake.OutputListHash.Equals(possibleHit.CasElement))
                {
                    if (simpleHash.Equals(possibleHit.HashElement))
                    {
                        // A cache hit!  Our strong fingerprint matched
                        return new FullCacheRecord(possibleHit, await session.GetCacheEntryAsync(possibleHit).SuccessAsync());
                    }
                }
            }

            // A cache miss - add the content to the cache and then
            // add the build.
            CasHash inputList = await AddToCasAsync(fake.OutputList, accessMethod, session).SuccessAsync();
            CasHash[] items = new CasHash[fake.Outputs.Length];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = await AddToCasAsync(fake.Outputs[i], accessMethod, session).SuccessAsync();
            }

            CasEntries entries = new CasEntries(items, determinism);

            FullCacheRecordWithDeterminism cacheRecord = await session.AddOrGetAsync(weak, inputList, simpleHash, entries).SuccessAsync();
            XAssert.AreEqual(null, cacheRecord.Record);

            // Produce a full cache record manually - such that the CacheId is our own "NewRecordCacheId"
            return new FullCacheRecord(new StrongFingerprint(weak, inputList, simpleHash, NewRecordCacheId), entries);
        }

        /// <summary>
        /// This will do a build into the session with a given name
        /// and output count.
        /// </summary>
        /// <param name="session">The cache session to work with</param>
        /// <param name="pipName">Some text that acts as a base element in the output</param>
        /// <param name="pipSize">Number of elements in the output.  Must be enough to cover the variants</param>
        /// <param name="accessMethod">Method (File or stream) for how files are materialized from the cache</param>
        /// <param name="determinism">Determinism to provide for new build records</param>
        /// <param name="generateVerifiablePip">Indicates that the pip generated should be verfiiable by CheckContentAsync</param>
        /// <returns>The FullCacheRecord of the build</returns>
        /// <remarks>
        /// This will do a "fake build" including a cache lookup via weak fingerprints
        /// and then return either the existing FullCacheRecord or add the build as a new
        /// one.  A new FullCacheRecord will have the StrongFingerprint.CacheId set to NewRecordCacheId.
        ///
        /// The outputs of this pip will be unique for each call, but have the same strong fingerprint.
        ///
        /// The WeakFingerprint and input hash element are derived from the pipName.
        /// </remarks>
        public static Task<FullCacheRecord> DoNonDeterministicPipAsync(
            ICacheSession session,
            string pipName,
            bool generateVerifiablePip = false,
            int pipSize = DefaultFakeBuildSize,
            CacheDeterminism determinism = default(CacheDeterminism),
            CasAccessMethod accessMethod = CasAccessMethod.Stream)
        {
            Contract.Requires(session != null);
            Contract.Requires(pipName != null);
            Contract.Requires(pipSize > 0);

            FakeStrongFingerprint fakePrint = FakeStrongFingerprint.Create(pipName, generateVerifiablePip, pipSize);

            return DoPipAsyncImpl(session, fakePrint.WeakFingerprint, fakePrint.HashElement, fakePrint.FakeBuild, determinism, accessMethod);
        }

        /// <summary>
        /// Adds a file to the CAS either by stream or by writing the stream to disk and
        /// adding the file from disk.
        /// </summary>
        /// <param name="fileStream">Filestream to add</param>
        /// <param name="method">How to add the entry</param>
        /// <param name="session">Session to add to.</param>
        /// <returns>The hash of the new CAS content, or a failure.</returns>
        private static async Task<Possible<CasHash, Failure>> AddToCasAsync(Stream fileStream, CasAccessMethod method, ICacheSession session)
        {
            switch (method)
            {
                case CasAccessMethod.Stream:
                    return await session.AddToCasAsync(fileStream);

                case CasAccessMethod.FileSystem:
                    string filePath = Path.GetTempFileName();

                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.Delete))
                    {
                        await fileStream.CopyToAsync(fs);
                    }

                    var retVal = await session.AddToCasAsync(filePath, FileState.ReadOnly);

                    File.Delete(filePath);

                    return retVal;

                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Various methods to access the CAS.
        /// </summary>
        public enum CasAccessMethod
        {
            /// <summary>
            /// Use streams to access CAS content.
            /// </summary>
            Stream,

            /// <summary>
            /// Have the CAS materialize or read from files on the file system.
            /// </summary>
            FileSystem
        }
    }
}
