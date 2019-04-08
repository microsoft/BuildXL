// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Test related objects that tweak the app behavior 
    /// </summary>
    public readonly struct TestContext
    {
        /// <nodoc/>
        public TestContext(IEnumerable<TextDocumentItem> prePopulatedDocuments, bool forceSynchronousMessages, bool forceNoRecomputationDelay, Action<string> errorReporter)
        {
            Contract.Requires(prePopulatedDocuments != null);

            PrePopulatedDocuments = prePopulatedDocuments;
            ForceSynchronousMessages = forceSynchronousMessages;
            ForceNoRecomputationDelay = forceNoRecomputationDelay;
            ErrorReporter = errorReporter;
        }

        /// <summary>
        /// The document manager can start with a set of pre-populated documents
        /// </summary>
        public IEnumerable<TextDocumentItem> PrePopulatedDocuments { get; }

        /// <summary>
        /// Whether to force all notifications from the app to behave synchronously
        /// </summary>
        public bool ForceSynchronousMessages { get; }

        /// <summary>
        /// Whether to force that the workspace recomputation happens with no delays (keystrokes are coalesced in the regular case)
        /// </summary>
        public bool ForceNoRecomputationDelay { get; }

        /// <summary>
        /// Optional action that gets notified of an error.
        /// </summary>
        [CanBeNull]
        public Action<string> ErrorReporter { get; }
    }
}
