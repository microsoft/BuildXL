// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Host.Configuration.Test
{
    public class LauncherManagedProcessTests : TestBase
    {
        public LauncherManagedProcessTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task StopExitsOnWaitForExit()
        {
            // Arrange
            using var tempDirectory = new DisposableDirectory(FileSystem);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var serviceId = "MyServiceId";

            var lifetimeManager = new ServiceLifetimeManager(tempDirectory.Path, TimeSpan.FromMilliseconds(10));
            var mock = new MockDeploymentLauncherHost(serviceLifetimeManager: lifetimeManager, serviceId);

            var process = (MockLauncherProcess)mock.CreateProcess(new ProcessStartInfo("foo.exe"));
            // This allows handling a graceful shutdown
            mock.ShutdownGracefully(true);
            // But this will prevent the process from calling 'OnExit' callback to simulate the case
            // when the process respects the lifetime manager's signals but OnExit event is not raised.
            process.CallOnExitWhenServiceStopped = false;

            var managedProcess = new LauncherManagedProcess(process, serviceId, lifetimeManager);
            bool waitForExitWasCalled = false;
            process.WaitForExitFunc = _ =>
                                      {
                                          waitForExitWasCalled = true;
                                          return true;
                                      };

            // Act
            await managedProcess.StopAsync(context, gracefulShutdownTimeout: TimeSpan.FromSeconds(1), killTimeout: TimeSpan.FromSeconds(1))
                .ShouldBeSuccess();

            // Assert
            waitForExitWasCalled.Should().BeTrue();
        }
    }
}
