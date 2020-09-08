// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginFactory: IPluginFactory
    {
        /// <nodoc />
        public static PluginFactory Instance = new PluginFactory();

        private const string StartPluginProcessArgumentsForamt = "--ipcmoniker {0} --logdir {1} --logVerbose {2}";

        private readonly Func<PluginCreationArgument, IPlugin> m_pluginRunInProcessCreationFunc = argument =>
        {
            var processStartInfo = new ProcessStartInfo(argument.PluginPath)
            {
                Arguments = string.Format(StartPluginProcessArgumentsForamt, argument.ConnectionOption.IpcMoniker, argument.ConnectionOption.LogDir, argument.ConnectionOption.LogVerbose),
                UseShellExecute = false,
                RedirectStandardOutput = false,
            };

            string id = argument.PluginId;

            var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();

            var client = argument.CreatePluginClientFunc.Invoke(argument.ConnectionOption);
            var plugin = new Plugin(id, argument.PluginPath, process, client);
            return plugin;
        };

        private readonly Func<PluginCreationArgument, IPlugin> m_pluginRunInThreadCreationFunc = argument =>
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
        };

        /// <nodoc />
        public IPlugin CreatePlugin(PluginCreationArgument pluginCreationArgument)
        {
            return pluginCreationArgument.RunInSeparateProcess ?
                    m_pluginRunInProcessCreationFunc.Invoke(pluginCreationArgument) :
                    m_pluginRunInThreadCreationFunc.Invoke(pluginCreationArgument);
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

        /// <nodoc />
        public static void SetGrpcPluginClientBasedOnMessageType(IPlugin plugin, PluginMessageType messageType)
        {
            //switch(messageType)
            //{
            //    case PluginMessageType.ParseLogMessage:
            //        plugin.SetLogParsePluginClient();
            //        break;
            //    default:
            //        break;
            //}
        }
    }
}
