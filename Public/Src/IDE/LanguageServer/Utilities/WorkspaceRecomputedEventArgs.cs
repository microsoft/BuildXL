// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Workspaces.Core;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Event that is fired when the workspace is recomputed after a series of text change events
    /// </summary>
    public sealed class WorkspaceRecomputedEventArgs : EventArgs
    {
        /// <summary>
        /// Update workspace after all document changes have been applied
        /// </summary>
        public Workspace UpdatedWorkspace { get; }

        /// <summary>
        /// All document change items that triggered the workspace update
        /// </summary>
        public IReadOnlyCollection<TextDocumentItem> TextDocumentItems { get; }

        /// <nodoc/>
        public WorkspaceRecomputedEventArgs(Workspace updatedWorkspace, IReadOnlyCollection<TextDocumentItem> textDocumentItems)
        {
            UpdatedWorkspace = updatedWorkspace;
            TextDocumentItems = textDocumentItems;
        }
    }
}
