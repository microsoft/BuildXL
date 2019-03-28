// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;

namespace Test.BuildXL.Storage
{
    /// <nodoc />
    public struct EnumerateDirectoryEntry
    {
        /// <nodoc />
        public string Path { get; }

        /// <nodoc />
        public string FileName { get; }

        /// <nodoc />
        public FileAttributes Attributes { get; }

        /// <nodoc />
        public string FullName => System.IO.Path.Combine(Path, FileName);

        /// <nodoc />
        public long Size { get; }

        /// <nodoc />
        public EnumerateDirectoryEntry(string path, string fileName, FileAttributes attributes, long size)
        {
            Path = path;
            FileName = fileName;
            Attributes = attributes;
            Size = size;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if ((Attributes & FileAttributes.Directory) != 0)
            {
                return FullName;
            }

            return $"{FullName} ({Size} bytes)";
        }
    }

    /// <nodoc />
    public static class EnumerateDirectoryExtensions
    {
        /// <nodoc />
        public static List<EnumerateDirectoryEntry> EnumerateDirectories(this IFileSystem fileSystem, string directoryPath, string pattern, bool recursive)
        {
            List<EnumerateDirectoryEntry> entries = new List<EnumerateDirectoryEntry>();

            var enumerationResult = fileSystem.EnumerateDirectoryEntries(
                directoryPath,
                recursive,
                pattern,
                (path, fileName, attributes) => { entries.Add(new EnumerateDirectoryEntry(path, fileName, attributes, 0)); });

            if (!enumerationResult.Succeeded)
            {
                throw enumerationResult.ThrowForKnownError();
            }

            return entries;
        }

        /// <nodoc />
        public static List<EnumerateDirectoryEntry> EnumerateFiles(this IFileSystem fileSystem, string directoryPath, string pattern, bool recursive)
        {
            List<EnumerateDirectoryEntry> entries = new List<EnumerateDirectoryEntry>();

            var enumerationResult = fileSystem.EnumerateFiles(
                directoryPath,
                recursive,
                pattern,
                (path, fileName, attributes, size) => { entries.Add(new EnumerateDirectoryEntry(path, fileName, attributes, size)); });

            if (!enumerationResult.Succeeded)
            {
                throw enumerationResult.ThrowForKnownError();
            }

            return entries;
        }
    }
}