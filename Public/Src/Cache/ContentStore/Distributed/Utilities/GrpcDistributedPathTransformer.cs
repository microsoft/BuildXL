// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Path transformer for distributed file copies.
    /// </summary>
    public class GrpcDistributedPathTransformer : IAbsolutePathTransformer
    {
        private static readonly string _localMachineName = Environment.MachineName;
        internal const string BlobFileExtension = ".blob";

        /// <inheritdoc />
        public AbsolutePath GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent)
        {
            string contentHashString = contentHash.ToHex();
            var pathRoot = new AbsolutePath(Encoding.UTF8.GetString(contentLocationIdContent));
            return pathRoot / contentHash.HashType.ToString() / contentHashString.Substring(0, 3) / (contentHashString + BlobFileExtension);
        }

        /// <inheritdoc />
        public byte[] GetLocalMachineLocation(AbsolutePath cacheRoot)
        {
            if (!cacheRoot.IsLocal)
            {
                throw new ArgumentException($"Local cache root must be a local path. Found {cacheRoot}.");
            }

            if (!cacheRoot.GetFileName().Equals(Constants.SharedDirectoryName))
            {
                cacheRoot = cacheRoot / Constants.SharedDirectoryName;
            }

            string networkPathRoot = cacheRoot.Path.Replace(":", "$");

            if (OperatingSystemHelper.IsWindowsOS)
            {
                // Only unify paths along casing if on Windows
                networkPathRoot = Path.Combine(@"\\" + _localMachineName, networkPathRoot).ToUpperInvariant();
            }
            else
            {
                // Path.Combine ignores the first parameter if the second is a rooted path. To get the machine name before the rooted network path, the combination must be done manually.
                networkPathRoot = Path.Combine(Path.DirectorySeparatorChar + _localMachineName, networkPathRoot.TrimStart(Path.DirectorySeparatorChar));
            }

            return Encoding.UTF8.GetBytes(networkPathRoot);
        }

        /// <inheritdoc />
        public byte[] GetPathLocation(AbsolutePath path)
        {
            var tempPath = path;
            while (tempPath != null && !tempPath.Path.EndsWith(Constants.SharedDirectoryName, StringComparison.OrdinalIgnoreCase) || tempPath.IsRoot)
            {
                tempPath = tempPath.GetParentPath<AbsolutePath>();
            }

            if (tempPath.IsRoot)
            {
                throw new ArgumentException("Rooted path does not match a Cache root");
            }

            return Encoding.UTF8.GetBytes(tempPath.Path.ToUpperInvariant());
        }
    }
}
