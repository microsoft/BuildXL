using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.Roxis.Client;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <nodoc />
    public class RoxisMemoizationDatabase : MemoizationDatabase
    {
        private readonly SerializationPool _serializationPool = new SerializationPool();

        private readonly RoxisMemoizationDatabaseConfiguration _configuration;
        private readonly IRoxisClient _client;
        private readonly IClock _clock;

        private static readonly byte[] ReplacementTokenPrefix = Encoding.UTF8.GetBytes("R$");
        private static readonly byte[] LastAccessKeyPrefix = Encoding.UTF8.GetBytes("L$");

        private static readonly string DefaultReplacementToken = string.Empty;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(RoxisMemoizationDatabase));

        /// <nodoc />
        public RoxisMemoizationDatabase(RoxisMemoizationDatabaseConfiguration configuration, IRoxisClient client, IClock clock)
        {
            _configuration = configuration;
            _client = client;
            _clock = clock;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (_client is IStartupShutdownSlim client)
            {
                return client.StartupAsync(context);
            }
            else
            {
                return BoolResult.SuccessTask;
            }

        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // TODO: remove is
            if (_client is IStartupShutdownSlim client)
            {
                return client.ShutdownAsync(context);
            }
            else
            {
                return BoolResult.SuccessTask;
            }
        }

        /// <inheritdoc />
        public override Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotImplementedException("Enumerating all strong fingerprints is not supported");
        }

        private ByteString GetReplacementTokenKey(byte[] strongFingerprintKey)
        {
            // TODO: switch to block copy https://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
            return ReplacementTokenPrefix.Concat(strongFingerprintKey).ToArray();
        }

        private ByteString GetLastAccessKey(byte[] key)
        {
            // TODO: switch to block copy https://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
            return LastAccessKeyPrefix.Concat(key).ToArray();
        }

        /// <inheritdoc />
        protected override async Task<Result<bool>> CompareExchangeCore(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            var strongFingerprintKey = Serialize(strongFingerprint);
            var replacementTokenKey = GetReplacementTokenKey(strongFingerprintKey);

            // Always create a new unique replacement token. Notice here that we convert ToString instead of
            // ToByteArray. We need to keep this symmetric, otherwise we risk CompareExchange bugs.
            var replacementToken = Guid.NewGuid().ToString();

            var now = _clock.UtcNow;
            ByteString? comparand = expectedReplacementToken.Equals(DefaultReplacementToken, StringComparison.Ordinal) ? null : (ByteString?)expectedReplacementToken;
            // TODO: we'd really like to serialize all at once to avoid allocating so much
            var request = new CommandRequest(new Command[] {
                new CompareExchangeCommand() {
                    Key = strongFingerprintKey,
                    Value = Serialize(replacement),
                    CompareKey = replacementTokenKey,
                    CompareKeyValue = replacementToken,
                    Comparand = comparand,
                    ExpiryTimeUtc = ComputeDefaultTTL(now),
                },
                GetUpdateLastAccessCommand(strongFingerprintKey, now)
            });

            var serverResponse = await _client.ExecuteAsync(context, request);
            if (!serverResponse)
            {
                return new Result<bool>(serverResponse);
            }

            var response = serverResponse.Value!;
            var result = response.Results[0] as CompareExchangeResult;
            Contract.AssertNotNull(result);

            return result.Exchanged;
        }

        private SetCommand GetUpdateLastAccessCommand(byte[] strongFingerprintKey, DateTime now)
        {
            return new SetCommand()
            {
                ExpiryTimeUtc = ComputeDefaultTTL(now),
                Key = GetLastAccessKey(strongFingerprintKey),
                Value = now.ToFileTimeUtc(),
                Overwrite = true,
            };
        }

        private DateTime? ComputeDefaultTTL(DateTime now) => _configuration.DefaultTimeToLive == null ? null : now + _configuration.DefaultTimeToLive;

        /// <inheritdoc />
        protected override async Task<ContentHashListResult> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            var strongFingerprintKey = Serialize(strongFingerprint);
            var replacementTokenKey = GetReplacementTokenKey(strongFingerprintKey);

            var request = new CommandRequest(new Command[] {
                new GetCommand()
                {
                    Key = strongFingerprintKey,
                },
                new GetCommand()
                {
                    Key = replacementTokenKey,
                },
                GetUpdateLastAccessCommand(strongFingerprintKey, _clock.UtcNow),
            });

            var serverResponse = await _client.ExecuteAsync(context, request);
            if (!serverResponse)
            {
                return new ContentHashListResult(serverResponse);
            }

            var response = serverResponse.Value!;
            var getKeyResult = response.Results[0] as GetResult;
            Contract.Assert(getKeyResult != null);

            var getReplacementTokenResult = response.Results[1] as GetResult;
            Contract.Assert(getReplacementTokenResult != null);

            var replacementToken = getReplacementTokenResult.Value?.ToString() ?? DefaultReplacementToken;
            
            if (getKeyResult.Value == null)
            {
                return new ContentHashListResult(
                        new ContentHashListWithDeterminism(null, CacheDeterminism.None),
                        replacementToken
                    );
            }

            return new ContentHashListResult(
                    DeserializeContentHashListWithDeterminism(getKeyResult.Value),
                    replacementToken
                );
        }

        /// <inheritdoc />
        protected override async Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var lastAccessTimeKey = GetLastAccessKey(Serialize(weakFingerprint));
            var request = new CommandRequest(new[] {
                new PrefixEnumerateCommand()
                {
                    Key = lastAccessTimeKey,
                }
            });

            var serverResponse = await _client.ExecuteAsync(context, request);
            if (!serverResponse)
            {
                return new Result<LevelSelectors>(serverResponse);
            }

            var response = serverResponse.Value!;
            var result = response.Results[0] as PrefixEnumerateResult;
            Contract.AssertNotNull(result);

            var selectors = new List<(long TimeUtc, Selector Selector)>();
            foreach (var pair in result.Pairs)
            {
                var selector = DeserializeStrongFingerprint(pair.Key.Value.Skip(LastAccessKeyPrefix.Length).ToArray()).Selector;
                var lastAccessTimeAsFileTimeUtc = BitConverter.ToInt64((byte[])pair.Value, 0);
                selectors.Add((lastAccessTimeAsFileTimeUtc, selector));
            }

            return new Result<LevelSelectors>(new LevelSelectors(selectors
                .OrderByDescending(entry => entry.TimeUtc)
                .Select(entry => entry.Selector)
                .ToList(), hasMore: false));
        }

        #region Serialization / Deserialization
        private byte[] Serialize(StrongFingerprint strongFingerprint)
        {
            return _serializationPool.Serialize(strongFingerprint, (value, writer) => value.Serialize(writer));
        }

        private byte[] Serialize(ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            return _serializationPool.Serialize(contentHashListWithDeterminism, (value, writer) => value.Serialize(writer));
        }

        private byte[] Serialize(Fingerprint fingerprint)
        {
            return _serializationPool.Serialize(fingerprint, (value, writer) => value.Serialize(writer));
        }

        private StrongFingerprint DeserializeStrongFingerprint(byte[] data)
        {
            return _serializationPool.Deserialize(data, r => StrongFingerprint.Deserialize(r));
        }

        private ContentHashListWithDeterminism DeserializeContentHashListWithDeterminism(byte[] data)
        {
            return _serializationPool.Deserialize(data, r => ContentHashListWithDeterminism.Deserialize(r));
        }
        #endregion
    }
}
