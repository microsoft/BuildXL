// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Extension methods for <see cref="IIpcLogger "/>.
    /// </summary>
    public static class LoggerExtensions
    {
        /// <nodoc />
        public static void Info(this IIpcLogger logger, string format, params object[] args) => logger?.Log(LogLevel.Info, format, args);

        /// <nodoc />
        public static void Info(this IIpcLogger logger, StringBuilder message) => logger?.Log(LogLevel.Info, message);

        /// <nodoc />
        public static void Verbose(this IIpcLogger logger, string format, params object[] args) => logger?.Log(LogLevel.Verbose, format, args);

        /// <nodoc />
        public static void Verbose(this IIpcLogger logger, StringBuilder message) => logger?.Log(LogLevel.Verbose, message);

        /// <nodoc />
        public static void Warning(this IIpcLogger logger, string format, params object[] args) => logger?.Log(LogLevel.Warning, format, args);

        /// <nodoc />
        public static void Warning(this IIpcLogger logger, StringBuilder message) => logger?.Log(LogLevel.Warning, message);

        /// <nodoc />
        public static void Error(this IIpcLogger logger, string format, params object[] args) => logger?.Log(LogLevel.Error, format, args);

        /// <nodoc />
        public static void Error(this IIpcLogger logger, StringBuilder message) => logger?.Log(LogLevel.Error, message);

        /// <summary>
        /// Formats log message using some default formatting.
        /// </summary>
        public static string Format(LogLevel level, string messageFormat, params object[] args)
        {
            var timestamp = GetFormattedTimestamp(level);
            string message = args.Length == 0 ? messageFormat : string.Format(CultureInfo.CurrentCulture, messageFormat, args);
            return timestamp + message;
        }

        /// <summary>
        /// Formats log message using some default formatting. Modifies a provided StringBuilder
        /// and returns a reference to it.
        /// </summary>
        public static StringBuilder Format(LogLevel level, StringBuilder message)
        {
            var timestamp = GetFormattedTimestamp(level);
            message.Insert(0, timestamp);
            return message;
        }

        /// <summary>
        /// Returns a timestamp formatted for logging
        /// </summary>
        public static string GetFormattedTimestamp(LogLevel level)
        {
            string time = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.CurrentCulture, "{0} [{1}] ", time, level);
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IIpcProvider"/>.
    /// </summary>
    public static class IpcProviderExtensions
    {
        /// <summary>
        /// First calls <see cref="IpcMoniker.Create(string)"/>, then
        /// <see cref="IIpcProvider.RenderConnectionString(IpcMoniker)"/> on the returned moniker.
        /// </summary>
        public static string LoadAndRenderMoniker(this IIpcProvider ipcProvider, string monikerId)
        {
            return ipcProvider.RenderConnectionString(IpcMoniker.Create(monikerId));
        }

        /// <summary>
        /// Creates a new moniker (<see cref="IpcMoniker.CreateNew"/>) and then renders
        /// it to a connection string (<see cref="IIpcProvider.RenderConnectionString"/>).
        /// </summary>
        public static string CreateNewConnectionString(this IIpcProvider ipcProvider)
        {
            return ipcProvider.RenderConnectionString(IpcMoniker.CreateNew());
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IpcMoniker"/>.
    /// </summary>
    public static class IpcMonikerExtensions
    {
        /// <summary>
        /// Given a <see cref="StringTable"/>, returns <see cref="IpcMoniker.Id"/> as a <see cref="StringId"/>.
        /// </summary>
        public static StringId ToStringId(this IpcMoniker moniker, StringTable table) => StringId.Create(table, moniker.Id);
    }
}
