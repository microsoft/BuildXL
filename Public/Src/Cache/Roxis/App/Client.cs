// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Roxis.Client;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Utilities;
using CLAP;
using Roxis;

namespace BuildXL.Cache.Roxis.App
{
    /// <summary>
    /// CLAP-required class with the command for setting up an adhoc client. Should only be used for testing.
    /// </summary>
    internal class Client
    {
        [Verb(IsDefault = true)]
        public static void Run(
            [Required] string command,
            [Required] string[] parameters,
            string? grpcHost = null,
            int? grpcPort = null,
            bool debug = false)
        {
            if (debug)
            {
                Debugger.Launch();
            }

            using var consoleCancellationSource = new ConsoleCancellationSource();

            Utilities.WithLoggerAsync(async (logger) =>
            {
                var metadataClientConfiguration = new RoxisClientConfiguration();
                metadataClientConfiguration.GrpcHost = grpcHost ?? metadataClientConfiguration.GrpcHost;
                metadataClientConfiguration.GrpcPort = grpcPort ?? metadataClientConfiguration.GrpcPort;

                await RunClientAsync(
                    logger,
                    new RoxisClientArguments(command, parameters) { Client = metadataClientConfiguration },
                    consoleCancellationSource.Token);
            }, null).GetAwaiter().GetResult();
        }

        private static async Task RunClientAsync(
            ILogger logger,
            RoxisClientArguments arguments,
            CancellationToken cancellationToken = default)
        {
            var tracingContext = new Context(logger);
            var context = new OperationContext(tracingContext, cancellationToken);

            var command = CommandExtensions.FromString(arguments.Command, arguments.Parameters).ThrowIfFailure();
            var commandRequest = new CommandRequest(new[] { command });

            var client = new RoxisClient(arguments.Client);
            await client.StartupAsync(context).ThrowIfFailureAsync();

            var commandResponse = await client.ExecuteAsync(context, commandRequest).ThrowIfFailureAsync();

            var jsonSerializerOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
            };
            jsonSerializerOptions.Converters.Add(new ByteStringConverter());

            foreach (var result in commandResponse.Results)
            {
                context.TraceInfo(JsonSerializer.Serialize(result, result.GetType(), jsonSerializerOptions));
            }

            await client.ShutdownAsync(context).ThrowIfFailureAsync();
        }

        public class ByteStringConverter : JsonConverter<ByteString>
        {
            public override ByteString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, ByteString value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(Encoding.UTF8.GetString(value.Value));
            }
        }

        private class RoxisClientArguments
        {
            public RoxisClientConfiguration Client { get; set; } = new RoxisClientConfiguration();

            public string Command { get; }

            public string[] Parameters { get; }

            public RoxisClientArguments(string command, string[] parameters)
            {
                Command = command;
                Parameters = parameters;
            }
        }
    }
}
