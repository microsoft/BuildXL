// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Defines utility methods for working with VFS
    /// </summary>
    public static class VfsUtilities
    {
        private static readonly string DirectorySeparatorCharString = Path.DirectorySeparatorChar.ToString();
        private static readonly char[] PathSplitChars = new[] { Path.DirectorySeparatorChar };
        private static readonly char[] FilePlacementInfoFileNameSplitChars = new[] { '-' };

        /// <summary>
        /// Gets whether a path is contained in another path
        /// </summary>
        public static bool IsPathWithin(this string path, string candidateParent)
        {
            if (path.Length <= candidateParent.Length || !path.StartsWith(candidateParent, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (candidateParent.EndsWith(DirectorySeparatorCharString))
            {
                return true;
            }

            return path[candidateParent.Length] == Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Gets whether a path is contained in another path and returns the relative path from <paramref name="candidateParent"/> if <paramref name="path"/> is a subpath.
        /// </summary>
        public static bool TryGetRelativePath(this string path, string candidateParent, [NotNullWhen(true)]out string? relativePath)
        {
            if (path.IsPathWithin(candidateParent))
            {
                relativePath = path.Substring(candidateParent.Length + (candidateParent.EndsWith(DirectorySeparatorCharString) ? 0 : 1));
                return true;
            }
            else
            {
                relativePath = default;
                return false;
            }
        }

        /// <summary>
        /// Parses a VFS cas relative path to extract file placement data
        /// </summary>
        public static bool TryParseCasRelativePath(string casRelativePath, out VfsFilePlacementData data)
        {
            data = default;
            var parts = casRelativePath.Split(PathSplitChars);
            if (parts.Length != 3)
            {
                return false;
            }

            if (Enum.TryParse<HashType>(parts[0], ignoreCase: true, out var hashType))
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(parts[2]);
                    var fileNameParts = fileName.Split(FilePlacementInfoFileNameSplitChars);

                    if (fileNameParts.Length != 4)
                    {
                        return false;
                    }

                    var hash = new ContentHash(hashType, HexUtilities.HexToBytes(fileNameParts[0]));
                    if (Enum.TryParse<FileRealizationMode>(fileNameParts[1], out var realizationMode)
                        && Enum.TryParse<FileAccessMode>(fileNameParts[2], out var accessMode))
                    {
                        data = new VfsFilePlacementData(hash, realizationMode, accessMode);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    new ErrorResult(ex).IgnoreFailure();
                    return false;
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a CAS relative path for the given placement data
        /// </summary>
        public static string CreateCasRelativePath(VfsFilePlacementData data, int nodeIndex)
        {
            var hashHex = data.Hash.ToHex();
            return $@"{data.Hash.HashType}\{hashHex.Substring(0, 3)}\{hashHex}-{data.RealizationMode}-{data.AccessMode}-{nodeIndex}.blob";
        }
    }
}
