// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using BuildXL.Engine.Cache.Fingerprints;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Represents old bond types that have internal members representing new bond types
    /// that can be adapted into old bond types
    /// </summary>
    public interface IBondAdaptable
    {
        /// <summary>
        /// Adapts new bond values into old bond blobs
        /// </summary>
        /// <param name="bufferProvider">the buffer provider to use for serialization</param>
        void Adapt(IBufferProvider bufferProvider);
    }

    /// <summary>
    /// A old bond type wrapper for a new
    /// </summary>
    /// <typeparam name="TValue">the old bond value type</typeparam>
    public interface IBondWrapper<TValue>
    {
        /// <summary>
        /// The old bond value
        /// </summary>
        TValue Value { get; set; }
    }

    public partial class BuildStartData : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionPipGraphCacheDescriptor, PipGraphCacheDescriptor>(CachedGraphDescriptor, bufferProvider);
            DistributionTypeExtension.Adapt<DistributionContentHash, BondContentHash>(SymlinkFileContentHash, bufferProvider);
        }

        #endregion
    }

    public partial class FileArtifactKeyedHash : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionContentHash, BondContentHash>(ContentHash, bufferProvider);
        }

        #endregion
    }

    public partial class PipCompletionData : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
        }

        #endregion
    }

    public partial class SinglePipBuildRequest : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionCacheFingerprint, BondFingerprint>(Fingerprint, bufferProvider);
        }

        #endregion
    }

    public partial class PipBuildRequest : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt(Pips, bufferProvider);
            DistributionTypeExtension.Adapt(Hashes, bufferProvider);
        }

        #endregion
    }

    public partial class AttachCompletionInfo : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionContentHash, BondContentHash>(WorkerCacheValidationContentHash, bufferProvider);
        }

        #endregion
    }

    public partial class DataWrapper : IBondAdaptable
    {
        /// <nodoc/>
        public virtual void Adapt(IBufferProvider bufferProvider)
        {
        }
    }

    public partial class DistributionPipGraphCacheDescriptor : IBondWrapper<PipGraphCacheDescriptor>
    {
        /// <nodoc/>
        public PipGraphCacheDescriptor Value { get; set; }

        /// <nodoc/>
        public override void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionPipGraphCacheDescriptor, PipGraphCacheDescriptor>(this, bufferProvider);
        }
    }

    public partial class DistributionContentHash : IBondWrapper<BondContentHash>
    {
        /// <nodoc/>
        public BondContentHash Value { get; set; }

        /// <nodoc/>
        public override void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionContentHash, BondContentHash>(this, bufferProvider);
        }
    }

    public partial class DistributionCacheFingerprint : IBondWrapper<BondFingerprint>
    {
        /// <nodoc/>
        public BondFingerprint Value { get; set; }

        /// <nodoc/>
        public override void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt<DistributionCacheFingerprint, BondFingerprint>(this, bufferProvider);
        }
    }

    public partial class SinglePipBuildRequest
    {
        /// <summary>
        /// For tracing purposes only. Not transferred as a part of Bond call.
        /// </summary>
        internal long SemiStableHash { get; set; }
    }

    public partial class WorkerNotificationArgs : IBondAdaptable
    {
        #region IBondAdaptable Members

        /// <nodoc/>
        public void Adapt(IBufferProvider bufferProvider)
        {
            DistributionTypeExtension.Adapt(CompletedPips, bufferProvider);
        }

        #endregion
    }
}
#endif
