// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Roxis.Server;
using BuildXL.Utilities;
using CLAP;

namespace BuildXL.Cache.Roxis.App
{
    /// <summary>
    /// CLAP-required class with the command for setting up a server
    /// </summary>
    internal class Server
    {
        [Verb(IsDefault = true)]
        public static void Run(string? configurationFilePath = null, string? logFilePath = null, bool debug = false)
        {
            if (debug)
            {
                Debugger.Launch();
            }

            using var consoleCancellationSource = new ConsoleCancellationSource();
            Utilities.WithLoggerAsync(
                async (logger) => await RunServerAsync(logger, configurationFilePath, consoleCancellationSource.Token),
                logFilePath).GetAwaiter().GetResult();
        }

        private static async Task RunServerAsync(
            ILogger logger,
            string? configurationFilePath,
            CancellationToken cancellationToken = default)
        {
            RoxisServiceConfiguration? configuration = null;
            if (configurationFilePath != null)
            {
                using var stream = File.OpenRead(configurationFilePath);

                configuration = await JsonSerializer.DeserializeAsync<RoxisServiceConfiguration>(stream, new JsonSerializerOptions()
                {
                    AllowTrailingCommas = true,
                    IgnoreReadOnlyProperties = true,
                    IgnoreNullValues = true,
                    PropertyNameCaseInsensitive = true,
                }, cancellationToken);
            }

            await RoxisService.RunAsync(configuration, logger, cancellationToken);
        }

    }
}
