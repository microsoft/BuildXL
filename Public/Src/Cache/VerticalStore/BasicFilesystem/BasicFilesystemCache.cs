// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities;
using Newtonsoft.Json;
using BuildXL.Native.IO;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// A very basic filesystem backed cache
    /// </summary>
    /// <remarks>
    /// The main goal is to make a persistent cache that
    /// can be used to test out the API and cache behaviors.
    /// </remarks>
    public sealed class BasicFilesystemCache : ICache
    {
        internal const string CAS_HASH_TOKEN = "VSO0";
        internal const string WFP_TOKEN = "WFP";
        internal const string CAS_TOKEN = "CAS";
        internal const string END_TOKEN = "END";

        // Files that are pending delete in the GC are renamed with this extension
        // Renaming them back to the original name is how "pin" and other operations
        // remove the file from pending deletion.
        private const string PendingDelete = ".!";

        // Session name prefixes for in-progress and completed sessions
        internal const string InProgressSessionPrefix = "!";
        internal const string CompletedSessionPrefix = "=";

        // Change this if the format ever changes
        // Previous value F162F19F-33D9-498C-8C82-AF279EFE140B
        private static readonly Guid CasEntriesFormat = new Guid("CF4FE11A-CE2D-4DA4-B726-63F539A9F4CE");

        // Strong fingerprints are stored in sessions in binary and are this size per entry
        private static readonly int StrongFingerprintLength = WeakFingerprintHash.Length + CasHash.Length + FingerprintUtilities.FingerprintLength;

        // Avoid implicit allocations of this array when doing String.Split() for a single char.
        private static readonly char[] HashTagOrPoundSignSplitChar = { '#' };

        /// <summary>
        /// Our event source.
        /// </summary>
        public static readonly EventSource EventSource = new EventSource("BasicFilesystemEvt", EventSourceSettings.EtwSelfDescribingEventFormat);

        // m_cacheRoot is defined as the directory where it does all of its work - everything else is relative to that
        private readonly string m_cacheRoot;

        /// <summary>
        /// Shard locations for the CAS
        /// </summary>
        private readonly string[] m_casShardRoots;

        /// <summary>
        /// Shard locations for the weak fingerprints
        /// </summary>
        private readonly string[] m_weakFingerprintShardRoots;

        // Root path for the sessions
        private readonly string m_sessionRoot;

        private readonly string m_cacheId;

        private readonly bool m_strictMetadataCasCoupling;

        private readonly int m_contentionBackoffMax;

        // This specific cache's GUID
        private readonly Guid m_cacheGuid;

        private readonly bool m_readOnly;

        private bool m_shutdown = false;

        private readonly List<Action<Failure>> m_notificationListeners = new List<Action<Failure>>();

        // A random number generator for use within the cache backoff logic
        private readonly Random m_random = new Random();

        // Failures encountered during construction that cause a change in behavior.
        private readonly List<Failure> m_initialMessages = new List<Failure>();

        // This hold the open named sessions (un-named, un-tracked sessions are ignored)
        internal readonly ConcurrentDictionary<string, int> OpenSessions = new ConcurrentDictionary<string, int>();

        // For the unit tests to be able to look into the cache locations
        internal string[] CasRoots => m_casShardRoots;

        internal string[] FingerprintRoots => m_weakFingerprintShardRoots;

        internal string SessionRoot => m_sessionRoot;

        /// <summary>
        /// The amount of time that content will be in existence from creation until the first available GC collection.
        /// </summary>
        internal readonly TimeSpan TimeToLive;

        // This counts the number of occurences of the cache hitting an extreme error (after all retries are exhausted)
        // IsDisconnected is just a hint to the caller that the cache is likely not operating correctly
        // and future calls will likely fail.  It does not change any internal behavior of this cache.
        internal int DisconnectCount = 0;

        /// <summary>
        /// Create and instance of the cache
        /// </summary>
        /// <param name="cacheId">Cache ID</param>
        /// <param name="rootPath">Root path to place the cache</param>
        /// <param name="readOnly">Construct the cache as read-only</param>
        /// <param name="strictMetadataCasCoupling">True if cache is to be strict</param>
        /// <param name="isauthoritative">WHether the basic file system cache operates in authoritative mode.</param>
        /// <param name="contentionBackoffMax">Maximum contention backoff delay in milliseconds</param>
        /// <param name="defaultMinFingerprintAgeMinutes">Default min time that a new cache will allow fingerprints to exist for.</param>
        /// <remarks>
        /// May throw an exception on construction if the cache can not be created
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        internal BasicFilesystemCache(string cacheId, string rootPath, bool readOnly, bool strictMetadataCasCoupling, bool isauthoritative, int contentionBackoffMax, int defaultMinFingerprintAgeMinutes)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(rootPath != null);
            Contract.Requires(!isauthoritative || (isauthoritative && strictMetadataCasCoupling));

            m_cacheId = cacheId;
            m_cacheRoot = Path.GetFullPath(rootPath);
            IsAuthoritative = isauthoritative;
            m_strictMetadataCasCoupling = strictMetadataCasCoupling;

            // We don't want to have less than 3 ms of backoff, no matter how little/negative the user may ask for
            m_contentionBackoffMax = Math.Max(contentionBackoffMax, 3);

            m_sessionRoot = Path.Combine(m_cacheRoot, "Sessions");

            string shardFile = Path.Combine(m_cacheRoot, "Shards");
            if (File.Exists(shardFile))
            {
                // Read the file containing sharding information
                // The file format is:
                // WFP
                // <0 or more rows of sharding locations for the weak fingerprints>
                // CAS
                // <0 or more rows of sharding locations for the weak fingerprints>
                // Number of shards for each section must be a power of 2.
                using (FileStream fs = new FileStream(shardFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (StreamReader reader = new StreamReader(fs))
                {
                    while (!reader.EndOfStream)
                    {
                        string tokenLine = reader.ReadLine().Trim();

                        if (tokenLine.Equals(WFP_TOKEN, StringComparison.OrdinalIgnoreCase))
                        {
                            if (m_weakFingerprintShardRoots != null)
                            {
                                throw new NotSupportedException("Cannot specify the WFP Token twice in a file");
                            }

                            m_weakFingerprintShardRoots = ReadShards(WFP_TOKEN, reader);
                        }
                        else if (tokenLine.Equals(CAS_TOKEN, StringComparison.OrdinalIgnoreCase))
                        {
                            if (m_casShardRoots != null)
                            {
                                throw new NotSupportedException("Cannot specify the CAS Token twice in a file");
                            }

                            m_casShardRoots = ReadShards(CAS_HASH_TOKEN, reader);
                        }
                        else if (!string.IsNullOrWhiteSpace(tokenLine) && !tokenLine.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                        {
                            // Ok, bad format, so we're done.
                            throw new FileNotFoundException(string.Format(System.Globalization.CultureInfo.InvariantCulture, "SHARDS File {0} contained invalid line {1}", shardFile, tokenLine));
                        }
                    }
                }
            }

            if (m_casShardRoots == null || m_casShardRoots.Length == 0)
            {
                string casRoot = Path.Combine(m_cacheRoot, CAS_HASH_TOKEN);
                m_casShardRoots = new string[4096];
                for (int i = 0; i < 4096; i++)
                {
                    m_casShardRoots[i] = casRoot;
                }
            }

            if (m_weakFingerprintShardRoots == null || m_weakFingerprintShardRoots.Length == 0)
            {
                string casRoot = Path.Combine(m_cacheRoot, WFP_TOKEN);
                m_weakFingerprintShardRoots = new string[4096];
                for (int i = 0; i < 4096; i++)
                {
                    m_weakFingerprintShardRoots[i] = casRoot;
                }
            }

            // Not make sure we have a GUID in our cache
            string guidFile = Path.Combine(m_cacheRoot, "GUID");
            Guid cacheGuid;

            // If we are being constructed read-only, we can not create a cache
            // but just use an existing one.  It is better to actually ask for
            // read-only rather than discovering the read-only state
            if (readOnly)
            {
                cacheGuid = ReadGuid(guidFile);
            }
            else
            {
                // Since we are not being constructed read-only, lets see
                // if we can access the share in a non-read-only way
                // Obviously, the directories either need to be created
                // if they are not there but this will fail if we are
                // read-only and they are not there.  (We want that)
                FileUtilities.CreateDirectory(m_cacheRoot);
                FileUtilities.CreateDirectory(m_sessionRoot);

                foreach (string casRoot in m_casShardRoots.Distinct())
                {
                    FileUtilities.CreateDirectory(casRoot);
                }

                foreach (string wfpRoot in m_weakFingerprintShardRoots.Distinct())
                {
                    FileUtilities.CreateDirectory(wfpRoot);
                }

                try
                {
                    // First try reading the GUID file
                    cacheGuid = ReadGuid(guidFile);
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // If that fails, we likely need to create a Guid
                    cacheGuid = CacheDeterminism.NewCacheGuid();
                    try
                    {
                        // Write the Guid file
                        byte[] guidBytes = cacheGuid.ToByteArray();
                        using (FileStream file = new FileStream(guidFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            file.Write(guidBytes, 0, guidBytes.Length);
                        }
                    }
                    catch
                    {
                        // If we failed to write the Guid file we
                        // may have just missed getting the guid
                        // in the first place, so let us try to
                        // read it again.  This failure we let
                        // go all the way out.
                        cacheGuid = ReadGuid(guidFile);
                    }
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                // Now that we think we can open the cache read-write
                // check that we can actually do so by trying to open
                // a marker file shared-read-write.  Note that if we need
                // to shut down the cache into read-only mode we would
                // be able to do that by just changing the rights on the
                // marker file.
                string rwMarker = Path.Combine(m_cacheRoot, "ReadWrite-Marker");
                try
                {
                    using (var file = File.Open(rwMarker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                    {
                        file.Seek(0, SeekOrigin.Begin);
                        file.WriteByte(1);
                    }
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // TODO: Telemetry for falling back to read-only
                    m_initialMessages.Add(new CacheFallbackToReadonlyFailure(m_cacheRoot, m_cacheId));
                    readOnly = true;
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }

            m_readOnly = readOnly;
            m_cacheGuid = cacheGuid;

            // Now load or write out the config file.
            string configPath = Path.Combine(m_cacheRoot, "Cache.Config.Json");
            string fileContents = null;
            try
            {
                fileContents = File.ReadAllText(configPath);
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch
            {
                // Write out the file contents.
                try
                {
                    CacheConfiguration newConfig = new CacheConfiguration();

                    // TODO: Some other settings like strict metadata CAS coupling should be properties of the cache and not something
                    // each client gets to invent on its own. Move them here instead of taking them as parameters beyond an initial default.
                    newConfig.FingerprintMinAge = TimeSpan.FromMinutes(defaultMinFingerprintAgeMinutes);

                    fileContents = JsonConvert.SerializeObject(newConfig);

                    using (var fs = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(fs))
                    {
                        writer.Write(fileContents);
                    }
                }
                catch
                {
                    // If we failed to write the Config file we
                    // may have just missed getting the Config
                    // in the first place, so let us try to
                    // read it again.  This failure we let
                    // go all the way out.
                    fileContents = File.ReadAllText(configPath);
                }
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            CacheConfiguration config = ReadConfigFile(fileContents);

            TimeToLive = config.FingerprintMinAge;
        }

        /// <summary>
        /// Reads the actual shards from the file
        /// </summary>
        /// <param name="token">The token to append to each line read to form a complete path.</param>
        /// <param name="reader">The file stream.</param>
        /// <returns>An array of 4096 file locations</returns>
        /// <remarks>
        /// The Shard file looks like:
        /// WFP
        /// 0 to 4096 shards where count is a power of 2
        /// END
        /// CAS
        /// 0 to 4096 shards where count is a power of 2
        /// END
        /// </remarks>
        private static string[] ReadShards(string token, StreamReader reader)
        {
            string currentLine = reader.ReadLine();
            List<string> shards = new List<string>();

            while (!currentLine.Equals(END_TOKEN, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(currentLine))
                {
                    // Skip whitespace lines
                    continue;
                }

                shards.Add(Path.Combine(currentLine, token));

                currentLine = reader.ReadLine().Trim();
            }

            if (shards.Count == 0)
            {
                return new string[0];
            }

            // Shards must be a power of 2.
            if ((shards.Count & (shards.Count - 1)) != 0)
            {
                throw new NotSupportedException("Number of shards must be a power of 2");
            }

            int slotsPerShard = 4096 / shards.Count;
            int currentSlot = 0;

            string[] shardArray = new string[4096];

            foreach (string oneShard in shards)
            {
                for (int i = 0; i < slotsPerShard; i++)
                {
                    shardArray[currentSlot++] = oneShard;
                }
            }

            return shardArray;
        }

        private static Guid ReadGuid(string filename)
        {
            byte[] guidBytes = new byte[16];
            int size;
            using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                size = file.Read(guidBytes, 0, guidBytes.Length);
            }

            if (size != guidBytes.Length)
            {
                File.Delete(filename);
                throw new FileNotFoundException("GUID file was there but not correct format", filename);
            }

            return new Guid(guidBytes);
        }

        private static CacheConfiguration ReadConfigFile(string fileContents)
        {
            return JsonConvert.DeserializeObject<CacheConfiguration>(fileContents);
        }

        /// <summary>
        /// Sets the cache as disconnected and sends a notification about the state change and
        /// failure cause.
        /// </summary>
        /// <param name="disconnectCause">Exception causing the disconnect</param>
        private void DisconnectAndNotify(Exception disconnectCause)
        {
            Interlocked.Increment(ref DisconnectCount);
            var failure = new CacheDisconnectedFromStorageFailure(m_cacheId, disconnectCause);
            m_notificationListeners.ForEach((callback) => { callback(failure); });
        }

        // Helper method that enumerates into tasks since everything we have is local
        private static IEnumerable<Task<T>> EnumerateIntoTasks<T>(IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                yield return Task.FromResult(item);
            }
        }

        internal static bool IsPendingDelete(string filename)
        {
            return filename.EndsWith(PendingDelete, StringComparison.Ordinal);
        }

        // This undoes the marking of a pending delete on a given file
        // path.  Note that it is not an error for this to fail in any way.
        // The reason is that various races between either deleting or
        // someone else renaming the file could happen and we depend on
        // the atomicity of rename to be the key.
        // This function is only called in the slow path where the file
        // was already not found.  If we wish to improve performance of
        // this we would go down to pInvoke and just do the operation and
        // ignore the return result and not throw an exception that we
        // then have to ignore.
        // Handles the file path being either the pending delete name or
        // the target name
        internal static void UndoPendingDelete(string filePath)
        {
            string pendingDeletePath = filePath;
            if (IsPendingDelete(filePath))
            {
                filePath = filePath.Substring(0, filePath.Length - PendingDelete.Length);
            }
            else
            {
                pendingDeletePath = filePath + PendingDelete;
            }

            try
            {
                // We think about checking if the pending
                // delete file exists such that we don't do the
                // rename, but that is another filesystem operation
                // and if it is true we do the rename which is a
                // filesystem operation and if it is false we don't.
                // What this just means is that we do an extra I/O
                // if we are going to potentially successfully rename
                // the file but we still would have to ignore the rename
                // failures due to race potentials.
                // However, this should reduce (slightly) the number of
                // exceptions we need to actively ignore along the pin
                // path for remote caches when the aggregator looks to
                // see if we need to upload something.
                if (File.Exists(pendingDeletePath))
                {
                    File.Move(pendingDeletePath, filePath);
                }
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // Yes, we really don't care if the move failed
                // for any reason.
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        // Random backoff increase - up to doubling
        private int NextBackoffTime(int backoffTime)
        {
            if (backoffTime < 1)
            {
                backoffTime = 1;
            }
            else
            {
                // The random number generator must be single-threaded
                lock (m_random)
                {
                    backoffTime += m_random.Next(1, backoffTime + 1);
                }
            }

            return backoffTime;
        }

        /// <summary>
        /// Wrapper to GetTempFileName that handles retry
        /// </summary>
        /// <returns>
        /// String of the file created for use
        /// </returns>
        /// <remarks>
        /// Under certain conditions that are not quite clear, an
        /// error can happen with GetTempFileName that is temporal
        /// and unexpected.  This seems to be caused by other items
        /// playing where the temp file is being created, such as
        /// maybe the Search Indexer or a virus scanner.
        /// </remarks>
        internal async Task<string> CreateTempFile()
        {
            int backoffTime = 0;

            do
            {
                try
                {
                    return Path.GetTempFileName();
                }
                catch (UnauthorizedAccessException)
                {
                    // This is the error that happens when there are
                    // some strange interactions with delete & create
                    // that it seems we have seen in the system under
                    // "special" conditions.
                    // We just want to retry (with backoff) to work
                    // around the problem.
                }

                // The first time through it will not do anything (0)
                await Task.Delay(backoffTime);

                // Random backoff increase - up to doubling
                // First time through it will just go from 0 to 1.
                backoffTime = NextBackoffTime(backoffTime);
            }
            while (backoffTime < m_contentionBackoffMax);

            // One last try that will then surface the error
            // if it has not recovered
            return Path.GetTempFileName();
        }

        /// <summary>
        /// Try to open the file as a stream as asked
        /// </summary>
        /// <param name="path">Full path to the file</param>
        /// <param name="mode">FileMode</param>
        /// <param name="access">FileAccess</param>
        /// <param name="share">FileShare</param>
        /// <param name="bufferSize">buffer size</param>
        /// <param name="useAsync">Use async open (default true)</param>
        /// <param name="handlePendingDelete">Automatically handle pending GC delete (default true)</param>
        /// <returns>The opened stream as requested or exception/failure</returns>
        /// <remarks>
        /// In order to handle potential races in accessing files from other
        /// threads or processes or machines, we need to try to open the stream
        /// as requested and potentially retry if the failure to open was due
        /// to a sharing/locking issue.  We do the retry operation much like
        /// any collision detect/retry (ethernet hardware, etc) with the first
        /// retry being "right away" and then progressive backoff as we go.
        /// </remarks>
        internal async Task<FileStream> ContendedOpenStreamAsync(string path, FileMode mode, FileAccess access, FileShare share = FileShare.Delete, int bufferSize = 16384, bool useAsync = true, bool handlePendingDelete = true)
        {
            Contract.Requires(path != null);

            // Most of the time things just work the first time so we let
            // that be the fast path.
            int backoffTime = 0;

            // As long as we will not wait too long...  (and at least once)
            do
            {
                try
                {
                    try
                    {
                        return new FileStream(path, mode, access, share, bufferSize, useAsync);
                    }
                    catch (FileNotFoundException)
                    {
                        if (!handlePendingDelete)
                        {
                            throw;
                        }

                        UndoPendingDelete(path);
                        return new FileStream(path, mode, access, share, bufferSize, useAsync);
                    }
                }
                catch (FileNotFoundException)
                {
                    // This is not a retry case...
                    throw;
                }
                catch (DirectoryNotFoundException)
                {
                    // Only FileMode.Open and FileMode.Truncate are only for existing files
                    if ((mode == FileMode.Open) || (mode == FileMode.Truncate))
                    {
                        throw;
                    }

                    // Failed to get the directory, so we make the directory and retry
                    // Note that this can fail due to high-load-races so...
                    try
                    {
                        FileUtilities.CreateDirectory(Path.GetDirectoryName(path));
                    }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                    catch
                    {
                        // Nothing to throw here - we were just trying to undo a potential
                        // race with directory existing on file creation
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // Any other failure needs to get a retry in as if it was due to
                    // contention.  Not just file sharing contention but other errors
                    // due to load or timing.

                    // A strange one includes UnauthorizedAccessException which can
                    // happen when a directory along the path is in transition from
                    // existing and not existing.  This should resolve itself into a
                    // not found case or a sharing violation case soon but the only way
                    // to know is to allow the retry to happen.  It is unfortunate that
                    // this race in the filesystem is there but it is so we just retry.

                    // Others are just variations of file in use, lock, semaphore timeout,
                    // and a slew of other failures under load, where the right answer is
                    // to retry.
                }

                // The first time through it will not do anything (0)
                await Task.Delay(backoffTime);

                // Random backoff increase - up to doubling
                // First time through it will just go from 0 to 1.
                backoffTime = NextBackoffTime(backoffTime);
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            while (backoffTime < m_contentionBackoffMax);

            // One last try after we get to our max backoff time...
            // This time without any catches of sharing errors.
            try
            {
                return new FileStream(path, mode, access, share, bufferSize, useAsync);
            }
            catch (Exception e)
            {
                DisconnectAndNotify(e);
                throw;
            }
        }

        internal string ToPath(CasHash casHash)
        {
            string casName = casHash.ToString();
            int index = (casHash.BaseHash.RawHash[0] << 4) + (casHash.BaseHash.RawHash[1] >> 4);

            return Path.Combine(m_casShardRoots[index], casName.Substring(0, 3), casName);
        }

        /// <summary>
        /// Returns true of the CasHash item exists in this cache
        /// </summary>
        /// <param name="casHash">The CasHash item</param>
        /// <returns>True if it exists</returns>
        internal bool CasExists(CasHash casHash)
        {
            string casFilePath = ToPath(casHash);

            if (File.Exists(casFilePath))
            {
                return true;
            }

            UndoPendingDelete(casFilePath);

            return File.Exists(casFilePath);
        }

        /// <summary>
        /// Copy a CasHash item to a given filename
        /// </summary>
        /// <param name="casHash">The CasHash item</param>
        /// <param name="filename">Target filename</param>
        /// <remarks>
        /// The target filename will be overwritten if it exists.
        ///
        /// Note that failure here will cause an exception to be handled
        /// by the specific use case.
        /// </remarks>
        [SuppressMessage("AsyncUsage", "AsyncFixer02", Justification = "ReadAllBytes and WriteAllBytes have async versions in .NET Standard which cannot be used in full framework.")]
        internal async Task CopyFromCasAsync(CasHash casHash, string filename)
        {
            Contract.Requires(filename != null);

            // The NoItem CasHash means an empty file if you copy it
            if (CasHash.NoItem.Equals(casHash))
            {
                File.WriteAllBytes(filename, new byte[0]);
            }
            else
            {
                string casFilePath = ToPath(casHash);

                // This should always work unless we get a file not found
                // and since we know we have pinned the entry before we got
                // here it should not be that either.
                // However, under stress, SMB produces strange errors
                // so we need to potentially retry this
                int backoffTime = 0;
                while (true)
                {
                    try
                    {
                        try
                        {
                            File.Copy(casFilePath, filename, true);
                            return;
                        }
                        catch (FileNotFoundException)
                        {
                            // If the file was not found, we may need to do the
                            // rename and try again.
                            UndoPendingDelete(casFilePath);
                            File.Copy(casFilePath, filename, true);
                            return;
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // This case should just fail right away since
                        // file not found is a end-point
                        throw;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // This case should just fail right away since
                        // directory not found is a end-point
                        throw;
                    }
                    catch (Exception e)
                    {
                        // Try to make sure that the output file does not exist
                        // such that any failure to produce it is not leaving a
                        // partial file there (albeit there are things that can
                        // cause this to fail so it is best effort)
                        try
                        {
                            File.Delete(filename);
                        }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                        catch
                        {
                            // Yes, a blind catch - this file, if it fails to delete,
                            // we will likely be overwritten when we retry the operation.
                            // (And this could likely fail due to virus scanner
                            // issues with breaking file semantics)
                            // BuildXL should fail at this point anyway if we are
                            // not retrying as the result of the call will be a failure.
                        }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                        // If we have finally gone beyond the retry backoff
                        // time limit, we may need to report the error
                        if (backoffTime > m_contentionBackoffMax)
                        {
                            DisconnectAndNotify(e);
                            throw;
                        }

                        // Get the next backoff time
                        backoffTime = NextBackoffTime(backoffTime);

                        // Wait a bit before retrying
                        await Task.Delay(backoffTime);
                    }
                }
            }
        }

        /// <summary>
        /// Add a given local filename to the CAS as the given CasHash
        /// </summary>
        /// <param name="filename">Local filename - must exist</param>
        /// <param name="casHash">The CasHash item</param>
        /// <returns>True if added, false if it already existed</returns>
        /// <remarks>
        /// This adds the given local file to the CAS as the given CasHash
        /// It handles, as best as possible, not doing costly I/O (non-local)
        /// if the item is already in the CAS.
        ///
        /// Adding is handled atomically via rename/move on the CAS
        /// but the data movement to the CAS is a File.Copy using OS level
        /// API for maximal performance and no partial state visible.
        ///
        /// Note that failure here will cause an exception to be handled
        /// by the specific use case.
        /// </remarks>
        internal async Task<bool> AddToCasAsync(string filename, CasHash casHash)
        {
            Contract.Requires(filename != null);

            if (!CasExists(casHash))
            {
                string path = ToPath(casHash);

                // Make sure the CAS shared directory exists - if this fails, we fail to
                // add to the CAS
                string directory = Path.GetDirectoryName(path);
                FileUtilities.CreateDirectory(directory);

                // This name is a unique name for a given attempt at a CAS entry.
                // We depend on uniqueness here to allow multiple uploads at the
                // same time.  The GUID is first, with "{" at the start so as to
                // not match any CAS entry and can be later found for cleanup.
                string casTempFile = Path.Combine(directory, Guid.NewGuid().ToString("B") + Path.GetFileName(path));
                try
                {
                    int backoffTime = 0;

                    // This should always work - failures here are failure to
                    // store into the CAS - Note that SMB and File.Copy sometimes
                    // seem to cause some strange behavior - this required overwrite
                    // set to true to be needed (the file name is unique anyway)
                    // and potential retries on failures
                    bool completed = false;
                    while (!completed)
                    {
                        try
                        {
                            File.Copy(filename, casTempFile, true);
                            completed = true;
                        }
                        catch (Exception e)
                        {
                            // If we have finally gone beyond the retry backoff
                            // time limit, we may need to report the error
                            if (backoffTime > m_contentionBackoffMax)
                            {
                                DisconnectAndNotify(e);
                                throw;
                            }

                            // Get the next backoff time
                            backoffTime = NextBackoffTime(backoffTime);

                            // Wait a bit before retrying
                            await Task.Delay(backoffTime);
                        }
                    }

                    // We rename the file here after having gotten the file
                    // into the directory under a temporary name.  However, there
                    // is a problem on Windows with rename after having created
                    // the file (and closed it) and that is that some external
                    // agent (Defender or Search Indexer or Backup) may go and
                    // touch the file right after it is created.  This then has
                    // a potential race where the rename fails since even if they
                    // open the file in full sharing mode (even FileShare.Write
                    // and FileShare.Delete) the rename operation will fail with
                    // access failure and file in use.  The is due to the fact
                    // that the filesystem locks the filename and not just the
                    // backing data so rename gets stuck.  This loop here should
                    // not be needed but is required to get around the semantic
                    // behavior impact of Defender and other anti-virus tools.
                    backoffTime = 0;
                    while (!File.Exists(path))
                    {
                        try
                        {
                            File.Move(casTempFile, path);

                            // Yes, we moved the file - return true
                            return true;
                        }
                        catch (Exception e)
                        {
                            // If we have finally gone beyond the retry backoff
                            // time limit, we may need to report the error
                            if (backoffTime > m_contentionBackoffMax)
                            {
                                // One last check to see if the target exists
                                // as that would not be an error
                                if (File.Exists(path))
                                {
                                    return false;
                                }

                                // We are out of retries - time to throw
                                DisconnectAndNotify(e);
                                throw;
                            }

                            // Get the next backoff time
                            backoffTime = NextBackoffTime(backoffTime);

                            // Wait a bit before retrying
                            await Task.Delay(backoffTime);
                        }
                    }
                }
                finally
                {
                    // Clean up our temp file if it still is there
                    // We harden this against the semantics breaking virus scanners
                    // that let the close operation succeed and yet then cause
                    // failures to delete the file as they still hold references to it.
                    // If anything goes wrong with this, the GC will clean up
                    // for us anyway (plus these cases should generally be relatively
                    // rare as they happen due to races in adding the same content to the CAS.
                    try
                    {
                        File.Delete(casTempFile);
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch
                    {
                        // Yes, a blind catch - this file, if it fails to delete,
                        // will be picked up the the GC as an invalidly formatted
                        // file name and will be deleted when it finally becomes
                        // available for deletion.
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }
            }

            return false;
        }

        internal static async Task WriteSessionFingerprints(Stream file, IEnumerable<StrongFingerprint> fingerprints)
        {
            // The buffer is the 3 hashes added up...
            byte[] buffer = new byte[StrongFingerprintLength];
            foreach (var strong in fingerprints)
            {
                strong.WeakFingerprint.CopyTo(buffer, 0);
                strong.CasElement.CopyTo(buffer, WeakFingerprintHash.Length);
                strong.HashElement.CopyTo(buffer, WeakFingerprintHash.Length + CasHash.Length);

                // Write a strong fingerprint record...
                await file.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private async Task<StrongFingerprint> ReadStrongFingerprintAsync(FileStream file)
        {
            // We need to read each strong fingerprint record as a single async read
            // but, unfortunately, we need the data as 3 different arrays for the
            // constructors of the different hashes - how annoying.
            byte[] buffer = new byte[StrongFingerprintLength];
            int size = await file.ReadAsync(buffer, 0, buffer.Length);
            Contract.Assume(size == buffer.Length);

            byte[] weak = new byte[WeakFingerprintHash.Length];
            Array.Copy(buffer, weak, weak.Length);

            byte[] cas = new byte[CasHash.Length];
            Array.Copy(buffer, weak.Length, cas, 0, cas.Length);

            byte[] hash = new byte[FingerprintUtilities.FingerprintLength];
            Array.Copy(buffer, weak.Length + cas.Length, hash, 0, hash.Length);

            return new StrongFingerprint(
                new WeakFingerprintHash(weak),
                new CasHash(cas),
                new Hash(hash, FingerprintUtilities.FingerprintLength),
                CacheId);
        }

        internal async Task<IEnumerable<Task<StrongFingerprint>>> EnumerateSessionDataAsync(string sessionId)
        {
            List<StrongFingerprint> strongFingerprintList = new List<StrongFingerprint>();
            string sessionFile = Path.Combine(m_sessionRoot, CompletedSessionPrefix + sessionId);

            using (FileStream file = await ContendedOpenStreamAsync(sessionFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                long numFingerprints = file.Length / StrongFingerprintLength;

                if (numFingerprints * StrongFingerprintLength != file.Length)
                {
                    throw new FormatException("Session file is of incorrect length");
                }

                List<Task<StrongFingerprint>> sessionDataList = new List<Task<StrongFingerprint>>();
                while (numFingerprints > 0)
                {
                    // This is fine - we ensure that this async call is completed before leaving file's using block
#pragma warning disable AsyncFixer04 // A disposable object used in a fire & forget async call
                    var t = ReadStrongFingerprintAsync(file);
#pragma warning restore AsyncFixer04 // A disposable object used in a fire & forget async call

                    sessionDataList.Add(t);

                    // Await tasks one at a time so we don't concurrently rip the file apart
                    await t;

                    numFingerprints--;
                }

                return sessionDataList;
            }
        }

        #region CasEntries serialization

        internal string GetWeakFingerprintDirectory(WeakFingerprintHash weak)
        {
            string hash = weak.ToString();

            // We shard the weak fingerprints into 3-nibble directories
            // and then within there we may shard again - we really don't
            // need anything more than that.  Note that this is slightly
            // different than the CAS does as we don't repeat the nibbles
            // we use in the shard - a simple implementation detail and
            // not at all critical other than it keeps the paths a bit
            // shorter as we already are running into path length issues
            // given that a full strong fingerprint has 3 * 40 characters
            // plus some directory separators plus any cache path overhead.

            // Yes, this can be much faster if we did this while generating
            // the string from the hash.  To be adjusted later
            int index = (weak.FingerprintHash.RawHash[0] << 4) + (weak.FingerprintHash.RawHash[1] >> 4);

            string path = Path.Combine(m_weakFingerprintShardRoots[index], hash.Substring(0, 3), hash.Substring(3));
            return path;
        }

        private string GetStrongFingerprintFilename(WeakFingerprintHash weak, CasHash casElement, Hash hashElement)
        {
            return Path.Combine(GetWeakFingerprintDirectory(weak), casElement.ToString() + "#" + hashElement.ToString());
        }

        private static CasHash GetCasHashFromStrongFingerprintFilename(string filename)
        {
            string name = Path.GetFileName(filename);

            int split = name.IndexOf("#", StringComparison.Ordinal);
            Contract.Assert(split == CasHash.Length * 2, "CasHash string should be 2x the length");

            ContentHash hash;
            var parsed = ContentHashingUtilities.TryParse(name.Substring(0, split), out hash);
            Contract.Assert(parsed);
            return new CasHash(hash);
        }

        internal string GetStrongFingerprintFilename(StrongFingerprint strong)
        {
            Contract.Requires(strong != null);

            return GetStrongFingerprintFilename(strong.WeakFingerprint, strong.CasElement, strong.HashElement);
        }

        // Check if a strong fingerprint exists.
        internal bool StrongFingerprintExists(StrongFingerprint strong)
        {
            Contract.Requires(strong != null);

            string filePath = GetStrongFingerprintFilename(strong);
            if (File.Exists(filePath))
            {
                return true;
            }

            UndoPendingDelete(filePath);

            return File.Exists(filePath);
        }

        private StrongFingerprint StrongFingerprintFromFilename(WeakFingerprintHash weak, string filename)
        {
            // Any file that is not of the right format it ignored here
            string[] parts = Path.GetFileName(filename).Split(HashTagOrPoundSignSplitChar);
            if (parts.Length == 2)
            {
                CasHash casElement;

                // Anything that does not parse is ignored...
                if (CasHash.TryParse(parts[0], out casElement))
                {
                    // We still return pending delete fingerprints but we don't undo
                    // the pending delete here - we just undo the pending delete if
                    // the fingerprint is actually read (which means the fingerprint
                    // could go away at any point, which is true anyway, but it gives
                    // the best chance to not have a fingerprint still pending delete
                    // when we try to add it back.
                    if (IsPendingDelete(parts[1]))
                    {
                        parts[1] = parts[1].Substring(0, parts[1].Length - PendingDelete.Length);
                    }

                    Hash hashElement;
                    if (Hash.TryParse(parts[1], out hashElement))
                    {
                        return new StrongFingerprint(weak, casElement, hashElement, CacheId);
                    }
                }
            }

            // Not a valid fingerprint
            return null;
        }

        private StrongFingerprint StrongFingerprintFromFilename(string filename)
        {
            int lastPos = filename.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastPos > 0)
            {
                int middlePos = filename.LastIndexOf(Path.DirectorySeparatorChar, lastPos - 1);
                if (middlePos > 0)
                {
                    int firstPos = filename.LastIndexOf(Path.DirectorySeparatorChar, middlePos - 1);
                    if ((firstPos > 0) && ((middlePos - firstPos) == 4))
                    {
                        string weakString = filename.Substring(firstPos + 1, 3) +
                                            filename.Substring(middlePos + 1, lastPos - middlePos - 1);

                        WeakFingerprintHash weak;
                        if (WeakFingerprintHash.TryParse(weakString, out weak))
                        {
                            return StrongFingerprintFromFilename(weak, filename);
                        }
                    }
                }
            }

            // Not a valid fingerprint
            return null;
        }

        /// <summary>
        /// Enumeration of the strong fingerprints for a given weak fingerprint
        /// </summary>
        /// <param name="weak">Weak fingerprint</param>
        /// <returns>Enumeration of StrongFingerprints</returns>
        internal IEnumerable<StrongFingerprint> EnumerateStrongFingerprints(WeakFingerprintHash weak)
        {
            string path = GetWeakFingerprintDirectory(weak);
            if (Directory.Exists(path))
            {
                // I wish there was a just local path enumeration - this
                // builds full paths and I need to just get the last element (the file name)
                foreach (string filePath in Directory.EnumerateFiles(path, "*#*"))
                {
                    StrongFingerprint result = StrongFingerprintFromFilename(weak, filePath);
                    if (result != null)
                    {
                        yield return result;
                    }
                }
            }
        }

        private static async Task<bool> ReadArrayAsync(FileStream file, byte[] array)
        {
            int size = await file.ReadAsync(array, 0, array.Length);
            return size == array.Length;
        }

        private static Task WriteArrayAsync(FileStream file, byte[] array)
        {
            return file.WriteAsync(array, 0, array.Length);
        }

        // The strong fingerprints hold the CasEntries data.  This is usually small as it
        // is just an array of CasHashes for all of the associated files for this cache entry.
        // File Format:
        //    Guid : 16 bytes for the GUID of this cache format
        //    Guid : 16 bytes of Guid that describes who thinks this is deterministic
        //           This is all 0 for no one, The Tool Determinism Guid for tool, otherwise it is the cache GUID
        //    Count:  4 bytes of count
        //    CasHash * Count : 20 bytes * count entries for the CasHash entries
        //    CacheGuid : 16 bytes that are never read but written for diagnostic purposes
        internal async Task WriteCacheEntryAsync(FileStream file, CasEntries entries)
        {
            await WriteArrayAsync(file, CasEntriesFormat.ToByteArray());

            await WriteArrayAsync(file, entries.Determinism.Guid.ToByteArray());

            // Now write the number of entries we will have
            await WriteArrayAsync(file, BitConverter.GetBytes(entries.Count));

            // TODO: how should the fastest write work - this keeps
            // the total memory load down but makes many more WriteArray calls
            byte[] digest = new byte[CasHash.Length];
            foreach (CasHash entry in entries)
            {
                entry.CopyTo(digest, 0);
                await WriteArrayAsync(file, digest);
            }

            // Just for tracking information (not ever read), we add
            // the Cache guid to the end of the metadata file
            await WriteArrayAsync(file, m_cacheGuid.ToByteArray());
        }

        internal async Task<Possible<CasEntries, Failure>> ReadCacheEntryAsync(FileStream file)
        {
            byte[] guidArray = new byte[16];

            if (!await ReadArrayAsync(file, guidArray))
            {
                return new BasicFilesystemMetadataFailure(CacheId, file, "Failed to read file format Guid");
            }

            Guid fileVersion = new Guid(guidArray);
            if (!fileVersion.Equals(CasEntriesFormat))
            {
                return new BasicFilesystemMetadataFailure(CacheId, file, "Invalid file format: " + fileVersion.ToString("D"));
            }

            if (!await ReadArrayAsync(file, guidArray))
            {
                return new BasicFilesystemMetadataFailure(CacheId, file, "Failed to read determinism Guid");
            }

            CacheDeterminism determinism = CacheDeterminism.ViaCache(new Guid(guidArray), DateTime.UtcNow.Add(TimeToLive));

            byte[] entriesCountArray = new byte[4];
            if (!await ReadArrayAsync(file, entriesCountArray))
            {
                return new BasicFilesystemMetadataFailure(CacheId, file, "Failed to read entries count");
            }

            int entriesCount = BitConverter.ToInt32(entriesCountArray, 0);

            byte[] digest = new byte[CasHash.Length];

            CasHash[] entries = new CasHash[entriesCount];
            for (int i = 0; i < entries.Length; i++)
            {
                if (!await ReadArrayAsync(file, digest))
                {
                    return new BasicFilesystemMetadataFailure(CacheId, file, "Read file error during entries reading");
                }

                entries[i] = new CasHash(digest);
            }

            // Include the determinism
            return new CasEntries(entries, determinism);
        }

        /// <summary>
        /// Reads the CAS entries associated with a strong fingerprint.
        /// </summary>
        /// <param name="strong">The strong fingerprint</param>
        /// <returns>The associated CAS entries</returns>
        internal async Task<Possible<CasEntries, Failure>> ReadCacheEntryAsync(StrongFingerprint strong)
        {
            Contract.Requires(strong != null);

            try
            {
                using (FileStream file = await ContendedOpenStreamAsync(GetStrongFingerprintFilename(strong), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    try
                    {
                        return await ReadCacheEntryAsync(file);
                    }
                    catch (Exception e)
                    {
                        // We should really look at such errors here and try to deal with
                        // them somehow.  The net effect should potentially cause
                        // different failures (some are NoMatchingFingerprintFailure,
                        // such as when the file format does not match)
                        throw new NotImplementedException("Error handling missing", e);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                return new NoMatchingFingerprintFailure(strong);
            }
            catch (DirectoryNotFoundException)
            {
                return new NoMatchingFingerprintFailure(strong);
            }
            catch (Exception e)
            {
                return new StrongFingerprintAccessFailure(m_cacheId, strong, e);
            }
        }

        #endregion CasEntries serialization

        // BasicFilesystem GC
        // This API is not part of the standard cache API as it is
        // specific to the BasicFilesystem implementation.  This
        // API should also not be executed by arbitrary clients as
        // the I/O overheads are non-trivial.
        #region BasicFilesystem GC

        // BasicFilesystem Garbage Collection Design
        //
        // The core principle for this GC is that the sessions are the
        // "roots" that keep items in storage.That is, logically, we
        // want to be able to trace from a session to fingerprints to
        // CAS entries.
        //
        // The GC runs in parallel with multiple cache clients so it
        // runs relatively conservative with rather large asymmetric
        // fences.
        //
        // In order to deal with very large numbers of items, they can
        // be filtered by up to the shard prefix.  This reduces the
        // number of live hashes that need to be known at one time and
        // allows for segmented cleanup at the cost of multiple passes
        // of loading the "roots" (be it from Sessions or from Fingerprints)
        //
        // In order to not delete newly added content, CAS and Fingerprints
        // must be over a minimum age (measured in days) before they are
        // even eligible for collection.
        //
        // Much of the pipeline is done in parallel and async to help
        // improve performance and overlap I/O.This is also pipelined
        // as much as reasonable.
        //
        // The GC really should be run on the storage host machine due
        // to performance considerations but it can run from anywhere
        // with full write access to the cache.

        /// <summary>
        /// The default minimum age (in seconds) of an unreferenced fingerprint
        /// before it is deleted.
        /// </summary>
        /// <remarks>
        /// This is set to a value longer than a reasonable full build would take.
        /// That is, the time from the first fingerprint being written in the build
        /// until the completed session record is written, the fingerprints are
        /// logically unreferenced and thus are not held in the cache by a session.
        /// The age needs to be long enough such that the fingerprint can become
        /// referenced even in the really worst case and then some situation.
        /// This trades some delay of actually evicting data from the build cache
        /// for the lack of high cost coordination with all of the build clients.
        /// This allows the GC to runs 100% in parallel with and without handshake
        /// with any/all of the builds that may be concurrently operating.
        ///
        /// In all cases, this value must be at least as long as the time it
        /// takes to run a single GC pass too since anything smaller than that
        /// will have inconsistent views of the state of the roots for its own
        /// processing.
        ///
        /// The default value is very large relative to that above constraint.
        /// It is best to be significantly beyond the normal expected time.
        /// Lowering this time may be reasonable for smaller/simpler build
        /// environments but they usually don't store nearly as much so it does
        /// not matter nearly as much.
        /// </remarks>
        public const int DefaultFingerprintMinimumAge = 2 * 24 * 60 * 60;

        /// <summary>
        /// The default minimum age (in seconds) of an unreferenced CAS entry
        /// before it is deleted.
        /// </summary>
        /// <remarks>
        /// This needs to be set to a value longer than a reasonable time from
        /// when a CAS item is pinned or generated and the fingerprint is written
        /// (or read).
        ///
        /// In the read case of a fingerprint, the time is actually not interesting
        /// since you tend to read the fingerprint before accessing/pinning the CAS
        /// item.  However, in the write case, it could take a while for a build step
        /// to complete writing all of the CAS items (if there are many and large
        /// items) and then the fingerprint is written to reference those items.
        /// This trades some delay of actually evicting data from the build cache
        /// for the lack of high cost coordination with all of the build clients.
        /// This allows the GC to runs 100% in parallel with and without handshake
        /// with any/all of the builds that may be concurrently operating.
        ///
        /// In all cases, this value must be at least as long as the time it
        /// takes to run a single GC pass too since anything smaller than that
        /// will have inconsistent views of the state of the roots for its own
        /// processing.
        ///
        /// The default value is very large relative to that above constraint.
        /// It is best to be significantly beyond the normal expected time.
        /// Lowering this time may be reasonable for smaller/simpler build
        /// environments but they usually don't store nearly as much so it does
        /// not matter nearly as much.
        /// </remarks>
        public const int DefaultCasMinimumAge = 4 * 60 * 60;

        /// <summary>
        /// Create a hash prefix filter based on prefix string
        /// </summary>
        /// <param name="prefixFilter">Prefix hex string - 0 to 3 hex characters</param>
        /// <returns>Prefix filter that returns true if the given hash matches the prefix</returns>
        private static Func<Hash, bool> MakeFilter(string prefixFilter)
        {
            Contract.Requires((prefixFilter != null) && Regex.IsMatch(prefixFilter, @"^[0-9A-Fa-f]{0,3}$"));

            const byte TopNibble = 0xF0;

            Func<Hash, bool> filter = null;
            switch (prefixFilter.Length)
            {
                case 0:
                    {
                        filter = (hash) => true;
                    }

                    break;

                case 1:
                    {
                        byte prefixNibble = byte.Parse(prefixFilter + "0", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

                        filter = (hash) => (hash.RawHash[0] & TopNibble) == prefixNibble;
                    }

                    break;

                case 2:
                    {
                        byte prefixByte = byte.Parse(prefixFilter, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

                        filter = (hash) => hash.RawHash[0] == prefixByte;
                    }

                    break;

                case 3:
                    {
                        byte prefixByte = byte.Parse(prefixFilter.Substring(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                        byte prefixNibble = byte.Parse(prefixFilter.Substring(2) + "0", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

                        filter = (hash) => (hash.RawHash[0] == prefixByte) &&
                                           ((hash.RawHash[1] & TopNibble) == prefixNibble);
                    }

                    break;

                default:
                    Contract.Assert(false, "Prefix filter length unsupported!");
                    break;
            }

            return filter;
        }

        /// <summary>
        /// Helper base class for the counters
        /// </summary>
        /// <typeparam name="T">The type of the counter subclass</typeparam>
        /// <remarks>
        /// This uses the generic type to enforce that some of the core methods
        /// only apply to the matching subclass type.
        /// </remarks>
        private abstract class Counters<T> : BaseCounters
        {
            protected abstract string Prefix { get; }

            /// <summary>
            /// Enumerate the stats counters within this instance
            /// </summary>
            /// <returns>All of the public "double" fields only</returns>
            private static IEnumerable<FieldInfo> PublicDoubles()
            {
                foreach (FieldInfo field in typeof(T).GetFields())
                {
                    if (field.FieldType == typeof(double))
                    {
                        yield return field;
                    }
                }
            }

            /// <summary>
            /// Export the counters to the given dictionary
            /// </summary>
            /// <param name="output">Dictionary into which entries are made for each counter</param>
            public void Export(Dictionary<string, double> output)
            {
                Export(output, Prefix);
            }

            /// <summary>
            /// Add another instance of this counters class to the counters in this instance
            /// </summary>
            /// <param name="other">Another instance of the counters</param>
            public void Combine(T other)
            {
                foreach (FieldInfo field in PublicDoubles())
                {
                    double t = (double)field.GetValue(this);
                    t += (double)field.GetValue(other);
                    field.SetValue(this, t);
                }
            }

            /// <summary>
            /// Collect counters from an enumeration of parallel executing tasks that
            /// will return their counts.
            /// </summary>
            /// <param name="tasks">The set of tasks that will return counts</param>
            /// <remarks>
            /// This uses OutOfOrder task completion such that we can keep as many
            /// tasks running from the enumeration as reasonable.
            /// </remarks>
            public void CollectTasks(IEnumerable<Task<T>> tasks)
            {
                foreach (var counts in tasks.OutOfOrderTasks())
                {
                    Combine(counts.Result);
                }
            }
        }

        #region GC stats classes

        /// <summary>
        /// Stats from Fingerprint to CAS reading of roots phase
        /// </summary>
        private sealed class CasReadRootsCounters : Counters<CasReadRootsCounters>
        {
            /// <summary>
            /// Prefix for the export of the statistics
            /// </summary>
            protected override string Prefix => "CAS_ReadRoots";

            /// <summary>
            /// Number of fingerprints read
            /// </summary>
            public double Fingerprints = 0;

            /// <summary>
            /// Number of invalid fingerprints encountered
            /// </summary>
            public double InvalidFingerprints = 0;

            /// <summary>
            /// Raw count of CAS references in the fingerprints before prefix filtering
            /// </summary>
            public double RawCount = 0;

            /// <summary>
            /// Number of CAS references read from fingerprints after prefix filtering
            /// </summary>
            public double Count = 0;

            /// <summary>
            /// Number of unique CAS references found in the fingerprints
            /// </summary>
            public double UniqueCount = 0;

            /// <summary>
            /// Total wall-clock time for this phase (in seconds)
            /// </summary>
            public double Time = 0;
        }

        /// <summary>
        /// Stats from Fingerprint to CAS collection phase
        /// </summary>
        private sealed class CasCollectionCounters : Counters<CasCollectionCounters>
        {
            /// <summary>
            /// Prefix for the export of the statistics
            /// </summary>
            protected override string Prefix => CAS_TOKEN;

            /// <summary>
            /// Number of items found in the CAS
            /// </summary>
            public double Items = 0;

            /// <summary>
            /// Total size of the items found in the CAS
            /// </summary>
            public double ItemSize = 0;

            /// <summary>
            /// Number of items that are unidentified
            /// </summary>
            public double Unidentified = 0;

            /// <summary>
            /// Total size of unidentified items
            /// </summary>
            public double UnidentifiedSize = 0;

            /// <summary>
            /// Number of CAS items that are referenced
            /// </summary>
            public double Live = 0;

            /// <summary>
            /// Total size of the referenced items
            /// </summary>
            public double LiveSize = 0;

            /// <summary>
            /// Number of to be collected CAS items that were skipped
            /// due to some issue during collection
            /// </summary>
            public double Skipped = 0;

            /// <summary>
            /// Total size of skipped items
            /// </summary>
            public double SkippedSize = 0;

            /// <summary>
            /// Number of CAS items that were resurrected from pending delete
            /// </summary>
            public double Resurrected = 0;

            /// <summary>
            /// Total size of the resurrected items
            /// </summary>
            public double ResurrectedSize = 0;

            /// <summary>
            /// Number of CAS items that were too young to be processed
            /// </summary>
            public double TooYoung = 0;

            /// <summary>
            /// Total size of the young CAS items
            /// </summary>
            public double TooYoungSize = 0;

            /// <summary>
            /// Number of CAS items that have been newly marked as PendingDelete
            /// </summary>
            public double Pending = 0;

            /// <summary>
            /// Total size of the newly marked PendingDelete items
            /// </summary>
            public double PendingSize = 0;

            /// <summary>
            /// Number of CAS items that have been deleted
            /// </summary>
            public double Collected = 0;

            /// <summary>
            /// Total size of the deleted CAS items
            /// </summary>
            public double CollectedSize = 0;

            /// <summary>
            /// Total amount of CPU time spent in this collection phase
            /// </summary>
            public double Seconds = 0;

            /// <summary>
            /// Number of shards that were collected (each runs in its own thread)
            /// </summary>
            public double Shards = 0;

            /// <summary>
            /// Number of partitions that were collected (each runs in its own thread)
            /// </summary>
            public double Partitions = 0;

            /// <summary>
            /// Total wall-clock time for this phase (in seconds)
            /// </summary>
            public double Time = 0;
        }

        /// <summary>
        /// Stats from Session to Fingerprint reading of roots phase
        /// </summary>
        private sealed class FingerprintReadRootsCounters : Counters<FingerprintReadRootsCounters>
        {
            /// <summary>
            /// Prefix for the export of the statistics
            /// </summary>
            protected override string Prefix => "Fingerprint_ReadRoots";

            /// <summary>
            /// Number of sessions read
            /// </summary>
            public double Sessions = 0;

            /// <summary>
            /// Raw count of Fingerprint references in the sessions before prefix filtering
            /// </summary>
            public double RawCount = 0;

            /// <summary>
            /// Number of Fingerprint references read from sessions after prefix filtering
            /// </summary>
            public double Count = 0;

            /// <summary>
            /// Number of unique Fingerprint references found in the sessions
            /// </summary>
            public double UniqueCount = 0;

            /// <summary>
            /// Total wall-clock time for this phase (in seconds)
            /// </summary>
            public double Time = 0;
        }

        /// <summary>
        /// Stats from Session to Fingerprint collection phase
        /// </summary>
        private sealed class ProcessFingerprintsCounters : Counters<ProcessFingerprintsCounters>
        {
            /// <summary>
            /// Prefix for the export of the statistics
            /// </summary>
            protected override string Prefix => "Fingerprint";

            /// <summary>
            /// Number of weak fingerprints encountered
            /// </summary>
            public double WeakFingerprints = 0;

            /// <summary>
            /// Total number of strong fingerprints encountered
            /// </summary>
            public double StrongFingerprints = 0;

            /// <summary>
            /// Number of items that are unidentified (were not fingerprints)
            /// </summary>
            public double Unidentified = 0;

            /// <summary>
            /// Number of Fingerprints that are referenced
            /// </summary>
            public double Live = 0;

            /// <summary>
            /// Number of to be collected Fingerprints that were skipped
            /// due to some issue during collection
            /// </summary>
            public double Skipped = 0;

            /// <summary>
            /// Number of Fingerprints that were resurrected from pending delete
            /// </summary>
            public double Resurrected = 0;

            /// <summary>
            /// Number of Fingerprints that were too young to be processed
            /// </summary>
            public double TooYoung = 0;

            /// <summary>
            /// Number of Fingerprints that have been newly marked as PendingDelete
            /// </summary>
            public double Pending = 0;

            /// <summary>
            /// Number of Fingerprints that have been deleted
            /// </summary>
            public double Collected = 0;

            /// <summary>
            /// Number of Weak Fingerprints that have been deleted
            /// </summary>
            public double CollectedWeak = 0;

            /// <summary>
            /// Number of Weak Fingerprints that might have been deleted but were skipped
            /// </summary>
            public double SkippedWeak = 0;

            /// <summary>
            /// This is the count of concurrently collected weak fingerprints
            /// </summary>
            public double ConcurrentlyCollectedWeak = 0;

            /// <summary>
            /// Total amount of CPU time spent in this collection phase
            /// </summary>
            public double Seconds = 0;

            /// <summary>
            /// Number of shards that were collected (each runs in its own thread)
            /// </summary>
            public double Shards = 0;

            /// <summary>
            /// Number of partitions that were collected (each runs in its own thread)
            /// </summary>
            public double Partitions = 0;

            /// <summary>
            /// Total wall-clock time for this phase (in seconds)
            /// </summary>
            public double Time = 0;
        }

        #endregion GC stats classes

        #region Fingerprint to CAS GC

        /// <summary>
        /// Special class that does a shared Hash Set of CasHash for both
        /// performance and scale.
        /// </summary>
        private sealed class LargeCasHashSet
        {
            // We will use the 3rd byte of the CasHash as the
            // sharding byte - such that it is outside of the
            // prefix shard byte and thus will continue to provide
            // value of sharding even in prefix-sharded GCs
            private const int ShardByte = 2;

            // The set of sharded HashSets (256 of them)
            private readonly HashSet<CasHash>[] m_casHashSets = new HashSet<CasHash>[256];

            public LargeCasHashSet()
            {
                for (int i = 0; i < m_casHashSets.Length; i++)
                {
                    m_casHashSets[i] = new HashSet<CasHash>();
                }
            }

            /// <summary>
            /// Add this CasHash to the HashSet
            /// </summary>
            /// <param name="hash">The CasHash to add to the set</param>
            /// <remarks>
            /// Adding to this HashSet is safe to do in parallel with other
            /// add operations but not safe against any read operations.
            /// This is specifically done such that our reading performance
            /// is not impacted by the locking.  Given the sharding, the
            /// adding to the HashSets is basically split into 256 locks
            /// and thus will be relatively un-contended due to the random
            /// distribution of operations.
            /// </remarks>
            public void Add(CasHash hash)
            {
                // Use the 3rd bytes to shard such that this benefits
                // even with the prefix sharding of the GC
                byte shard = hash.BaseHash.RawHash[ShardByte];

                // Note that it is only during add we need to be locked
                // as the add is done in parallel.  We never do any other
                // operations in parallel with Add - just add.
                lock (m_casHashSets[shard])
                {
                    m_casHashSets[shard].Add(hash);
                }
            }

            /// <summary>
            /// Returns TRUE if the CasHash exists in the collection
            /// </summary>
            /// <param name="hash">CasHash to look for</param>
            /// <returns>True if the CasHash exists</returns>
            /// <remarks>
            /// Must not be run while there are potential Add() operations
            /// but it is fully parallel safe when not doing Add() operations
            /// </remarks>
            public bool Contains(CasHash hash)
            {
                // Use the 3rd bytes to shard such that this benefits
                // even with the prefix sharding of the GC
                byte shard = hash.BaseHash.RawHash[ShardByte];

                // No need for lock on Contains as we don't add
                // while checking for contents
                return m_casHashSets[shard].Contains(hash);
            }

            /// <summary>
            /// The sum of all of the separate concurrent bags of stuff
            /// Not safe while doing Add() operations but fully safe
            /// after that.  This is a non-trivial operation since it
            /// much make a sum of all shards of the collection and thus
            /// this is a function.
            /// </summary>
            /// <returns>Count of unique CasHash entries</returns>
            public long Count()
            {
                long count = 0;

                foreach (var hashSet in m_casHashSets)
                {
                    count += hashSet.Count;
                }

                return count;
            }
        }

        /// <summary>
        /// Returns the minimum set of shard locations for a given prefix.
        /// </summary>
        /// <param name="prefix">Prefix to return shards for</param>
        /// <param name="shardRoots">shards to search</param>
        /// <returns>A minimized set of roots.</returns>
        private static string[] GetShardRootsForPrefix(string prefix, string[] shardRoots)
        {
            if (prefix.Length > 3)
            {
                prefix = prefix.Substring(0, 3);
            }

            if (prefix.Length == 3)
            {
                int index = int.Parse(prefix, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new string[] { shardRoots[index] };
            }

            // The prefix covers more than one shard. Enumerate the shards covered.
            string prefixMin = prefix;
            string prefixMax = prefix;
            while (prefixMin.Length < 3)
            {
                prefixMin += "0";
                prefixMax += "F";
            }

            int min = int.Parse(prefixMin, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int max = int.Parse(prefixMax, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            HashSet<string> ret = new HashSet<string>();

            for (int i = min; i <= max; i++)
            {
                ret.Add(shardRoots[i]);
            }

            return ret.ToArray();
        }

        private string[] GetCasShardRootsForPrefix(string prefix)
        {
            return GetShardRootsForPrefix(prefix, m_casShardRoots);
        }

        private string[] GetWfpShardRootsForPrefix(string prefix)
        {
            return GetShardRootsForPrefix(prefix, m_weakFingerprintShardRoots);
        }

        private string[] GetWfpShardRoots()
        {
            return GetWfpShardRootsForPrefix(string.Empty);
        }

        private static Task<CasCollectionCounters> CollectCas(LargeCasHashSet roots, DateTime minAge, string shard)
        {
            return Task.Run<CasCollectionCounters>(() =>
            {
                // The stats we will be returning...
                CasCollectionCounters counts = new CasCollectionCounters();

                ElapsedTimer timer = ElapsedTimer.StartNew();

                foreach (FileInfo casFile in new DirectoryInfo(shard).EnumerateFiles())
                {
                    long casSize = 0;

                    try
                    {
                        casSize = casFile.Length;
                        counts.Items++;
                        counts.ItemSize += casSize;

                        string hashName = casFile.Name;
                        bool pending = IsPendingDelete(hashName);
                        if (pending)
                        {
                            hashName = hashName.Substring(0, hashName.Length - PendingDelete.Length);
                        }

                        CasHash cas;
                        if (!CasHash.TryParse(hashName, out cas))
                        {
                            // invalid file name in the cas
                            // if it is too young, we leave it as it may be a temporary import
                            if (casFile.LastWriteTimeUtc > minAge)
                            {
                                counts.TooYoung++;
                                counts.TooYoungSize += casSize;
                            }
                            else
                            {
                                // Old enough to get deleted
                                casFile.Delete();
                                counts.Unidentified++;
                                counts.UnidentifiedSize += casSize;
                            }
                        }
                        else
                        {
                            if (roots.Contains(cas))
                            {
                                counts.Live++;
                                counts.LiveSize += casSize;

                                // If it happens to be in pending delete we will undo that
                                if (pending)
                                {
                                    UndoPendingDelete(casFile.FullName);
                                    counts.Resurrected++;
                                    counts.ResurrectedSize += casSize;
                                }
                            }
                            else
                            {
                                if (casFile.LastWriteTimeUtc > minAge)
                                {
                                    counts.TooYoung++;
                                    counts.TooYoungSize += casSize;
                                }
                                else
                                {
                                    if (pending)
                                    {
                                        casFile.Delete();
                                        counts.Collected++;
                                        counts.CollectedSize += casSize;
                                    }
                                    else
                                    {
                                        // Set the date to now and rename it
                                        // to pending delete.  This give the file
                                        // at least fingerprint Minimum Age in the
                                        // pending delete state.
                                        casFile.LastWriteTimeUtc = DateTime.UtcNow;
                                        casFile.MoveTo(casFile.FullName + PendingDelete);
                                        counts.Pending++;
                                        counts.PendingSize += casSize;
                                    }
                                }
                            }
                        }
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch
                    {
                        counts.Skipped++;
                        counts.SkippedSize += casSize;
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }

                counts.Seconds += timer.TotalSeconds;
                counts.Shards++;    // Coded this way to keep FxCop from getting confused!

                return counts;
            });
        }

        private static IEnumerable<Task<CasCollectionCounters>> CollectCas(LargeCasHashSet roots, DateTime minAge, IEnumerable<string> shards)
        {
            foreach (string shard in shards)
            {
                yield return CollectCas(roots, minAge, shard);
            }
        }

        private static Task<CasCollectionCounters> CollectCasShard(LargeCasHashSet roots, DateTime minAge, string prefixFilter, string shardBasePath)
        {
            return Task.Run<CasCollectionCounters>(() =>
            {
                CasCollectionCounters counts = new CasCollectionCounters();

                var shards = Directory.EnumerateDirectories(shardBasePath, prefixFilter + "???".Substring(prefixFilter.Length));

                counts.CollectTasks(CollectCas(roots, minAge, shards));

                counts.Partitions++;    // Coded this way to keep FxCop from getting confused

                return counts;
            });
        }

        // The goal here is to parallelize on the distinct storage shards first and then go into actual collection within
        // For simple caches this ends up being 1 - for large caches this can be as high as 4096 but then the shard within
        // each is limited to 1 - thus the overall total is never more than 4096 but with one extra level of async/parallelism.
        private IEnumerable<Task<CasCollectionCounters>> CollectCasShard(LargeCasHashSet roots, DateTime minAge, string prefixFilter)
        {
            foreach (string shard in GetCasShardRootsForPrefix(prefixFilter).Distinct())
            {
                yield return CollectCasShard(roots, minAge, prefixFilter, shard);
            }
        }

        private async Task<CasEntries> ReadCasEntries(CacheActivity eventing, string strong)
        {
            using (var stream = await ContendedOpenStreamAsync(strong, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, handlePendingDelete: false))
            {
                var possible = await ReadCacheEntryAsync(stream);
                if (possible.Succeeded)
                {
                    return possible.Result;
                }

                eventing.Write(possible.Failure);

                // Corrupted fingerprint - try to delete it.
                try
                {
                    File.Delete(strong);
                }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                catch
                {
                    // We ignore any failure here as there is not much to do.
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                return default(CasEntries);
            }
        }

        // This is to return FP Scanning counts
        private readonly struct FpScanCount
        {
            public readonly long Valid;
            public readonly long Invalid;

            private FpScanCount(long valid, long invalid)
            {
                Valid = valid;
                Invalid = invalid;
            }

            public FpScanCount Add(FpScanCount other)
            {
                return new FpScanCount(Valid + other.Valid, Invalid + other.Invalid);
            }

            // A few constants
            public static readonly FpScanCount Zero = new FpScanCount(0, 0);
            public static readonly FpScanCount ValidOne = new FpScanCount(1, 0);
            public static readonly FpScanCount InvalidOne = new FpScanCount(0, 1);
        }

        private async Task<FpScanCount> CollectCasRootsStrongFingerprint(CacheActivity eventing, string strong, Action<CasHash> addToRoots)
        {
            CasEntries casEntries;

            try
            {
                casEntries = await ReadCasEntries(eventing, strong);
            }
            catch (DirectoryNotFoundException)
            {
                // The whole weak fingerprint is now gone, so the strong fingerprint
                // we were trying to load must have been collected
                return FpScanCount.Zero;
            }
            catch (FileNotFoundException)
            {
                if (IsPendingDelete(strong))
                {
                    strong = strong.Substring(0, strong.Length - PendingDelete.Length);

                    // A valid transition is from pending delete to deleted, so check if
                    // that is what happened and if so, return an empty set.
                    if (!File.Exists(strong))
                    {
                        return FpScanCount.Zero;
                    }
                }
                else
                {
                    strong = strong + PendingDelete;
                }

                // Any error now should flow up...
                casEntries = await ReadCasEntries(eventing, strong);
            }

            if (casEntries.IsValid)
            {
                // Now get the CasEntry from the Strong Fingerprint
                addToRoots(GetCasHashFromStrongFingerprintFilename(strong));

                foreach (CasHash cas in casEntries)
                {
                    addToRoots(cas);
                }

                return FpScanCount.ValidOne;
            }

            return FpScanCount.InvalidOne;
        }

        // Collect all of the CAS references within this weak fingerprint
        private async Task<FpScanCount> CollectCasRootsWeakFingerprint(CacheActivity eventing, string weak, Action<CasHash> addToRoots)
        {
            // This little dance is needed to support weak fingerprint directories
            // going away at arbitrary points - we either get the set of strong fingerprints
            // or not - if the error is because the directory is not there then there
            // is nothing to find and it is not an error.
            string[] strongFingerprints = null;
            try
            {
                strongFingerprints = Directory.GetFiles(weak);
            }
            catch (DirectoryNotFoundException)
            {
                // The directory for a weak fingerprint was deleted while we were about
                // to look at it.  This is just fine
            }
            catch (UnauthorizedAccessException)
            {
                // The directory for a weak fingerprint was being deleted while while we were about
                // to look at it.  This is just fine
                // (It is annoying that this exception can happen in addition to the not found one
                // if the timing is just right)
            }

            FpScanCount count = FpScanCount.Zero;
            if (strongFingerprints != null)
            {
                foreach (var strong in strongFingerprints)
                {
                    count = count.Add(await CollectCasRootsStrongFingerprint(eventing, strong, addToRoots));
                }
            }

            return count;
        }

        private Task<FpScanCount> CollectCasRootsShard(CacheActivity eventing, string shard, Action<CasHash> addToRoots)
        {
            return Task.Run(() =>
            {
                FpScanCount count = FpScanCount.Zero;

                foreach (var task in Directory.EnumerateDirectories(shard).Select((weak) => CollectCasRootsWeakFingerprint(eventing, weak, addToRoots)).OutOfOrderTasks())
                {
                    // Get any errors that may have been reported to be
                    // exposed.  OutOfOrderTasks() already makes sure they
                    // are completed but the error state is not exposed
                    // until you do this.
                    count = count.Add(task.Result);
                }

                return count;
            });
        }

        /// <summary>
        /// This call will collect all of the unreferenced CAS items based on the strong fingerprints
        /// </summary>
        /// <param name="log">The text writer to which information may be logged?</param>
        /// <param name="casMinimumAge">Minimum age of CAS items (in seconds) before they are considered eligible for collection</param>
        /// <param name="prefixFilter">0-3 hex digits to filter</param>
        /// <param name="activityId">Activity ID of the GC</param>
        /// <returns>Statistics dictionary with details of what happened</returns>
        /// <remarks>
        /// This only does Fingerprint->CAS collection.
        /// CAS items must be in pending delete state for casMinimumAge before they are deleted
        /// CAS items must be older than casMinimumAge before they are marked pending delete
        /// The prefix filter lets us partition the CAS items we wish to manage during this instance.
        /// This allows a very large cache to be GC'ed incrementally - without the need to have that many
        /// unique CAS hashes in memory at the same time.
        /// The prefix works at the character boundary of the CAS hash.
        /// An empty string is no filter, a 1 character string matches the first character, etc.
        /// Note that errors that prevent safe operation will throw an exception out and stop
        /// the operation of the GC.
        /// </remarks>
        public Possible<Dictionary<string, double>, Failure> CollectUnreferencedCasItems(TextWriter log, int casMinimumAge = DefaultCasMinimumAge, string prefixFilter = "", Guid activityId = default(Guid))
        {
            Contract.Requires(log != null);
            Contract.Requires(casMinimumAge > 0);
            Contract.Requires((prefixFilter != null) && Regex.IsMatch(prefixFilter, @"^[0-9A-F]{0,3}$"));

            using (var eventing = new CacheActivity(EventSource, CacheActivity.StatisticOptions, activityId, nameof(CollectUnreferencedCasItems), CacheId))
            {
                eventing.StartWithMethodArguments(new
                {
                    CasMinimumAge = casMinimumAge,
                    PrefixFilter = prefixFilter,
                });

                // Age needs to be from the start of the GC cycle
                DateTime minAge = DateTime.UtcNow.AddSeconds(-casMinimumAge);

                Dictionary<string, double> statistics = new Dictionary<string, double>();

                // We will hold CAS roots as CasHash structs since they are 20 bytes and
                // as CasHash as a string is an object with 40 characters in it - nearly 100 bytes.
                // That is a 5:1 difference in storage.  And since CasHash has no pointers,
                // the GC cost is that much simpler.
                LargeCasHashSet casRoots = new LargeCasHashSet();

                // First we need to collect the roots from all of the fingerprints we find
                // as there is no chance at filtering here.  That is, we need to check all
                // fingerprints for any references to cas hashes that may be in our prefix
                // filter range.
                var filter = MakeFilter(prefixFilter);
                try
                {
                    log.WriteLine("Reading fingerprints...");

                    CasReadRootsCounters rootCounts = new CasReadRootsCounters();

                    ElapsedTimer rootsTimer = ElapsedTimer.StartNew();

                    // Interlocked does not work on double so we use these to count
                    // raw and instance counts of CasHash roots
                    long rawCount = 0;
                    long count = 0;

                    Action<CasHash> addToRoots = (casHash) =>
                    {
                        Interlocked.Increment(ref rawCount);
                        if (filter(casHash.BaseHash))
                        {
                            Interlocked.Increment(ref count);
                            casRoots.Add(casHash);
                        }
                    };

                    // Get all of the locations that fingerprints may exist
                    List<string> dirs = new List<string>();
                    foreach (string shardRoot in GetWfpShardRoots())
                    {
                        dirs.AddRange(Directory.EnumerateDirectories(shardRoot));
                    }

                    // For each of those locations, read all fingerprints to find all CAS references (roots)
                    foreach (var task in dirs.Select((shard) => CollectCasRootsShard(eventing, shard, addToRoots)).OutOfOrderTasks())
                    {
                        // Get any errors that may have been reported to be
                        // exposed.  OutOfOrderTasks() already makes sure they
                        // are completed but the error state is not exposed
                        // until you do this.
                        rootCounts.Fingerprints += task.Result.Valid;
                        rootCounts.InvalidFingerprints += task.Result.Invalid;
                    }

                    rootCounts.RawCount += rawCount;
                    rootCounts.Count += count;

                    rootCounts.UniqueCount = casRoots.Count();
                    rootCounts.Time += rootsTimer.TotalSeconds;

                    log.WriteLine(
                        "Found {0:N0} unique CAS references from {1:N0} fingerprints in {2:N2} seconds",
                        rootCounts.UniqueCount,
                        rootCounts.Fingerprints,
                        rootCounts.Time);

                    rootCounts.Export(statistics);
                }
                catch (Exception e)
                {
                    var failure = new GcFailure(CacheId, "Reading CAS roots from fingerprints", e);
                    log.WriteLine("Error: {0}", failure.Describe());

                    eventing.StopFailure(failure);
                    return failure;
                }

                // Now that we have collected all of the roots for our CAS search, time to
                // switch to that.
                // Now we need to enumerate across all of the strong fingerprints to find ones that are
                // not referenced.
                ElapsedTimer scanTimer = ElapsedTimer.StartNew();

                log.WriteLine("Scanning CAS for collection...");

                CasCollectionCounters counts = new CasCollectionCounters();
                counts.CollectTasks(CollectCasShard(casRoots, minAge, prefixFilter));

                counts.Time += scanTimer.TotalSeconds;

                counts.Export(statistics);

                log.WriteLine(
                    "CAS: collected {0:N0} [{1:N0} bytes], pending {2:N0} [{3:N0} bytes], skipped {4:N0} of {5:N0} [{6:N0} bytes] in {7:N2} seconds",
                    counts.Collected,
                    counts.CollectedSize,
                    counts.Pending,
                    counts.PendingSize,
                    counts.Skipped,
                    counts.Items,
                    counts.ItemSize,
                    counts.Time);

                eventing.WriteStatistics(statistics);
                eventing.Stop();
                return statistics;
            }
        }

        #endregion Fingerprint to CAS GC

        #region Session To Fingerprint GC

        private Task<ProcessFingerprintsCounters> CollectFingerprints(HashSet<StrongFingerprint> roots, DateTime minAge, string prefixDir)
        {
            return Task.Run<ProcessFingerprintsCounters>(() =>
            {
                // The stats we will be returning...
                ProcessFingerprintsCounters counts = new ProcessFingerprintsCounters();

                ElapsedTimer timer = ElapsedTimer.StartNew();

                foreach (string weakFingerprint in Directory.EnumerateDirectories(prefixDir))
                {
                    counts.WeakFingerprints++;

                    try
                    {
                        bool weakIsEmpty = true;
                        foreach (string fingerprintFilename in Directory.EnumerateFiles(weakFingerprint))
                        {
                            weakIsEmpty = false;

                            StrongFingerprint strong = StrongFingerprintFromFilename(fingerprintFilename);
                            if (strong == null)
                            {
                                // This file does not look like a fingerprint file.
                                // If it is old enough, try to get rid of it.
                                if (File.GetLastWriteTimeUtc(fingerprintFilename) > minAge)
                                {
                                    try
                                    {
                                        File.Delete(fingerprintFilename);
                                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                                    catch
                                    {
                                        // Failed to delete an old unrecognized file
                                        // It will get caught the next time around, if still there
                                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                                }

                                counts.Unidentified++;
                            }
                            else
                            {
                                counts.StrongFingerprints++;

                                if (roots.Contains(strong))
                                {
                                    // It is alive!
                                    counts.Live++;

                                    // If it happens to end in pending delete we will undo that
                                    if (IsPendingDelete(fingerprintFilename))
                                    {
                                        UndoPendingDelete(fingerprintFilename);
                                        counts.Resurrected++;
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        // Is it old enough that we do something about it
                                        // This is true for both normal and pending delete.
                                        // If the file is "normal" we will allow it to remain
                                        // "normal" until it is at least this old.  The reason
                                        // for that is that new files could show up at any
                                        // time and we really need that to be safe.
                                        // Then, if it is old enough, we reset the date and
                                        // try to rename it.  This way we are sure that it remains
                                        // in pending delete for at least one more GC cycle and
                                        // allows us to notice if it gets referenced.  Any
                                        // referencing of a fingerprint in normal use will undo
                                        // the pending delete state and thus return the
                                        // file to "normal" (but not rewrite the date,
                                        // albeit that may also happen)
                                        if (File.GetLastWriteTimeUtc(fingerprintFilename) > minAge)
                                        {
                                            counts.TooYoung++;
                                        }
                                        else
                                        {
                                            // No root pointing to it, lets check the
                                            // pending delete state
                                            if (IsPendingDelete(fingerprintFilename))
                                            {
                                                File.Delete(fingerprintFilename);
                                                counts.Collected++;
                                            }
                                            else
                                            {
                                                // Set the date to now and rename it
                                                // to pending delete.  This give the file
                                                // at least fingerprint Minimum Age in the
                                                // pending delete state.
                                                File.SetLastWriteTimeUtc(fingerprintFilename, DateTime.UtcNow);
                                                File.Move(fingerprintFilename, fingerprintFilename + PendingDelete);
                                                counts.Pending++;
                                            }
                                        }
                                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                                    catch
                                    {
                                        // Any failure trying to deal with the
                                        // fingerprint gets it skipped
                                        // It will be handled in a later GC
                                        counts.Skipped++;
                                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                                }
                            }
                        }

                        // If the weak fingerprint looks empty
                        // we try to delete it but we don't care if
                        // it fails as we will get it next time
                        if (weakIsEmpty)
                        {
                            try
                            {
                                Directory.Delete(weakFingerprint);
                                counts.CollectedWeak++;
                            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                            catch
                            {
                                // We really don't care about failure to collect a weak
                                // fingerprint directory - it is perfectly normal
                                counts.SkippedWeak++;
                            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                        }
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch
                    {
                        // This just means that a directory was already deleted
                        // by another concurrent GC that may be running
                        // No big deal - will just record that
                        counts.ConcurrentlyCollectedWeak++;
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }

                counts.Seconds += timer.TotalSeconds;
                counts.Shards++;    // Coded this way to keep FxCop from getting confused

                return counts;
            });
        }

        private IEnumerable<Task<ProcessFingerprintsCounters>> CollectFingerprints(HashSet<StrongFingerprint> roots, DateTime minAge, IEnumerable<string> weakPrefixes)
        {
            foreach (string weakPrefixDirectory in weakPrefixes)
            {
                yield return CollectFingerprints(roots, minAge, weakPrefixDirectory);
            }
        }

        private Task<ProcessFingerprintsCounters> CollectFingerprintsShard(HashSet<StrongFingerprint> roots, DateTime minAge, string prefixFilter, string shardBasePath)
        {
            return Task.Run<ProcessFingerprintsCounters>(() =>
            {
                ProcessFingerprintsCounters counts = new ProcessFingerprintsCounters();

                var shards = Directory.EnumerateDirectories(shardBasePath, prefixFilter + "???".Substring(prefixFilter.Length));

                counts.CollectTasks(CollectFingerprints(roots, minAge, shards));

                counts.Partitions++;    // Coded this way to keep FxCop from getting confused

                return counts;
            });
        }

        // The goal here is to parallelize on the distinct storage shards first and then go into actual collection within
        // For simple caches this ends up being 1 - for large caches this can be as high as 4096 but then the shard within
        // each is limited to 1 - thus the overall total is never more than 4096 but with one extra level of async/parallelism.
        private IEnumerable<Task<ProcessFingerprintsCounters>> CollectFingerprintShard(HashSet<StrongFingerprint> roots, DateTime minAge, string prefixFilter)
        {
            foreach (string shard in GetWfpShardRootsForPrefix(prefixFilter).Distinct())
            {
                yield return CollectFingerprintsShard(roots, minAge, prefixFilter, shard);
            }
        }

        /// <summary>
        /// This call will collect all of the unreferenced fingerprints based on session references
        /// </summary>
        /// <param name="log">The text writer to which information may be logged?</param>
        /// <param name="prefixFilter">0-3 hex digits to filter</param>
        /// <param name="activityId">Activity ID of the GC</param>
        /// <returns>Statistics dictionary with details of what happened</returns>
        /// <remarks>
        /// This only does Session->Fingerprint collection.
        /// Fingerprints must be in pending delete state for fingerprintMinimumAge before they are deleted
        /// Fingerprints must be older than fingerprintMinimumAge before they are marked pending delete
        /// The prefix filter lets us partition the fingerprints we wish to manage during this instance.
        /// This allows a very large cache to be GC'ed incrementally - without the need to have that many
        /// unique fingerprints in memory at the same time.
        /// The prefix works at the character boundary of the weak fingerprint part of the fingerprint.
        /// An empty string is no filter, a 1 character string matches the first character, etc.
        /// Note that errors that prevent safe operation will throw an exception out and stop
        /// the operation of the GC.
        /// </remarks>
        public Possible<Dictionary<string, double>, Failure> CollectUnreferencedFingerprints(TextWriter log, string prefixFilter = "", Guid activityId = default(Guid))
        {
            Contract.Requires(log != null);
            Contract.Requires((prefixFilter != null) && Regex.IsMatch(prefixFilter, @"^[0-9A-Fa-f]{0,3}$"));

            using (var eventing = new CacheActivity(EventSource, CacheActivity.StatisticOptions, activityId, nameof(CollectUnreferencedFingerprints), CacheId))
            {
                eventing.StartWithMethodArguments(new
                {
                    PrefixFilter = prefixFilter,
                });

                // Age needs to be from the start of the GC cycle
                DateTime minAge = DateTime.UtcNow.Subtract(TimeToLive);

                HashSet<StrongFingerprint> roots = new HashSet<StrongFingerprint>();

                Dictionary<string, double> statistics = new Dictionary<string, double>();

                var filter = MakeFilter(prefixFilter);
                try
                {
                    log.WriteLine("Reading sessions...");

                    FingerprintReadRootsCounters rootsCounters = new FingerprintReadRootsCounters();

                    ElapsedTimer rootsTimer = ElapsedTimer.StartNew();

                    foreach (string sessionId in BasicFilesystemCacheSession.EnumerateCompletedSessions(m_sessionRoot))
                    {
                        rootsCounters.Sessions++;
                        foreach (var strongTask in EnumerateSessionDataAsync(sessionId).GetAwaiter().GetResult())
                        {
                            rootsCounters.RawCount++;

                            StrongFingerprint strong = strongTask.Result;
                            if (filter(strong.WeakFingerprint.FingerprintHash))
                            {
                                rootsCounters.Count++;
                                roots.Add(strongTask.Result);
                            }
                        }
                    }

                    rootsCounters.UniqueCount = roots.Count;
                    rootsCounters.Time += rootsTimer.TotalSeconds;

                    log.WriteLine(
                        "Found {0:N0} unique fingerprints from {1:N0} sessions in {2:N2} seconds",
                        rootsCounters.UniqueCount,
                        rootsCounters.Sessions,
                        rootsCounters.Time);

                    rootsCounters.Export(statistics);
                }
                catch (Exception e)
                {
                    var failure = new GcFailure(CacheId, "Reading fingerprint roots from sessions", e);
                    log.WriteLine("Error: {0}", failure.Describe());

                    eventing.StopFailure(failure);
                    return failure;
                }

                // Now we need to enumerate across all of the strong fingerprints to find ones that are
                // not referenced.
                ElapsedTimer scanTimer = ElapsedTimer.StartNew();

                log.WriteLine("Scanning fingerprints for collection...");

                ProcessFingerprintsCounters counts = new ProcessFingerprintsCounters();
                counts.CollectTasks(CollectFingerprintShard(roots, minAge, prefixFilter));

                counts.Time += scanTimer.TotalSeconds;

                counts.Export(statistics);

                log.WriteLine(
                    "Fingerprints: collected {0:N0}, pending {1:N0}, skipped {2:N0} in {3:N2} seconds",
                    counts.Collected,
                    counts.Pending,
                    counts.Skipped,
                    counts.Time);

                eventing.WriteStatistics(statistics);
                eventing.Stop();
                return statistics;
            }
        }

        #endregion Session To Fingerprint GC

        #endregion BasicFilesystem GC

        /// <summary>
        /// Delete the given session id
        /// </summary>
        /// <param name="sessionId">The session ID to delete</param>
        /// <remarks>
        /// No error if given if the session ID does not exist as that is the same as deleted.
        /// May throw if the session exists but could not be deleted due to locks/etc.
        /// Do we want this to become an API in normal caches?  Right now this is BasicFilesystem only
        /// but session management really belongs further up.
        /// </remarks>
        public void DeleteSession(string sessionId)
        {
            string sessionFile = Path.Combine(m_sessionRoot, CompletedSessionPrefix + sessionId);
            File.Delete(sessionFile);
        }

        private struct CacheConfiguration
        {
            public TimeSpan FingerprintMinAge;
        }

        #region ICache interface methods

        /// <inheritdoc/>
        public string CacheId => m_cacheId;

        /// <inheritdoc/>
        public Guid CacheGuid => m_cacheGuid;

        /// <inheritdoc/>
        public bool StrictMetadataCasCoupling => m_strictMetadataCasCoupling;

        /// <summary>
        /// Whether or not the basic file system cache acts as an authoritative cache.
        /// In authoritative mode the cache stamps responses with its cache guid of the cache
        /// In non-authoritative mode, returns the determinism guid as is.
        /// </summary>
        public bool IsAuthoritative { get; }

        /// <inheritdoc/>
        public bool IsShutdown => m_shutdown;

        /// <inheritdoc/>
        public bool IsReadOnly => m_readOnly;

        /// <inheritdoc/>
        public bool IsDisconnected => DisconnectCount > 0;

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000", Justification = "Sessions are return to caller and are verified closed")]
        public Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            Contract.Requires(!IsShutdown);
            var session = BasicFilesystemCacheSession.TryCreateBasicFilesystemCacheSession(this, true);

            if (session.Succeeded)
            {
                return Task.FromResult<Possible<ICacheReadOnlySession, Failure>>(session.Result);
            }
            else
            {
                return Task.FromResult<Possible<ICacheReadOnlySession, Failure>>(session.Failure);
            }
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000", Justification = "Sessions are return to caller and are verified closed")]
        public Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!IsReadOnly);

            var session = BasicFilesystemCacheSession.TryCreateBasicFilesystemCacheSession(this, false);

            if (session.Succeeded)
            {
                return Task.FromResult<Possible<ICacheSession, Failure>>(session.Result);
            }
            else
            {
                return Task.FromResult<Possible<ICacheSession, Failure>>(session.Failure);
            }
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!IsReadOnly);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));

            return Task.Run<Possible<ICacheSession, Failure>>(() =>
            {
                var session = BasicFilesystemCacheSession.TryCreateBasicFilesystemCacheSession(this, false, m_sessionRoot, sessionId);

                if (session.Succeeded)
                {
                    return session.Result;
                }
                else
                {
                    return new DuplicateSessionIdFailure(CacheId, sessionId, session.Failure.CreateException());
                }
            });
        }

        /// <inheritdoc/>
        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            Contract.Requires(!IsShutdown);
            return EnumerateIntoTasks(BasicFilesystemCacheSession.EnumerateCompletedSessions(m_sessionRoot));
        }

        /// <inheritdoc/>
        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));

            try
            {
                return new Possible<IEnumerable<Task<StrongFingerprint>>, Failure>(EnumerateSessionDataAsync(sessionId).GetAwaiter().GetResult());
            }
            catch (Exception e)
            {
                return new UnknownSessionFailure(CacheId, sessionId, e);
            }
        }

        /// <inheritdoc/>
        public Task<Possible<string, Failure>> ShutdownAsync()
        {
            Contract.Requires(!IsShutdown);

            m_shutdown = true;

            if (OpenSessions.Count > 0)
            {
                return Task.FromResult(new Possible<string, Failure>(new ShutdownWithOpenSessionsFailure(CacheId, OpenSessions.Keys)));
            }

            return Task.FromResult(new Possible<string, Failure>(CacheId));
        }

        /// <inheritdoc/>
        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            Contract.Requires(!IsShutdown);

            m_notificationListeners.Add(notificationCallback);

            foreach (Failure failure in m_initialMessages)
            {
                notificationCallback(failure);
            }
        }

        #endregion ICache interface methods
    }
}
