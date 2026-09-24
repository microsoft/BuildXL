// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
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
