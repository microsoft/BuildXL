// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Channels;
using BuildXL.Pips.Operations;
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
