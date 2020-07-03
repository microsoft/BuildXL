// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class GetHostNameTests
    {
        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void TestLocalWindowsPath()
        {
            Assert.Equal("localhost", GetHostFromAbsolutePath(new AbsolutePath(@"C:\absolute\path")));
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void TestRemoteWindowsPath()
        {
            Assert.Equal("TestMachineName", GetHostFromAbsolutePath(new AbsolutePath(@"\\TestMachineName\absolute\path")));
        }

        [Fact]
        public void TestLocalLinuxPath()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                Assert.Equal("localhost", GetHostFromAbsolutePath(new AbsolutePath(@"/localhost/absolute/path")));
            }
        }

        [Fact]
        public void TestRemoteLinuxPath()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                Assert.Equal("TestMachineName", GetHostFromAbsolutePath(new AbsolutePath(@"/TestMachineName/absolute/path")));
            }
        }

        private string GetHostFromAbsolutePath(AbsolutePath path)
        {
            return GrpcFileCopier.GetHostName(path.IsLocal, path.GetSegments());
        }
    }
}