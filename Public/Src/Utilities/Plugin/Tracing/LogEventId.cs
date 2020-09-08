// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Plugin.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        PluginManagerStarting = 12300,
        PluginManagerLoadingPlugin = 12301,
        PluginManagerLogMessage = 12302,
        PluginManagerLoadingPluginsFinished = 12303,
        PluginManagerSendOperation = 12304,
        PluginManagerResponseReceived = 12305,
        PluginManagerShutDown = 12306,
        PluginManagerForwardedPluginClientMessage = 12307,
        PluginManagerErrorMessage = 12308
    }
}
