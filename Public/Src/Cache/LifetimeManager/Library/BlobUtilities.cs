// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    internal static class BlobUtilities
    {
        /// <summary>
        /// Extracts the name of a blob, without the ".blob" extension.
        /// </summary>
        public static bool TryExtractContentHashFromBlobName(string fullName, [NotNullWhen(true)] out string? blobName)
        {
            if (!fullName.EndsWith(".blob"))
            {
                blobName = null;
                return false;
            }

            blobName = fullName[..^".blob".Length];
            return true;
        }
    }
}
