// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Token for exclusive access to a HashAlgorithm.
    /// </summary>
    public readonly struct HasherToken : IEquatable<HasherToken>, IDisposable
    {
        private readonly Pool<HashAlgorithm>.PoolHandle _poolHandle;

        /// <summary>
        ///     Gets hash algorithm this token uses.
        /// </summary>
        public HashAlgorithm Hasher { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="HasherToken" /> struct.
        /// </summary>
        /// <remarks>
        ///     When the token is disposed, the hasher will be added back to the object pool.
        /// </remarks>
        public HasherToken(Pool<HashAlgorithm>.PoolHandle pooledHasher)
        {
            Contract.Requires(pooledHasher.Value != null);

            Hasher = pooledHasher.Value;
            _poolHandle = pooledHasher;

        }

        /// <inheritdoc />
        public void Dispose()
        {
            Hasher.Initialize();
            _poolHandle.Dispose();
        }

        /// <inheritdoc />
        public bool Equals(HasherToken other)
        {
            return Hasher == other.Hasher;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is HasherToken)
            {
                return Equals((HasherToken)obj);
            }

            return false;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Hasher.GetHashCode();
        }

        /// <summary>
        ///     Equality operator.
        /// </summary>
        public static bool operator ==(HasherToken left, HasherToken right)
        {
            return left.Equals(right);
        }

        /// <summary>
        ///     Inequality operator.
        /// </summary>
        public static bool operator !=(HasherToken left, HasherToken right)
        {
            return !left.Equals(right);
        }
    }
}
