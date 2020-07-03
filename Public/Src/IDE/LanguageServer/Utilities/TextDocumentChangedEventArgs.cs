// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer
{
        /// <nodoc />
    public sealed class TextDocumentChangedEventArgs : EventArgs
    {
        /// <nodoc />
        public TextDocumentChangedEventArgs(TextDocumentItem document)
        {
            Document = document;
        }

        /// <nodoc />
        public TextDocumentItem Document { get; }
    }
}
