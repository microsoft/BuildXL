// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Engine.Serialization
{
    /// <summary>
    /// Extends <see cref="FileSystemStreamProvider"/> to allow accessing paths referring to entries
    /// in a specified zip file.
    /// </summary>
    /// <example>
    /// Given a <see cref="ZipFileSystemStreamProvider"/> created with the zip file path 'D:\myfolder\BuildXL.zip',
    /// opening the path 'D:\myfolder\BuildXL.zip\BuildXL\PathTable' would open the 'BuildXL\PathTable' entry in 'D:\myfolder\BuildXL.zip'
    /// </example>
    public class ZipFileSystemStreamProvider : FileSystemStreamProvider
    {
        /// <summary>
        /// The path to the zip file
        /// </summary>
        public readonly string ZipFilePath;

        private Dictionary<string, string> m_pathToEntryNameMap;

        /// <summary>
        /// Class constructor
        /// </summary>
        public ZipFileSystemStreamProvider(string zipFilePath)
        {
            ZipFilePath = zipFilePath;
        }

        /// <inheritdoc />
        public override Disposable<Stream> OpenReadStream(string path)
        {
            if (path.StartsWith(ZipFilePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return OpenArchiveForRead().ChainSelect(archive =>
                {
                    var relativePath = m_pathToEntryNameMap.GetOrDefault(path, defaultValue: path.Substring(ZipFilePath.Length + 1));
                    var entry = archive.GetEntry(relativePath);
                    return (Stream)new TrackedStream(archive.GetEntry(relativePath).Open(), leaveOpen: false, precomputedLength: entry.Length);
                });
            }

            return base.OpenReadStream(path);
        }

        /// <summary>
        /// Opens the zip archive
        /// </summary>
        public Disposable<ZipArchive> OpenArchiveForRead()
        {
            return Disposable.Create(base.OpenReadStream(ZipFilePath))
                .ChainSelect(zipFileStream =>
                {
                    var archive = new ZipArchive(zipFileStream.Value, ZipArchiveMode.Read);

                    if (m_pathToEntryNameMap == null)
                    {
                        m_pathToEntryNameMap = archive.Entries.ToDictionary(
                            entry => Path.Combine(ZipFilePath, entry.FullName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)),
                            entry => entry.FullName,
                            StringComparer.OrdinalIgnoreCase);
                    }

                    return archive;
                });
        }
    }
}
