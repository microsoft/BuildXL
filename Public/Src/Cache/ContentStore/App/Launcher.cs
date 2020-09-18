// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the Deployment launcher verb for downloading and running deployments.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run deployment launcher")]
        internal void Launcher
            (
            [Required, Description("Path to LauncherSettings file")] string settingsPath,
            [DefaultValue(false)] bool debug
            )
        {
            Initialize();

            if (debug)
            {
                System.Diagnostics.Debugger.Launch();
            }

            try
            {
                Validate();

                var configJson = File.ReadAllText(settingsPath);

                var settings = JsonSerializer.Deserialize<LauncherSettings>(configJson, DeploymentUtilities.ConfigurationSerializationOptions);

                runAsync().GetAwaiter().GetResult();

                async Task runAsync()
                {
                    var launcher = new DeploymentLauncher(settings, _fileSystem);
                    var token = _cancellationToken;
                    var context = new OperationContext(new Context(_logger), token);

                    try
                    {
                        await launcher.StartupAsync(context).ThrowIfFailureAsync();
                        var task = token.WaitForCancellationAsync();
                        await task;
                    }
                    finally
                    {
                        await launcher.ShutdownAsync(context).ThrowIfFailureAsync();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
