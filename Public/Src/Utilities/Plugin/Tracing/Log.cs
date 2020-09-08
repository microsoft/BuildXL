// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#nullable enable

namespace BuildXL.Plugin.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("PluginLogger")]
    public abstract partial class Logger : LoggerBase
    {
        internal Logger()
        {
        }

        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerForwardedPluginClientMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] From pluginClient-{id}: {message}")]
        internal abstract void PluginManagerForwardedPluginClientMessage(LoggingContext loggingContext, string id, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerStarting,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] Started")]
        internal abstract void PluginManagerStarting(LoggingContext loggingContext);
        
        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerLoadingPluginsFinished,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] Finished loading plugins")]
        internal abstract void PluginManagerLoadingPluginsFinished(LoggingContext loggingContext);

        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerLoadingPlugin,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] Starting plugin {pluginPath} successfuly, identity: {pluginName}-{id}")]
        internal abstract void PluginManagerLoadingPlugin(LoggingContext loggingContext, string pluginPath, string pluginName, string id);

        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerLogMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] {message}")]
        internal abstract void PluginManagerLogMessage(LoggingContext loggingContext, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerErrorMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] {message}")]
        internal abstract void PluginManagerErrorMessage(LoggingContext loggingContext, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PluginManagerShutDown,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "[{ShortProductName} Plugin Manager] Shutting down")]
        internal abstract void PluginManagerShutDown(LoggingContext loggingContext);
    }
}
#pragma warning restore CA1823 // Unused field
