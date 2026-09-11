// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core.Qualifier;

namespace BuildXL.Pips
{
    /// <summary>
    /// Mutable pip state
    /// </summary>
    /// <remarks>
    /// While a <code>Pip</code> is strictly immutable, all mutable information associated with a Pip goes here.
    /// (This class also holds some immutable information that is often needed to identify a Pip,
    /// in particular <code>NodeIdValue</code>, <code>SemiStableHash</code>.)
    /// </remarks>
    internal class MutablePipState
    {
        /// <summary>
        /// Identifier of this pip that is stable across BuildXL runs with an identical schedule
        /// </summary>
        /// <remarks>
        /// This identifier is not necessarily unique, but should be quite unique in practice.
        /// This property is equivalent to <see cref="Pip.SemiStableHash"/> for the pip represented, and therefore immutable.
        /// </remarks>
        internal long SemiStableHash { get; }

        /// <summary>
        /// Associated PageableStoreId for the mutable. Used to retrieve the corresponding Pip
        /// </summary>
        internal PageableStoreId StoreId;

        private WeakReference<Pip> m_weakPip;

        internal readonly PipType PipType;
        /// <summary>
        /// /// Constructor used for deserialization
        /// </summary>
        protected MutablePipState(PipType piptype, long semiStableHash, PageableStoreId storeId)
        {
            PipType = piptype;
            SemiStableHash = semiStableHash;
            StoreId = storeId;
        }

        /// <summary>
        /// Creates a new mutable from a live pip
        /// </summary>
        public static MutablePipState Create(Pip pip)
        {
            Contract.Requires(pip != null);
            Contract.Assert(PipState.Ignored == 0);

            MutablePipState mutable;

            switch (pip.PipType)
            {
                case PipType.Ipc:
                    var pipAsIpc = (IpcPip)pip;
                    var serviceKind =
                        pipAsIpc.IsServiceFinalization ? ServicePipKind.ServiceFinalization :
                        pipAsIpc.ServicePipDependencies.Any() ? ServicePipKind.ServiceClient :
                        ServicePipKind.None;
                    var serviceInfo = new ServiceInfo(serviceKind, pipAsIpc.ServicePipDependencies, monikerId: pipAsIpc.IpcInfo.IpcMonikerId);
                    mutable = new ProcessMutablePipState(
                        pip.PipType,
                        pip.SemiStableHash,
                        default(PageableStoreId),
                        serviceInfo,
                        Process.Options.IsLight,
                        default(RewritePolicy),
                        AbsolutePath.Invalid,
                        Process.MinPriority,
                        pip.Provenance.ModuleId,
                        StringId.Invalid,
                        pip.Provenance.QualifierId,
                        Process.MinWeight,
                        0,
                        0,
                        0,
                        0);
                    break;
                case PipType.Process:
                    var pipAsProcess = (Process)pip;
                    mutable = new ProcessMutablePipState(
                        pip.PipType, 
                        pip.SemiStableHash, 
                        default(PageableStoreId), 
                        pipAsProcess.ServiceInfo, 
                        pipAsProcess.ProcessOptions, 
                        pipAsProcess.RewritePolicy, 
                        pipAsProcess.Executable.Path, 
                        pipAsProcess.Priority,
                        pipAsProcess.Provenance.ModuleId,
                        pipAsProcess.ToolDescription,
                        pipAsProcess.Provenance.QualifierId,
                        pipAsProcess.Weight,
                        pipAsProcess.Dependencies.Length,
                        pipAsProcess.DirectoryDependencies.Length,
                        pipAsProcess.FileOutputs.Length,
                        pipAsProcess.DirectoryOutputs.Length,
                        preserveOutputsTrustLevel: pipAsProcess.PreserveOutputsTrustLevel,
                        isSucceedFast: pipAsProcess.SucceedFastExitCodes.Length > 0);
                    break;
                case PipType.CopyFile:
                    var pipAsCopy = (CopyFile)pip;
                    mutable = new CopyMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), pipAsCopy.OutputsMustRemainWritable);
                    break;
                case PipType.SealDirectory:
                    var seal = (SealDirectory)pip;
                    
