// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Adapts an already finalized graph to the dynamic graph surface.
    /// </summary>
    public sealed partial class PipGraph : IDynamicGraph
    {
        /// <inheritdoc />
        IPipTable IDynamicGraph.PipTable => PipTable;

        /// <inheritdoc />
        SemanticPathExpander IDynamicGraph.SemanticPathExpander => SemanticPathExpander;

        /// <inheritdoc />
        StringId IDynamicGraph.ApiServerMoniker => ApiServerMoniker;

        /// <inheritdoc />
        public bool TrySealPipForDynamicScheduling(PipId pipId, out string unsupportedFeature)
        {
            unsupportedFeature = GetUnsupportedDynamicPipFeature(this, pipId);
            return unsupportedFeature == null;
        }

        private static string GetUnsupportedDynamicPipFeature(IDynamicGraph graph, PipId pipId, bool checkExistenceAssertions = true)
        {
            if (graph.PipTable.GetServiceInfo(pipId).Kind != ServicePipKind.None)
            {
                return "service-related pips";
            }

            var pip = graph.GetPipFromPipId(pipId);
            if (pip is Process process)
            {
                if (process.AllowUndeclaredSourceReads)
                {
                    return "undeclared source reads";
                }

                if (process.HasSharedOpaqueDirectoryOutputs)
                {
                    return "shared opaque directories";
                }

                if (checkExistenceAssertions)
                {
                    foreach (var directory in process.DirectoryOutputs)
                    {
                        if (graph.GetExistenceAssertionsUnderOpaqueDirectory(directory).Count != 0)
                        {
                            return "output-directory existence assertions";
                        }
                    }
                }
            }
            else if (pip is SealDirectory sealDirectory && sealDirectory.Kind == SealDirectoryKind.SharedOpaque)
            {
                return "shared opaque directories";
            }

            return null;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<PipId> ReadPipAdmissionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var pipId in PipTable.StableKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return pipId;
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
