// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Core;

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
