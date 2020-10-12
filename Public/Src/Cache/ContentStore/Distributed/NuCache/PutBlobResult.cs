// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <nodoc />
    public class PutBlobResult : BoolResult
    {
        private readonly ContentHash _hash;

        private readonly long _blobSize;
        private readonly long? _newRedisCapacity; 

        private readonly bool _alreadyInRedis;

        private readonly string? _redisKey;
        private readonly string? _extraMsg;

        private readonly SkipReason _skipReason;

        private enum SkipReason
        {
            NotSkipped,
            OutOfCapacity,
            Throttled,
        }

        /// <nodoc />
        private PutBlobResult(ContentHash hash, long blobSize, bool alreadyInRedis = false, long? newRedisCapacity = null, string? redisKey = null, SkipReason skipReason = SkipReason.NotSkipped, string? extraMsg = null)
            : base(succeeded: true)
        {
            _hash = hash;
            _blobSize = blobSize;
            _alreadyInRedis = alreadyInRedis;
            _newRedisCapacity = newRedisCapacity;
            _redisKey = redisKey;
            _extraMsg = extraMsg;
            _skipReason = skipReason;
        }

        /// <nodoc />
        public PutBlobResult(ContentHash hash, long blobSize, string errorMessage)
            : base(errorMessage)
        {
            _hash = hash;
            _blobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ResultBase other, string message, ContentHash hash, long blobSize)
            : base(other, message)
        {
            _hash = hash;
            _blobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ResultBase other, ContentHash hash, long blobSize)
            : base(other)
        {
            _hash = hash;
            _blobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <nodoc />
        public static PutBlobResult OutOfCapacity(ContentHash hash, long blobSize, string redisKey, string? extraMsg = null)
        {
            return new PutBlobResult(hash, blobSize, redisKey: redisKey, skipReason: SkipReason.OutOfCapacity, extraMsg: extraMsg);
        }

        /// <nodoc />
        public static PutBlobResult Throttled(ContentHash hash, long blobSize, string redisKey, string extraMsg)
        {
            Contract.RequiresNotNullOrEmpty(extraMsg);
            return new PutBlobResult(hash, blobSize, redisKey: redisKey, skipReason: SkipReason.Throttled, extraMsg: extraMsg);
        }

        /// <nodoc />
        public static PutBlobResult RedisHasAlready(ContentHash hash, long blobSize, string redisKey) => new PutBlobResult(hash, blobSize, redisKey: redisKey, alreadyInRedis: true);

        /// <nodoc />
        public static PutBlobResult NewRedisEntry(ContentHash hash, long blobSize, long newCapacity, string redisKey) => new PutBlobResult(hash, blobSize, newRedisCapacity: newCapacity, alreadyInRedis: false, redisKey: redisKey);

        /// <inheritdoc />
        public override string ToString()
        {
            string baseResult = $"Hash=[{_hash.ToShortString()}], BlobSize=[{_blobSize}], RedisKey=[{_redisKey}]";
            if (Succeeded)
            {
                return $"{baseResult}. AlreadyInRedis=[{_alreadyInRedis}], SkipReason=[{_skipReason}] NewCapacity=[{_newRedisCapacity}]. {_extraMsg}";
            }

            return $"{baseResult}. {ErrorMessage}";
        }
    }
}
