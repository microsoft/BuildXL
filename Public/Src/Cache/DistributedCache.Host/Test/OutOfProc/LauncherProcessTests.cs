// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Host.Configuration.Test
{
    public class LauncherProcessTests : TestBase
    {
        public LauncherProcessTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public void EventIsRaisedWhenTheProcessExits()
        {
            // Arrange
            var processInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false };
            var process = new LauncherProcess(processInfo);

            bool exitWasCalled = false;
            process.Exited += () =>
                              {
                                  exitWasCalled = true;
                              };

            // Act
            var context = new OperationContext(new Context(TestGlobal.Logger));
            process.Start(context);

            process.Kill(context);
            process.WaitForExit(TimeSpan.FromSeconds(5));

            // Assert
            exitWasCalled.Should().BeTrue();
        }
    }
}
