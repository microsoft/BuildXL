// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Engine.DirectGraph
{
    /// <summary>
    /// Applies protocol operations to a BuildXL pip graph.
    /// </summary>
    public interface IDirectGraphGraphBuilder
    {
        /// <summary>
        /// Handles one graph construction operation.
        /// </summary>
        Task<GraphOperationResponse> HandleOperationAsync(GraphOperation operation, CancellationToken cancellationToken);
    }
}
