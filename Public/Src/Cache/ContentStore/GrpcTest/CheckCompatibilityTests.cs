// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using Xunit;

namespace ContentStoreTest.Grpc
{
    public class CheckCompatibility
    {
        [Fact]
        public void TestCompatibilityCheck()
        {
            GrpcClientBase.CheckCompatibility(Capabilities.All, Capabilities.ContentOnly).ShouldBeError();

            // ContentOnly is compatible with no capability because none of them are considered required.
            GrpcClientBase.CheckCompatibility(Capabilities.None, Capabilities.ContentOnly).ShouldBeSuccess();

            GrpcClientBase.CheckCompatibility(Capabilities.Memoization, Capabilities.All).ShouldBeSuccess();

            GrpcClientBase.CheckCompatibility(Capabilities.None, Capabilities.All).ShouldBeSuccess();
        }
    }
}
