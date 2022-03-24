// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;

namespace BuildXL.Cache.Host.Configuration.Test
{
    public class MockDeploymentLauncherHost : IDeploymentLauncherHost
    {
        private readonly ServiceLifetimeManager _serviceLifetimeManager;
        private readonly string _serviceId;
        private bool _shutdownGracefully = true;

        /// <nodoc />
        public MockDeploymentLauncherHost(ServiceLifetimeManager serviceLifetimeManager, string serviceId)
        {
            _serviceLifetimeManager = serviceLifetimeManager;
            _serviceId = serviceId;
        }

        /// <inheritdoc />
        public ILauncherProcess CreateProcess(ProcessStartInfo info)
        {
            return new MockLauncherProcess(info, _serviceLifetimeManager, _serviceId, _shutdownGracefully);
        }

        /// <inheritdoc />
        public IDeploymentServiceClient CreateServiceClient()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not null if <see cref="ChildProcessExited"/> was called.
        /// </summary>
        public string ChildProcessExitedDescription { get; private set; }

        /// <nodoc />
        public void ChildProcessExited(OperationContext context, string description)
        {
            ChildProcessExitedDescription = description;
        }

        /// <summary>
        /// Sets a flag whether the child process should be shutdown gracefully or not.
        /// </summary>
        public void ShutdownGracefully(bool shutdownGracefully)
        {
            _shutdownGracefully = shutdownGracefully;
        }
    }
}
