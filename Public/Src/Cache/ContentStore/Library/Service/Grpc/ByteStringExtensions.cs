// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using Google.Protobuf;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Provides helper methods for constructing <see cref="ByteString"/> instances.
    /// </summary>
    public static class ByteStringExtensions
    {
        private static readonly Lazy<Func<byte[], ByteString>> UnsafeFromBytesFunc = new Lazy<Func<byte[], ByteString>>(() => CreateUnsafeFromBytesFunc());

        /// <summary>
        /// Creates <see cref="ByteString"/> without copying a given <paramref name="buffer"/>.
        /// </summary>
        /// <remarks>
        /// This method is unsafe and can be used only when the buffer's ownership can be "transferred" to <see cref="ByteString"/>, for instance,
        /// the method can be used when the <see cref="ByteString"/> is transient.
        /// </remarks>
        public static ByteString UnsafeCreateFromBytes(byte[] buffer)
        {
            return UnsafeFromBytesFunc.Value(buffer);
        }

        private static Func<byte[], ByteString> CreateUnsafeFromBytesFunc()
        {
            var internalType = typeof(ByteString).GetNestedType("Unsafe", BindingFlags.NonPublic);
            Contract.Assert(internalType != null, "Can't find ByteString.Unsafe type.");
            var method = internalType.GetMethod("FromBytes", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException($"Can't find method Unsafe.FromBytes in ByteString class.");
            }

            return (Func<byte[], ByteString>)Delegate.CreateDelegate(typeof(Func<byte[], ByteString>), method);
        }
    }
}
