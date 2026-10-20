// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.DirectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.DirectGraph
{
    /// <summary>
    /// Frontend for tools that contribute pips directly to the BuildXL graph.
    /// </summary>
    public sealed class DirectGraphFrontEnd : FrontEnd<DirectGraphWorkspaceResolver>
    {
        private readonly CounterCollection<DirectGraphCounter> m_counters = new();

        /// <summary>
        /// Environment variable used to provide the tool with the DirectGraph communication channel.
        /// </summary>
        public const string ChannelEnvironmentVariable = "BUILDXL_DIRECT_GRAPH_CHANNEL";

        /// <inheritdoc />
        public override string Name => KnownResolverKind.DirectGraphResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters => false;

        /// <inheritdoc />
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { KnownResolverKind.DirectGraphResolverKind };

        /// <inheritdoc />
        public override IResolver CreateResolver(string kind)
        {
            Contract.Requires(kind == KnownResolverKind.DirectGraphResolverKind);

            return new DirectGraphResolver(Host, Context, Name, m_counters);
        }

        /// <inheritdoc />
        public override void LogStatistics(Dictionary<string, long> statistics)
        {
            foreach (var counter in m_counters.AsStatistics("DirectGraph"))
            {
                // Every build asks every front end for its statistics, so a build that did not use this one reports nothing.
                if (counter.Value != 0)
                {
                    statistics.Add(counter.Key, counter.Value);
                }
            }
        }
    }
}
