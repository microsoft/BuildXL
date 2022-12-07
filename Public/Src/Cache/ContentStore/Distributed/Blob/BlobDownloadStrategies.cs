// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Tracing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    internal abstract class BlobDownloadStrategyBase : IBlobDownloadStrategy
    {
        protected BlobDownloadStrategyConfiguration Configuration { get; }
        protected IAbsFileSystem FileSystem { get; }

        private readonly IRetryPolicy _retryPolicy;

        public BlobDownloadStrategyBase(BlobDownloadStrategyConfiguration configuration, IAbsFileSystem? fileSystem = null)
        {
            Configuration = configuration;
            FileSystem = fileSystem ?? PassThroughFileSystem.Default;

            var retryPolicyConfiguration = Configuration.RetryPolicyConfiguration ?? RetryPolicyConfiguration.Default;
            _retryPolicy = retryPolicyConfiguration.AsRetryPolicy(IsExceptionTransient);
        }

        protected virtual bool IsExceptionTransient(Exception exception)
        {
            // NOTE: by default, we assume every exception is non-retryable. The Azure Blob Storage SDK based methods
            // have their own internal retry policy, so this is not needed for them.
            return false;
        }

        public Task<RemoteDownloadResult> DownloadAsync(OperationContext context, RemoteDownloadRequest downloadRequest)
        {
            return _retryPolicy.ExecuteAsync(async () =>
            {
                return await RemoteDownloadAsync(context, downloadRequest);
            }, context.Token);
        }

        public abstract Task<RemoteDownloadResult> RemoteDownloadAsync(OperationContext context, RemoteDownloadRequest downloadRequest);

        protected FileStream OpenFileStream(AbsolutePath path, long length, bool randomAccess)
        {
            Contract.Requires(length >= 0);

            var flags = FileOptions.Asynchronous;
            if (randomAccess)
            {
                flags |= FileOptions.RandomAccess;
            }
            else
            {
                flags |= FileOptions.SequentialScan;
            }

            var stream = FileSystem.OpenForWrite(
                            path,
                            length,
                            FileMode.Create,
                            FileShare.ReadWrite,
                            flags).Stream;

            return (stream as FileStream)!;
        }

        protected static TimeSpan? GetWriteDurationIfAvailable(Stream stream)
        {
            if (stream is TrackingFileStream trackingFileStream)
            {
                return trackingFileStream.WriteDuration;
            }

            return null;
        }
    }

    internal sealed class BlobSdkDownloadToFileStrategy : BlobDownloadStrategyBase
    {
        public BlobSdkDownloadToFileStrategy(BlobDownloadStrategyConfiguration configuration, IAbsFileSystem? fileSystem = null) : base(configuration, fileSystem)
        {
        }

        public override async Task<RemoteDownloadResult> RemoteDownloadAsync(OperationContext context, RemoteDownloadRequest downloadRequest)
        {
            var stopwatch = StopwatchSlim.Start();

            // NOTE: can't use this because of ordering of operations
            try
            {
                await downloadRequest.Reference.DownloadToFileAsync(
                    downloadRequest.AbsolutePath.ToString(),
                    FileMode.Create,
                    accessCondition: null,
                    options: null,
                    operationContext: null,
                    cancellationToken: context.Token);
            }
            catch (StorageException exception) when (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
            {
                return new RemoteDownloadResult()
                {
                    ResultCode = PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                    DownloadResult = new DownloadResult()
                    {
                        DegreeOfParallelism = 1,
                        DownloadDuration = stopwatch.Elapsed,
                    },
                };
            }

            return new RemoteDownloadResult()
            {
                ResultCode = PlaceFileResult.ResultCode.PlacedWithCopy,
                DownloadResult = new DownloadResult()
                {
                    DegreeOfParallelism = 1,
                    DownloadDuration = stopwatch.Elapsed,
                },
            };
        }
    }

    internal sealed class BlobSdkDownloadToStreamStrategy : BlobDownloadStrategyBase
    {
        public BlobSdkDownloadToStreamStrategy(BlobDownloadStrategyConfiguration configuration, IAbsFileSystem? fileSystem = null) : base(configuration, fileSystem)
        {
        }

        public override async Task<RemoteDownloadResult> RemoteDownloadAsync(OperationContext context, RemoteDownloadRequest downloadRequest)
        {
            var stopwatch = StopwatchSlim.Start();

            Stream maybeRemoteStream;
            try
            {
                maybeRemoteStream = await downloadRequest.Reference.OpenReadAsync(accessCondition: null, options: null, operationContext: null, cancellationToken: context.Token);
            }
            catch (StorageException exception) when (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
            {
                return new RemoteDownloadResult()
                {
                    ResultCode = PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                    DownloadResult = new DownloadResult()
                    {
                        DegreeOfParallelism = 1,
                        DownloadDuration = stopwatch.Elapsed,
                    },
                };
            }

            var timeToFirstByteDuration = stopwatch.ElapsedAndReset();

            using var remoteStream = maybeRemoteStream;

            using var fileStream = OpenFileStream(downloadRequest.AbsolutePath, remoteStream.Length, randomAccess: false);

            var openFileStreamDuration = stopwatch.ElapsedAndReset();

            await remoteStream.CopyToAsync(fileStream, Configuration.FileDownloadBufferSize, context.Token);

            var downloadDuration = stopwatch.ElapsedAndReset();

            return new RemoteDownloadResult()
            {
                ResultCode = PlaceFileResult.ResultCode.PlacedWithCopy,
                FileSize = remoteStream.Length,
                TimeToFirstByteDuration = timeToFirstByteDuration,
                DownloadResult = new DownloadResult()
                {
                    DegreeOfParallelism = 1,
                    OpenFileStreamDuration = openFileStreamDuration,
                    DownloadDuration = downloadDuration,
                    WriteDuration = GetWriteDurationIfAvailable(fileStream),
                },
            };
        }
    }

    public class HttpClientDownloadException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public HttpClientDownloadException(HttpStatusCode code)
        {
            StatusCode = code;
        }

        public HttpClientDownloadException(HttpStatusCode code, string? message) : base(message)
        {
            StatusCode = code;
        }

        public HttpClientDownloadException(HttpStatusCode code, string? message, Exception? innerException) : base(message, innerException)
        {
            StatusCode = code;
        }
    }

    internal abstract class HttpClientDownloadStrategyBase : BlobDownloadStrategyBase
    {
        protected static HttpClient HttpClient { get; } = new HttpClient();

        private readonly IClock _clock;

        /// <summary>
        /// Based upon https://docs.microsoft.com/en-us/azure/architecture/best-practices/retry-service-specific#general-rest-and-retry-guidelines
        /// </summary>
        private static readonly int[] RetryableStatusCodes = new int[]
        {
            (int)HttpStatusCode.RequestTimeout,
            (int)429, // HttpStatusCode.TooManyRequests does not exist in netstandard
            (int)HttpStatusCode.InternalServerError,
            (int)HttpStatusCode.BadGateway,
            (int)HttpStatusCode.ServiceUnavailable,
            (int)HttpStatusCode.GatewayTimeout,
        };

        protected HttpClientDownloadStrategyBase(BlobDownloadStrategyConfiguration configuration, IClock clock, IAbsFileSystem? fileSystem = null) : base(configuration, fileSystem)
        {
            _clock = clock;
        }

        protected override bool IsExceptionTransient(Exception exception)
        {
            if (exception is HttpClientDownloadException downloadException && RetryableStatusCodes.Contains((int)downloadException.StatusCode))
            {
                return true;
            }

            return base.IsExceptionTransient(exception);
        }

        protected string CreateDownloadUrl(RemoteDownloadRequest downloadRequest)
        {
            var downloadUrl = downloadRequest.Reference.Uri.AbsoluteUri;

            // If we created the reference using a client that auths through SAS tokens, the download URL should already contain all necessary auth information.
            if (downloadRequest.Reference.ServiceClient.Credentials.IsSAS)
            {
                downloadUrl += downloadRequest.Reference.ServiceClient.Credentials.SASToken;
            }
            else
            {
                var sasUrlQuery = downloadRequest.Reference.GetSharedAccessSignature(new SharedAccessBlobPolicy()
                {
                    Permissions = SharedAccessBlobPermissions.Read,
                    SharedAccessExpiryTime = _clock.UtcNow + TimeSpan.FromDays(1),
                });

                downloadUrl += sasUrlQuery;
            }

            return downloadUrl;
        }
    }

    internal sealed class HttpClientDownloadToStreamStrategy : HttpClientDownloadStrategyBase
    {
        public HttpClientDownloadToStreamStrategy(BlobDownloadStrategyConfiguration configuration, IClock clock, IAbsFileSystem? fileSystem = null) : base(configuration, clock, fileSystem)
        {
        }

        public override async Task<RemoteDownloadResult> RemoteDownloadAsync(OperationContext context, RemoteDownloadRequest downloadRequest)
        {
            var stopwatch = StopwatchSlim.Start();

            using var request = new HttpRequestMessage(HttpMethod.Get, CreateDownloadUrl(downloadRequest));
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.Token);
            var timeToFirstByteDuration = stopwatch.ElapsedAndReset();

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new RemoteDownloadResult()
                {
                    ResultCode = PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                    TimeToFirstByteDuration = timeToFirstByteDuration,
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpClientDownloadException(response.StatusCode);
            }

            var fileSize = response.Content.Headers.ContentLength ?? -1;

            using var fileStream = OpenFileStream(downloadRequest.AbsolutePath, fileSize, randomAccess: false);

            var openFileStreamDuration = stopwatch.ElapsedAndReset();

#if NETCOREAPP3_1_OR_GREATER
            await response.Content.CopyToAsync(fileStream, cancellationToken: context.Token);
#else
            await response.Content.CopyToAsync(fileStream);
#endif

            var downloadDuration = stopwatch.ElapsedAndReset();

            return new RemoteDownloadResult()
            {
                ResultCode = PlaceFileResult.ResultCode.PlacedWithCopy,
                FileSize = fileSize,
                TimeToFirstByteDuration = timeToFirstByteDuration,
                DownloadResult = new DownloadResult()
                {
                    DegreeOfParallelism = 1,
                    OpenFileStreamDuration = openFileStreamDuration,
                    DownloadDuration = downloadDuration,
                    WriteDuration = GetWriteDurationIfAvailable(fileStream),
                },
            };
        }
    }

    internal sealed class HttpClientDownloadToMemoryMappedFileStrategy : HttpClientDownloadStrategyBase
    {
        public HttpClientDownloadToMemoryMappedFileStrategy(BlobDownloadStrategyConfiguration configuration, IClock clock, IAbsFileSystem? fileSystem = null) : base(configuration, clock, fileSystem)
        {
        }

        public override async Task<RemoteDownloadResult> RemoteDownloadAsync(OperationContext context, RemoteDownloadRequest downloadRequest)
        {
            var stopwatch = StopwatchSlim.Start();

            using var request = new HttpRequestMessage(HttpMethod.Get, CreateDownloadUrl(downloadRequest));
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.Token);
            var timeToFirstByteDuration = stopwatch.ElapsedAndReset();

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new RemoteDownloadResult()
                {
                    ResultCode = PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                    TimeToFirstByteDuration = timeToFirstByteDuration,
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpClientDownloadException(response.StatusCode);
            }

            var fileSize = response.Content.Headers.ContentLength ?? -1;

            using var fileStream = OpenFileStream(downloadRequest.AbsolutePath, fileSize, randomAccess: false);

            var openFileStreamDuration = stopwatch.ElapsedAndReset();

            // TODO: if the file size is > 4GB, the following memory map will fail because Windows is at most able to
            // map 4GB of data at a time.
            using var mmap = MemoryMappedFile.CreateFromFile(
                (fileStream as FileStream)!,
                mapName: null,
                capacity: fileSize,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: false);

            // WARNING: we do NOT dispose the following object on _purpose_. Disposing it causes us to wait until the
            // OS has written down the contents of the mmap file to disk, which is not how FileStream et al. work.
            var mmapStream = mmap.CreateViewStream();
            using var _ = mmapStream.SafeMemoryMappedViewHandle;

            var memoryMapDuration = stopwatch.ElapsedAndReset();

#if NETCOREAPP3_1_OR_GREATER
            await response.Content.CopyToAsync(mmapStream, cancellationToken: context.Token);
#else
            await response.Content.CopyToAsync(mmapStream);
#endif

            var downloadDuration = stopwatch.ElapsedAndReset();

            return new RemoteDownloadResult()
            {
                ResultCode = PlaceFileResult.ResultCode.PlacedWithCopy,
                FileSize = fileSize,
                TimeToFirstByteDuration = timeToFirstByteDuration,
                DownloadResult = new DownloadResult()
                {
                    DegreeOfParallelism = 1,
                    OpenFileStreamDuration = openFileStreamDuration,
                    MemoryMapDuration = memoryMapDuration,
                    DownloadDuration = downloadDuration,
                    WriteDuration = GetWriteDurationIfAvailable(fileStream),
                },
            };
        }
    }
}
