// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
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
            [DefaultValue(false)] bool debug,
            [DefaultValue(false)] bool shutdown = false
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

                var settings = JsonSerializer.Deserialize<LauncherApplicationSettings>(configJson, DeploymentUtilities.ConfigurationSerializationOptions);

                var launcher = new DeploymentLauncher(settings, _fileSystem);

                runAsync().GetAwaiter().GetResult();
                async Task runAsync()
                {
                    if (shutdown)
                    {
                        var context = new OperationContext(new Context(_logger), _cancellationToken);
                        await launcher.LifetimeManager.ShutdownServiceAsync(context, settings.LauncherServiceId);
                        return;
                    }

                    var host = new EnvironmentVariableHost(new Context(_logger));
                    settings.DeploymentParameters.AuthorizationSecret ??= await host.GetPlainSecretAsync(settings.DeploymentParameters.AuthorizationSecretName, _cancellationToken);
                    
                    var telemetryFieldsProvider = new HostTelemetryFieldsProvider(settings.DeploymentParameters)
                    {
                        ServiceName = "DeploymentLauncher"
                    };
                    var arguments = new LoggerFactoryArguments(_logger, host, settings.LoggingSettings, telemetryFieldsProvider);

                    var replacementLogger = LoggerFactory.CreateReplacementLogger(arguments);
                    using (replacementLogger.DisposableToken)
                    {
                        var token = _cancellationToken;
                        var context = new OperationContext(new Context(replacementLogger.Logger), token);

                        await launcher.LifetimeManager.RunInterruptableServiceAsync(context, settings.LauncherServiceId, async token =>
                        {
                            try
                            {
                                await launcher.StartupAsync(context).ThrowIfFailureAsync();
                                using var tokenAwaitable = token.ToAwaitable();
                                await tokenAwaitable.CompletionTask;
                            }
                            finally
                            {
                                await launcher.ShutdownAsync(context).ThrowIfFailureAsync();
                            }

                            return true;
                        });
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
