// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using ContentStoreTest.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Microsoft.WindowsAzure.Storage.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using Microsoft.WindowsAzure.Storage;
using ContentStoreTest.Distributed.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    /// <summary>
    /// Wrapper around local storage instance.
    /// </summary>
    public sealed class AzuriteStorageProcess : IDisposable
    {
        private DisposableDirectory _tempDirectory;
        private PassThroughFileSystem _fileSystem;
        private ILogger _logger;

        private ProcessUtility _process;
        public string ConnectionString { get; private set; }

        private bool _disposed;
        private LocalRedisFixture _storageFixture;
        private int _portNumber;

        internal bool Closed { get; private set; }

        internal bool Initialized => _fileSystem != null;

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
        };

        internal AzuriteStorageProcess()
        {
        }

        private void Init(ILogger logger, LocalRedisFixture storageFixture)
        {
            _fileSystem = new PassThroughFileSystem(logger);
            _logger = logger;
            _tempDirectory = new DisposableDirectory(_fileSystem, "StorageTests");
            _storageFixture = storageFixture;
            _disposed = false;

            // The instance is re-initialized, so we need to re-register it for finalization to detect resource leaks.
            GC.ReRegisterForFinalize(this);
        }

        public override string ToString()
        {
            return ConnectionString;
        }

        /// <summary>
        /// Creates an empty instance of a database.
        /// </summary>
        public static AzuriteStorageProcess CreateAndStartEmpty(
            LocalRedisFixture storageFixture,
            ILogger logger)
        {
            return CreateAndStart(storageFixture, logger);
        }

        /// <summary>
        /// Creates an instance of a database with a given data.
        /// </summary>
        public static AzuriteStorageProcess CreateAndStart(
            LocalRedisFixture storageFixture,
            ILogger logger)
        {
            logger.Debug($"Fixture '{storageFixture.Id}' has {storageFixture.DatabasePool.ObjectsInPool} available storage databases.");
            var instance = storageFixture.EmulatorPool.GetInstance();
            var oldOrNew = instance.Instance._process != null ? "an old" : "a new";
            logger.Debug($"LocalStorageProcessDatabase: got {oldOrNew} instance from the pool.");

            var result = instance.Instance;
            if (result.Closed)
            {
                throw new ObjectDisposedException("instance", "The instance is already closed!");
            }

            result.Init(logger, storageFixture);
            try
            {
                result.Start();
                return result;
            }
            catch (Exception e)
            {
                logger.Error("Failed to start a local database. Exception=" + e);
                result.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose() => Dispose(close: false);

        /// <nodoc />
        public void Dispose(bool close)
        {
            if (close)
            {
                // Closing the instance and not returning it back to the pool.
                Close();
                return;
            }

            if (_disposed)
            {
                // The type should be safe for double dispose.
                return;
            }

            try
            {
                // Clear the containers in the storage account to allow reuse
                ClearAsync().GetAwaiter().GetResult();

            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"Exception connecting to clear storage process {_process.Id} with port {_portNumber}: {ex.ToString()}. Has process exited {_process.HasExited} with output {_process.GetLogs()}");
                Close();
            }

            _logger.Debug($"Returning database to pool in fixture '{_storageFixture.Id}'");
            _storageFixture.EmulatorPool.PutInstance(this);
            _disposed = true;
        }

        public async Task ClearAsync(string prefix = null)
        {
            AzureBlobStorageCredentials creds = new AzureBlobStorageCredentials(ConnectionString);
            var blobClient = creds.CreateCloudBlobClient();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var token = cts.Token;

            BlobContinuationToken continuation = null;
            while (!cts.IsCancellationRequested)
            {
                var containers = await blobClient.ListContainersSegmentedAsync(
                    prefix: prefix,
                    detailsIncluded: ContainerListingDetails.None,
                    maxResults: null,
                    continuation,
                    options: null,
                    operationContext: null,
                    cancellationToken: token);
                continuation = containers.ContinuationToken;

                foreach (var container in containers.Results)
                {
                    await container.DeleteIfExistsAsync(accessCondition: null, options: null, operationContext: null, cancellationToken: token);
                }

                if (continuation == null)
                {
                    break;
                }
            }
        }

        ~AzuriteStorageProcess()
        {
            // If the emulator is not gracefully closed,
            // then BuildXL will fail because surviving blob.exe instance.
            // So we're failing fast instead and will print the process Id that caused the issue.
            // This may happen only if the database is not disposed gracefully.
            if (Initialized && !Closed)
            {
                string message = $"Storage process {_process?.Id} was not closed correctly.";

                _logger.Debug(message);
                throw new InvalidOperationException(message);
            }
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            GC.SuppressFinalize(this);

            if (_process != null)
            {
                _logger.Debug($"Killing the storage process {_process?.Id}...");
                SafeKillProcess();
            }

            _tempDirectory.Dispose();
            _fileSystem.Dispose();
            Closed = true;
        }

        private void SafeKillProcess()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                    _logger.Debug("The storage process is killed");
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Start()
        {
            // Can reuse an existing process only when this instance successfully created a connection to it.
            // Otherwise the test will fail with NRE.
            if (_process != null)
            {
                _logger.Debug("Storage process is already running. Reusing an existing instance.");
                return;
            }

            _logger.Debug("Starting a storage server.");


            var storageName = (OperatingSystemHelper.IsWindowsOS ? "tools/win-x64/blob.exe"
                : (OperatingSystemHelper.IsLinuxOS ? "tools/linux-x64/blob"
                : "tools/osx-x64/blob"));
            string storageServerPath = Path.GetFullPath(Path.Combine("azurite", storageName));
            if (!File.Exists(storageServerPath))
            {
                throw new InvalidOperationException($"Could not find {storageName} at {storageServerPath}");
            }

            _portNumber = 0;

            const int maxRetries = 10;
            for (int i = 0; i < maxRetries; i++)
            {
                var storageServerWorkspacePath = _tempDirectory.CreateRandomFileName();
                _fileSystem.CreateDirectory(storageServerWorkspacePath);
                _portNumber = PortExtensions.GetNextAvailablePort();

                var args = $"--blobPort {_portNumber} --location {storageServerWorkspacePath}";
                _logger.Debug($"Running cmd=[{storageServerPath} {args}]");

                _process = new ProcessUtility(storageServerPath, args, createNoWindow: true, workingDirectory: Path.GetDirectoryName(storageServerPath));

                _process.Start();

                string processOutput;
                if (_process == null)
                {
                    processOutput = "[Process could not start]";
                    throw new InvalidOperationException(processOutput);
                }

                if (_process.HasExited)
                {
                    if (_process.WaitForExit(5000))
                    {
                        throw new InvalidOperationException(_process.GetLogs());
                    }

                    throw new InvalidOperationException("Process or either wait handle timed out. " + _process.GetLogs());
                }

                processOutput = $"[Process {_process.Id} is still running]";

                _logger.Debug("Process output: " + processOutput);

                ConnectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:{_portNumber}/devstoreaccount1;";

                AzureBlobStorageCredentials creds = new AzureBlobStorageCredentials(ConnectionString);

                var client = creds.CreateCloudBlobClient();
                try
                {
                    bool exists = client.GetContainerReference("test").ExistsAsync(DefaultBlobStorageRequestOptions, null).GetAwaiter().GetResult();
                    break;
                }
                catch (StorageException ex)
                {
                    SafeKillProcess();
                    _logger.Debug($"Retrying for exception connecting to storage process {_process.Id} with port {_portNumber}: {ex.ToString()}. Has process exited {_process.HasExited} with output {_process.GetLogs()}");

                    if (i != maxRetries - 1)
                    {
                        Thread.Sleep(300);
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    SafeKillProcess();
                    _logger.Error(
                        $"Exception connecting to storage process {_process.Id} with port {_portNumber}: {ex.ToString()}. Has process exited {_process.HasExited} with output {_process.GetLogs()}");
                    throw;
                }
            }

            _logger.Debug($"Storage server {_process.Id} is up and running at port {_portNumber}.");
        }

    }
}
