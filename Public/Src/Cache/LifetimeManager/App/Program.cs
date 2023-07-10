// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.BlobLifetimeManager.Library;
using CLAP;
using CLAP.Validation;

namespace BuildXL.Cache.BlobLifetimeManager
{
    public class Program
    {
        private const string ConnectionStringsEnvironmentVariable = "LIFETIME_MANAGER_CONNECTION_STRINGS";

        private static void Main(string[] args)
        {
            Parser.Run<Program>(args);
        }

        [Verb(
            IsDefault = true,
            Description = $"Run the lifetime manager. Note that connections strings for the L3 cache must be provided via the {ConnectionStringsEnvironmentVariable} " +
                $"environment variable in the format of comma-separated strings.")]
        public static void Run(
            [Description("Max size for the L3 in MB.")]
            [Required]
            [MoreThan(0)]
            long maxSizeMB,

            [Required]
            [Description("Cache universe for which to perform GC.")]
            string cacheUniverse,

            [Required]
            [Description("Cache namespace for which to perform GC.")]
            string cacheNamespace,

            [Description("Database path to use if you don't want to create a new one from scratch.")]
            [DefaultValue(null)]
            string? databasePath,

            [Description("Perform a dry run. Delete operations against blob storage will be logged, but not performed.")]
            [DefaultValue(false)]
            bool dryRun,


            [Description("Degree of parallelism to use for each shard when enumerating content from blob storage.")]
            [DefaultValue(100)]
            int contentDegreeOfParallelism,

            [Description("Degree of parallelism to use for each shard when downloading fingerprints from blob storage.")]
            [DefaultValue(10)]
            int fingerprintDegreeOfParallelism,

            [DefaultValue(Severity.Debug)]
            Severity logSeverity,

            bool debug)
        {
            if (debug)
            {
                Debugger.Launch();
            }

            var connectionStringsString = Environment.GetEnvironmentVariable(ConnectionStringsEnvironmentVariable);
            if (string.IsNullOrEmpty(connectionStringsString))
            {
                throw new ArgumentException($"Connections strings for the L3 cache must be provided via the {ConnectionStringsEnvironmentVariable} environment variable " +
                    $"in the format of comma-separated strings.");
            }

            var connectionStrings = connectionStringsString.Split(',');

            RunCoreAsync(
                maxSizeMB * 1024 * 1024,
                connectionStrings,
                cacheUniverse,
                cacheNamespace,
                databasePath,
                logSeverity,
                fingerprintDegreeOfParallelism,
                contentDegreeOfParallelism,
                dryRun).Wait();
        }

        private static async Task RunCoreAsync(
            long maxSize,
            string[] connectionStrings,
            string cacheUniverse,
            string cacheNamespace,
            string? databasePath,
            Severity logSeverity,
            int fingerprintDegreeOfParallelism,
            int contentDegreeOfParallelism,
            bool dryRun)
        {
            using var logger = new Logger(new ConsoleLog(logSeverity), new FileLog(Path.Join(Environment.CurrentDirectory, "BlobLifetimeManager.log")));
            var context = new OperationContext(new Context(logger));

            var creds = connectionStrings.Select(connString => new AzureStorageCredentials(new PlainTextSecret(connString)));

            var nameToCred = creds.ToDictionary(
                cred => BlobCacheStorageAccountName.Parse(cred.GetAccountName()),
                cred => cred);

            var shardingScheme = new ShardingScheme(ShardingAlgorithm.JumpHash, nameToCred.Keys.ToList());

            var config = new ShardedBlobCacheTopology.Configuration(
                shardingScheme,
                new StaticBlobCacheSecretsProvider(nameToCred),
                cacheUniverse,
                cacheNamespace);

            var topology = new ShardedBlobCacheTopology(config);

            RocksDbLifetimeDatabase db;
            if (string.IsNullOrEmpty(databasePath))
            {
                // TODO: for the sake of keeping PRs as small as possible, we're not implementing checkpointing yet.
                // Checkpointing should be created in the future to avoid the cost of enumerating the L3 every time this runs.
                var dbCreator = new LifetimeDatabaseCreator(SystemClock.Instance, topology!);

                db = await dbCreator.CreateAsync(context, contentDegreeOfParallelism, fingerprintDegreeOfParallelism);
            }
            else
            {
                if (dryRun)
                {
                    logger.Debug("Dry run is being used with a pre-existing DB. Cloning DB to prevent state changes in future runs.");

                    var clonePath = databasePath + "_clone";
                    if(Directory.Exists(clonePath))
                    {
                        Directory.Delete(clonePath, true);
                    }
                    Directory.CreateDirectory(clonePath);
                    copyDirectory(databasePath, clonePath);
                    databasePath = clonePath;

                    static void copyDirectory(string sourceDir, string destinationDir)
                    {
                        var dir = new DirectoryInfo(sourceDir);
                        DirectoryInfo[] dirs = dir.GetDirectories();
                        Directory.CreateDirectory(destinationDir);

                        foreach (FileInfo file in dir.GetFiles())
                        {
                            string targetFilePath = Path.Combine(destinationDir, file.Name);
                            file.CopyTo(targetFilePath);
                        }

                        foreach (DirectoryInfo subDir in dirs)
                        {
                            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                            copyDirectory(subDir.FullName, newDestinationDir);
                        }
                    }
                }

                db = RocksDbLifetimeDatabase.Create(
                    new RocksDbLifetimeDatabase.Configuration
                    {
                        DatabasePath = databasePath,
                        LruEnumerationPercentileStep = 0.05,
                        LruEnumerationBatchSize = 1000,
                    },
                    SystemClock.Instance).ThrowIfFailure();
            }

            Console.CancelKeyPress += delegate {
                db.Dispose();
                logger.Dispose();
            };

            using (db)
            {
                var manager = new Library.BlobLifetimeManager(db, topology!, SystemClock.Instance);

                _ = await manager.GarbageCollectAsync(context, maxSize, dryRun, contentDegreeOfParallelism, fingerprintDegreeOfParallelism);
            }
        }
    }
}
