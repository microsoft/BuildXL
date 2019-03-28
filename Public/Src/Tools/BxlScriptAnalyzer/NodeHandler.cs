// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Method describing a callback for a node
    /// </summary>
    public delegate bool NodeHandler(INode node, DiagnosticsContext context);
}
