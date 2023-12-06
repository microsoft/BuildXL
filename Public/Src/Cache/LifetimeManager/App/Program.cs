// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.FileSystem;

namespace BuildXL.Cache.BlobLifetimeManager
{
    public class Program
    {
        private const string ConnectionStringsEnvironmentVariable = "LIFETIME_MANAGER_CONNECTION_STRINGS";
        private static readonly Tracer Tracer = new(nameof(Program));

        private static void Main(string[] args)
        {
            Parser.RunConsole<Program>(args);
        }

        [Verb(
            IsDefault = true,
            Description = $"Run the lifetime manager. Note that connections strings for the L3 cache must be provided via the {ConnectionStringsEnvironmentVariable} " +
                $"environment variable in the format of comma-separated strings.")]
        public static void Run(
            [Required]
            [Description("Path to the garbage collection configuration.")]
            string configPath,

            [Description("Perform a dry run. Delete operations against blob storage will be logged, but not performed.")]
            [DefaultValue(false)]
            bool dryRun,

            [Description("Degree of parallelism to use for each shard when enumerating content from blob storage.")]
            [DefaultValue(100)]
            int contentDegreeOfParallelism,

            [Description("Degree of parallelism to use for each shard when downloading fingerprints from blob storage.")]
            [DefaultValue(10)]
            int fingerprintDegreeOfParallelism,

            [Required]
            [Description("ID of the current GC run. This is used to identify which run produced a checkpoint. It should be an alphanumeric string.")]
            string runId,

            [DefaultValue(Severity.Debug)]
            Severity logSeverity,

            [Description("Whether to write logs to a file on disk.")]
            [DefaultValue(false)]
            bool enableFileLogging,

            [Description("For tracing. Name of the cache instance that is being GCd.")]
            [DefaultValue("")]
            string cacheInstance,

            bool debug)
        {
            RunCoreAsync(configPath, dryRun, contentDegreeOfParallelism, fingerprintDegreeOfParallelism, runId, logSeverity, enableFileLogging, cacheInstance, debug).GetAwaiter().GetResult();
        }

        public static async Task RunCoreAsync(
            string configPath,
            bool dryRun,
            int contentDegreeOfParallelism,
            int fingerprintDegreeOfParallelism,
            string runId,
            Severity logSeverity,
            bool enableFileLogging,
            string cacheInstance,
            bool debug)
        {
            if (debug)
            {
                Debugger.Launch();
            }

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file does not exist: {configPath}");
                return;
            }

            BlobQuotaKeeperConfig config;
            try
            {
                using var stream = File.OpenRead(configPath);
                config = await JsonUtilities.JsonDeserializeAsync<BlobQuotaKeeperConfig>(stream);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read configuration file {configPath}: {e}");
                return;
            }

            if (config.LastAccessTimeDeletionThreshold < TimeSpan.FromDays(1))
            {
                Console.WriteLine($"To minimize the impact on read access latency, Azure Blob Storage only updates the last access time on the first read on a given 24-hour period. " +
                    $"Subsequent reads in the same 24-hour period do not update the last access time.\n" +
                    $"Because of this, {nameof(config.LastAccessTimeDeletionThreshold)} can't be less than one day, since otherwise we might be acting on outdated information. " +
                    $"Configured value: {config.LastAccessTimeDeletionThreshold}");
                return;
            }

            var secretsProvider = new EnvironmentVariableCacheSecretsProvider(ConnectionStringsEnvironmentVariable);
            var accounts = secretsProvider.ConfiguredAccounts;

            using var logger = new Logger(new ConsoleLog(logSeverity, printSeverity: true));

            if (enableFileLogging)
            {
                logger.AddLog(new FileLog(Path.Join(Environment.CurrentDirectory, "BlobLifetimeManager.log"), logSeverity));
            }

            using var cts = new ConsoleCancellationSource();
            var context = new OperationContext(new Context(logger), cts.Token);

            await new Library.BlobLifetimeManager().RunAsync(
                context,
                config,
                PassThroughFileSystem.Default,
                secretsProvider,
                accounts,
                SystemClock.Instance,
                runId,
                contentDegreeOfParallelism,
                fingerprintDegreeOfParallelism,
                cacheInstance,
                dryRun);
        }
    }
}
