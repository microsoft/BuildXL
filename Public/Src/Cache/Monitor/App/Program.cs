using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CLAP;

namespace BuildXL.Cache.Monitor.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Parser.Run<Program>(args);
        }

        [Verb(IsDefault = true)]
        public static void Run(string configurationFilePath = null, string backupFilePath = null)
        {
            var cancellationToken = new CancellationToken();
            RunMonitorAsync(configurationFilePath, backupFilePath, cancellationToken).Wait();
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
            Monitor.Configuration configuration = null;

            if (string.IsNullOrEmpty(configurationFilePath))
            {
                configuration = new Monitor.Configuration();

                var applicationKey = Environment.GetEnvironmentVariable("CACHE_MONITOR_APPLICATION_KEY");
                if (string.IsNullOrEmpty(applicationKey))
                {
                    throw new ArgumentException($"Please specify a configuration file or set the `CACHE_MONITOR_APPLICATION_KEY` environment variable to your application key");
                }
                configuration.ApplicationKey = applicationKey;

                return configuration;
            }

            if (!File.Exists(configurationFilePath))
            {
                throw new ArgumentException($"Configuration file does not exist at `{configurationFilePath}`",
                    nameof(configurationFilePath));
            }

            using var stream = File.OpenText(configurationFilePath);
            var serializer = CreateSerializer();

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
