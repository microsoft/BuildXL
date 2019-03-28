// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
