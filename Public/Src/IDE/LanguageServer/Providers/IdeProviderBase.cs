// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Ide.LanguageServer.Tracing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Base class for all providers
    /// </summary>
    /// <remarks>
    /// Exposes common objects (workspace, path table, etc.) for subclasses to consume. Additionally it makes sure the exposed workspace is always up to date.
    /// </remarks>
    public abstract class IdeProviderBase
    {
        /// <nodoc/>
        protected PathTable PathTable { get; }

        /// <summary>
        /// Last computed workspace. If there are any in-flight changes, getting the workspace blocks the thread until those changes are applied.
        /// </summary>
        protected Workspace Workspace => m_incrementalWorkspaceProvider.WaitForRecomputationToFinish();

        /// <nodoc/>
        protected ITypeChecker TypeChecker => Workspace.GetSemanticModel().TypeChecker;

        /// <nodoc/>
        protected ProviderContext ProviderContext { get; }

        /// <summary>
        /// The set of changed text items that the last computed workspace is based on.
        /// </summary>
        /// <remarks>
        /// Empty the first time the workspace is computed.
        /// </remarks>
        protected IReadOnlyCollection<TextDocumentItem> ChangedTextDocumentItems { get; private set; }

        /// <nodoc/>
        protected StreamJsonRpc.JsonRpc JsonRpc { get; }

        /// <nodoc/>
        protected Logger Logger => ProviderContext.Logger;

        /// <nodoc/>
        protected LoggingContext LoggingContext => ProviderContext.LoggingContext;

        private readonly IncrementalWorkspaceProvider m_incrementalWorkspaceProvider;

        /// <nodoc />
        protected IdeProviderBase(ProviderContext providerContext)
        {
            ProviderContext = providerContext;
            JsonRpc = providerContext.JsonRpc;
            PathTable = providerContext.PathTable;
            m_incrementalWorkspaceProvider = providerContext.IncrementalWorkspaceProvider;
            m_incrementalWorkspaceProvider.WorkspaceRecomputed += OnWorkspaceRecomputed;
            ChangedTextDocumentItems = new TextDocumentItem[0];
        }

        /// <summary>
        /// This method is called every time the workspace is recomputed.
        /// </summary>
        /// <remarks>
        /// Just updates the workspace and the set of text document
        /// changes to reflect the most up-to-date ones, but subclasses can add extra behavior.
        /// </remarks>
        protected virtual void OnWorkspaceRecomputed(object sender, WorkspaceRecomputedEventArgs workspaceRecomputedEventArgs)
        {
            ChangedTextDocumentItems = workspaceRecomputedEventArgs.TextDocumentItems;
        }

        /// <summary>
        /// Returns a source file with the given <paramref name="uriString"/>.
        /// </summary>
        /// <remarks>
        /// The function emits a log entry if the given source file is missing from the <see cref="Workspace"/>.
        /// </remarks>
        protected bool TryFindSourceFile(string uriString, out ISourceFile sourceFile)
        {
            var uri = new Uri(uriString);

            if (!uri.TryGetSourceFile(Workspace, PathTable, out sourceFile))
            {
                Logger.LanguageServerCanNotFindSourceFile(LoggingContext, uriString);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to resolve a node by the given <paramref name="documentPosition"/>.
        /// </summary>
        /// <remarks>
        /// The function emits a log entry if the given source file is missing from the <see cref="Workspace"/>.
        /// </remarks>
        protected bool TryFindNode(TextDocumentPositionParams documentPosition, out INode currentNode)
        {
            Contract.Requires(documentPosition.Position != null);

            var lineAndColumn = documentPosition.Position.ToLineAndColumn();

            if (!TryFindSourceFile(documentPosition.TextDocument.Uri, out var sourceFile))
            {
                // The message that the file is missing was logged.
                currentNode = null;
                return false;
            }

            return DScriptNodeUtilities.TryGetNodeAtPosition(sourceFile, lineAndColumn, out currentNode);
        }
    }
}
