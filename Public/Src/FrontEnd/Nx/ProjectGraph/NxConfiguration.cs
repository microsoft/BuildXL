// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Nx.ProjectGraph
{
    /// <summary>
    /// For now there is nothing specific about Nx that needs to be part of the project graph
    /// </summary>
    /// <remarks>
    /// A configuration object is required to conform to the JavaScript resolver infrastructure, even if
    /// it is empty
    /// </remarks>
    public sealed class NxConfiguration
    {
    }
}
