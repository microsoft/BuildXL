// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Information about changed path.
    /// </summary>
    public readonly struct ChangedPathInfo : IEquatable<ChangedPathInfo>
    {
        /// <summary>
        /// Changed path.
        /// </summary>
        public readonly string Path;

        /// <summary>
        /// Kinds of changes to the path.
        /// </summary>
        public readonly PathChanges PathChanges;

        /// <summary>
        /// Creates an instance of <see cref="ChangedPathInfo"/>.
        /// </summary>
        public ChangedPathInfo(string path, PathChanges pathChanges)
        {
            Path = path;
            PathChanges = pathChanges;
        }

        /// <inheritdoc />
        public bool Equals(ChangedPathInfo other)
        {
            if (PathChanges != other.PathChanges)
            {
                return false;
            }

            return string.Equals(Path, other.Path, OperatingSystemHelper.PathComparison);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            return obj is ChangedPathInfo location && Equals(location);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Path.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)PathChanges;
                return hashCode;
            }
        }

    }
}
