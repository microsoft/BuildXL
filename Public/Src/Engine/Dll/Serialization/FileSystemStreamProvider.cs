// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Engine.Serialization
{
    /// <summary>
    /// Provides access to streams based on path
    /// </summary>
    public class FileSystemStreamProvider
    {
        /// <summary>
        /// The default file system stream provider
        /// </summary>
        public static readonly FileSystemStreamProvider Default = new FileSystemStreamProvider();

        /// <summary>
        /// Opens the stream at the given path with read access
        /// </summary>
        public virtual Disposable<Stream> OpenReadStream(string path)
        {
            return new Disposable<Stream>(
                FileUtilities.CreateFileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read | FileShare.Delete,
                            // Ok to evict the file from standby since the file will be overwritten and never reread from disk after this point.
                            FileOptions.SequentialScan),
                null);
        }
    }
}
