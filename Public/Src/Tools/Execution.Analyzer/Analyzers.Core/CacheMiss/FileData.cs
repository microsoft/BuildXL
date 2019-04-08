// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer.Analyzers.CacheMiss
{
    /// <summary>
    /// Artifact and Hash of a file
    /// </summary>
    internal struct FileData : IEquatable<FileData>
    {
        public AbsolutePath Path => File.Path;

        public FileArtifact File;
        public ContentHash Hash;

        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(File.Path.GetHashCode(), Hash.GetHashCode());
        }

        public bool Equals(FileData other)
        {
            return File.Path == other.File.Path && Hash == other.Hash;
        }
    }
}
