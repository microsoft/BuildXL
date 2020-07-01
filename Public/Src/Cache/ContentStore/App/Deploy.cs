// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using CLAP;
using ContentStore.Grpc;
using Grpc.Core;
using PinRequest = BuildXL.Cache.ContentStore.Stores.PinRequest;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Attempt to ingest drops in specified drops and files in deployment configuration into deployment target directory.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Deploy drops/files specified in deployment manifest")]
        internal void Deploy(
            [Required, Description("Location where loose source files should be drawn from.")] string sourceRoot,
            [Required, Description("Target directory for deployment.")] string targetDirectory,
            [Required, Description("Location of deployment manifest json file")] string deploymentConfigPath,
            [Required, Description("Location of drop.exe to run to download drops")] string dropExePath,
            [Required, Description("Personal access token to use to authenticate to drop service")] string dropToken,
            [Description("Maximum size of files to retain"), DefaultValue(50)] int retentionSizeGb)
        {
            try
            {
                Initialize();
                var deploymentRoot = new AbsolutePath(targetDirectory);

                var deploymentRunner = new DeploymentRunner(
                            context: new OperationContext(new Context(_logger)),
                            sourceRoot: new AbsolutePath(sourceRoot),
                            deploymentRoot: deploymentRoot,
                            deploymentConfigurationPath: new AbsolutePath(deploymentConfigPath),
                            fileSystem: _fileSystem,
                            retentionSizeGb: retentionSizeGb,
                            dropExeFilePath: new AbsolutePath(dropExePath),
                            dropToken: dropToken
                            );

                deploymentRunner.RunAsync().GetAwaiter().GetResult().ThrowIfFailure();
            }
            catch (Exception e)
            {
                _logger.Error(e.ToString());
                throw;
            }
        }
    }
}
