// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <summary>
    /// Encapsulates state needed for creating completion items.
    /// </summary>
    public sealed class CompletionState
    {
        /// <summary>
        /// The TypeScript AST Node that completion is being requested for.
        /// </summary>
        /// <remarks>
        /// Note that the AST node may not accurately reflect the type of completion that should be
        /// performed. As the node is the closest node that "contains" the text position.
        /// So, if the completion is done at this location:
        ///
        /// <code>
        /// const myVariable = functionCall(I);
        /// </code>
        ///
        /// The node will represent the call expression "functionCall()", which should return
        /// completion items (if appropriate) for the function arguments rather than the return type.
        /// </remarks>
        public readonly INode StartingNode;

        /// <summary>
        /// The position at which the completion was requested
        /// </summary>
        public readonly TextDocumentPositionParams PositionParameters;

        /// <summary>
        /// The type checker associated with the completion.
        /// </summary>
        public readonly ITypeChecker TypeChecker;

        /// <summary>
        /// The BuildXL <see cref="Workspace"/> associated with the completion
        /// </summary>
        public readonly Workspace Workspace;

        /// <summary>
        /// The BuildXL <see cref="PathTable"/> associated with the completion.
        /// </summary>
        public readonly PathTable PathTable;

        /// <nodoc/>
        public CompletionState(INode startingNode, TextDocumentPositionParams positionParameters, ITypeChecker typeChecker, Workspace workspace, PathTable pathTable)
        {
            Contract.Requires(startingNode != null);
            Contract.Requires(positionParameters != null);
            Contract.Requires(typeChecker != null);
            Contract.Requires(workspace != null);
            Contract.Requires(pathTable != null);

            StartingNode = startingNode;
            PositionParameters = positionParameters;
            TypeChecker = typeChecker;
            Workspace = workspace;
            PathTable = pathTable;
        }
    }
}
