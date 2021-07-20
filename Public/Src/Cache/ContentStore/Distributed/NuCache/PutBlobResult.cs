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
        private readonly ShortHash? _hash;

        private readonly long? _blobSize;
        private readonly long? _newRedisCapacity; 

        private readonly bool _alreadyInRedis;

        private readonly string? _redisKey;
        private readonly string? _extraMsg;

        private readonly SkipReason _skipReason;

        internal enum SkipReason
        {
            NotSkipped,
            OutOfCapacity,
            Throttled,
        }

        /// <nodoc />
        internal PutBlobResult(
            ShortHash? hash = null,
            long? blobSize = null,
            bool alreadyInRedis = false,
            long? newRedisCapacity = null,
            string? redisKey = null,
            SkipReason skipReason = SkipReason.NotSkipped,
            string? extraMsg = null)
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
        public PutBlobResult(ShortHash? hash, long? blobSize, string errorMessage)
            : base(errorMessage)
        {
            _hash = hash;
            _blobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ShortHash? hash, long? blobSize, ResultBase other, string message)
            : base(other, message)
        {
            _hash = hash;
            _blobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ResultBase other, ShortHash hash, long blobSize)
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
        public static PutBlobResult OutOfCapacity(ShortHash hash, long blobSize, string redisKey, string? extraMsg = null)
        {
            return new PutBlobResult(hash, blobSize, redisKey: redisKey, skipReason: SkipReason.OutOfCapacity, extraMsg: extraMsg);
        }

        /// <nodoc />
        public static PutBlobResult Throttled(ShortHash hash, long blobSize, string redisKey, string extraMsg)
        {
            Contract.RequiresNotNullOrEmpty(extraMsg);
            return new PutBlobResult(hash, blobSize, redisKey: redisKey, skipReason: SkipReason.Throttled, extraMsg: extraMsg);
        }

        /// <nodoc />
        public static PutBlobResult RedisHasAlready(ShortHash hash, long blobSize, string redisKey)
        {
            return new PutBlobResult(hash, blobSize, redisKey: redisKey, alreadyInRedis: true);
        }

        /// <nodoc />
        public static PutBlobResult NewRedisEntry(ShortHash hash, long blobSize, string redisKey, long newCapacity)
        {
            return new PutBlobResult(hash, blobSize, newRedisCapacity: newCapacity, alreadyInRedis: false, redisKey: redisKey);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string baseResult = $"Hash=[{_hash}], BlobSize=[{_blobSize}], RedisKey=[{_redisKey}]";
            if (Succeeded)
            {
                return $"{baseResult}. AlreadyInRedis=[{_alreadyInRedis}], SkipReason=[{_skipReason}] NewCapacity=[{_newRedisCapacity}]. {_extraMsg}";
            }

            return $"{baseResult}. {ErrorMessage}";
        }
    }
}
