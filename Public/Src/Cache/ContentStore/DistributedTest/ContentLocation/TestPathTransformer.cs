// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;

namespace ContentStoreTest.Distributed.ContentLocation
{
    public class TestPathTransformer : IPathTransformer<AbsolutePath>
    {
        public byte[] GetLocalMachineLocation(AbsolutePath cacheRoot)
        {
            return Encoding.Default.GetBytes(cacheRoot.Path.ToCharArray());
        }

        public virtual AbsolutePath GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent)
        {
            string rootPath = new string(Encoding.Default.GetChars(contentLocationIdContent));
            return PathUtilities.GetContentPath(rootPath, contentHash);
        }

        public byte[] GetPathLocation(AbsolutePath path)
        {
            string rootPath = PathUtilities.GetRootPath(path);
            return Encoding.Default.GetBytes(rootPath.ToCharArray());
        }
    }
}
