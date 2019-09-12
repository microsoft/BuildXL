// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using ContentStoreTest.Extensions;
using BuildXL.Utilities;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Distributed.Redis
{
    /// <summary>
    /// Wrapper around local redis instance.
    /// </summary>
    public sealed class LocalRedisProcessDatabase : ITestRedisDatabase
    {
        private DisposableDirectory _tempDirectory;
        private IClock _clock;
        private PassThroughFileSystem _fileSystem;
        private ILogger _logger;

        private ProcessUtility _process;
        private ConnectionMultiplexer _connectionMultiplexer;
        public string ConnectionString { get; private set; }

        private bool _disposed;
        private LocalRedisFixture _redisFixture;

        internal bool Closed { get; private set; }

        internal bool Initialized => _fileSystem != null;

        internal LocalRedisProcessDatabase()
        {
        }

        private void Init(ILogger logger, IClock clock, LocalRedisFixture redisFixture)
        {
            _fileSystem = new PassThroughFileSystem(logger);
            _logger = logger;
            _tempDirectory = new DisposableDirectory(_fileSystem, "RedisTests");
            _clock = clock;
            _redisFixture = redisFixture;
            _disposed = false;
        }

        public override string ToString()
        {
            return ConnectionString;
        }

        /// <summary>
        /// Creates an empty instance of a database.
        /// </summary>
        public static LocalRedisProcessDatabase CreateAndStartEmpty(
            LocalRedisFixture redisFixture,
            ILogger logger,
            IClock clock)
        {
            return CreateAndStart(redisFixture, logger, clock, initialData: null, expiryData: null, setData: null);
        }

        /// <summary>
        /// Creates an instance of a database with a given data.
        /// </summary>
        public static LocalRedisProcessDatabase CreateAndStart(
            LocalRedisFixture redisFixture,
            ILogger logger,
            IClock clock,
            IDictionary<RedisKey, RedisValue> initialData,
            IDictionary<RedisKey, DateTime> expiryData,
            IDictionary<RedisKey, RedisValue[]> setData)
        {
            logger.Debug($"Fixture '{redisFixture.Id}' has {redisFixture.DatabasePool.ObjectsInPool} available redis databases.");
            var instance = redisFixture.DatabasePool.GetInstance();
            var oldOrNew = instance.Instance._process != null ? "an old" : "a new";
            logger.Debug($"LocalRedisProcessDatabase: got {oldOrNew} instance from the pool.");

            var result = instance.Instance;
            result.Init(logger, clock, redisFixture);
            try
            {
                result.Start(initialData, expiryData, setData);
                return result;
            }
            catch (Exception e)
            {
                logger.Error("Failed to start a local database. Exception=" + e);
                result.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                // The type should be safe for double dispose.
                return;
            }

            _logger.Debug($"Returning database to pool in fixture '{_redisFixture.Id}'");
            _redisFixture.DatabasePool.PutInstance(this);
            _disposed = true;
        }

        public void Close()
        {
            _connectionMultiplexer?.Close(allowCommandsToComplete: false);
            _connectionMultiplexer?.Dispose();

            if (_process != null)
            {
                SafeKillProcess();
            }

            _tempDirectory.Dispose();
            _fileSystem.Dispose();
            Closed = true;
        }

        private void SafeKillProcess()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process?.Kill();
                    _process?.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        public bool BatchCalled { get; set; }

        private void Start(
            IDictionary<RedisKey, RedisValue> initialData,
            IDictionary<RedisKey, DateTime> expiryData,
            IDictionary<RedisKey, RedisValue[]> setData)
        {
            StartRedisServerIfNeeded();

            var database = GetDatabase().WithKeyPrefix(RedisContentLocationStoreFactory.DefaultKeySpace);

            try
            {
                _logger.Debug("Flushing the database...");
            }
            catch (InvalidOperationException)
            {
                // InvalidOperation is thrown when there is no active tests.
                throw;
            }

            var flushResult = database.Execute("FLUSHALL");
            try
            {
                _logger.Debug($"Flushing the database completed. Result={flushResult}");
            }
            catch (InvalidOperationException)
            {
                // InvalidOperation is thrown when there is no active tests.
                throw;
            }

            if (initialData != null)
            {
                foreach (KeyValuePair<RedisKey, RedisValue> kvp in initialData)
                {
                    string key = kvp.Key;
                    key = key.Substring(RedisContentLocationStoreFactory.DefaultKeySpace.Length);
                    if (expiryData != null && expiryData.TryGetValue(kvp.Key, out var expiryDate))
                    {
                        database.StringSet(key, kvp.Value, expiryDate - _clock.UtcNow);
                    }
                    else
                    {
                        database.StringSet(key, kvp.Value, TimeSpan.FromSeconds(30));
                    }
                }
            }

            if (setData != null)
            {
                foreach (KeyValuePair<RedisKey, RedisValue[]> kvp in setData)
                {
                    string key = kvp.Key;
                    key = key.Substring(RedisContentLocationStoreFactory.DefaultKeySpace.Length);
                    database.SetAdd(key, kvp.Value);
                }
            }
        }

        private void StartRedisServerIfNeeded()
        {
            if (_process != null)
            {
                try
                {
                    _logger.Debug($"Redis process is already running. Reusing an existing instance.");
                }
                catch (InvalidOperationException)
                {
                    // InvalidOperation is thrown when there is no active tests.
                    throw;
                }

                return;
            }

            try
            {
                _logger.Debug("Starting a redis server.");
            }
            catch (InvalidOperationException)
            {
                // InvalidOperation is thrown when there is no active tests.
                throw;
            }

            string redisServerPath = Path.GetFullPath("redis-server.exe");

            if (!File.Exists(redisServerPath))
            {
                throw new InvalidOperationException("Could not find redis-server.exe at " + redisServerPath);
            }

            int portNumber = 0;

            const int maxRetries = 10;
            for (int i = 0; i < maxRetries; i++)
            {
                var fileName = _tempDirectory.CreateRandomFileName();
                var redisServerLogsPath = _tempDirectory.CreateRandomFileName();
                _fileSystem.CreateDirectory(redisServerLogsPath);
                portNumber = PortExtensions.GetNextAvailablePort();
                string newConfig = $@"
timeout 0
tcp-keepalive 0
dir {redisServerLogsPath}
port {portNumber}";

                File.WriteAllText(fileName.Path, newConfig);

                var args = $" {fileName}";
                try
                {
                    _logger.Debug($"Running cmd=[{redisServerPath} {args}]");
                }
                catch (InvalidOperationException)
                {
                    // InvalidOperation is thrown when there is no active tests.
                    throw;
                }

                const bool createNoWindow = true;
                _process = new ProcessUtility(redisServerPath, args, createNoWindow);

                _process.Start();

                string processOutput;
                if (_process == null)
                {
                    processOutput = "[Process could not start]";
                    throw new InvalidOperationException(processOutput);
                }

                if (createNoWindow)
                {
                    if (_process.HasExited)
                    {
                        if (_process.WaitForExit(5000))
                        {
                            throw new InvalidOperationException(_process.GetLogs());
                        }

                        throw new InvalidOperationException("Process or either wait handle timed out. " + _process.GetLogs());
                    }

                    processOutput = $"[Process {_process.Id} is still running]";
                }

                try
                {
                    _logger.Debug("Process output: " + processOutput);
                }
                catch (InvalidOperationException)
                {
                    // InvalidOperation is thrown when there is no active tests.
                    throw;
                }

                ConnectionString = $"localhost:{portNumber}";
                ConfigurationOptions options = ConfigurationOptions.Parse(ConnectionString);
                options.ConnectTimeout = 2000;
                options.SyncTimeout = 2000;
                try
                {
                    _connectionMultiplexer = ConnectionMultiplexer.Connect(options);
                    break;
                }
                catch (RedisConnectionException ex)
                {
                    SafeKillProcess();
                    try
                    {
                        _logger.Debug($"Retrying for exception connecting to redis process {_process.Id} with port {portNumber}: {ex.ToString()}. Has process exited {_process.HasExited} with output {_process.GetLogs()}");
                    }
                    catch (InvalidOperationException)
                    {
                        // InvalidOperation is thrown when there is no active tests.
                        throw;
                    }

                    if (i != maxRetries - 1)
                    {
                        Thread.Sleep(300);
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    SafeKillProcess();
                    _logger.Error(
                        $"Exception connecting to redis process {_process.Id} with port {portNumber}: {ex.ToString()}. Has process exited {_process.HasExited} with output {_process.GetLogs()}");
                    throw;
                }
            }

            try
            {
                _logger.Debug($"Redis server {_process.Id} is up and running at port {portNumber}.");
            }
            catch (InvalidOperationException)
            {
                // InvalidOperation is thrown when there is no active tests.
                throw;
            }
        }

        public void Execute()
        {
            BatchCalled = true;
        }

        public Task<RedisValue> StringGetAsync(RedisKey key)
        {
            var database = GetDatabase();
            return database.StringGetAsync(key);
        }

        private IDatabase GetDatabase()
        {
            return _connectionMultiplexer.GetDatabase();
        }

        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, When condition)
        {
            var database = GetDatabase();
            return database.StringSetAsync(key, value, null, condition);
        }

        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiryTimespan, When condition)
        {
            var database = GetDatabase();
            return database.StringSetAsync(key, value, expiryTimespan, condition);
        }

        public Task<long> StringIncrementAsync(RedisKey key)
        {
            var database = GetDatabase();
            return database.StringIncrementAsync(key);
        }

        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit)
        {
            var database = GetDatabase();
            return database.StringSetBitAsync(key, offset, bit);
        }

        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue range)
        {
            var database = GetDatabase();
            return database.StringSetRangeAsync(key, offset, range);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime expiryDateTime)
        {
            var database = GetDatabase();
            return database.KeyExpireAsync(key, expiryDateTime);
        }

        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key)
        {
            var database = GetDatabase();
            return database.KeyTimeToLiveAsync(key);
        }

        public Task<bool> KeyDeleteAsync(RedisKey key)
        {
            var database = GetDatabase();
            return database.KeyDeleteAsync(key);
        }

        public Task<bool> SetAddAsync(RedisKey key, RedisValue value)
        {
            var database = GetDatabase();
            return database.SetAddAsync(key, value);
        }

        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values)
        {
            var database = GetDatabase();
            return database.SetAddAsync(key, values);
        }

        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value)
        {
            var database = GetDatabase();
            return database.SetRemoveAsync(key, value);
        }

        public Task<RedisValue[]> SetMembersAsync(RedisKey key)
        {
            var database = GetDatabase();
            return database.SetMembersAsync(key);
        }

        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count)
        {
            var database = GetDatabase();
            return database.SetRandomMembersAsync(key, count);
        }

        public Task<long> SetLengthAsync(RedisKey key)
        {
            var database = GetDatabase();
            return database.SetLengthAsync(key);
        }

        public IEnumerable<RedisKey> Keys => GetKeys();

        private IEnumerable<RedisKey> GetKeys()
        {
            var database = GetDatabase();
            foreach (var endpoint in _connectionMultiplexer.GetEndPoints())
            {
                IServer server = _connectionMultiplexer.GetServer(endpoint);
                foreach (RedisKey key in server.Keys())
                {
                    yield return key;
                }
            }
        }

        public Task<long> DeleteStringKeys(Func<string, bool> shouldDelete)
        {
            var database = GetDatabase();
            var deletedKeys = GetKeys().Where(key => database.KeyType(key) == RedisType.String && shouldDelete(key)).ToArray();
            return database.KeyDeleteAsync(deletedKeys);
        }

        public IDictionary<RedisKey, MockRedisValueWithExpiry> GetDbWithExpiry()
        {
            Dictionary<RedisKey, MockRedisValueWithExpiry> dict = new Dictionary<RedisKey, MockRedisValueWithExpiry>();
            var database = GetDatabase();
            foreach (RedisKey key in GetKeys())
            {
                string stringKey = key;
                stringKey = stringKey.Substring(RedisContentLocationStoreFactory.DefaultKeySpace.Length);
                RedisKey redisKey = stringKey;
                redisKey = redisKey.Prepend(RedisContentLocationStoreFactory.DefaultKeySpace);
                if (!dict.ContainsKey(redisKey))
                {
                    var type = database.KeyType(key);
                    if (type != RedisType.String)
                    {
                        continue;
                    }

                    var valueWithExpiry = database.StringGetWithExpiry(key);
                    DateTime? expiryDate;
                    if (valueWithExpiry.Expiry == null)
                    {
                        expiryDate = null;
                    }
                    else if (valueWithExpiry.Expiry.Value == default(TimeSpan))
                    {
                        expiryDate = null;
                    }
                    else
                    {
                        expiryDate = _clock.UtcNow + valueWithExpiry.Expiry;
                    }

                    dict[redisKey] = new MockRedisValueWithExpiry(valueWithExpiry.Value, expiryDate);
                }
            }

            return dict;
        }

        public Task<RedisResult> ExecuteScriptAsync(string script, RedisKey[] keys, RedisValue[] values)
        {
            var database = GetDatabase();
            return database.ScriptEvaluateAsync(script, keys, values);
        }

        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags command)
        {
            var database = GetDatabase();
            return database.StringGetAsync(key, command);
        }

        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags command = CommandFlags.None)
        {
            var database = GetDatabase();
            return database.HashGetAllAsync(key, command);
        }

        public Task<bool> HashSetAsync(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None)
        {
            var database = GetDatabase();
            return database.HashSetAsync(key, hashField, value, when, flags);
        }

        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            var database = GetDatabase();
            return database.HashGetAsync(key, hashField, flags);
        }

        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags command = CommandFlags.None)
        {
            var database = GetDatabase();
            return database.KeyExistsAsync(key, command);
        }
    }
}
