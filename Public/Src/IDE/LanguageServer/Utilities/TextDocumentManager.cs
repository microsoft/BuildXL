// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer
{
    /// <nodoc />
    public sealed class TextDocumentManager
    {
        private readonly ConcurrentDictionary<AbsolutePath, TextDocumentItem> m_documents = new ConcurrentDictionary<AbsolutePath, TextDocumentItem>();

        /// <summary>
        /// All the directories this manager ever knew about
        /// </summary>
        /// <remarks>
        /// Does not take into consideration removed files
        /// </remarks>
        public ICollection<AbsolutePath> Directories { get; } = new HashSet<AbsolutePath>();

        /// <summary>
        /// All the paths to document items this manager knows about
        /// </summary>
        public ICollection<AbsolutePath> DocumentItemPaths => m_documents.Keys;

        /// <summary>
        /// Returns a current path table.
        /// </summary>
        public PathTable PathTable { get; }

        /// <nodoc />
        public TextDocumentManager(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            PathTable = pathTable;
        }

        /// <summary>
        /// Adds a document with a given id and content.
        /// </summary>
        public bool Add(AbsolutePath uri, TextDocumentItem document)
        {
            Contract.Requires(document != null);

            if (m_documents.TryAdd(uri, document))
            {
                UpdateDirectories(uri);
                return true;
            }

            // The document was in the list, it is possible that the document is reopened after some changes
            // in another document that caused some issues in this one.
            OnChanged(document);

            return false;
        }

        /// <summary>
        /// Notify that the document with a given path has changed.
        /// </summary>
        public void Change(AbsolutePath path, VersionedTextDocumentIdentifier documentIdentifier, TextDocumentContentChangeEvent[] changeEvents)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(changeEvents != null);

            if (changeEvents.Length == 0)
            {
                return;
            }

            // In some cases it is possible to get 'Change' event without getting 'Add' event.
            // In this case we consider the document as a new one.
            
            if (!TryGetOrAddDocument(path, documentIdentifier, changeEvents, out var document))
            {
                // Need to log this, IMO.
                return;
            }

            var version = documentIdentifier.Version;
            if (document.Version >= version)
            {
                return;
            }

            foreach (var ev in changeEvents)
            {
                Apply(document, ev);
            }

            document.Version = version;
            OnChanged(document);
        }

        /// <summary>
        /// Tries to obtain the document with a given path.
        /// </summary>
        public bool TryGetDocument(AbsolutePath path, out TextDocumentItem document)
        {
            return m_documents.TryGetValue(path, out document);
        }

        /// <summary>
        /// Removes the document with a given path.
        /// </summary>
        public void Remove(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            // TODO: should we notify when the document is removed?
            m_documents.TryRemove(path, out var _);
        }

        /// <summary>
        /// An event that will be invoked when the document has changed.
        /// </summary>
        public event EventHandler<TextDocumentChangedEventArgs> Changed;

        /// <summary>
        /// Clears existing event handlers that could've left from the previous state.
        /// </summary>
        public void ClearEventHandlers()
        {
            Changed = null;
        }

        private void UpdateDirectories(AbsolutePath pathToFile)
        {
            var currentDirectory = pathToFile.GetParent(PathTable);

            while (currentDirectory.IsValid)
            {
                Directories.Add(currentDirectory);
                currentDirectory = currentDirectory.GetParent(PathTable);
            }
        }

        private bool TryGetOrAddDocument(AbsolutePath path, VersionedTextDocumentIdentifier documentIdentifier, TextDocumentContentChangeEvent[] changeEvents, out TextDocumentItem document)
        {
            if (!m_documents.TryGetValue(path, out document))
            {
                // The document is missing from the internal storage and the changeset is empty.
                // Can't do anything here.
                if (changeEvents.Length == 0)
                {
                    return false;
                }

                lock (m_documents)
                {
                    if (!m_documents.TryGetValue(path, out document))
                    {
                        document = new TextDocumentItem()
                        {
                            Version = documentIdentifier.Version,
                            Uri = documentIdentifier.Uri,
                            Text = changeEvents.Last().Text,
                        };

                        document = m_documents.GetOrAdd(path, document);
                        return true;
                    }
                }
            }

            return true;
        }

        private static void Apply(TextDocumentItem document, TextDocumentContentChangeEvent ev)
        {
            if (ev.Range != null)
            {
                var startPos = GetPosition(document.Text, (int)ev.Range.Start.Line, (int)ev.Range.Start.Character);
                var endPos = GetPosition(document.Text, (int)ev.Range.End.Line, (int)ev.Range.End.Character);
                var newText = document.Text.Substring(0, startPos) + ev.Text + document.Text.Substring(endPos);
                document.Text = newText;
            }
            else
            {
                document.Text = ev.Text;
            }
        }

        private static int GetPosition(string text, int line, int character)
        {
            var pos = 0;
            for (; line >= 0; line--)
            {
                var lf = text.IndexOf('\n', pos);
                if (lf < 0)
                {
                    return text.Length;
                }

                pos = lf + 1;
            }

            var linefeed = text.IndexOf('\n', pos);
            var max = 0;
            if (linefeed < 0)
            {
                max = text.Length;
            }
            else if (linefeed > 0 && text[linefeed - 1] == '\r')
            {
                max = linefeed - 1;
            }
            else
            {
                max = linefeed;
            }

            pos += character;
            return (pos < max) ? pos : max;
        }

        private void OnChanged(TextDocumentItem document)
        {
            Changed?.Invoke(this, new TextDocumentChangedEventArgs(document));
        }
    }
}
