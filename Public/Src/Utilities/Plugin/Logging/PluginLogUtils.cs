// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Plugin.Logging;
using ILogger = Grpc.Core.Logging.ILogger;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public static class PluginLogUtils
    {
        /// <nodoc />
        public static PluginLoggerBase GetLogger<T>(string logDir, string logName, string port)
        {
            Directory.CreateDirectory(logDir);
            var stream = File.Open(Path.Combine(logDir, logName + $"-{port}.log"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
            var sw = new StreamWriter(stream);
            sw.AutoFlush = true;
            return new TextLogger(TextWriter.Synchronized(sw), typeof(T));
        }

        /// <nodoc />
        public static ILogger CreateLoggerForPluginClients(LoggingContext loggingContext, string pluginId)
        {
            return new ForwardedLogger((level, message) =>
                Tracing.Logger.Log.PluginManagerForwardedPluginClientMessage(
                    loggingContext,
                    pluginId,
                    string.Format(CultureInfo.InvariantCulture, "[{0}]{1}", level, message)));
        }
    }
}
