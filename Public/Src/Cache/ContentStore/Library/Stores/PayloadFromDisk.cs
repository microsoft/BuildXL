﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// This class is specifically for usage within <see cref="ContentDirectorySnapshot{T}"/>. It is a reference type on purpose, in order to avoid issues
    /// with the maximum object size limit.
    /// </summary>
    /// <typeparam name="T">Type tagged with a hash</typeparam>
    public class PayloadFromDisk<T> : IComparable<PayloadFromDisk<T>>
    {
        /// <summary>
        /// Hash for the <see cref="Payload"/>
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        /// Information for which <see cref="Hash"/> applies
        /// </summary>
        public T Payload { get; }

        /// <nodoc />
        public PayloadFromDisk(ContentHash hash, T payload)
        {
            Hash = hash;
            Payload = payload;
        }

        /// <inheritdoc />
        public int CompareTo(PayloadFromDisk<T> other)
        {
            return Hash.CompareTo(other.Hash);
        }
    }
}
