// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Bond;
using Bond.IO.Safe;
using Bond.Protocols;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Native.IO;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <nodoc/>
    public static class DistributionTypeExtension
    {
        /// <nodoc/>
        public static DistributionPipGraphCacheDescriptor ToDistributionPipGraphCacheDescriptor(this PipGraphCacheDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return null;
            }

            return new DistributionPipGraphCacheDescriptor
            {
                Value = descriptor,
            };
        }

        /// <nodoc/>
        public static PipGraphCacheDescriptor ToPipGraphCacheDescriptor(this DistributionPipGraphCacheDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return null;
            }

            return descriptor.Value ?? FromBytes<PipGraphCacheDescriptor>(descriptor.Data.Data);
        }

        /// <nodoc/>
        public static DistributionContentHash ToDistributedContentHash(this BondContentHash hash)
        {
            if (hash == null)
            {
                return null;
            }

            return new DistributionContentHash
            {
                Value = hash,
            };
        }

        /// <nodoc/>
        public static BondContentHash ToBondContentHash(this DistributionContentHash hash)
        {
            if (hash == null)
            {
                return null;
            }

            return hash.Value ?? FromBytes<BondContentHash>(hash.Data.Data);
        }

        /// <nodoc/>
        public static DistributionCacheFingerprint ToDistributionCacheFingerprint(this BondFingerprint fingerprint)
        {
            if (fingerprint == null)
            {
                return null;
            }

            return new DistributionCacheFingerprint
            {
                Value = fingerprint,
            };
        }

        /// <nodoc/>
        public static BondFingerprint ToCacheFingerprint(this DistributionCacheFingerprint fingerprint)
        {
            if (fingerprint == null)
            {
                return null;
            }

            return fingerprint.Value ?? FromBytes<BondFingerprint>(fingerprint.Data.Data);
        }

        /// <summary>
        /// Changes a old bond wrapper for a new bond type to represent its new bond value
        /// as blob data
        /// </summary>
        /// <typeparam name="TWrapper">the wrapper (an old bond type)</typeparam>
        /// <typeparam name="TValue">the wrapped value type (an new bond type)</typeparam>
        /// <param name="wrapper">the wrapper for the new bond type</param>
        /// <param name="bufferProvider">the buffer provider which provides buffers used during serialization</param>
        public static void Adapt<TWrapper, TValue>(TWrapper wrapper, IBufferProvider bufferProvider)
            where TWrapper : DataWrapper, IBondWrapper<TValue>
        {
            if (wrapper != null && wrapper.Value != null && (wrapper.Data == null || wrapper.Data.Count == 0))
            {
                wrapper.Data = ToBlob(wrapper.Value, bufferProvider);
            }
        }

        /// <summary>
        /// Changes a list of old bond wrappers for an new bond type to represent their new bond values
        /// as blob data
        /// </summary>
        /// <typeparam name="TWrapper">the wrapper (an old bond type)</typeparam>
        /// <typeparam name="TValue">the wrapped value type (an new bond type)</typeparam>
        /// <param name="list">the list of wrappers for the new bond type</param>
        /// <param name="bufferProvider">the buffer provider which provides buffers used during serialization</param>
        public static void Adapt<TWrapper, TValue>(List<TWrapper> list, IBufferProvider bufferProvider)
            where TWrapper : DataWrapper, IBondWrapper<TValue>
        {
            if (list != null)
            {
                foreach (var item in list)
                {
                    Adapt<TWrapper, TValue>(item, bufferProvider);
                }
            }
        }

        /// <summary>
        /// Changes a list of old bond wrappers for a new bond type to represent their new bond values
        /// as blob data
        /// </summary>
        /// <typeparam name="TAdaptable">the wrapper (an old bond type)</typeparam>
        /// <param name="list">the list of wrappers for the new bond type</param>
        /// <param name="bufferProvider">the buffer provider which provides buffers used during serialization</param>
        public static void Adapt<TAdaptable>(List<TAdaptable> list, IBufferProvider bufferProvider)
            where TAdaptable : IBondAdaptable
        {
            if (list != null)
            {
                foreach (var item in list)
                {
                    item.Adapt(bufferProvider);
                }
            }
        }

        /// <summary>
        /// Serializes a new bond value to an old bond blob
        /// </summary>
        /// <typeparam name="T">the type of the value</typeparam>
        /// <param name="value">the new bond value</param>
        /// <param name="bufferProvider">the buffer provider which provides buffers used during serialization</param>
        /// <returns>the old bond blow representing the new bond value</returns>
        public static Microsoft.Bond.BondBlob ToBlob<T>(T value, IBufferProvider bufferProvider)
        {
            OutputBuffer buffer = bufferProvider.GetOutputBuffer();
            var startPosition = buffer.Position;
            CompactBinaryWriter<OutputBuffer> writer = new CompactBinaryWriter<OutputBuffer>(buffer);
            Serialize.To(writer, value);
            bufferProvider.ReleaseOutputBuffer();
            return new Microsoft.Bond.BondBlob(buffer.Data.Array, (int)startPosition, (int)(buffer.Position - startPosition));
        }

        /// <summary>
        /// Deserializes an old bond blob to its new bond value representation
        /// </summary>
        /// <typeparam name="T">the type of the value</typeparam>
        /// <returns>the new bond value</returns>
        public static T FromBytes<T>(ArraySegment<byte> bytes)
        {
            InputBuffer buffer = new InputBuffer(bytes);
            Bond.Protocols.CompactBinaryReader<InputBuffer> reader = new CompactBinaryReader<InputBuffer>(buffer);
            return Bond.Deserialize<T>.From(reader);
        }

        /// <nodoc />
        public static BondReparsePointType ToBondReparsePointType(this ReparsePointType reparsePointType)
        {
            switch (reparsePointType)
            {
                case ReparsePointType.None:
                    return BondReparsePointType.None;
                case ReparsePointType.SymLink:
                    return BondReparsePointType.SymLink;
                case ReparsePointType.MountPoint:
                    return BondReparsePointType.MountPoint;
                case ReparsePointType.NonActionable:
                    return BondReparsePointType.NonActionable;
                default:
                    throw Contract.AssertFailure("Cannot convert ReparsePointType to BondReparsePointType");
            }
        }

        /// <nodoc />
        public static ReparsePointType ToReparsePointType(this BondReparsePointType reparsePointType)
        {
            switch (reparsePointType)
            {
                case BondReparsePointType.None:
                    return ReparsePointType.None;
                case BondReparsePointType.SymLink:
                    return ReparsePointType.SymLink;
                case BondReparsePointType.MountPoint:
                    return ReparsePointType.MountPoint;
                case BondReparsePointType.NonActionable:
                    return ReparsePointType.NonActionable;
                default:
                    throw Contract.AssertFailure("Cannot convert BondReparsePointType to ReparsePointType");
            }
        }
    }
}
#endif