                    mutable = new SealDirectoryMutablePipState(
                        pip.PipType,
                        pip.SemiStableHash,
                        default(PageableStoreId),
                        seal.DirectoryRoot,
                        seal.Kind,
                        seal.Patterns,
                        seal.IsComposite,
                        seal.Scrub,
                        seal.ContentFilter);
                    break;
                default:
                    mutable = new MutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId));
                    break;
            }

            mutable.m_weakPip = new WeakReference<Pip>(pip);
            return mutable;
        }

        /// <summary>
        /// Serializes
        /// </summary>
        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write((byte)PipType);
            writer.Write(SemiStableHash);
            StoreId.Serialize(writer);
            SpecializedSerialize(writer);
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        internal static MutablePipState Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var pipType = (PipType)reader.ReadByte();
            var semiStableHash = reader.ReadInt64();
            var storeId = PageableStoreId.Deserialize(reader);

            switch (pipType)
            {
                case PipType.Ipc:
                case PipType.Process:
                    return ProcessMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId);
                case PipType.CopyFile:
                    return CopyMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId);
                case PipType.SealDirectory:
                    return SealDirectoryMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId);
                default:
                    return new MutablePipState(pipType, semiStableHash, storeId);
            }
        }

        /// <summary>
        /// Implemented by derived classes that need custom deserialization
        /// </summary>
        protected virtual void SpecializedSerialize(BuildXLWriter writer)
        {
        }

        /// <summary>
        /// Checks if pip outputs must remain writable.
        /// </summary>
        /// <returns></returns>
        public virtual bool MustOutputsRemainWritable() => false;

        /// <summary>
        /// Checks if pip outputs must be preserved.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsPreservedOutputsPip() => false;

        /// <summary>
        /// Checks if pip runs tool with incremental capability.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsIncrementalTool() => false;

        /// <summary>
        /// Checks if pip using a non-empty preserveOutputAllowlist
        /// </summary>
        /// <returns></returns>
        public virtual bool HasPreserveOutputAllowlist() => false;

        /// <summary>
        /// Get pip preserve outputs trust level
        /// </summary>
        /// <returns></returns>
        public virtual int GetProcessPreserveOutputsTrustLevel() => 0;

        internal bool IsAlive
        {
            get
            {
                if (m_weakPip == null)
                {
                    return false;
                }

                Pip pip;
                return m_weakPip.TryGetTarget(out pip);
            }
        }

        internal Pip InternalGetOrSetPip<TTable>(TTable table, PipId pipId, PipQueryContext context, Func<TTable, PipId, PageableStoreId, PipQueryContext, Pip> creator)
        {
            lock (this)
            {
                Pip pip;
                if (m_weakPip == null)
                {
                    pip = creator(table, pipId, StoreId, context);
                    m_weakPip = new WeakReference<Pip>(pip);
                }
                else if (!m_weakPip.TryGetTarget(out pip))
                {
                    m_weakPip.SetTarget(pip = creator(table, pipId, StoreId, context));
                }

                return pip;
            }
        }
    }

    /// <summary>
    /// Mutable pip state for Process pips.
    /// </summary>
    internal sealed class ProcessMutablePipState : MutablePipState
    {
        internal readonly ServiceInfo ServiceInfo;
        internal readonly Process.Options ProcessOptions;
        internal readonly int Priority;
        internal readonly int PreserveOutputTrustLevel;
        internal readonly RewritePolicy RewritePolicy;
        internal readonly AbsolutePath ExecutablePath;
        internal readonly ModuleId ModuleId;
        internal readonly StringId ToolDescription;
        internal readonly QualifierId QualifierId;
        internal readonly int Weight;
        internal readonly int NumFileDependencies;
        internal readonly int NumDirectoryDependencies;
        internal readonly int NumFileOutputs;
        internal readonly int NumDirectoryOutputs;
        internal readonly bool IsSucceedFast;

        internal ProcessMutablePipState(
            PipType pipType,
            long semiStableHash,
            PageableStoreId storeId,
            ServiceInfo serviceInfo,
            Process.Options processOptions,
            RewritePolicy rewritePolicy,
            AbsolutePath executablePath,
            int priority,
            ModuleId moduleId,
            StringId toolDescription,
            QualifierId qualifierId,
            int weight,
            int numFileDependencies,
            int numDirectoryDependencies,
            int numFileOutputs,
            int numDirectoryOutputs,
            int preserveOutputsTrustLevel = 0,
            bool isSucceedFast = false)
            : base(pipType, semiStableHash, storeId)
        {
            ServiceInfo = serviceInfo;
            ProcessOptions = processOptions;
            RewritePolicy = rewritePolicy;
            ExecutablePath = executablePath;
            Priority = priority;
            PreserveOutputTrustLevel = preserveOutputsTrustLevel;
            ModuleId = moduleId;
            ToolDescription = toolDescription;
            QualifierId = qualifierId;
            Weight = weight;
            NumFileDependencies = numFileDependencies;
            NumDirectoryDependencies = numDirectoryDependencies;
            NumFileOutputs = numFileOutputs;
            NumDirectoryOutputs = numDirectoryOutputs;
            IsSucceedFast = isSucceedFast;
        }

        /// <summary>
        /// Shortcut for whether this is a start or shutdown pip
        /// </summary>
        internal bool IsStartOrShutdown
        {
            get
            {
                return ServiceInfo != null && ServiceInfo.Kind.IsStartOrShutdown();
            }
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(ServiceInfo, ServiceInfo.InternalSerialize);
            writer.WriteCompact((int)ProcessOptions);
            writer.Write((byte)RewritePolicy);
            writer.Write(ExecutablePath);
            writer.WriteCompact(Priority);
            writer.WriteCompact(PreserveOutputTrustLevel);
            writer.Write(ModuleId);
            writer.Write(ToolDescription);
            writer.WriteCompact(QualifierId.Id);
            writer.Write(Weight);
            writer.Write(NumFileDependencies);
            writer.Write(NumDirectoryDependencies);
            writer.Write(NumFileOutputs);
            writer.Write(NumDirectoryOutputs);
            writer.Write(IsSucceedFast);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId)
        {
            ServiceInfo serviceInfo = reader.ReadNullable(ServiceInfo.InternalDeserialize);
            int options = reader.ReadInt32Compact();
            RewritePolicy rewritePolicy = (RewritePolicy) reader.ReadByte();
            AbsolutePath executablePath = reader.ReadAbsolutePath();
            int priority = reader.ReadInt32Compact();
            int preserveOutputTrustLevel = reader.ReadInt32Compact();
            ModuleId moduleId = reader.ReadModuleId();
            StringId toolDescription = reader.ReadStringId();
            QualifierId qualifierId = new QualifierId(reader.ReadInt32Compact());
            int weight = reader.ReadInt32();
            int numFileDependencies = reader.ReadInt32();
            int numDirectoryDependencies = reader.ReadInt32();
            int numFileOutputs = reader.ReadInt32();
            int numDirectoryOutputs = reader.ReadInt32();
            bool isSucceedFast = reader.ReadBoolean();

            return new ProcessMutablePipState(
                pipType,
                semiStableHash,
                storeId,
                serviceInfo,
                (Process.Options)options,
                rewritePolicy,
                executablePath,
                priority,
                moduleId,
                toolDescription,
                qualifierId,
                weight,
                numFileDependencies,
                numDirectoryDependencies,
                numFileOutputs,
                numDirectoryOutputs,
                preserveOutputsTrustLevel: preserveOutputTrustLevel,
                isSucceedFast: isSucceedFast);
        }

        public override bool IsPreservedOutputsPip() => (ProcessOptions & Process.Options.AllowPreserveOutputs) != 0;

        public override bool IsIncrementalTool() => (ProcessOptions & Process.Options.IncrementalTool) == Process.Options.IncrementalTool;

        public override bool HasPreserveOutputAllowlist() => (ProcessOptions & Process.Options.HasPreserveOutputAllowlist) != 0;

        public override bool MustOutputsRemainWritable() => (ProcessOptions & Process.Options.OutputsMustRemainWritable) != 0;

        public override int GetProcessPreserveOutputsTrustLevel() => PreserveOutputTrustLevel;
    }

    internal sealed class CopyMutablePipState : MutablePipState
    {
        private readonly bool m_keepOutputWritable;

        internal CopyMutablePipState(
            PipType pipType,
            long semiStableHash,
            PageableStoreId storeId,
            bool keepOutputWritable)
            : base(pipType, semiStableHash, storeId)
        {
            m_keepOutputWritable = keepOutputWritable;
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(m_keepOutputWritable);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId)
        {
            bool keepOutputWritable = reader.ReadBoolean();

            return new CopyMutablePipState(pipType, semiStableHash, storeId, keepOutputWritable);
        }

        public override bool MustOutputsRemainWritable() => m_keepOutputWritable;
    }

    /// <summary>
    /// Mutable pip state for SealDirectory pips.
    /// </summary>
    internal sealed class SealDirectoryMutablePipState : MutablePipState
    {
        /// <summary>
        /// Bit flags packed alongside <see cref="SealDirectoryKind"/> (see <see cref="SealDirectoryKindShift"/>)
        /// into <see cref="m_packedState"/>, replacing what used to be 3 separate fields (byte + 2 bools).
        /// </summary>
        [Flags]
        private enum SealFlags : byte
        {
            None = 0,
            IsComposite = 1 << 0,
            Scrub = 1 << 1,
            HasContentFilter = 1 << 2,
            ContentFilterExclude = 1 << 3,
        }

        // SealDirectoryKind (0-5) only needs 3 bits, so it is packed into the top bits of the same byte
        // that holds the SealFlags above.
        private const int SealDirectoryKindShift = 4;

        internal readonly AbsolutePath DirectoryRoot;
        internal readonly ReadOnlyArray<StringId> Patterns;

        private readonly byte m_packedState;

        /// <summary>
        /// Regex for <see cref="ContentFilter"/>, or <c>null</c> when there is no filter (the common case).
        /// Using a plain nullable reference here (instead of a <see cref="Nullable{T}"/>-wrapped struct) avoids
        /// the extra HasValue/padding overhead that a rarely-populated field would otherwise cost on every
        /// SealDirectory pip.
        /// </summary>
        private readonly string m_contentFilterRegex;

        internal SealDirectoryKind SealDirectoryKind => (SealDirectoryKind)(m_packedState >> SealDirectoryKindShift);

        internal bool IsComposite => (m_packedState & (byte)SealFlags.IsComposite) != 0;

        internal bool Scrub => (m_packedState & (byte)SealFlags.Scrub) != 0;

        internal SealDirectoryContentFilter? ContentFilter =>
            m_contentFilterRegex == null
                ? (SealDirectoryContentFilter?)null
                : new SealDirectoryContentFilter(
                    (m_packedState & (byte)SealFlags.ContentFilterExclude) != 0
                        ? SealDirectoryContentFilter.ContentFilterKind.Exclude
                        : SealDirectoryContentFilter.ContentFilterKind.Include,
                    m_contentFilterRegex);

        public SealDirectoryMutablePipState(
            PipType piptype,
            long semiStableHash,
            PageableStoreId storeId,
            AbsolutePath directoryRoot,
            SealDirectoryKind sealDirectoryKind,
            ReadOnlyArray<StringId> patterns,
            bool isComposite,
            bool scrub,
            SealDirectoryContentFilter? contentFilter)
            : base(piptype, semiStableHash, storeId)
        {
            DirectoryRoot = directoryRoot;
            Patterns = patterns;

            byte packed = (byte)((byte)sealDirectoryKind << SealDirectoryKindShift);
            packed |= isComposite ? (byte)SealFlags.IsComposite : (byte)0;
            packed |= scrub ? (byte)SealFlags.Scrub : (byte)0;

            if (contentFilter != null)
            {
                packed |= (byte)SealFlags.HasContentFilter;
                m_contentFilterRegex = contentFilter.Value.Regex;
                packed |= contentFilter.Value.Kind == SealDirectoryContentFilter.ContentFilterKind.Exclude ? (byte)SealFlags.ContentFilterExclude : (byte)0;
            }

            m_packedState = packed;
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(DirectoryRoot);
            writer.Write(m_packedState);
            writer.Write(Patterns, (w, v) => w.Write(v));
            if ((m_packedState & (byte)SealFlags.HasContentFilter) != 0)
            {
                // Write unconditionally on the same HasContentFilter bit that Deserialize checks, so the two
                // can never disagree about whether a regex string follows on the stream (m_contentFilterRegex
                // is expected to be non-null whenever this bit is set, but guard with ?? to avoid corrupting/
                // misaligning the stream if that invariant is ever violated).
                writer.Write(m_contentFilterRegex ?? string.Empty);
            }
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId)
        {
            var directoryRoot = reader.ReadAbsolutePath();
            var packed = reader.ReadByte();
            var patterns = reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId());

            var sealDirectoryKind = (SealDirectoryKind)(packed >> SealDirectoryKindShift);
            var isComposite = (packed & (byte)SealFlags.IsComposite) != 0;
            var scrub = (packed & (byte)SealFlags.Scrub) != 0;

            SealDirectoryContentFilter? contentFilter = null;
            if ((packed & (byte)SealFlags.HasContentFilter) != 0)
            {
                var kind = (packed & (byte)SealFlags.ContentFilterExclude) != 0
                    ? SealDirectoryContentFilter.ContentFilterKind.Exclude
                    : SealDirectoryContentFilter.ContentFilterKind.Include;
                contentFilter = new SealDirectoryContentFilter(kind, reader.ReadString());
            }

            return new SealDirectoryMutablePipState(
                pipType,
                semiStableHash,
                storeId,
                directoryRoot,
                sealDirectoryKind,
                patterns,
                isComposite,
                scrub,
                contentFilter);
        }
    }
}
