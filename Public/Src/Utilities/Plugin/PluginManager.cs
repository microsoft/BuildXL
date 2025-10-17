﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Plugin.Grpc;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Plugin
{
    /// <summary>
    /// A enum for plugin failure reasons to be put into Domino.stats as an int
    /// (for enabling telemetry-queryable checking of plugin failure reasons).
    /// This list is not comprehensive! Add to this list as needed!
    /// Most failure exceptions will be caught in PluginClient.HandleRpcExceptionWithCallAsync
    /// </summary>
    public enum PluginFailureReason
    {
        /// <summary>
        /// "Unknown": we don't have a classification for this failure. Check Domino.plugin.log
        /// Also could be "None" when there is no failure
        /// </summary>
        Unknown,
        /// <summary>
        /// Plugin failed to load. Check logs for details. (There was more info added here: https://dev.azure.com/mseng/Domino/_git/BuildXL.Internal/pullrequest/739622 but that was reverted via https://dev.azure.com/mseng/Domino/_git/BuildXL.Internal/pullrequest/742682)
        /// </summary>
        PluginLoadError,
        /// <summary>
        /// The plugin communication round-trip did not come back before the gRPC deadline
        /// </summary>
        TimeoutExceeded,
        // Add more values here as needed (and the assignment of that value elsewhere)
    }

    /// <nodoc />
    public class PluginManager
    {
        private class PendingPlugin
        {
            public PluginCreationArgument PluginCreationArguments;
            public IPlugin PreloadedPlugin => PluginCreationArguments.PreloadedPlugin;
            public Task<Possible<IPlugin>> PluginTask;
        }

        private readonly ConcurrentDictionary<string, PendingPlugin> m_plugins;
        private readonly ConcurrentDictionary<PluginMessageType, IPlugin> m_pluginHandlers;
        private bool m_isDisposed = false;
        private readonly LoggingContext m_loggingContext;
        private readonly IReadOnlyList<string> m_pluginPaths;
        private readonly string m_logDirectory;

        private readonly List<PluginMessageType> m_defaultSupportedOperationResponse = new List<PluginMessageType> { PluginMessageType.Unknown };

        private Task m_pluginsLoadedTask;

        private readonly TaskSourceSlim<Unit> m_pluginStopTaskSource;

        /// <summary>
        /// Number of plugins registerd in plugin handlers
        /// </summary>
        public int PluginHandlersCount => m_pluginHandlers.Count;

        /// <summary>
        /// Number of plugins being loaded, this will count even the plugin supported message
        /// type is unknown or duplicated with another plugin
        /// </summary>
        public int PluginsCount => m_plugins.Count;

        #region statistics
        /// <summary>
        /// How long did the plugin take to load at the beginning of the build
        /// (last value wins in the case of multiple loaded plugins)
        /// </summary>
        public long PluginLoadingTimeMs => m_pluginLoadingTimeMs;
        private long m_pluginLoadingTimeMs = 0;

        /// <summary>
        /// How long did the plugin take to process requests (cumulative across all
        /// loaded plugins)
        /// </summary>
        public long PluginTotalProcessTimeMs => m_pluginTotalProcessTimeMs;
        private long m_pluginTotalProcessTimeMs = 0;

        /// <summary>
        /// How many plugins were successfully loaded at the beginning of the build
        /// </summary>
        public int PluginLoadedSuccessfulCount => m_pluginLoadedSuccessfulCount;
        private int m_pluginLoadedSuccessfulCount = 0;

        /// <summary>
        /// How many plugins failed to load at the beginning of the build
        /// </summary>
        public int PluginLoadedFailureCount => m_pluginLoadedFailureCount;
        private int m_pluginLoadedFailureCount = 0;

        /// <summary>
        /// How many requests were processed successfully by plugins (cumulative across
        /// all loaded plugins)
        /// </summary>
        public int PluginProcessedRequestCounts => m_pluginProcessedRequestCounts;
        private int m_pluginProcessedRequestCounts = 0;

        /// <summary>
        /// How many requests processed by the plugin were over a certain threshold 
        /// (cumulative across all loaded plugins)
        /// </summary>
        public long PluginRequestsOverThreshold => m_pluginRequestsOverThreshold;
        private long m_pluginRequestsOverThreshold = 0;
        private const long Threshold = 3000; //The previous GrpcPluginSettings timeout was 3000 ms

        /// <summary>
        /// How many plugins were unregistered due to failures (when a plugin fails to
        /// return a response for a request, the assumption is that the plugin can no
        /// longer be relied on, so it is unregistered for the rest of the build)
        /// </summary>
        public int PluginUnregisteredCounts => m_pluginUnregisteredCounts;
        private int m_pluginUnregisteredCounts = 0;

        /// <summary>
        /// Average time to process a request in milliseconds (includes all successfully
        /// processed requests across all loaded plugins)
        /// </summary>
        public long PluginAverageRequestProcessTimeMs => m_pluginAverageRequestProcessTimeMs;
        private long m_pluginAverageRequestProcessTimeMs = 0;

        /// <summary>
        /// The process request time (in milliseconds) of the longest request to the plugin
        /// </summary>
        public long PluginLongestRequestProcessTimeMs => m_pluginLongestRequestProcessTimeMs;
        private long m_pluginLongestRequestProcessTimeMs = 0;

        /// <summary>
        /// A value representing why the plugin failed. This allows telemetry
        /// queries to easily show failure reasons without reading log files
        /// (last value wins in the case of multiple loaded plugins)
        /// </summary>
        public PluginFailureReason PluginFailureReason
        {
            //Using the thread-safe Interlocked.Exchange for enum https://stackoverflow.com/q/7177169/2246411
            get
            {
                return (PluginFailureReason)m_pluginFailureReason;
            }
            set
            {
                Interlocked.Exchange(ref m_pluginFailureReason, (int)value);
            }
        }
        private int m_pluginFailureReason = (int)PluginFailureReason.Unknown;
        #endregion statistics

        /// <nodoc />
        public PluginManager(LoggingContext loggingContext, string logDirectory, IEnumerable<string> pluginLocations)
        {
            m_plugins = new ConcurrentDictionary<string, PendingPlugin>();
            m_pluginHandlers = new ConcurrentDictionary<PluginMessageType, IPlugin>();
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

        private Task LoadPluginsAsync()
        {
            var tasks = m_pluginPaths.Select(pluginPath => GetOrCreateAsync(pluginPath)).ToList();
            return Task.WhenAll(tasks).ContinueWith(task =>
            {
                Tracing.Logger.Log.PluginManagerLoadingPluginsFinished(m_loggingContext);
            });
        }

        /// <nodoc />
        public bool CanHandleMessage(PluginMessageType messageType)
        {
            return m_pluginHandlers.TryGetValue(messageType, out _);
        }

        /// <nodoc />
        public HashSet<PluginMessageType> GetSupportedMessageTypesOfLoadedPlugins()
        {
            //Ensure all plugin handles are created
            m_pluginsLoadedTask.WithTimeoutAsync(TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();

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
                Interlocked.Add(ref m_pluginTotalProcessTimeMs, sw.ElapsedMilliseconds);

                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Received response for requestId:{response.RequestId} for {messageType} in {sw.ElapsedMilliseconds} ms after {response.Attempts} retries " +
                    $"[{(response.Succeeded ? "SUCCEEDED" : $"FAILED (Failure: {response.Failure.Describe()}")}]");

                if (sw.ElapsedMilliseconds > m_pluginLongestRequestProcessTimeMs)
                {
                    Interlocked.Exchange(ref m_pluginLongestRequestProcessTimeMs, sw.ElapsedMilliseconds);
                }

                if (sw.ElapsedMilliseconds > Threshold)
                {
                    Interlocked.Increment(ref m_pluginRequestsOverThreshold);
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Request requestId:{response.RequestId} exceeded the stat threshold of {Threshold} ms ({sw.ElapsedMilliseconds} ms)");
                }

                long movingAverage = m_pluginAverageRequestProcessTimeMs - m_pluginAverageRequestProcessTimeMs / m_pluginProcessedRequestCounts + sw.ElapsedMilliseconds / m_pluginProcessedRequestCounts;
                Interlocked.Exchange(ref m_pluginAverageRequestProcessTimeMs, movingAverage);

                if (!response.Succeeded)
                {
                    if (response.Failure.Describe().Contains("DeadlineExceeded"))
                    {
                        PluginFailureReason = PluginFailureReason.TimeoutExceeded;
                    }

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

        /// <summary>
        /// Rather than providing a "pluginPath" arg that directly links to a plugin exe, a .config file link can be provided instead.
        /// The settings for the plugin (timeout, supported message types, supported processes etc) will be loaded from this config file
        /// quickly while the actual plugin is started asynchronously. This means some early plugin messages that would have been
        /// skipped anyway don't have to wait on the plugin to start up and respond to the SupportedOperation message. Messages that *do*
        /// need to be sent to the plugin before it is started will still wait on EnsurePluginCreated
        /// </summary>
        private void PreloadPluginFromConfig(PluginCreationArgument pluginCreationArgument)
        {
            Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Preloading plugin object from {pluginCreationArgument.PluginPath}");
            PluginConfig config = JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(pluginCreationArgument.PluginPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });

            pluginCreationArgument.PluginPath = config.PluginPath;

            IPluginClient client = pluginCreationArgument.CreatePluginClientFunc.Invoke(pluginCreationArgument.ConnectionOption);
            client.RequestTimeout = config.Timeout;
            client.SupportedProcesses = new HashSet<string>(config.SupportedProcesses, StringComparer.OrdinalIgnoreCase);
            client.ExitGracefully = config.ExitGracefully;
            Plugin plugin = new Plugin(pluginCreationArgument.PluginId, config.PluginPath, client);

            plugin.SupportedMessageType = config.MessageTypes;
            foreach (PluginMessageType supportedMessageType in config.MessageTypes)
            {
                RegisterPluginHandler(supportedMessageType, plugin);
            }

            pluginCreationArgument.PreloadedPlugin = plugin;
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

                if (pluginCreationArgument.PluginPath.EndsWith(".config"))
                {
                    PreloadPluginFromConfig(pluginCreationArgument);
                }

                var sw = Stopwatch.StartNew();
                var result = await PluginFactory.Instance.CreatePluginAsync(pluginCreationArgument);

                Interlocked.Add(ref m_pluginLoadingTimeMs, sw.ElapsedMilliseconds);

                if (result.Succeeded)
                {
                    Interlocked.Increment(ref m_pluginLoadedSuccessfulCount);
                    var plugin = result.Result;
                    Tracing.Logger.Log.PluginManagerLoadingPlugin(m_loggingContext, plugin.FilePath, plugin.Name, plugin.Id);
                }
                else
                {
                    Interlocked.Increment(ref m_pluginLoadedFailureCount);
                    PluginFailureReason = PluginFailureReason.PluginLoadError;
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Failed to load {pluginCreationArgument.PluginPath} because {result.Failure.Describe()}");
                }

                return result.Succeeded ? new Possible<IPlugin>(result.Result) : new Failure<IPlugin>(null);
            }
            catch (Exception e)
            {
                Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Failed to create plugin: {e}");
                return new Failure<string>($"Can't start plugin with exception {e}");
            }
        }

        /// <nodoc />
        public Task<Possible<List<PluginMessageType>>> GetSupportedMessageTypeAsync(IPlugin plugin)
        {
            return CallWithEnsurePluginCreatedWrapperAsync(
                PluginMessageType.SupportedOperation, plugin,
                plugin.GetSupportedPluginMessageType,
                m_defaultSupportedOperationResponse);
        }

        /// <nodoc />
        public async Task<Possible<LogParseResult>> LogParseAsync(string executable, string message, bool isErrorOutput)
        {
            IPlugin plugin = null;
            var messageType = PluginMessageType.ParseLogMessage;
            if (!m_pluginHandlers.TryGetValue(messageType, out plugin))
            {
                return new Failure<string>($"No plugin is available to handle {messageType}");
            }

            HashSet<string> pluginSupportedProccesses = plugin.PluginClient.SupportedProcesses;
            if (pluginSupportedProccesses != null && pluginSupportedProccesses.Count > 0 && !pluginSupportedProccesses.Contains(Path.GetFileName(executable)))
            {
                return new Failure<string>($"Skipping {messageType} for {executable} as it is not listed as a supported process");
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
            if (!m_pluginHandlers.TryGetValue(messageType, out plugin))
            {
                return new Failure<string>($"No plugin is available to handle {messageType}");
            }

            HashSet<string> pluginSupportedProccesses = plugin.PluginClient.SupportedProcesses;
            if (pluginSupportedProccesses != null && pluginSupportedProccesses.Count > 0 && !pluginSupportedProccesses.Contains(Path.GetFileName(executable)))
            {
                return new Failure<string>($"Skipping {messageType} for {executable} as it is not listed as a supported process");
            }

            return await CallWithEnsurePluginCreatedWrapperAsync(
                messageType, plugin,
                () => { return plugin.ProcessResultAsync(executable, arguments, input, output, error, exitCode, pipSemiStableHash); },
                new ProcessResultMessageResponse());
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
            PendingPlugin pendingPlugin = m_plugins.GetOrAdd(startPluginArguments.PluginPath, path => new PendingPlugin
            {
                PluginCreationArguments = startPluginArguments,
                PluginTask = CreatePluginAsync(startPluginArguments),
            });

            Possible<IPlugin> creationResult = await pendingPlugin.PluginTask; // Await the plugin startup task before we request Supported Message Types

            if (!creationResult.Succeeded)
            {
                m_plugins.TryRemove(startPluginArguments.PluginPath, out _);
                return creationResult;
            }

            if (startPluginArguments.PreloadedPlugin != null)
            {
                // If the plugin was "preloaded", then it was already added to the handlers list so we don't need to call GetSupportedMessageType
                return startPluginArguments.PreloadedPlugin;
            }

            var plugin = creationResult.Result;
            Possible<List<PluginMessageType>> messageType = await GetSupportedMessageTypeAsync(plugin);
            if (messageType.Succeeded)
            {
                // Register the plugin in handlers only when response of supportedMessageType is received
                foreach (var pluginMessageType in messageType.Result)
                {
                    RegisterPluginHandler(pluginMessageType, plugin);
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

        private void RegisterPluginHandler(PluginMessageType pluginMessageType, IPlugin plugin)
        {
            IPlugin alreadyRegisteredPlugin = null;
            if (!m_pluginHandlers.TryGetValue(pluginMessageType, out alreadyRegisteredPlugin))
            {
                m_pluginHandlers.TryAdd(pluginMessageType, plugin);
            }
            else
            {
                Tracing.Logger.Log.PluginManagerWarningMessage(m_loggingContext, $"Two plugins ({plugin.FilePath} and {alreadyRegisteredPlugin.FilePath}) can handle {pluginMessageType}. This scenario is not currently supported.");
            }
        }

        /// <nodoc />
        private void UnRegisterPlugin(IPlugin plugin)
        {
            foreach (PluginMessageType messageType in Enum.GetValues(typeof(PluginMessageType)).Cast<PluginMessageType>())
            {
                bool success = m_pluginHandlers.TryGetValue(messageType, out IPlugin pluginHandler);
                if (success && pluginHandler == plugin)
                {
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Unregistering plugin handler for {messageType}");
                    success = m_pluginHandlers.TryRemove(messageType, out _);
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

        private async Task StopAllPlugins()
        {
            // Shutdown request to plugins should only wait on startup task for plugins that need to exit gracefully
            List<Task<Possible<IPlugin>>> pluginTasksToWaitOn = m_plugins.Values
                .Where(p => p.PreloadedPlugin == null || p.PreloadedPlugin.ExitGracefully)
                .Select(p => p.PluginTask)
                .ToList();

            Possible<IPlugin>[] results = await Task.WhenAll(pluginTasksToWaitOn);

            await Task.WhenAll(results.Where(r => r.Succeeded)
                .Select(r =>
                {
                    Tracing.Logger.Log.PluginManagerLogMessage(m_loggingContext, $"Stop plugin {r.Result.Name}-{r.Result.Id}");
                    return r.Result.StopAsync();
                }))
                .ContinueWith(t => m_pluginStopTaskSource.TrySetResult(Unit.Void));
        }

        /// <nodoc />
        public void Clear()
        {
            m_pluginStopTaskSource.Task.GetAwaiter().GetResult();

            foreach (var pendingPlugin in m_plugins.Values)
            {
                if (pendingPlugin.PluginTask.Result.Succeeded)
                {
                    pendingPlugin.PluginTask.Result.Result.Dispose();
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

