// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Ipc.Common;
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
                var moniker1 = IpcMoniker.Create(id);
                var moniker2 = IpcMoniker.Create(id);

                // assert they are equal and that no underlying moniker was created
                Assert.Equal(moniker1, moniker2);

                // serialize those monikers
                provider.RenderConnectionString(moniker1);
                provider.RenderConnectionString(moniker2);

                // assert that exactly 1 underlying moniker was created
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
                var moniker1 = IpcMoniker.CreateNew();
                var moniker2 = IpcMoniker.CreateNew();
                Assert.NotEqual(moniker1, moniker2);

                // serialize those monikers
                provider.RenderConnectionString(moniker1);
                provider.RenderConnectionString(moniker2);

                // assert that exactly 2 calls to render connection string we mde
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
            internal int NumRenderConnectionStringCalls = 0;

            public string RenderConnectionString(IpcMoniker moniker)
            {
                NumRenderConnectionStringCalls++;
                return string.Empty;
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
