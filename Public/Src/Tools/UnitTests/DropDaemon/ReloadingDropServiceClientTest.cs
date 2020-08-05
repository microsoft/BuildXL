// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Drop.App.Core;
using Microsoft.VisualStudio.Services.Drop.WebApi;
using Microsoft.VisualStudio.Services.ItemStore.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.ServicePipDaemon;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.DropDaemon
{
    public class ReloadingDropServiceClientTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private ITestOutputHelper Output { get; }

        public ReloadingDropServiceClientTest(ITestOutputHelper output)
            : base(output)
        {
            Output = output;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestNoAuthExceptionThrownMeansNoReloading(bool operationReturnsResult)
        {
            var reloadingClient = new ReloadingDropServiceClient(
                logger: TestLogger,
                clientConstructor: () => new MockDropServiceClient(dropOperation: () => { }));

            await CallDropOperationAsync(reloadingClient, operationReturnsResult);
            XAssert.AreEqual(1, reloadingClient.Reloader.CurrentVersion);

            await CallDropOperationAsync(reloadingClient, operationReturnsResult);
            await CallDropOperationAsync(reloadingClient, operationReturnsResult);
            await CallDropOperationAsync(reloadingClient, operationReturnsResult);
            XAssert.AreEqual(1, reloadingClient.Reloader.CurrentVersion);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(4, true)]
        [InlineData(1, false)]
        [InlineData(4, false)]
        public async Task TestAuthExceptionThrownFromDropOperation(int numOfAuthFailures, bool operationReturnsResult)
        {
            int counter = 0;
            var reloadingClient = new ReloadingDropServiceClient(
                logger: TestLogger,
                clientConstructor: () => new MockDropServiceClient(
                    dropOperation: () =>
                    {
                        counter++;
                        if (counter <= numOfAuthFailures)
                        {
                            ThrowUnauthorizedException();
                        }
                    }));

            await CallDropOperationAsync(reloadingClient, operationReturnsResult);
            XAssert.AreEqual(numOfAuthFailures + 1, reloadingClient.Reloader.CurrentVersion);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(4, true)]
        [InlineData(1, false)]
        [InlineData(4, false)]
        public async Task TestAuthExceptionThrownFromDropClientConstructor(int numOfAuthFailures, bool operationReturnsResult)
        {
            Action<int> maybeThrow = (cnt) =>
            {
                // first time it must succeed; in real use cases, failing the first time around would mean that the
                // user indeed is not authorized, in which case we should fail.
                // At some point later, say, session token expired, so Auth exception is thrown.
                if (cnt > 1 && cnt <= numOfAuthFailures + 1)
                {
                    TestLogger.Verbose("cnt = {0}, throwing", cnt);
                    ThrowUnauthorizedException();
                }
            };

            int counter = 0;
            var reloadingClient = new ReloadingDropServiceClient(
                logger: TestLogger,
                clientConstructor: () =>
                {
                    maybeThrow(++counter);
                    return new MockDropServiceClient(dropOperation: () =>
                    {
                        maybeThrow(++counter);
                    });
                });

            await CallDropOperationAsync(reloadingClient, operationReturnsResult);
            XAssert.AreEqual(2, reloadingClient.Reloader.CurrentVersion);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestPermanentAuthException(bool operationReturnsResult)
        {
            var retryIntervals = new[] { TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20) };
            var reloadingClient = new ReloadingDropServiceClient(
                logger: TestLogger,
                retryIntervals: retryIntervals,
                clientConstructor: () => new MockDropServiceClient(dropOperation: ThrowUnauthorizedException));
            await Assert.ThrowsAsync<VssUnauthorizedException>(() => CallDropOperationAsync(reloadingClient, operationReturnsResult));
            XAssert.AreEqual(retryIntervals.Length + 1, reloadingClient.Reloader.CurrentVersion);
        }

        private IIpcLogger TestLogger => new LambdaLogger((level, format, args) => Output.WriteLine(LoggerExtensions.Format(level, format, args)));

        private Task CallDropOperationAsync(ReloadingDropServiceClient reloadingClient, bool operationReturnsResult)
        {
            return operationReturnsResult
                // test Task<U> RetryAsync<U> 
                ? reloadingClient.CreateAsync("name", true, null, false, CancellationToken.None)
                // test Task RetryAsync
                : reloadingClient.DownloadAsync("name", null, CancellationToken.None, false);
        }

        private void ThrowUnauthorizedException()
        {
            throw new VssUnauthorizedException();
        }
    }

    internal class MockDropServiceClient : IDropServiceClient
    {
        private readonly Action m_dropOperation;

        public MockDropServiceClient(Action dropOperation)
        {
            AppDomain.CurrentDomain.AssemblyResolve += MockDropClient.CurrentDomain_AssemblyResolve;
            m_dropOperation = dropOperation;
        }

        Task<DropItem> IDropServiceClient.CreateAsync(string dropName, bool isAppendOnly, DateTime? expirationDate, bool chunkDedup, CancellationToken cancellationToken)
        {
            m_dropOperation();
            return Task.FromResult(new DropItem());
        }

        Task IDropDownloader.DownloadAsync(string dropName, DropServiceClientDownloadContext downloadContext, CancellationToken cancellationToken, bool releaseLocalCache)
        {
            m_dropOperation();
            return Task.CompletedTask;
        }

        #region Unimplemented/Unused methods
        uint IDropServiceClient.AttemptNumber
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        bool IDropServiceClient.DisposeTelemetry
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        Task<Tuple<IEnumerable<BlobIdentifier>, AssociationsStatus>> IDropServiceClient.AssociateAsync(string dropName, List<FileBlobDescriptor> preComputedBlobIds, bool abortIfAlreadyExists, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<bool> IDropServiceClient.DeleteAsync(DropItem dropItem, bool synchronous, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        void IDisposable.Dispose()
        {
            throw new NotImplementedException();
        }

        Task<DownloadResult> IDropDownloader.DownloadManifestToFilePathAsync(string dropName, string filePath, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IDropServiceClient.DownloadFilesAsync(IEnumerable<BlobToFileMapping> mappings, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IDropServiceClient.FinalizeAsync(string dropName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<DropItem> IDropServiceClient.GetDropAsync(string dropName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<Uri> IDropServiceClient.GetDropUri(string dropName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<IEnumerable<DropItem>> IDropServiceClient.ListAsync(string dropNamePrefix, PathOptions pathOptions, bool includeNonFinalizedDrops, CancellationToken cancellationToken, RetrievalOptions retrievalOptions, SizeOptions sizeOptions, ExpirationDateOptions expirationDateOptions, IDomainId domainId)
        {
            throw new NotImplementedException();
        }

        Task IDropServiceClient.UploadAndAssociateAsync(string dropName, List<FileBlobDescriptor> preComputedBlobIds, bool abortIfAlreadyExists, AssociationsStatus firstAssociationStatus, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<IAsyncEnumerator<IEnumerable<BlobToFileMapping>>> IDropServiceClient.ListFilePagesAsync(string dropName, bool tryToRetrieveFromLocalCache, CancellationToken cancellationToken, bool allowPartial, IEnumerable<string> directories, bool recursive, bool getDownloadUris)
        {
            throw new NotImplementedException();
        }

        Task IDropServiceClient.PublishAsync(string dropName, string sourceDirectory, bool abortIfAlreadyExists, List<FileBlobDescriptor> preComputedBlobIds, Action<FileBlobDescriptor> hashCompleteCallback, bool includeEmptyDirectories, bool lowercasePaths, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IDropServiceClient.UpdateExpirationAsync(string dropName, DateTime? expirationTime, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        string IDropServiceClient.GetVersionString()
        {
            throw new NotImplementedException();
        }

        public Task RepairManifestAsync(string dropName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<DropItem> CreateAsync(IDomainId domainId, string dropName, bool isAppendOnly, DateTime? expirationDate, bool chunkDedup, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<MultiDomainInfo>> GetDomainsAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
