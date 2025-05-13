// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Interfaces;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Configuration;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Custom serilog sink that wraps the IIpcLogger interface.
    /// </summary>
    /// <remarks>
    /// Follows the pattern described here: https://github.com/serilog/serilog/wiki/Developing-a-sink
    /// </remarks>
    public class SbomLoggingSerilogSink : ILogEventSink
    {
        private readonly IIpcLogger m_logger;

        /// <nodoc />
        public SbomLoggingSerilogSink(IIpcLogger logger)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage();
            m_logger.Log(BuildXL.Ipc.Interfaces.LogLevel.Info, message);
        }
    }

    /// <summary>
    /// Extension for generating a LoggerConfiguration for the SbomLoggingSerilogSink.
    /// </summary>
    public static class SbomLoggingSerilogSinkExtensions
    {
        /// <nodoc />
        public static LoggerConfiguration SbomLoggingSerilogSink(
            this LoggerSinkConfiguration loggerConfiguration,
            IIpcLogger logger)
        {
            return loggerConfiguration.Sink(new SbomLoggingSerilogSink(logger));
        }
    }
}