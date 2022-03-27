// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.OutOfProc;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Host.Configuration.Test
{
    public class CacheServiceWrapperTests : TestBase
    {
        public CacheServiceWrapperTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task ChildProcessExitedIsCalledOnProcessExit()
        {
            // Arrange
            using var tempDirectory = new DisposableDirectory(FileSystem);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var configuration = CreateConfiguration(tempDirectory.Path, shutdownTimeout: TimeSpan.FromSeconds(10));
            var wrapper = CreateServiceWrapperWithHost(configuration, tempDirectory.Path, out var host);

            await wrapper.StartupAsync(context).ShouldBeSuccess();

            var mockProcess = (MockLauncherProcess)wrapper.LaunchedProcess!;

            // Act
            int exitCode = -1;
            mockProcess.OnExited(exitCode);

            // Assert
            host.ChildProcessExitedDescription.Should().Contain(exitCode.ToString(), "The restart reason should have an exit code.");

            // Shutdown still should be successful.
            await wrapper.ShutdownAsync(context).ShouldBeSuccess();
        }

        [Fact]
        public async Task EnvironmentVariablesPassedToChildProcess()
        {
            // Arrange
            using var tempDirectory = new DisposableDirectory(FileSystem);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var configuration = CreateConfiguration(tempDirectory.Path,
                environmentVariables: new Dictionary<string, string>()
                                      {
                                          ["Env1"] = "Env1Value"
                                      });

            var wrapper = CreateServiceWrapper(configuration, tempDirectory.Path);

            await wrapper.StartupAsync(context).ShouldBeSuccess();

            // Assert
            var environment = ((MockLauncherProcess)(wrapper.LaunchedProcess!)).ProcessStartInfo.Environment;
            environment.Should().ContainKey("Env1");
            environment.Should().ContainValue("Env1Value");

            foreach (var info in CacheServiceWrapperConfiguration.DefaultDotNetEnvironmentVariables)
            {
                environment.Should().ContainKey(info.Key);
                environment.Should().ContainValue(info.Value);
            }

            await wrapper.ShutdownAsync(context).ShouldBeSuccess();
        }

        [Fact]
        public async Task TestProcessLifetimeWithLifetimeManager()
        {
            // Arrange
            using var tempDirectory = new DisposableDirectory(FileSystem);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var configuration = CreateConfiguration(tempDirectory.Path, shutdownTimeout: TimeSpan.FromSeconds(10));
            var wrapper = CreateServiceWrapperWithHost(configuration, tempDirectory.Path, out var host);

            await wrapper.StartupAsync(context).ShouldBeSuccess();

            var mockProcess = (MockLauncherProcess)wrapper.LaunchedProcess!;

            // Forcing the timeout if the process won't respect the shutdown properly.
            bool killActionWasCalled = false;
            mockProcess.KillAction = () =>
                                     {
                                         killActionWasCalled = true;

#pragma warning disable AsyncFixer02 // Task.Delay should be used instead of Thread.Sleep.
                                         Thread.Sleep(20_000);
#pragma warning restore AsyncFixer02
                                     };

            mockProcess.Started.Should().BeTrue();

            await wrapper.ShutdownAsync(context).ShouldBeSuccess();

            // The mock process is configured to respect the shutdown so the kill action should not be called.
            killActionWasCalled.Should().BeFalse();
            host.ChildProcessExitedDescription.Should().BeNullOrEmpty("ChildProcessExit callback should not be called during a normal shutdown.");
        }

        [Fact]
        public async Task TestProcessIsKillIfNotExitGracefully()
        {
            // Arrange
            using var tempDirectory = new DisposableDirectory(FileSystem);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var configuration = CreateConfiguration(tempDirectory.Path, shutdownTimeout: TimeSpan.FromSeconds(1));
            var wrapper = CreateServiceWrapperWithHost(configuration, tempDirectory.Path, out var host);

            // Setting up the test case logic to not respect the shutdown signals from the lifetime manager.
            host.ShutdownGracefully(false);

            await wrapper.StartupAsync(context).ShouldBeSuccess();

            var mockProcess = (MockLauncherProcess)wrapper.LaunchedProcess!;

            bool killActionWasCalled = false;
            
            mockProcess.KillAction = () =>
                                     {
                                         killActionWasCalled = true;
                                     };

            // Assert
            // When the child process does not exit gracefully, the Kill method is called, but the shutdown will timeout.
            // This is not perfect and maybe two timeouts should be used: one for graceful shutdown and another one for the overall shutdown.
            // The shutdown still should be successful, because once the timeout is passed the Kill method is called and that method should terminate the process succesffully.
            await wrapper.ShutdownAsync(context).ShouldBeSuccess();
            killActionWasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task ShutdownFailsWithTimeoutIfKillGotStuck()
        {
            // Arrange
            using var tempDirectory = new DisposableDirectory(FileSystem);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var configuration = CreateConfiguration(tempDirectory.Path, shutdownTimeout: TimeSpan.FromSeconds(1), terminationTimeout: TimeSpan.FromSeconds(1));
            var wrapper = CreateServiceWrapperWithHost(configuration, tempDirectory.Path, out var host);

            // Setting up the test case logic to not respect the shutdown signals from the lifetime manager.
            host.ShutdownGracefully(false);

            await wrapper.StartupAsync(context).ShouldBeSuccess();

            var mockProcess = (MockLauncherProcess)wrapper.LaunchedProcess!;

            bool killActionWasCalled = false;
            
            mockProcess.KillAction = () =>
                                     {
                                         killActionWasCalled = true;
#pragma warning disable AsyncFixer02 // Task.Delay should be used instead of Thread.Sleep.
                                         Thread.Sleep(20_000);
#pragma warning restore AsyncFixer02
                                     };

            // Assert
            // The shutdown should fail with timeout, because the Kill method won't terminate in time.
            await wrapper.ShutdownAsync(context).ShouldBeError("timed out after");
            killActionWasCalled.Should().BeTrue();
        }
        
        private static CacheServiceWrapper CreateServiceWrapper(CacheServiceWrapperConfiguration configuration, AbsolutePath path, bool useServiceLifetimeTracking = true)
        {
            var lifetimeManager = new ServiceLifetimeManager(path, TimeSpan.FromMilliseconds(10));
            return CreateServiceWrapper(lifetimeManager, configuration, useServiceLifetimeTracking);
        }

        private static CacheServiceWrapper CreateServiceWrapper(ServiceLifetimeManager lifetimeManager, CacheServiceWrapperConfiguration configuration, bool useServiceLifetimeTracking = true)
        {
            var mockHost = new MockDeploymentLauncherHost(useServiceLifetimeTracking ? lifetimeManager : null, configuration.ServiceId);
            return new CacheServiceWrapper(
                configuration,
                lifetimeManager,
                new RetrievedSecrets(new Dictionary<string, Secret>()),
                mockHost,
                (context, reason) => mockHost.ChildProcessExited(context, reason)
            );
        }

        private static CacheServiceWrapper CreateServiceWrapperWithHost(CacheServiceWrapperConfiguration configuration, AbsolutePath path, out MockDeploymentLauncherHost host)
        {
            var lifetimeManager = new ServiceLifetimeManager(path, TimeSpan.FromMilliseconds(10));
            host = new MockDeploymentLauncherHost(lifetimeManager, configuration.ServiceId);
            var mockHost = host = new MockDeploymentLauncherHost(lifetimeManager, configuration.ServiceId);
            return new CacheServiceWrapper(
                configuration,
                lifetimeManager,
                new RetrievedSecrets(new Dictionary<string, Secret>()),
                host,
                (context, reason) => mockHost.ChildProcessExited(context, reason)
            );
        }

        private CacheServiceWrapperConfiguration CreateConfiguration(AbsolutePath root, TimeSpan? shutdownTimeout = null, TimeSpan? terminationTimeout = null, Dictionary<string, string> environmentVariables = null)
        {
            return new CacheServiceWrapperConfiguration(
                "TestServiceId",
                executable: createEmpty("casaas.exe"),
                root,
                new HostParameters() { },
                cacheConfigPath: createEmpty("CacheConfiguration.json"),
                createDirectory("data"),
                useInterProcSecretsCommunication: true,
                environmentVariables: environmentVariables)
                   {
                       ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5),
                       ProcessTerminationTimeout = terminationTimeout ?? TimeSpan.FromSeconds(1),
                   };

            AbsolutePath createEmpty(string path)
            {
                var file = root / path;
                FileSystem.CreateEmptyFile(file);
                return file;
            }

            AbsolutePath createDirectory(string path)
            {
                var directory = root / path;
                FileSystem.CreateDirectory(directory);
                return directory;
            }
        }
    }
}
