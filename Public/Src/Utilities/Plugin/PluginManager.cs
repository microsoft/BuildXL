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
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public class PluginManager
    {
        private readonly ConcurrentDictionary<string, Task<Possible<IPlugin>>> m_plugins;
        private readonly PluginHandlers m_pluginHandlers;
        private bool m_isDisposed = false;
        private readonly LoggingContext m_loggingContext;
        private readonly IReadOnlyList<string> m_pluginPaths;
        private readonly string m_logDirectory;
        private const string ResponseReceivedLogMessageFormat = "Received response for requestId:{0} for {1} in {2} ms after retry:{3}";

        private readonly List<PluginMessageType> m_defaultSupportedOperationResponse = new List<PluginMessageType> { PluginMessageType.Unknown };

    private readonly TaskSourceSlim<Unit> m_pluginStopTask;

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
        #endregion statistics

        /// <nodoc />
        public PluginManager(LoggingContext loggingContext, string logDirectory, IEnumerable<string> pluginLocations)
        {
            m_plugins = new ConcurrentDictionary<string, Task<Possible<IPlugin>>>();
            m_pluginHandlers = new PluginHandlers();
            m_pluginStopTask = TaskSourceSlim.Create<Unit>();
            m_loggingContext = loggingContext;
            m_logDirectory = logDirectory;
            m_pluginPaths = pluginLocations.ToList();
        }

        /// <nodoc />
        public void Start()
        {
            Tracing.Logger.Log.PluginManagerStarting(m_loggingContext);
            LoadPluginsAsync().Forget();
        }

        private IEnumerable<PluginFile> GetPluginsPaths()
        {
            return m_pluginPaths.Select(path => new PluginFile() { Path = path });
        }

        private async Task LoadPluginsAsync()
        {
            var tasks = GetPluginsPaths().Select(pluginFile => GetOrCreateAsync(pluginFile.Path)).ToList();
            await Task.WhenAll(tasks).ContinueWith(task =>
            {
                Tracing.Logger.Log.PluginManagerLoadingPluginsFinished(m_loggingContext);
            });

            return;
        }

        /// <nodoc />
        public bool CanHandleMessage(PluginMessageType messageType)
        {
            return m_pluginHandlers.TryGet(messageType, out _);
        }

        /// <nodoc />
        private void EnsurePluginLoaded(IPlugin plugin)
        {
            if (!plugin.StartCompletionTask.IsCompleted)
            {
                plugin.StartCompletionTask.GetAwaiter().GetResult();
            }
        }

        /// <nodoc />
        private async Task<Possible<T>> CallWithEnsurePluginLoadedWrapperAsync<T>(PluginMessageType messageType, IPlugin plugin, Func<Task<PluginResponseResult<T>>> call, T defaultReturnValue)
        {
            EnsurePluginLoaded(plugin);

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await call.Invoke();
                sw.Stop();
                Interlocked.Increment(ref m_pluginProcessedRequestCounts);
                Interlocked.Add(ref m_pluginTotalProcessTime, sw.ElapsedMilliseconds);

                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, string.Format(CultureInfo.InvariantCulture, ResponseReceivedLogMessageFormat, response.RequestId, messageType, sw.ElapsedMilliseconds, response.Attempts));
                return response.Succeeded ? response.Value : new Possible<T>(response.Failure);
            }
            catch(Exception e)
            {
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"grpc call with type {messageType.ToString()} failed with {e}");
                return new Failure<T>(defaultReturnValue);
            }
        }

        /// <nodoc />
        private async Task<Possible<IPlugin>> CreatePluginAsync(PluginCreationArgument pluinCreationArgument)
        {
            try
            {
                if (!File.Exists(pluinCreationArgument.PluginPath) && pluinCreationArgument.RunInSeparateProcess)
                {
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Can't Load plugin because {pluinCreationArgument.PluginPath} doesn't exist");
                    new Failure<IPlugin>(null);
                }

                var sw = Stopwatch.StartNew();
                var result = await PluginFactory.Instance.CreatePluginAsync(pluinCreationArgument);

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
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Failed to load {pluinCreationArgument.PluginPath} because {result.Failure.Describe()}");
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
            return CallWithEnsurePluginLoadedWrapperAsync(
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
                return new Failure<string>($"no plugin is available to handle {messageType}");
            }

            return await CallWithEnsurePluginLoadedWrapperAsync(
                messageType, plugin,
                () => { return plugin.ParseLogAsync(message, isErrorOutput); },
                new LogParseResult() { ParsedMessage = message});
        }

        /// <nodoc />
        public async Task<Possible<ExitCodeParseResult>> ExitCodeParseAsync(string content, string filePath, bool isErrorOutput)
        {
            IPlugin plugin = null;
            var messageType = PluginMessageType.HandleExitCode;
            if (!m_pluginHandlers.TryGet(messageType, out plugin))
            {
                return new Failure<string>($"no plugin is available to handle {messageType}");
            }

            return await CallWithEnsurePluginLoadedWrapperAsync(
                messageType, plugin,
                () => { return plugin.HandleExitCodeAsync(content, filePath, isErrorOutput); },
                new ExitCodeParseResult());
        }

        private PluginCreationArgument GetPluginArgument(string pluginPath, bool runInSeparateProcess)
        {
            var pluginId = PluginFactory.Instance.CreatePluginId();
            return new PluginCreationArgument()
            {
                PluginPath = pluginPath,
                RunInSeparateProcess = runInSeparateProcess,
                PluginId = pluginId,
                ConnectionOption = new PluginConnectionOption()
                {
                    IpcMoniker = IpcFactory.GetProvider().LoadAndRenderMoniker(IpcFactory.GetProvider().CreateNewMoniker().Id),
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
                        Tracing.Logger.Log.PluginManagerErrorMessage(m_loggingContext, $"Two plugins({plugin.FilePath} and {alreadyRegisteredPlugin.FilePath}) can hanlde {pluginMessageType} that we don't suuport this scenario");
                    }
                }
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Supported Messagey Type for {plugin.Name} is {string.Join(",", messageType.Result)}");
                plugin.SupportedMessageType = messageType.Result;
            }
            else
            {
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Can't get supported message tyep for {plugin.Name}");
            }

            return creationResult;
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
                .ContinueWith(t => m_pluginStopTask.TrySetResult(Unit.Void));
        }

        /// <nodoc />
        public void Clear()
        {
            m_pluginStopTask.Task.GetAwaiter().GetResult();

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

