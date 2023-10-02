// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginManager
    {
        private readonly ConcurrentDictionary<string, Task<Possible<IPlugin>>> m_plugins;
        private readonly PluginHandlers m_pluginHandlers;
        private readonly PluginFactory m_pluginFactory;
        private bool m_isDisposed = false;
        private readonly LoggingContext m_loggingContext;
        private readonly IReadOnlyList<string> m_pluginPaths;
        private readonly string m_logDirectory;

        private readonly List<PluginMessageType> m_defaultSupportedOperationResponse = new List<PluginMessageType> { PluginMessageType.Unknown };

        private Task m_pluginsLoadedTask;

        private readonly TaskSourceSlim<Unit> m_pluginStopTaskSource;

        /// <summary>
        /// Number of plugs registerd in plugin handlers
        /// </summary>
        public int PluginHandlersCount => m_pluginHandlers.Count;

        /// <summary>
        /// Number of plugs being loaded, this will count even the plugin supported message
        /// type is unknown or duplicated with another plugin
        /// </summary>
        public int PluginsCount => m_plugins.Count;

        #region statistics
        /// <nodoc />
        private long m_pluginLoadingTime = 0;
        /// <nodoc />
        public long PluginLoadingTime => m_pluginLoadingTime;
        private long m_pluginTotalProcessTime = 0;
        /// <nodoc />
        public long PluginTotalProcessTime => m_pluginTotalProcessTime;
        private int m_pluginLoadedSuccessfulCount = 0;
        /// <nodoc />
        public int PluginLoadedSuccessfulCount => m_pluginLoadedSuccessfulCount;
        private int m_pluginLoadedFailureCount = 0;
        /// <nodoc />
        public int PluginLoadedFailureCount => m_pluginLoadedFailureCount;
        private int m_pluginProcessedRequestCounts = 0;
        /// <nodoc />
        public int PluginProcessedRequestCounts => m_pluginProcessedRequestCounts;

        private int m_pluginUnregisteredCounts = 0;
        /// <nodoc />
        public int PluginUnregisteredCounts => m_pluginUnregisteredCounts;
        #endregion statistics

        /// <nodoc />
        public PluginManager(LoggingContext loggingContext, string logDirectory, IEnumerable<string> pluginLocations)
        {
            m_plugins = new ConcurrentDictionary<string, Task<Possible<IPlugin>>>();
            m_pluginHandlers = new PluginHandlers();
            m_pluginFactory = new PluginFactory(loggingContext);
            m_pluginStopTaskSource = TaskSourceSlim.Create<Unit>();
            m_loggingContext = loggingContext;
            m_logDirectory = logDirectory;
            m_pluginPaths = pluginLocations.ToList();
        }

        /// <nodoc />
        public void Start()
        {
            Tracing.Logger.Log.PluginManagerStarting(m_loggingContext);
            m_pluginsLoadedTask = LoadPluginsAsync();
            m_pluginsLoadedTask.Forget();
        }

        private IEnumerable<PluginFile> GetPluginsPaths()
        {
            return m_pluginPaths.Select(path => new PluginFile() { Path = path });
        }

        private Task LoadPluginsAsync()
        {
            var tasks = GetPluginsPaths().Select(pluginFile => GetOrCreateAsync(pluginFile.Path)).ToList();
            return Task.WhenAll(tasks).ContinueWith(task =>
            {
                Tracing.Logger.Log.PluginManagerLoadingPluginsFinished(m_loggingContext);
            });
        }

        /// <nodoc />
        public bool CanHandleMessage(PluginMessageType messageType)
        {
            return m_pluginHandlers.TryGet(messageType, out _);
        }

        /// <nodoc />
        public HashSet<PluginMessageType> GetSupportedMessageTypesOfLoadedPlugins()
        {
            //Ensure all plugin handles are created
            m_pluginsLoadedTask.GetAwaiter().GetResult();

            HashSet<PluginMessageType> result = new HashSet<PluginMessageType>();
            if (PluginHandlersCount > 0)
            {
                foreach (PluginMessageType messageType in Enum.GetValues(typeof(PluginMessageType)).Cast<PluginMessageType>())
                {
                    if (CanHandleMessage(messageType))
                    {
                        result.Add(messageType);
                    }
                }
            }

            return result;
        }

        /// <nodoc />
        private void EnsurePluginCreated(IPlugin plugin)
        {
            if (!plugin.StartCompletionTask.IsCompleted)
            {
                plugin.StartCompletionTask.GetAwaiter().GetResult();
            }
        }

        /// <nodoc />
        private async Task<Possible<T>> CallWithEnsurePluginCreatedWrapperAsync<T>(PluginMessageType messageType, IPlugin plugin, Func<Task<PluginResponseResult<T>>> call, T defaultReturnValue)
        {
            EnsurePluginCreated(plugin);

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await call.Invoke();
                sw.Stop();
                Interlocked.Increment(ref m_pluginProcessedRequestCounts);
                Interlocked.Add(ref m_pluginTotalProcessTime, sw.ElapsedMilliseconds);

                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Received response for requestId:{response.RequestId} for {messageType} in {sw.ElapsedMilliseconds} ms after {response.Attempts} retries");

                if (!response.Succeeded)
                {
                    UnRegisterPlugin(plugin);
                }

                return response.Succeeded ? response.Value : new Possible<T>(response.Failure);
            }
            catch(Exception e)
            {
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Grpc call with type {messageType} failed with {e}");
                return new Failure<T>(defaultReturnValue);
            }
        }

        /// <nodoc />
        private async Task<Possible<IPlugin>> CreatePluginAsync(PluginCreationArgument pluginCreationArgument)
        {
            try
            {
                if (!File.Exists(pluginCreationArgument.PluginPath) && pluginCreationArgument.RunInSeparateProcess)
                {
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Can't load plugin because {pluginCreationArgument.PluginPath} doesn't exist");
                    new Failure<IPlugin>(null);
                }

                var sw = Stopwatch.StartNew();
                var result = await m_pluginFactory.CreatePluginAsync(pluginCreationArgument);

                Interlocked.Add(ref m_pluginLoadingTime, sw.ElapsedMilliseconds);

                if (result.Succeeded)
                {
                    Interlocked.Increment(ref m_pluginLoadedSuccessfulCount);
                    var plugin = result.Result;
                    Tracing.Logger.Log.PluginManagerLoadingPlugin(m_loggingContext, plugin.FilePath, plugin.Name, plugin.Id);
                }
                else
                {
                    Interlocked.Increment(ref m_pluginLoadedFailureCount);
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Failed to load {pluginCreationArgument.PluginPath} because {result.Failure.Describe()}");
                }

                return result.Succeeded ? new Possible<IPlugin>(result.Result) : new Failure<IPlugin>(null);
            }
            catch (Exception e)
            {
                return new Failure<string>($"Can't start plugin with exception {e}");
            }
        }

        /// <nodoc />
        public Task<Possible<List<PluginMessageType>>> GetSupportedMessageTypeAsync(IPlugin plugin)
        {
            return CallWithEnsurePluginCreatedWrapperAsync(
                PluginMessageType.SupportedOperation, plugin,
                () => { return plugin.GetSupportedPluginMessageType(); },
                m_defaultSupportedOperationResponse);
        }

        /// <nodoc />
        public async Task<Possible<LogParseResult>> LogParseAsync(string message, bool isErrorOutput)
        {
            IPlugin plugin = null;
            var messageType = PluginMessageType.ParseLogMessage;
            if (!m_pluginHandlers.TryGet(messageType, out plugin))
            {
                return new Failure<string>($"No plugin is available to handle {messageType}");
            }

            return await CallWithEnsurePluginCreatedWrapperAsync(
                messageType, plugin,
                () => { return plugin.ParseLogAsync(message, isErrorOutput); },
                new LogParseResult() { ParsedMessage = message});
        }

        /// <nodoc />
        public async Task<Possible<ProcessResultMessageResponse>> ProcessResultAsync(string executable, 
                                                                                     string arguments,
                                                                                     ProcessStream input,
                                                                                     ProcessStream output,
                                                                                     ProcessStream error,
                                                                                     int exitCode,
                                                                                     string pipSemiStableHash)
        {
            IPlugin plugin = null;
            var messageType = PluginMessageType.ProcessResult;
            if (!m_pluginHandlers.TryGet(messageType, out plugin))
            {
                return new Failure<string>($"No plugin is available to handle {messageType}");
            }

            return await CallWithEnsurePluginCreatedWrapperAsync(
                messageType, plugin,
                () => { return plugin.ProcessResultAsync(executable, arguments, input, output, error, exitCode, pipSemiStableHash); },
                new ProcessResultMessageResponse());
        }

        private PluginCreationArgument GetPluginArgument(string pluginPath, bool runInSeparateProcess)
        {
            var pluginId = m_pluginFactory.CreatePluginId();
            return new PluginCreationArgument()
            {
                PluginPath = pluginPath,
                RunInSeparateProcess = runInSeparateProcess,
                PluginId = pluginId,
                ConnectionOption = new PluginConnectionOption()
                {
                    IpcMoniker = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcMoniker.CreateNew().Id),
                    LogDir = m_logDirectory,
                    Logger = PluginLogUtils.CreateLoggerForPluginClients(m_loggingContext, pluginId)
                },

                CreatePluginClientFunc = options =>
                {
                    int port = TcpIpConnectivity.ParsePortNumber(options.IpcMoniker);
                    return new PluginClient(IPAddress.Loopback.ToString(), port, options.Logger);
                }
            };
        }

        /// <nodoc />
        public Task<Possible<IPlugin>> GetOrCreateAsync(string pluginPath, bool runInProcess = true)
        {
            var startPluginArguments = GetPluginArgument(pluginPath, runInProcess);
            return GetOrCreateAsync(startPluginArguments);
        }

        /// <nodoc />
        public async Task<Possible<IPlugin>> GetOrCreateAsync(PluginCreationArgument startPluginArguments)
        {
            var creationResult = await m_plugins.GetOrAdd(startPluginArguments.PluginPath, path => CreatePluginAsync(startPluginArguments));
            if (!creationResult.Succeeded)
            {
                m_plugins.TryRemove(startPluginArguments.PluginPath, out _);
                return creationResult;
            }

            var plugin = creationResult.Result;
            Possible<List<PluginMessageType>> messageType = await GetSupportedMessageTypeAsync(plugin);
            if (messageType.Succeeded)
            {
                // Register the plugin in handlers only when response of supportedMessageType is received
                foreach (var pluginMessageType in messageType.Result)
                {
                    IPlugin alreadyRegisteredPlugin = null;
                    if (!m_pluginHandlers.TryGet(pluginMessageType, out alreadyRegisteredPlugin))
                    {
                        m_pluginHandlers.TryAdd(pluginMessageType, plugin);
                    }
                    else
                    {
                        Tracing.Logger.Log.PluginManagerErrorMessage(m_loggingContext, $"Two plugins ({plugin.FilePath} and {alreadyRegisteredPlugin.FilePath}) can handle {pluginMessageType}. This scenario is not currently supported.");
                    }
                }
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Supported message types for {plugin.Name} is {string.Join(",", messageType.Result)}");
                plugin.SupportedMessageType = messageType.Result;
            }
            else
            {
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Can't get supported message types for {plugin.Name}");
            }

            return creationResult;
        }

        /// <nodoc />
        private void UnRegisterPlugin(IPlugin plugin)
        {
            foreach (PluginMessageType messageType in Enum.GetValues(typeof(PluginMessageType)).Cast<PluginMessageType>())
            {
                bool success = m_pluginHandlers.TryGet(messageType, out IPlugin pluginHandler);
                if (success && pluginHandler == plugin)
                {
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Unregistering plugin handler for {messageType}");
                    success = m_pluginHandlers.TryRemove(messageType);
                    if (!success)
                    {
                        Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Unable to remove plugin handler for {messageType}");
                    }
                    else
                    {
                        Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Removed plugin handler for {messageType}");
                        Interlocked.Increment(ref m_pluginUnregisteredCounts);
                    }
                }
            }
        }

        /// <nodoc />
        public Task Stop()
        {
            return StopAllPlugins();
        }

        private Task StopAllPlugins()
        {
           return Task.WhenAll(m_plugins.Values.Where(pluginInfoTask => pluginInfoTask.Result.Succeeded)
                .Select(pluginInfoTask =>
                {
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Stop plugin {pluginInfoTask.Result.Result.Name}-{pluginInfoTask.Result.Result.Id}");
                    return pluginInfoTask.Result.Result.StopAsync();
                }))
                .ContinueWith(t => m_pluginStopTaskSource.TrySetResult(Unit.Void));
        }

        /// <nodoc />
        public void Clear()
        {
            m_pluginStopTaskSource.Task.GetAwaiter().GetResult();

            foreach (var pluginInfoTask in m_plugins.Values)
            {
                if (pluginInfoTask.Result.Succeeded)
                {
                    pluginInfoTask.Result.Result.Dispose();
                }
            }

            m_plugins.Clear();
        }

        /// <nodoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            Clear();

            Tracing.Logger.Log.PluginManagerShutDown(m_loggingContext);
            GC.SuppressFinalize(this);
            m_isDisposed = true;
        }
    }
}

