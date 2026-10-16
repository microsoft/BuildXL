// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Channels;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
{
    public sealed partial class PipGraph
    {
        /// <summary>
        /// Dynamic scheduler view over the mutable pip graph.
        /// </summary>
        public partial class Builder : IDynamicGraph
        {
            private readonly Lazy<Channel<PipId>> m_pipAdmissions = new Lazy<Channel<PipId>>(
                () => Channel.CreateUnbounded<PipId>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        AllowSynchronousContinuations = false,
                    }));

            // Reserves an output directory in the assertion map until its producer is recorded as sealed.
            // Assertion registration rejects this marker; the shared set must never be mutated.
            private static readonly HashSet<FileArtifact> s_dynamicOutputAssertionMarker = new HashSet<FileArtifact>();

            // Pips selected by the filter (including dependencies) and validated to have no output existence assertions.
            // Further existence assertions on these pips are rejected.
            private readonly ConcurrentBigSet<PipId> m_dynamicPipsWithSealedOutputs = new ConcurrentBigSet<PipId>();

            /// <inheritdoc />
            public bool TrySealPipForDynamicScheduling(PipId pipId, out string unsupportedFeature)
            {
                if (m_dynamicPipsWithSealedOutputs.Contains(pipId))
                {
                    unsupportedFeature = null;
                    return true;
                }

                // Do not check assertions here, we are going to do it (thread-safely) in the next step.
                unsupportedFeature = GetUnsupportedDynamicPipFeature(this, pipId, checkExistenceAssertions: false);
                if (unsupportedFeature != null)
                {
                    return false;
                }

                // Assertions can arrive concurrently at any time before a pip is scheduled. Adding a marker for each
                // output directory atomically checks that no assertions exist and blocks new ones until we publish
                // the sealed state. An empty set serves as the marker because legitimate assertion entries are nonempty.
                // This relies on output DirectoryArtifacts being unique per pip and on never sealing the same pip concurrently.
                using var pooledReservedDirectories = Pools.GetDirectoryArtifactList();
                var reservedDirectories = pooledReservedDirectories.Instance;
                try
                {
                    if (GetPipFromPipId(pipId) is Process process)
                    {
                        foreach (var directory in process.DirectoryOutputs)
                        {
                            // An empty set reserves this directory against assertion registration.
                            if (!OutputsUnderOpaqueExistenceAssertions.TryAdd(directory, s_dynamicOutputAssertionMarker))
                            {
                                unsupportedFeature = "output-directory existence assertions";
                                return false;
                            }

                            reservedDirectories.Add(directory);
                        }
                    }

                    m_dynamicPipsWithSealedOutputs.Add(pipId);
                    return true;
                }
                finally
                {
                    // Publish the sealed state before releasing reservations; also unwind partial failures.
                    foreach (var directory in reservedDirectories)
                    {
                        OutputsUnderOpaqueExistenceAssertions.CompareRemove(directory, s_dynamicOutputAssertionMarker);
                    }
                }
            }

            /// <inheritdoc />
            IPipTable IDynamicGraph.PipTable => PipTable;

            /// <inheritdoc />
            SemanticPathExpander IDynamicGraph.SemanticPathExpander => SemanticPathExpander;

            /// <inheritdoc />
            StringId IDynamicGraph.ApiServerMoniker =>
                m_lazyApiServerMoniker.IsValueCreated
                    ? StringId.Create(Context.StringTable, m_lazyApiServerMoniker.Value.Id)
                    : StringId.Invalid;

            /// <inheritdoc />
            public IAsyncEnumerable<PipId> ReadPipAdmissionsAsync(CancellationToken cancellationToken)
            {
                Contract.Assert(
                    m_configuration.Engine.UnsafeEnableDynamicGraph,
                    "Pip admissions can only be read when dynamic graph mode is enabled.");

                return m_pipAdmissions.Value.Reader.ReadAllAsync(cancellationToken);
            }

            private void PublishPipAdmission(Pip pip)
            {
                Contract.Requires(pip != null);
                PublishPipAdmission(pip.PipId);
            }

            private void PublishPipAdmission(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);

                if (m_configuration.Engine.UnsafeEnableDynamicGraph)
                {
                    Contract.Assert(m_pipAdmissions.Value.Writer.TryWrite(pipId), "Cannot publish a pip after graph completion.");
                }
            }

            private void CompletePipAdmissions(Exception failure = null)
            {
                // Make this a no-op if dynamic graph mode is not enabled
                if (m_configuration.Engine.UnsafeEnableDynamicGraph)
                {
                    m_pipAdmissions.Value.Writer.TryComplete(failure);
                }
            }
        }
    }
}
