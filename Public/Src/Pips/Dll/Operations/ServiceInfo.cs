// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// All service-related properties of a pip.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals", Justification = "Never used")]
    public sealed class ServiceInfo
    {
        /// <summary>
        /// Constant ServiceInfo instance for pips that are service non-related.
        /// </summary>
        public static readonly ServiceInfo None = new ServiceInfo(ServicePipKind.None);

        /// <summary>
        /// Constant ServiceInfo instance for service shutdown pips.
        /// </summary>
        public static readonly ServiceInfo ServiceShutdown = new ServiceInfo(ServicePipKind.ServiceShutdown);

        /// <summary>
        /// Whether and how this pip is related to service pips.
        /// </summary>
        [Pure]
        public ServicePipKind Kind { get; }

        /// <summary>
        /// Service pip dependencies.
        /// </summary>
        [Pure]
        public ReadOnlyArray<PipId> ServicePipDependencies { get; }

        /// <summary>
        /// A process to execute in order to gracefully kill this process.
        /// </summary>
        [Pure]
        public PipId ShutdownPipId { get; }

        /// <summary>
        /// Finalization Pip Ids.
        /// </summary>
        [Pure]
        public ReadOnlyArray<PipId> FinalizationPipIds { get; }

        /// <summary>
        /// Tag that is used to associate other pips with this service pip. Can only be defined for ServicePipKind.Service.        
        /// </summary>
        /// <remarks>
        /// Currently is used by ServicePipTracker to compute the overhang of service pips.
        /// </remarks>
        [Pure]
        public StringId TagToTrack { get; }

        /// <summary>
        /// Print-friendly name for the trackable tag.
        /// </summary>
        [Pure]
        public StringId DisplayNameForTrackableTag { get; }

        /// <nodoc />
        public ServiceInfo(
            ServicePipKind kind,
            ReadOnlyArray<PipId> servicePipDependencies = default(ReadOnlyArray<PipId>),
            PipId shutdownProcessPipId = default(PipId),
            ReadOnlyArray<PipId> finalizationPipIds = default(ReadOnlyArray<PipId>),
            StringId tagToTrack = default(StringId),
            StringId displayNameForTag = default(StringId))
        {
            Kind = kind;
            ServicePipDependencies = servicePipDependencies.IsValid ? servicePipDependencies : ReadOnlyArray<PipId>.Empty;
            ShutdownPipId = shutdownProcessPipId;
            FinalizationPipIds = finalizationPipIds.IsValid ? finalizationPipIds : ReadOnlyArray<PipId>.Empty;
            TagToTrack = tagToTrack;
            DisplayNameForTrackableTag = displayNameForTag;

            Contract.Assert(ServicePipDependencies.IsValid);
            Contract.Assert(FinalizationPipIds.IsValid);

            Contract.Assert(
                !ShutdownPipId.IsValid || Kind == ServicePipKind.Service,
                "'ShutdownProcessPipId' may only be specified when the pip is a 'Service'");

            Contract.Assert(
                !FinalizationPipIds.Any() || Kind == ServicePipKind.Service,
                "'FinalizationPipids' may only be specified when the pip is a 'Service'");

            // NOTE: The constraint below is not required by the engine, but is enforced here just to
            //       help users out, and catch builds that are *likely* to be errors.  If and when we
            //       encounter a case where a user will want to schedule a shutdown command from their
            //       own build specs (e.g., so that they can have some other pips depend on it), we can
            //       just lift this constraint.
            Contract.Assert(
                Kind != ServicePipKind.Service || ShutdownPipId.IsValid,
                "A pip that is a 'Service' must have the 'ShutdownProcessPipId' specified");

            Contract.Assert(
                ServicePipDependencies.All(pipId => pipId.IsValid),
                "'ServicePipDependencies' must not contain invalid PipIds");

            Contract.Assert(
                Kind != ServicePipKind.ServiceClient || ServicePipDependencies.Any(),
                "A pip that is a 'ServiceClient' must have some 'ServicePipDependencies'");

            Contract.Assert(
                !ServicePipDependencies.Any() || Kind == ServicePipKind.ServiceClient,
                "'ServicePipDependencies' may only be specified for a pip that is either a 'ServiceClient'");

            Contract.Assert(
                TagToTrack.IsValid == DisplayNameForTrackableTag.IsValid,
                "Tag and its display name must be specified at the same time");

            Contract.Assert(
                !TagToTrack.IsValid || Kind == ServicePipKind.Service,
                "'TagToTrack' may only be specified when the pip is a 'Service'");
        }

        /// <nodoc />
        [Pure]
        public bool IsStartOrShutdownKind => Kind.IsStartOrShutdown();

        internal static ServiceInfo Service(PipId shutdownPipId)
        {
            return new ServiceInfo(ServicePipKind.Service, shutdownProcessPipId: shutdownPipId);
        }

        internal static ServiceInfo ServiceClient(IEnumerable<PipId> servicePipDependencies)
        {
            return new ServiceInfo(ServicePipKind.ServiceClient, servicePipDependencies: ReadOnlyArray<PipId>.From(servicePipDependencies));
        }

        #region Serialization
        [Pure]
        internal static ServiceInfo InternalDeserialize(BuildXLReader reader)
        {
            var kind = (ServicePipKind)reader.ReadByte();
            if (kind == ServicePipKind.None)
            {
                return None;
            }

            var servicePipDependencies = reader.ReadReadOnlyArray(r => ReadPipId(r));
            var shutdownProcessPipId = reader.ReadBoolean() ? ReadPipId(reader) : PipId.Invalid;
            var finalizationPipIds = reader.ReadReadOnlyArray(r => ReadPipId(r));
            var tagToTrack = reader.ReadBoolean() ? reader.ReadStringId() : StringId.Invalid;
            var displayNameForTag = reader.ReadBoolean() ? reader.ReadStringId() : StringId.Invalid;
            return new ServiceInfo(kind, servicePipDependencies, shutdownProcessPipId, finalizationPipIds, tagToTrack, displayNameForTag);
        }

        internal static void InternalSerialize(BuildXLWriter writer, ServiceInfo info)
        {
            writer.Write((byte)info.Kind);
            if (info.Kind == ServicePipKind.None)
            {
                return;
            }

            writer.Write(info.ServicePipDependencies, (w, v) => WritePipId(w, v));
            writer.Write(info.ShutdownPipId.IsValid);
            if (info.ShutdownPipId.IsValid)
            {
                WritePipId(writer, info.ShutdownPipId);
            }

            writer.Write(info.FinalizationPipIds, (w, v) => WritePipId(w, v));

            writer.Write(info.TagToTrack.IsValid);
            if (info.TagToTrack.IsValid)
            {
                writer.Write(info.TagToTrack);
            }

            writer.Write(info.DisplayNameForTrackableTag.IsValid);
            if (info.DisplayNameForTrackableTag.IsValid)
            {
                writer.Write(info.DisplayNameForTrackableTag);
            }
        }

        private static void WritePipId(BuildXLWriter writer, PipId pipId) => writer.Write(pipId.Value);

        private static PipId ReadPipId(BuildXLReader reader)
        {
            var pipId = new PipId(reader.ReadUInt32());
            return (reader is PipReader pipReader) ? pipReader.RemapPipId(pipId) : pipId;
        }

        #endregion
    }
}
