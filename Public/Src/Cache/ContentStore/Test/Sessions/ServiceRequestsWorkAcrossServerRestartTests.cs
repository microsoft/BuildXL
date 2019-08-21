// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Sessions
{
    public abstract class ServiceRequestsWorkAcrossServerRestartTests<T> : ServiceClientContentSessionTestBase<T>
        where T : ServiceClientContentStore, ITestServiceClientContentStore
    {
        protected const uint RetrySeconds = 1;
        protected const uint RetryCount = 10;

        protected ServiceRequestsWorkAcrossServerRestartTests(string scenario, ITestOutputHelper output)
            : base(scenario, output)
        {
        }

        [Fact]
        public Task PinSurvivesRestartingServer()
        {
            return RunSessionTestAsync(ImplicitPin.None, async (context, session) =>
            {
                // Put some random content for requests that want to use it.
                var r1 = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                // Pin the content - we'll make sure the pin survives the server restart below.
                await session.PinAsync(context, r1.ContentHash, Token).ShouldBeSuccess();

                // Restart the server.
                ITestServiceClientContentStore store = ((TestServiceClientContentSession)session).Store;
                await store.RestartServerAsync(context);

                // Put content until LRU has to remove content.
                for (var i = 0; i < 3 * 2; i++)
                {
                    long size = (MaxSize - ContentByteCount) / 3;
                    await session.PutRandomAsync(context, ContentHashType, false, size, Token).ShouldBeSuccess();
                }

                // Verify pinning above survived the server restart and LRU.
                var r4 = await session.OpenStreamAsync(context, r1.ContentHash, Token);
                r4.Stream.Should().NotBeNull();
                using (r4.Stream)
                {
                    r4.ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public Task BuildIdServicesRestartingServer()
        {
            var mockLogger = new MockLogger();
            Logger = mockLogger;
            var sessionId = Guid.NewGuid().ToString();

            // Creating session with build id in it.
            SessionName = $"{Constants.BuildIdPrefix}{sessionId}";

            return RunSessionTestAsync(ImplicitPin.None, async (context, session) =>
            {
                mockLogger.CurrentBuildId.Should().Be(sessionId);

                // Restart the server.
                ITestServiceClientContentStore store = ((TestServiceClientContentSession)session).Store;
                await store.RestartServerAsync(context);

                // Check that build id is still set.
                mockLogger.CurrentBuildId.Should().Be(sessionId);
            });
        }

        [Fact]
        public Task PinManyAcrossServerRestart()
        {
            return RunManyForSessionAcrossServerRestartAsync(async (context, session, contentHash, n) =>
            {
                var r = await session.PinAsync(context, contentHash, Token);
                r.ShouldBeSuccess();
            });
        }

        [Fact]
        public Task OpenStreamManyAcrossServerRestart()
        {
            return RunManyForSessionAcrossServerRestartAsync(async (context, session, contentHash, n) =>
            {
                var r = await session.OpenStreamAsync(context, contentHash, Token).ShouldBeSuccess();
                r.Stream.Dispose();
            });
        }

        [Fact]
        public async Task PlaceFileManyAcrossServerRestart()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                await RunManyForSessionAcrossServerRestartAsync(async (context, session, contentHash, n) =>
                {
                    var path = directory.Path / $"file-{n}.dat";
                    FileSystem.FileExists(path).Should().BeFalse();

                    var r = await session.PlaceFileAsync(
                        context,
                        contentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.HardLink,
                        Token);

                    r.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
                    FileSystem.FileExists(path).Should().BeTrue();
                });
            }
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutStreamRunManyAcrossServerRestart(bool provideHash)
        {
            return RunManyForSessionAcrossServerRestartAsync(async (context, session, contentHash, n) =>
            {
                await session.PutRandomAsync(context, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutFileRunManyAcrossServerRestart(bool provideHash)
        {
            return RunManyForSessionAcrossServerRestartAsync(async (context, session, contentHash, n) =>
            {
                await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();
            });
        }

        private Task RunManyForStoreAcrossServerRestartAsync(Func<Context, IContentStore, Task> requestFunc)
        {
            return RunStoreTestAsync(async (context, store) =>
            {
                // Launch a bunch of duplicate requests in the background, with server restart mixed in.
                var tasks = new List<Task>(101);
                tasks.AddRange(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
                    requestFunc(new Context(Logger), store))));
                tasks.Add(((ITestServiceClientContentStore)store).RestartServerAsync(context));
                tasks.AddRange(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
                    requestFunc(new Context(Logger), store))));

                try
                {
                    await TaskSafetyHelpers.WhenAll(tasks);
                }
                catch (AggregateException ex)
                {
                    AggregateException singleException = ex.Flatten();
                    string failureMessage = string.Join(",", singleException.InnerExceptions.Select(x => x.Message));
                    Assert.True(false, failureMessage);
                }
            });
        }

        private Task RunManyForSessionAcrossServerRestartAsync(Func<Context, IContentSession, ContentHash, int, Task> requestFunc)
        {
            return RunSessionTestAsync(ImplicitPin.PutAndGet, async (context, session) =>
            {
                // Put some random content for requests that want to use it.
                var r1 = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                // Launch a bunch of duplicate requests in the background, with server restart mixed in.
                var tasks = new List<Task>(101);
                tasks.AddRange(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
                    requestFunc(new Context(Logger), session, r1.ContentHash, i))));
                tasks.Add(((TestServiceClientContentSession)session).Store.RestartServerAsync(context));
                tasks.AddRange(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
                    requestFunc(new Context(Logger), session, r1.ContentHash, 50 + i))));

                try
                {
                    await TaskSafetyHelpers.WhenAll(tasks);
                }
                catch (AggregateException ex)
                {
                    AggregateException singleException = ex.Flatten();
                    string failureMessage = string.Join(",", singleException.InnerExceptions.Select(x => x.Message));
                    Assert.True(false, failureMessage);
                }
            });
        }

        private class MockLogger : IOperationLogger
        {
            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <inheritdoc />
            public Severity CurrentSeverity => Severity.Unknown;

            /// <inheritdoc />
            public int ErrorCount => 0;

            /// <inheritdoc />
            public void Flush()
            {
            }

            /// <inheritdoc />
            public void Always(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Fatal(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Error(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Error(Exception exception, string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void ErrorThrow(Exception exception, string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Warning(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Info(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Debug(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Debug(Exception exception)
            {
            }

            /// <inheritdoc />
            public void Diagnostic(string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void Log(Severity severity, string message)
            {
            }

            /// <inheritdoc />
            public void LogFormat(Severity severity, string messageFormat, params object[] messageArgs)
            {
            }

            /// <inheritdoc />
            public void OperationFinished(in OperationResult result)
            {
            }

            /// <inheritdoc />
            public void TrackMetric(in Metric metric)
            {
            }

            /// <inheritdoc />
            public void TrackTopLevelStatistic(in Statistic statistic)
            {
            }

            public string CurrentBuildId;

            /// <inheritdoc />
            public void RegisterBuildId(string buildId)
            {
                CurrentBuildId = buildId;
            }

            /// <inheritdoc />
            public void UnregisterBuildId()
            {
                CurrentBuildId = null;
            }
        }

    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    [Trait("Category", "WindowsOSOnly")] // These use named event handles, which are not supported in .NET core
    public class InProcessServiceRequestsWorkAcrossServerRestartTests : ServiceRequestsWorkAcrossServerRestartTests<TestInProcessServiceClientContentStore>
    {
       public InProcessServiceRequestsWorkAcrossServerRestartTests(ITestOutputHelper output)
           : base(nameof(InProcessServiceRequestsWorkAcrossServerRestartTests), output)
       {
       }

       protected InProcessServiceRequestsWorkAcrossServerRestartTests(string scenario, ITestOutputHelper output)
           : base(scenario, output)
       {
       }

       protected override TestInProcessServiceClientContentStore CreateStore(
           AbsolutePath rootPath,
           ContentStoreConfiguration configuration,
           LocalServerConfiguration localContentServerConfiguration,
           TimeSpan? heartbeatOverride)
       {
           configuration.Write(FileSystem, rootPath).Wait();

           var grpcPortFileName = Guid.NewGuid().ToString();
           var serviceConfiguration = new ServiceConfiguration(
               new Dictionary<string, AbsolutePath> { { CacheName, rootPath } },
               rootPath,
               MaxConnections,
               GracefulShutdownSeconds,
               PortExtensions.GetNextAvailablePort(),
               grpcPortFileName);

           return new TestInProcessServiceClientContentStore
               (
               FileSystem,
               Logger,
               CacheName,
               Scenario,
               heartbeatOverride,
               serviceConfiguration,
               RetrySeconds,
               RetryCount,
               localContentServerConfiguration
               );
       }
    }

    // TODO: Test was not using GRPC at all. Determine if it should be removed.
    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    [Trait("Category", "QTestSkip")]
    /*public*/ class ExternalProcessServiceRequestsWorkAcrossServerRestartTests : ServiceRequestsWorkAcrossServerRestartTests<TestServiceClientContentStore>
    {
       public ExternalProcessServiceRequestsWorkAcrossServerRestartTests(ITestOutputHelper output)
           : base(nameof(ExternalProcessServiceRequestsWorkAcrossServerRestartTests), output)
       {
       }

       protected ExternalProcessServiceRequestsWorkAcrossServerRestartTests(string scenario, ITestOutputHelper output)
           : base(scenario, output)
       {
       }

       protected override TestServiceClientContentStore CreateStore(
           AbsolutePath rootPath,
           ContentStoreConfiguration configuration,
           LocalServerConfiguration localContentServerConfiguration,
           TimeSpan? heartbeatOverride)
       {
           configuration.Write(FileSystem, rootPath).Wait();

           var grpcPortFileName = Guid.NewGuid().ToString();
           var serviceConfiguration = new ServiceConfiguration(
               new Dictionary<string, AbsolutePath> { { CacheName, rootPath } },
               rootPath,
               MaxConnections,
               GracefulShutdownSeconds,
               PortExtensions.GetNextAvailablePort(),
               grpcPortFileName);

            return new TestServiceClientContentStore(
               Logger,
               FileSystem,
               new ServiceClientContentStoreConfiguration(CacheName, null, Scenario)
               {
                   RetryCount = RetryCount,
                   RetryIntervalSeconds = RetrySeconds,
               }, 
               heartbeatOverride,
               serviceConfiguration,
               localContentServerConfiguration);
        }
    }
}
