// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Extension methods for <see cref="ILogger"/>.
    /// </summary>
    public static class LoggerExtensions
    {
        /// <nodoc />
        public static void Info(this ILogger logger, string format, params object[] args) => logger?.Log(LogLevel.Info, format, args);

        /// <nodoc />
        public static void Verbose(this ILogger logger, string format, params object[] args) => logger?.Log(LogLevel.Verbose, format, args);

        /// <nodoc />
        public static void Warning(this ILogger logger, string format, params object[] args) => logger?.Log(LogLevel.Warning, format, args);

        /// <nodoc />
        public static void Error(this ILogger logger, string format, params object[] args) => logger?.Log(LogLevel.Error, format, args);

        /// <summary>
        /// Formats log message using some default formatting.
        /// </summary>
        public static string Format(LogLevel level, string messageFormat, params object[] args)
        {
            string time = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string prefix = string.Format(CultureInfo.CurrentCulture, "{0} [{1}] ", time, level);
            string message = args.Length == 0 ? messageFormat : string.Format(CultureInfo.CurrentCulture, messageFormat, args);
            return prefix + message;
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IIpcProvider"/>.
    /// </summary>
    public static class IpcProviderExtensions
    {
        /// <summary>
        /// First calls <see cref="IIpcProvider.LoadOrCreateMoniker(string)"/>, then
        /// <see cref="IIpcProvider.RenderConnectionString(IIpcMoniker)"/> on the returned moniker.
        /// </summary>
        public static string LoadAndRenderMoniker(this IIpcProvider ipcProvider, string monikerId)
        {
            return ipcProvider.RenderConnectionString(ipcProvider.LoadOrCreateMoniker(monikerId));
        }

        /// <summary>
        /// Creates a new moniker (<see cref="IIpcProvider.CreateNewMoniker"/>) and then renders
        /// it to a connection string (<see cref="IIpcProvider.RenderConnectionString"/>).
        /// </summary>
        public static string CreateNewConnectionString(this IIpcProvider ipcProvider)
        {
            return ipcProvider.RenderConnectionString(ipcProvider.CreateNewMoniker());
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IIpcMoniker"/>.
    /// </summary>
    public static class IIpcMonikerExtensions
    {
        /// <summary>
        /// Given a <see cref="StringTable"/>, returns <see cref="IIpcMoniker.Id"/> as a <see cref="StringId"/>.
        /// </summary>
        public static StringId ToStringId(this IIpcMoniker moniker, StringTable table) => StringId.Create(table, moniker.Id);
    }
}
