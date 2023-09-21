// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginFactory: IPluginFactory
    {
        /// <nodoc />
        private readonly LoggingContext m_loggingContext;

        /// <nodoc />
        public PluginFactory(LoggingContext loggingContext)
        {
            m_loggingContext = loggingContext;
        }

        private const string StartPluginProcessArgumentsForamt = "--ipcmoniker {0} --logdir {1} --logVerbose {2}";

        private IPlugin PluginCreation_RunInProcess(PluginCreationArgument argument)
        {
            Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"PluginFactory starting plugin {argument.PluginPath} with args " +
                $"{string.Format(StartPluginProcessArgumentsForamt, argument.ConnectionOption.IpcMoniker, argument.ConnectionOption.LogDir, argument.ConnectionOption.LogVerbose)}");

            var processStartInfo = new ProcessStartInfo(argument.PluginPath)
            {
                Arguments = string.Format(StartPluginProcessArgumentsForamt, argument.ConnectionOption.IpcMoniker, argument.ConnectionOption.LogDir, argument.ConnectionOption.LogVerbose),
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
            };

            string id = argument.PluginId;

            var process = new Process();
            process.StartInfo = processStartInfo;
            bool success = process.Start();

            Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"PluginFactory start result for plugin {argument.PluginPath}: {success}");

            var client = argument.CreatePluginClientFunc.Invoke(argument.ConnectionOption);
            var plugin = new Plugin(id, argument.PluginPath, process, client);
            return plugin;
        }

        private IPlugin PluginCreation_RunInThread(PluginCreationArgument argument)
        {
            Contract.RequiresNotNull(argument, "argument can't be null");
            Contract.RequiresNotNull(argument.RunInPluginThreadAction, "runInpluginThead can't be null");

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            Task pluginTask = Task.Run(() =>
            {
                argument.RunInPluginThreadAction.Invoke();
            }, cancellationTokenSource.Token);
            Thread.Sleep(500);
            var client = argument.CreatePluginClientFunc.Invoke(argument.ConnectionOption);
            var plugin = new Plugin(argument.PluginId, argument.PluginPath, pluginTask, cancellationTokenSource, client);
            return plugin;
        }

        /// <nodoc />
        public IPlugin CreatePlugin(PluginCreationArgument pluginCreationArgument)
        {
            return pluginCreationArgument.RunInSeparateProcess ?
                    PluginCreation_RunInProcess(pluginCreationArgument) :
                    PluginCreation_RunInThread(pluginCreationArgument);
        }

        /// <nodoc />
        public async Task<Possible<IPlugin>> CreatePluginAsync(PluginCreationArgument pluginCreationArgument)
        {
            PluginResponseResult<bool> pluginCreationResult;
            try
            {
                var plugin = CreatePlugin(pluginCreationArgument);
                pluginCreationResult = await plugin.StartAsync();

                if (pluginCreationResult.Succeeded)
                {
                    plugin.Status = PluginStatus.Running;
                    return new Possible<IPlugin>(plugin);
                }
                else
                {
                    string stderr = await plugin.PluginProcess.StandardError.ReadToEndAsync();
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Plugin process stderr:\n{stderr}");
                    plugin.PluginProcess.Kill();
                    return pluginCreationResult.Failure;
                }
            }
            catch(Exception e)
            {
                return new Failure<string>($"Exception happens when start plugin at {pluginCreationArgument.PluginPath}, Exception: {e}");
            }
        }

        /// <nodoc />
        public string CreatePluginId()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
