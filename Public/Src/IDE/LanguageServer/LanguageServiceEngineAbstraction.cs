// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// An engine abstraction for the language service.
    /// </summary>
    /// <remarks>
    /// Takes into consideration the in-flight changes that the DocumentManager reports
    /// so queries to get front end files go through the document manager.
    /// </remarks>
    public sealed class LanguageServiceEngineAbstraction : BasicFrontEndEngineAbstraction
    {
        private readonly TextDocumentManager m_documentManager;

        /// <nodoc />
        public LanguageServiceEngineAbstraction(TextDocumentManager documentManager, PathTable pathTable, BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem fileSystem) 
            : base(pathTable, fileSystem)
        {
            m_documentManager = documentManager;   
        }

        /// <nodoc />
        public override bool TryGetFrontEndFile(AbsolutePath path, string frontEnd, out Stream stream)
        {
            if (m_documentManager.TryGetDocument(path, out var document))
            {
                stream = new MemoryStream(Encoding.UTF8.GetBytes(document.Text ?? string.Empty));
                return true;
            }

            return base.TryGetFrontEndFile(path, frontEnd, out stream);
        }

        /// <nodoc />
        public override bool FileExists(AbsolutePath path)
        {
            if (m_documentManager.TryGetDocument(path, out _))
            {
                return true;
            }

            return base.FileExists(path);
        }

        /// <nodoc />
        public override bool DirectoryExists(AbsolutePath path)
        {
            if (m_documentManager.Directories.Contains(path))
            {
                return true;
            }

            return base.DirectoryExists(path);
        }

        /// <inheritdoc />
        public override IEnumerable<AbsolutePath> EnumerateEntries(AbsolutePath path, string pattern, bool recursive, bool directories)
        {
            var allEntries = directories ? m_documentManager.Directories : m_documentManager.DocumentItemPaths;
            
            var filteredEntries = allEntries.Where(
                pathToFile => ((!recursive && pathToFile.GetParent(m_pathTable) == path) || (recursive && pathToFile.IsWithin(m_pathTable, path))) 
                && FileUtilities.PathMatchPattern(pathToFile.GetName(m_pathTable).ToString(m_pathTable.StringTable), pattern));

            // If the root path exists, then it is always safe to union it with the base enumeration
            if (base.DirectoryExists(path))
            {
                return filteredEntries.Union(base.EnumerateEntries(path, pattern, recursive, directories)).ToReadOnlySet();
            }

            // Otherwise, if the root path is (virtually) defined by any of the content of the document manager,
            // then the real file system doesn't need to be explored
            if (m_documentManager.DocumentItemPaths.Any(pathToFile => pathToFile.IsWithin(m_pathTable, path)))
            {
                return filteredEntries;
            }

            // Finally if the document manager doesn't know about the path, we let the base enumeration decide
            // what to do when the root path does not exist
            return base.EnumerateEntries(path, pattern, recursive, directories);
        }
    }
}
