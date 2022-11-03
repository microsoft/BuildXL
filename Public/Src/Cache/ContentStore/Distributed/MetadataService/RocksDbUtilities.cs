// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Engine.Cache.KeyValueStores;
using RocksDbSharp;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    internal static class RocksDbUtilities
    {
        public static void ConfigureRocksDbTracingIfNeeded(
            OperationContext context,
            RocksDbContentLocationDatabaseConfiguration configuration,
            RocksDbStoreConfiguration settings,
            Tracer tracer,
            string componentName)
        {
            if (!configuration.OpenReadOnly && configuration.RocksDbTracingLevel is { } tracingLevel)
            {
                // Tracing operations only for non-readonly database.
                // We don't know if the current instance will be on a master machine, but the configuration is happening
                // before such decision is made.
                var traceLineHandler = CreateLogLineCallback(context, tracer, componentName: nameof(RocksDbContentMetadataDatabase));
                settings.LogLevel = tracingLevel;
                settings.HandleLogMessage = traceLineHandler;
            }
        }

        public static LogLineCallback CreateLogLineCallback(OperationContext context, Tracer tracer, string componentName)
        {
            const string operation = "RocksDbTrace";
            var rocksDbContext = context.CreateNested(componentName, caller: operation).TracingContext;
            return (logLevel, message) =>
                                        {
                                            Action<string> targetMethod = logLevel switch
                                            {
                                                LogLevel.Debug => (string msg) => tracer.Debug(rocksDbContext, msg, operation: operation),
                                                LogLevel.Info => (string msg) => tracer.Info(rocksDbContext, msg, operation: operation),
                                                LogLevel.Warn => (string msg) => tracer.Warning(rocksDbContext, msg, operation: operation),
                                                LogLevel.Error => (string msg) => tracer.Error(rocksDbContext, msg, operation: operation),
                                                LogLevel.Fatal => (string msg) => tracer.Error(rocksDbContext, msg, operation: operation),
                                                LogLevel.Header => (string msg) => tracer.Info(rocksDbContext, msg, operation: operation),
                                                _ => (string msg) => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
                                            };

                                            targetMethod(message);
                                        };
        }
    }
}
