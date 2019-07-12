// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// Service pip dependencies.
        /// </summary>
        [Pure]
        public ReadOnlyArray<PipId> FinalizationPipIds { get; }

        /// <nodoc />
        public ServiceInfo(
            ServicePipKind kind,
            ReadOnlyArray<PipId> servicePipDependencies = default(ReadOnlyArray<PipId>),
            PipId shutdownProcessPipId = default(PipId),
            ReadOnlyArray<PipId> finalizationPipIds = default(ReadOnlyArray<PipId>))
        {
            Kind = kind;
            ServicePipDependencies = servicePipDependencies.IsValid ? servicePipDependencies : ReadOnlyArray<PipId>.Empty;
            ShutdownPipId = shutdownProcessPipId;
            FinalizationPipIds = finalizationPipIds.IsValid ? finalizationPipIds : ReadOnlyArray<PipId>.Empty;

            // Calling invariant method explicitely because this is the only way to check it at least once.
            Invariant();
        }

        /// <nodoc />
        [Pure]
        public bool IsValid => ServicePipDependencies.IsValid;

        /// <nodoc />
        [Pure]
        public bool IsStartOrShutdownKind => Kind.IsStartOrShutdown();

        [ContractInvariantMethod]
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Method is not empty only when CONTRACTS_LIGHT_INVARIANTS symbol is defined.")]
        private void Invariant()
        {
            Contract.Invariant(ServicePipDependencies.IsValid);
            Contract.Invariant(FinalizationPipIds.IsValid);

            Contract.Invariant(
                !ShutdownPipId.IsValid || Kind == ServicePipKind.Service,
                "'ShutdownProcessPipId' may only be specified when the pip is a 'Service'");

            Contract.Invariant(
                !FinalizationPipIds.Any() || Kind == ServicePipKind.Service,
                "'FinalizationPipids' may only be specified when the pip is a 'Service'");

            // NOTE: The constraint below is not required by the engine, but is enforced here just to
            //       help users out, and catch builds that are *likely* to be errors.  If and when we
            //       encounter a case where a user will want to schedule a shutdown command from their
            //       own build specs (e.g., so that they can have some other pips depend on it), we can
            //       just lift this constraint.
            Contract.Invariant(
                Kind != ServicePipKind.Service || ShutdownPipId.IsValid,
                "A pip that is a 'Service' must have the 'ShutdownProcessPipId' specified");

            Contract.Invariant(
                ServicePipDependencies.All(pipId => pipId.IsValid),
                "'ServicePipDependencies' must not contain invalid PipIds");

            Contract.Invariant(
                Kind != ServicePipKind.ServiceClient || ServicePipDependencies.Any(),
                "A pip that is a 'ServiceClient' must have some 'ServicePipDependencies'");

            Contract.Invariant(
                !ServicePipDependencies.Any() || Kind == ServicePipKind.ServiceClient,
                "'ServicePipDependencies' may only be specified for a pip that is either a 'ServiceClient'");
        }

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
            return new ServiceInfo(kind, servicePipDependencies, shutdownProcessPipId, finalizationPipIds);
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
        }

        private static void WritePipId(BuildXLWriter writer, PipId pipId) => writer.WritePipIdValue(pipId.Value);

        private static PipId ReadPipId(BuildXLReader reader) => new PipId(reader.ReadPipIdValue());
        #endregion
    }
}
