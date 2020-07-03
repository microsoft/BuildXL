// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Method describing a callback for a node
    /// </summary>
    public delegate bool NodeHandler(INode node, DiagnosticsContext context);
}
