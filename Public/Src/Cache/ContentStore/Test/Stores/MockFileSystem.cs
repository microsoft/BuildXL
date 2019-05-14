// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;

namespace ContentStoreTest.Stores
{
    public sealed class MockFileSystem : MemoryFileSystem
    {
        public MockFileSystem(ITestClock clock) : base(clock)
        {
        }

        //public override bool DirectoryExists(AbsolutePath path)
        //{
        //    return true;
        //}

        public IEnumerable<FileInfo> EnumerateFilesResult { get; set; }

        public override IEnumerable<FileInfo> EnumerateFiles(AbsolutePath path, EnumerateOptions options)
        {
            if (EnumerateFilesResult == null)
            {
                return base.EnumerateFiles(path, options);
            }

            return EnumerateFilesResult;
        }

        //public override void MoveDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
        //{
            
        //}

        //public override void CreateDirectory(AbsolutePath path)
        //{
            
        //}

        //public override void DeleteDirectory(AbsolutePath path, DeleteOptions deleteOptions)
        //{
        //}
    }
}
