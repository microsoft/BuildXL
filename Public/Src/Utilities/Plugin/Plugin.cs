﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public static class PluginConstants
    {
        /// <summary>
        /// Name of the environment variable listing the capabilities of the currently loaded plugins
        /// </summary>
        public const string PluginCapabilitiesEnvVar = "[BUILDXL]LOADED_PLUGIN_CAPABILITIES";
    }

    /// <nodoc />
    public class Plugin : IPlugin
    {
        private readonly TaskSourceSlim<Unit> m_startCompletionTaskSoure;
        private bool m_disposed = false;

        /// <nodoc />
        public string FilePath { get; }
        /// <nodoc />
        public string Name { get; }
        /// <nodoc />
        public Process PluginProcess { get; set; }

        /// <summary>
        /// Task that has plugin server running inside it. It is mainly for test purpose
        /// </summary>
        public Task PluginTask { get; set; }

        /// <summary>
        /// Used to cancel the plugin running thread
        /// </summary>
        public CancellationTokenSource PluginTaskCancellationTokenSource { get; set; }

        /// <nodoc />
        public IPluginClient PluginClient { get; }
        /// <nodoc />
        public PluginStatus Status { get; set; }

        /// <nodoc />
        public string Id { get; }

        /// <nodoc />
        public Task StartCompletionTask {
            get
            { 
                return m_startCompletionTaskSoure.Task;
            }
        }

        /// <nodoc />
        public List<PluginMessageType> SupportedMessageType { get; set; }

        /// <nodoc />
        public int RequestTimeout
        {
            get
            {
                return PluginClient.RequestTimeout;
            }
            set
            {
                PluginClient.RequestTimeout = value;
            }
        }

        /// <nodoc />
        public HashSet<string> SupportedProcesses
        {
            get
            {
                return PluginClient.SupportedProcesses;
            }
            set
            {
                PluginClient.SupportedProcesses = value;
            }
        }

        /// <nodoc />
        public bool ExitGracefully
        {
            get
            {
                return PluginClient.ExitGracefully;
            }
            set
            {
                PluginClient.ExitGracefully = value;
            }
        }

        /// <nodoc />
        public Plugin(string id, string path, Process process, IPluginClient pluginClient)
        {
            Contract.RequiresNotNullOrEmpty(id, "plugin id is null");
            Contract.RequiresNotNullOrEmpty(path, "plugin path is null");
            Contract.RequiresNotNull(process, "plugin process is null");
            Contract.RequiresNotNull(pluginClient, "pluginclient is null");

            Id = id;
            FilePath = path;
            Name = Path.GetFileName(path);
            PluginProcess = process;
            PluginClient = pluginClient;
            Status = PluginStatus.Initialized;
            PluginProcess.Exited += HandleProcessExisted;
            m_startCompletionTaskSoure = TaskSourceSlim.Create<Unit>();
        }

        /// <nodoc />
        public Plugin(string id, string path, Task pluginTask, CancellationTokenSource cancellationTokenSource, IPluginClient pluginClient)
        {
            Contract.RequiresNotNullOrEmpty(id, "plugin id is null");
            Contract.RequiresNotNullOrEmpty(path, "plugin path is null");
            Contract.RequiresNotNull(pluginTask, "plugin task is null");
            Contract.RequiresNotNull(pluginClient, "pluginclient is null");

            Id = id;
            FilePath = path;
            Name = Path.GetFileName(path);
            PluginTask = pluginTask;
            PluginTaskCancellationTokenSource = cancellationTokenSource;
            PluginClient = pluginClient;
            Status = PluginStatus.Initialized;
            m_startCompletionTaskSoure = TaskSourceSlim.Create<Unit>();
        }

        /// <nodoc />
        public Plugin(string id, string path, IPluginClient pluginClient)
        {
            Contract.RequiresNotNullOrEmpty(id, "plugin id is null");
            Contract.RequiresNotNullOrEmpty(path, "plugin path is null");
            Contract.RequiresNotNull(pluginClient, "pluginclient is null");

            Id = id;
            FilePath = path;
            Name = Path.GetFileName(path);
            PluginClient = pluginClient;
            Status = PluginStatus.Initialized;
            m_startCompletionTaskSoure = TaskSourceSlim.Create<Unit>();
        }

        /// <nodoc />
        public void Kill()
        {
            if (PluginProcess != null && !PluginProcess.HasExited && !PluginProcess.WaitForExit(milliseconds: 1000))
            {
                PluginProcess.Kill();
                PluginProcess.WaitForExit(3000);
            }
        }

        /// <nodoc />
        private void HandleProcessExisted(object sender, EventArgs args)
        {
            Status = PluginStatus.Faulted;
        }

        /// <nodoc />
        public void Dispose()
        {
            if (!m_disposed)
            {
                PluginClient.Dispose();

                if (PluginProcess != null)
                {
                    if (!PluginProcess.HasExited)
                    {
                        Kill();
                    }
                    PluginProcess.Exited -= HandleProcessExisted;
                    PluginProcess.Dispose();
                }
                else if (PluginTask != null && !PluginTask.IsCompleted)
                {
                    PluginTaskCancellationTokenSource.Cancel();
                }

                m_disposed = true;
            }
        }

        /// <nodoc />
        public Task<PluginResponseResult<bool>> StartAsync()
        {
            var res =  PluginClient.StartAsync();
            m_startCompletionTaskSoure.TrySetResult(Unit.Void);
            return res;
        }

        /// <nodoc />
        public Task<PluginResponseResult<bool>> StopAsync()
        {
            return PluginClient.StopAsync();
        }

        /// <nodoc />
        public Task<PluginResponseResult<List<PluginMessageType>>> GetSupportedPluginMessageType()
        {
            return PluginClient.GetSupportedPluginMessageType();
        }

        /// <nodoc />
        public Task<PluginResponseResult<LogParseResult>> ParseLogAsync(string message, bool isErrorOutput)
        {
            return PluginClient.ParseLogAsync(message, isErrorOutput);
        }

        /// <nodoc />
        public Task<PluginResponseResult<ProcessResultMessageResponse>> ProcessResultAsync(string executable,
                                                                                           string arguments,
                                                                                           ProcessStream input,
                                                                                           ProcessStream output,
                                                                                           ProcessStream error,
                                                                                           int exitCode,
                                                                                           string pipSemiStableHash)
        {
            return PluginClient.ProcessResultAsync(executable, arguments, input, output, error, exitCode, pipSemiStableHash);
        }
    }
}
