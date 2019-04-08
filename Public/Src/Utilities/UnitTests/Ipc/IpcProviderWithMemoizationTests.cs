// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using Xunit;

namespace Test.BuildXL.Ipc
{
    public class IpcProviderWithMemoizationTests
    {
        [Fact]
        public void TestLoadMoniker()
        {
            var mockProvider = new MockProvider();
            using (var provider = new IpcProviderWithMemoization(mockProvider))
            {
                // load some monikers
                var id = Guid.NewGuid().ToString();
                var moniker1 = provider.LoadOrCreateMoniker(id);
                var moniker2 = provider.LoadOrCreateMoniker(id);

                // assert they are equal and that no underlying moniker was created
                Assert.Equal(moniker1, moniker2);
                Assert.Equal(0, mockProvider.NumCreateMonikerCalls);

                // serialize those monikers
                provider.RenderConnectionString(moniker1);
                provider.RenderConnectionString(moniker2);

                // assert that exactly 1 underlying moniker was created
                Assert.Equal(1, mockProvider.NumCreateMonikerCalls);
                Assert.Equal(1, mockProvider.NumRenderConnectionStringCalls);
            }
        }

        [Fact]
        public void TestCreateMoniker()
        {
            var mockProvider = new MockProvider();
            using (var provider = new IpcProviderWithMemoization(mockProvider))
            {
                // create some monikers
                var moniker1 = provider.CreateNewMoniker();
                var moniker2 = provider.CreateNewMoniker();

                // assert they are not equal and that no underlying moniker was created
                Assert.NotEqual(moniker1, moniker2);
                Assert.Equal(0, mockProvider.NumCreateMonikerCalls);

                // serialize those monikers
                provider.RenderConnectionString(moniker1);
                provider.RenderConnectionString(moniker2);

                // assert that exactly 2 underlying monikers were created
                Assert.Equal(2, mockProvider.NumCreateMonikerCalls);
                Assert.Equal(2, mockProvider.NumRenderConnectionStringCalls);
            }
        }

        private void AssertThrowsInvalidMoniker(Action a)
        {
            var ex = Assert.Throws<IpcException>(() => a());
            Assert.Equal(IpcException.IpcExceptionKind.InvalidMoniker, ex.Kind);
        }

        internal class MockProvider : IIpcProvider
        {
            internal int NumCreateMonikerCalls = 0;
            internal int NumRenderConnectionStringCalls = 0;

            public IIpcMoniker CreateNewMoniker()
            {
                NumCreateMonikerCalls++;
                return null;
            }

            public string RenderConnectionString(IIpcMoniker moniker)
            {
                NumRenderConnectionStringCalls++;
                return string.Empty;
            }

            public IIpcMoniker LoadOrCreateMoniker(string monikerId)
            {
                throw new NotImplementedException();
            }

            public IClient GetClient(string connectionString, IClientConfig config)
            {
                throw new NotImplementedException();
            }

            public IServer GetServer(string connectionString, IServerConfig config)
            {
                throw new NotImplementedException();
            }
        }
    }
}
