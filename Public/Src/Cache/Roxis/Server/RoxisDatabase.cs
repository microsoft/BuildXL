// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;

namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// Handles persistent storage for Roxis. Basically in charge of running commands against a RocksDb instance.
    /// </summary>
    public class RoxisDatabase : StartupShutdownSlimBase
    {
        private readonly SerializationPool _serializationPool = new SerializationPool();

        private readonly RoxisDatabaseConfiguration _configuration;
        private readonly IClock _clock;
        private KeyValueStoreAccessor? _accessor;

        [MemberNotNullWhen(true, nameof(_accessor))]
        private bool Started => StartupCompleted;

        // TODO: Add support for Spans in RocksDbSharp and make entire API surface use Span.
        // TODO: we can remove this lockset if we add support for transactions into RocksDbSharp
        private readonly LockSet<int> _locks = new LockSet<int>();

        protected override Tracer Tracer { get; } = new Tracer(nameof(RoxisDatabase));

        private enum Columns
        {
            Registers,
        }

        public RoxisDatabase(RoxisDatabaseConfiguration configuration, IClock clock)
        {
            Contract.RequiresNotNullOrEmpty(configuration.Path);

            _configuration = configuration;
            _clock = clock;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _accessor = KeyValueStoreAccessor.Open(new RocksDbStoreConfiguration(_configuration.Path)
            {
                AdditionalColumns = new[] { nameof(Columns.Registers) },
                DropMismatchingColumns = true,
                EnableStatistics = true,
                RotateLogsNumFiles = 5,
                RotateLogsMaxAge = TimeSpan.FromDays(7),
                RotateLogsMaxFileSizeBytes = (ulong)"100MB".ToSize(),
                EnableWriteAheadLog = _configuration.EnableWriteAheadLog,
                EnableFSync = _configuration.EnableFSync,
            }).ToResult(isNullAllowed: true).ThrowIfFailure();

            Contract.Assert(!_accessor.Disabled);

            return BoolResult.SuccessTask;
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _accessor?.Dispose();
            return BoolResult.SuccessTask;
        }

        public async Task<T> HandleAsync<T>(Command command)
            where T : CommandResult
        {
            var result = await HandleAsync(command);
            Contract.AssertNotNull(result);
            var casted = result as T;
            Contract.AssertNotNull(casted);
            return casted;
        }

        public async Task<CommandResult> HandleAsync(Command command)
        {
            return command.Type switch
            {
                CommandType.Get => await RegisterGetAsync((command as GetCommand)!),
                CommandType.Set => await RegisterSetAsync((command as SetCommand)!),
                CommandType.CompareExchange => await RegisterCompareExchangeAsync((command as CompareExchangeCommand)!),
                CommandType.CompareRemove => await RegisterCompareRemoveAsync((command as CompareRemoveCommand)!),
                CommandType.Remove => await RegisterRemoveAsync((command as RemoveCommand)!),
                CommandType.PrefixEnumerate => RegisterPrefixEnumerate((command as PrefixEnumerateCommand)!),
                // TODO: log key?
                _ => throw Contract.AssertFailure($"Unhandled command type `{command.Type}`"),
            };
        }

        #region Register Operations

        // TODO: need to implement proper gc here. The issue is that gc needs to take a lock on the key when iterating to make sure
        // there isn't any last minute changes. The current GarbageCollect methods don't support this, so we need to add one.

        //private async Task<GarbageCollectResult> RegisterGarbageCollectionAsync(OperationContext context)
        //{
        //    return await context.PerformOperationAsync(Tracer, async () =>
        //    {
        //        Contract.Assert(Started);
        //        return _accessor.Use(store =>
        //        {
        //            return store.GarbageCollect((key, value) => {
        //                return false;
        //            }, columnFamilyName: nameof(Columns.Registers));
        //        }).ToResult();
        //    });
        //}


        public async Task<SetResult> RegisterSetAsync(SetCommand command)
        {
            using var lockHandle = await GetLockHandleAsync(command.Key);

            Contract.Assert(Started);

            return _accessor.Use(
                static (store, state) =>
                {
                    var command = state.command;
                    var @this = state.@this;
                    if (command.Overwrite)
                    {
                        @this.SetRegisterInternal(command.Key, command.Value, command.ExpiryTimeUtc, store);
                        return new SetResult() { Set = true };
                    }

                    if (!store.TryGetValue(command.Key, out var data, columnFamilyName: nameof(Columns.Registers)))
                    {
                        @this.SetRegisterInternal(command.Key, command.Value, command.ExpiryTimeUtc, store);
                        return new SetResult() { Set = true };
                    }

                    var register = @this.DeserializeRegister(data);
                    if (register.HasExpired(@this._clock.UtcNow))
                    {
                        @this.SetRegisterInternal(command.Key, command.Value, command.ExpiryTimeUtc, store);
                        return new SetResult() { Set = true };
                    }

                    // TODO: perhaps should return the previous value if available? bothersome because we'd have to read in
                    // overwrite mode.
                    return new SetResult() { Set = false };
                },
                (@this: this, command))
                .ToResult(isNullAllowed: true)
                .ThrowIfFailure();
        }

        // TODO: maybe Get should allow passing in a expiry time and use it as a "Touch"?
        public async Task<GetResult> RegisterGetAsync(GetCommand command)
        {
            using var lockHandle = await GetLockHandleAsync(command.Key);

            Contract.Assert(Started);
            return _accessor.Use(
                static (store, state) =>
                {
                    var command = state.command;
                    var @this = state.@this;
                    if (!store.TryGetValue(command.Key, out var data, columnFamilyName: nameof(Columns.Registers)))
                    {
                        return new GetResult() { Value = null };
                    }

                    var register = @this.DeserializeRegister(data);
                    if (register.HasExpired(@this._clock.UtcNow))
                    {
                        store.Remove(command.Key, columnFamilyName: nameof(Columns.Registers));
                        return new GetResult() { Value = null };
                    }

                    return new GetResult() { Value = register.Value };
                },
                (@this: this, command))
                .ToResult(isNullAllowed: true)
                .ThrowIfFailure();
        }

        public async Task<RemoveResult> RegisterRemoveAsync(RemoveCommand command)
        {
            using var lockHandle = await GetLockHandleAsync(command.Key);

            Contract.Assert(Started);
            return _accessor.Use(
                static (store, command) =>
                {
                    store.Remove(command.Key, columnFamilyName: nameof(Columns.Registers));
                    return new RemoveResult();
                },
                command)
                .ToResult(isNullAllowed: true)
                .ThrowIfFailure();
        }

        public async Task<CompareExchangeResult> RegisterCompareExchangeAsync(CompareExchangeCommand command)
        {
            if ((command.CompareKey == null) != (command.CompareKeyValue == null))
            {
                var compareKeyString = command.CompareKey == null ? "NULL" : "BINARY";
                var compareKeyValueString = command.CompareKeyValue == null ? "NULL" : "BINARY";
                throw new ArgumentException($"Invalid arguments to CompareExchange. Expected {nameof(command.CompareKey)} and {nameof(command.CompareKeyValue)} to be either both null or both non-null. {nameof(command.CompareKey)}=[{compareKeyString}] {nameof(command.CompareKeyValue)}=[{compareKeyValueString}]");
            }

            using var lockHandle = await GetLockHandleAsync(command.Key);

            Contract.Assert(Started);
            return _accessor.Use(
                static (store, state) =>
                {
                    var command = state.command;
                    var @this = state.@this;

                    var compareKey = command.CompareKey ?? command.Key;

                    if (!store.TryGetValue(compareKey, out byte[]? readCompareKeyValue, columnFamilyName: nameof(Columns.Registers)))
                    {
                        readCompareKeyValue = null;
                    }
                    else
                    {
                        var currentRegister = @this.DeserializeRegister(readCompareKeyValue!);
                        if (currentRegister.HasExpired(@this._clock.UtcNow))
                        {
                            readCompareKeyValue = null;
                        }
                        else
                        {
                            readCompareKeyValue = currentRegister.Value;
                        }
                    }

                    if (@this.Equals(readCompareKeyValue, command.Comparand))
                    {
                        if (command.CompareKey != null && command.CompareKeyValue != null)
                        {
                            @this.SetRegisterInternal(command.CompareKey.Value, command.CompareKeyValue.Value, command.ExpiryTimeUtc, store);
                        }

                        @this.SetRegisterInternal(command.Key, command.Value, command.ExpiryTimeUtc, store);
                        return new CompareExchangeResult(readCompareKeyValue, exchanged: true);
                    }

                    return new CompareExchangeResult(readCompareKeyValue, exchanged: false);
                },
                (@this: this, command))
                .ToResult(isNullAllowed: true)
                .ThrowIfFailure();
        }

        private bool Equals(ByteString? v1, ByteString? v2)
        {
            if (v1 == null && v2 == null)
            {
                return true;
            }
            else if (v1 == null || v2 == null)
            {
                return false;
            }

            return v1.Value.Value.SequenceEqual(v2.Value.Value);
        }

        private void SetRegisterInternal(ByteString key, ByteString value, DateTime? expiryTimeUtc, IBuildXLKeyValueStore store)
        {
            var register = new Register(value, expiryTimeUtc);
            store.Put(key, SerializeRegister(register), columnFamilyName: nameof(Columns.Registers));
        }

        public async Task<CompareRemoveResult> RegisterCompareRemoveAsync(CompareRemoveCommand command)
        {
            using var lockHandle = await GetLockHandleAsync(command.Key);

            Contract.Assert(Started);
            return _accessor.Use(
                static (store, state) =>
                {
                    var command = state.command;
                    var @this = state.@this;

                    if (!store.TryGetValue(command.Key, out var data, columnFamilyName: nameof(Columns.Registers)))
                    {
                        return new CompareRemoveResult() { Value = null };
                    }

                    var register = @this.DeserializeRegister(data);
                    if (register.HasExpired(@this._clock.UtcNow))
                    {
                        store.Remove(command.Key, columnFamilyName: nameof(Columns.Registers));
                        return new CompareRemoveResult() { Value = null };
                    }

                    if (register.Value.SequenceEqual((byte[])command.Comparand))
                    {
                        store.Remove(command.Key, columnFamilyName: nameof(Columns.Registers));
                    }

                    return new CompareRemoveResult() { Value = register.Value };
                },
                (@this: this, command))
                .ToResult(isNullAllowed: true)
                .ThrowIfFailure();
        }

        public PrefixEnumerateResult RegisterPrefixEnumerate(PrefixEnumerateCommand command)
        {
            Contract.Assert(Started);
            return _accessor
                .Use(static (store, state) => new PrefixEnumerateResult(performSearch(state.@this, store, state.command.Key).ToList()), (@this: this, command))
                .ToResult(isNullAllowed: false)
                .ThrowIfFailure();

            static IEnumerable<KeyValuePair<ByteString, ByteString>> performSearch(RoxisDatabase @this, IBuildXLKeyValueStore store, ByteString prefix)
            {
                var now = @this._clock.UtcNow;

                foreach (var kvp in store.PrefixSearch(prefix, columnFamilyName: nameof(Columns.Registers)))
                {
                    var register = @this.DeserializeRegister(kvp.Value);

                    // WARNING: we don't perform any mutation on the database here because we are operating out of a
                    // snapshot, so the data may have changed in the mean time.
                    if (register.HasExpired(now))
                    {
                        continue;
                    }

                    yield return new KeyValuePair<ByteString, ByteString>(kvp.Key, register.Value);
                }
            }
        }

        // TODO: make non-async and back-propagate to gRPC handler
        private Task<LockSet<int>.LockHandle> GetLockHandleAsync(ByteString key)
        {
            int bucket = 0;

            var keySpan = (byte[])key;
            switch (keySpan.Length)
            {
                case 0:
                    break;
                case 1:
                    bucket = keySpan[0];
                    break;
                case 2:
                case 3:
                    bucket = BitConverter.ToInt16(keySpan, 0);
                    break;
                default:
                    bucket = BitConverter.ToInt32(keySpan, 0);
                    break;
            }

            return _locks.AcquireAsync(bucket);
        }
        #endregion

        #region Register Metadata
        private struct Register
        {
            public DateTime ExpiryTimeUtc { get; }

            public byte[] Value { get; }

            public Register(byte[] value, DateTime? expiryTimeUtc = null)
            {
                ExpiryTimeUtc = expiryTimeUtc ?? DateTime.MaxValue;
                Value = value;
            }

            public void Serialize(BuildXLWriter writer)
            {
                writer.Write(ExpiryTimeUtc);
                writer.WriteNullableByteArray(Value);
            }

            public static Register Deserialize(BuildXLReader reader)
            {
                var expiryTimeUtc = reader.ReadDateTime();
                var value = reader.ReadNullableByteArray();
                return new Register(value, expiryTimeUtc);
            }

            public bool HasExpired(DateTime now)
            {
                return ExpiryTimeUtc <= now;
            }
        }

        private byte[] SerializeRegister(Register register)
        {
            return _serializationPool.Serialize(register, (m, w) => m.Serialize(w));
        }

        private Register DeserializeRegister(byte[] data)
        {
            // WARNING: Do NOT transform this to a method group
            return _serializationPool.Deserialize(data, r => Register.Deserialize(r));
        }
        #endregion
    }
}
