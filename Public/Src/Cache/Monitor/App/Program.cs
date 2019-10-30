using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BuildXL.Cache.Monitor.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var configurationFilePath = @"C:\work\Monitor\Configuration.json";
            var backupFilePath = @"C:\work\Monitor\BackupConfiguration.json";

            var cancellationToken = new CancellationToken();
            RunMonitorAsync(null, backupFilePath, cancellationToken).Wait();
        }

        private static async Task RunMonitorAsync(string configurationFilePath = null, string backupFilePath = null, CancellationToken cancellationToken = default)
        {
            var configuration = LoadConfiguration(configurationFilePath);
            if (!string.IsNullOrEmpty(backupFilePath))
            {
                PersistConfiguration(configuration, backupFilePath, force: true);
            }

            using (var monitor = new Monitor(configuration))
            {
                await monitor.Run(cancellationToken);
            }
        }

        private static Monitor.Configuration LoadConfiguration(string configurationFilePath = null)
        {
            if (string.IsNullOrEmpty(configurationFilePath))
            {
                return new Monitor.Configuration();
            }

            if (!File.Exists(configurationFilePath))
            {
                throw new ArgumentException($"Configuration file does not exist at `{configurationFilePath}`",
                    nameof(configurationFilePath));
            }

            using var stream = File.OpenText(configurationFilePath);
            var serializer = CreateSerializer();

            Monitor.Configuration configuration = null;
            try
            {
                configuration = (Monitor.Configuration)serializer.Deserialize(stream, typeof(Monitor.Configuration));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException($"Invalid configuration file at `{configurationFilePath}`",
                    nameof(configurationFilePath),
                    exception);
            }

            if (configuration is null)
            {
                throw new ArgumentException($"Configuration file is empty at `{configurationFilePath}`",
                    nameof(configurationFilePath));
            }

            return configuration;
        }

        private static void PersistConfiguration(Monitor.Configuration configuration, string configurationFilePath, bool force = false)
        {
            Contract.RequiresNotNull(configuration);
            Contract.RequiresNotNullOrEmpty(configurationFilePath);

            if (File.Exists(configurationFilePath) && !force)
            {
                throw new ArgumentException($"File `{configurationFilePath}` already exists. Not overwriting it.",
                    nameof(configurationFilePath));
            }

            using var stream = File.CreateText(configurationFilePath);
            var serializer = CreateSerializer();
            serializer.Serialize(stream, configuration);
        }

        private static JsonSerializer CreateSerializer()
        {
            return new JsonSerializer
            {
                Formatting = Formatting.Indented,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
        }
    }
}
