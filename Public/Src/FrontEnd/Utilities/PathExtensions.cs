// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Utilities
{
    /// <nodoc/>
    /// TODO: For the path normalization methods, consider adding this functionality to the PathTable
    public static class PathExtensions
    {
        /// <summary>
        /// Returns an <see cref="AbsolutePath"/> whose string representation is normalized regarding casing, if the executing OS is case-insensitive
        /// </summary>
        public static AbsolutePath ToNormalizedAbsolutePath(this string absolutePath, PathTable pathTable)
        {
            return ToNormalizedPathArtifact(absolutePath, pathTable, AbsolutePath.Invalid, AbsolutePath.TryCreate);
        }

        /// <summary>
        /// Tries to create an <see cref="AbsolutePath"/> whose string representation is normalized regarding casing, if the executing OS is case-insensitive
        /// </summary>
        public static bool TryCreateNormalizedAbsolutePath(this string absolutePathString, PathTable pathTable, out AbsolutePath absolutePath)
        {
            return TryCreateNormalizedPathArtifact(absolutePathString, pathTable, AbsolutePath.Invalid, AbsolutePath.TryCreate, out absolutePath);
        }

        /// <summary>
        /// Returns a <see cref="RelativePath"/> whose string representation is normalized regarding casing, if the executing OS is case-insensitive
        /// </summary>
        public static RelativePath ToNormalizedRelativePath(this string relativePath, StringTable stringTable)
        {
            return ToNormalizedPathArtifact(relativePath, stringTable, RelativePath.Invalid, RelativePath.TryCreate);
        }

        /// <summary>
        /// Tries to create a <see cref="RelativePath"/> whose string representation is normalized regarding casing, if the executing OS is case-insensitive
        /// </summary>
        public static bool TryCreateNormalizedRelativePath(this string relativePathString, StringTable stringTable, out RelativePath relativePath)
        {
            return TryCreateNormalizedPathArtifact(relativePathString, stringTable, RelativePath.Invalid, RelativePath.TryCreate, out relativePath);
        }

        /// <summary>
        /// Tries to create a <see cref="PathAtom"/> whose string representation is normalized regarding casing, if the executing OS is case-insensitive
        /// </summary>
        public static bool TryCreateNormalizedPathAtom(this string pathAtomString, StringTable stringTable, out PathAtom pathAtom)
        {
            return TryCreateNormalizedPathArtifact(pathAtomString, stringTable, PathAtom.Invalid, PathAtom.TryCreate, out pathAtom);
        }

        /// <summary>
        /// Returns a <see cref="PathAtom"/> whose string representation is normalized regarding casing, if the executing OS is case-insensitive
        /// </summary>
        public static PathAtom ToNormalizedPathAtom(this string pathAtom, StringTable stringTable)
        {
            return ToNormalizedPathArtifact(pathAtom, stringTable, PathAtom.Invalid, PathAtom.TryCreate);
        }

        delegate bool TryCreateArtifact<T, in TTable>(TTable table, string artifactString, out T artifact);

        private static T ToNormalizedPathArtifact<T, TTable>(string pathArtifact, TTable table, T invalidArtifact, TryCreateArtifact<T, TTable> tryCreate)
        {
            if (!TryCreateNormalizedPathArtifact(pathArtifact, table, invalidArtifact, tryCreate, out T artifact))
            {
                Contract.Assert(false, I($"Cannot create a '{typeof(T)}' from '{pathArtifact}'"));
            }

            return artifact;
        }

        private static bool TryCreateNormalizedPathArtifact<T, TTable>(string pathArtifact, TTable table, T invalidArtifact, TryCreateArtifact<T, TTable> tryCreate, out T artifact)
        {
            if (pathArtifact == null)
            {
                artifact = invalidArtifact;
                return true;
            }

            string normalizedpathArtifact = pathArtifact;

            if (!OperatingSystemHelper.IsUnixOS)
            {
                normalizedpathArtifact = normalizedpathArtifact.ToLowerInvariant();
            }

            return tryCreate(table, normalizedpathArtifact, out artifact);
        }
    }
}
