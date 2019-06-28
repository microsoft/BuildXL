// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using CLAP;

// ReSharper disable UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    /// <summary>
    ///     Core application implementation with CLAP verbs.
    /// </summary>
    internal sealed partial class Application : IDisposable
    {
        private const string HashTypeDescription = "Content hash type (SHA1/SHA256/MD5/Vso0/DedupChunk/DedupNode)";

        /// <summary>
        ///     The name of this service (sent to Kusto as the value of the 
        ///     <see cref="CsvFileLog.ColumnKind.Service"/> column)
        /// </summary>
        private const string ServiceName = "ContentAddressableStoreService";

        private const string CsvLogFileExt = ".csv";
        private const string TmpCsvLogFileExt = ".csvtmp";

        /// <summary>
        ///     Name of the environment variable in which to look for a Kusto connection string.
        /// </summary>
        private const string KustoConnectionStringEnvVarName = "KustoConnectionString";

        /// <summary>
        ///     Target Kusto database for remote telemetry
        /// </summary>
        private const string KustoDatabase = "CloudBuildCBTest";

        /// <summary>
        ///     Target Kusto table for remote telemetry
        /// </summary>
        private const string KustoTable = "CloudBuildLogEvent";

        /// <summary>
        ///     The Kusto schema of the target table (<see cref="KustoTable"/>).
        ///
        ///     The schema for the target table can be automatically exported from the Kusto Explorer.
        /// </summary>
        private const string KustoTableSchema = "env_ver:string, env_name:string, env_time:datetime, env_epoch:string, env_seqNum:long, env_popSample:real, env_iKey:string, env_flags:long, env_cv:string, env_os:string, env_osVer:string, env_appId:string, env_appVer:string, env_cloud_ver:string, env_cloud_name:string, env_cloud_role:string, env_cloud_roleVer:string, env_cloud_roleInstance:string, env_cloud_environment:string, env_cloud_location:string, env_cloud_deploymentUnit:string, Stamp:string, Ring:string, BuildId:string, CorrelationId:string, ParentCorrelationId:string, Exception:string, Message:string, LogLevel:long, LogLevelFriendly:string, LoggingType:string, LoggingMethod:string, LoggingLineNumber:long, PreciseTimeStamp:datetime, LocalPreciseTimeStamp:datetime, ThreadId:long, ThreadPrincipal:string, Cluster:string, Environment:string, MachineFunction:string, Machine:string, Service:string, ServiceVersion:string, SourceNamespace:string, SourceMoniker:string, SourceVersion:string, ProcessId:long, BuildQueue:string";

        /// <summary>
        ///     CSV file schema to be passed to <see cref="CsvFileLog"/>.
        ///     This schema is automatically generated from <see cref="KustoTableSchema"/>
        /// </summary>
        private static readonly CsvFileLog.ColumnKind[] KustoTableCsvSchema = CsvFileLog.ParseTableSchema(KustoTableSchema);      

        private readonly CancellationToken _cancellationToken;
        private readonly IAbsFileSystem _fileSystem;
        private readonly ConsoleLog _consoleLog;
        private readonly Logger _logger;
        private readonly Tracer _tracer;
        private readonly KustoUploader _kustoUploader;
        private bool _waitForDebugger;
        private FileLog _fileLog;
        private CsvFileLog _csvFileLog;
        private Severity _fileLogSeverity = Severity.Diagnostic;
        private bool _logAutoFlush;
        private string _logDirectoryPath;
        private long _logMaxFileSize;
        private long _csvLogMaxFileSize = 100 * 1024 * 1024; // 100 MB
        private int _logMaxFileCount;
        private bool _pause;
        private string _scenario;
        private uint _connectionsPerSession;
        private uint _retryIntervalSeconds;
        private uint _retryCount;
        private bool _enableRemoteTelemetry;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Application"/> class.
        /// </summary>
        public Application(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _consoleLog = new ConsoleLog(Severity.Warning);
            _logger = new Logger(true, _consoleLog);
            _fileSystem = new PassThroughFileSystem(_logger);
            _tracer = new Tracer(nameof(Application));

            var kustoConnectionString = Environment.GetEnvironmentVariable(KustoConnectionStringEnvVarName);
            _kustoUploader = string.IsNullOrWhiteSpace(kustoConnectionString)
                ? null
                : new KustoUploader
                    (
                    kustoConnectionString,
                    database: KustoDatabase,
                    table: KustoTable,
                    deleteFilesOnSuccess: true,
                    checkForIngestionErrors: true,
                    log: _consoleLog
                    );
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_fileLog")]
        public void Dispose()
        {
            // 1. it's important to dispose _logger before log objects
            //    because _logger.Dispose() calls Flush() on its log objects
            // 2. it's important to dispose _csvFileLogger before _kustoUploader because
            //    csvFileLogger.Dispose() can post one last file to be uploaded to Kusto
            // 3. it's important to dispose _kustoUploader before _consoleLog because
            //    _kustoUploader uses _consoleLog
            _logger.Dispose();
            _fileLog?.Dispose();
            _csvFileLog?.Dispose();
            _kustoUploader?.Dispose(); 
            _consoleLog.Dispose();
            _fileSystem.Dispose();
        }

        /// <summary>
        ///     Show user help.
        /// </summary>
        /// <param name="help">Help string generated by CLAP.</param>
        /// <remarks>
        ///     This is intended to only be called by CLAP.
        /// </remarks>
        [Help(Aliases = "help,h,?")]
        public void ShowHelp(string help)
        {
            Contract.Requires(help != null);
            _logger.Always("ContentStore Tool");
            _logger.Always(help);
        }

        /// <summary>
        ///     Handle verb exception.
        /// </summary>
        /// <remarks>
        ///     This is intended to only be called by CLAP.
        /// </remarks>
        [Error]
        public void HandleError(ExceptionContext exceptionContext)
        {
            Contract.Requires(exceptionContext != null);
            _logger.Error(exceptionContext.Exception.InnerException != null
                ? $"{exceptionContext.Exception.Message}: {exceptionContext.Exception.InnerException.Message}"
                : exceptionContext.Exception.Message);
            exceptionContext.ReThrow = false;
        }

        /// <summary>
        ///     Set option to wait for debugger to attach.
        /// </summary>
        [Global("WaitForDebugger", Description = "Wait for debugger to attach")]
        public void SetWaitForDebugger(bool waitForDebugger)
        {
            _waitForDebugger = waitForDebugger;
        }

        /// <summary>
        ///     Set the console log line format to short or long form.
        /// </summary>
        [Global("LogLongForm", Description = "Use long logging form on console")]
        public void SetLogLongLayout(bool value)
        {
            foreach (var consoleLog in _logger.GetLog<ConsoleLog>())
            {
                consoleLog.UseShortLayout = !value;
            }
        }

        /// <summary>
        ///     Set the console log severity filter.
        /// </summary>
        [Global("LogSeverity", Description = "Set console severity filter")]
        public void SetLogSeverity(Severity logSeverity)
        {
            foreach (var consoleLog in _logger.GetLog<ConsoleLog>())
            {
                consoleLog.CurrentSeverity = logSeverity;
            }
        }

        /// <summary>
        ///     Set the file log severity filter.
        /// </summary>
        [Global("LogFileSeverity", Description = "Set file log severity filter")]
        public void SetLogFileSeverity(Severity severity)
        {
            _fileLogSeverity = severity;
        }

        /// <summary>
        ///     Enable automatic log file flushing.
        /// </summary>
        [Global("LogAutoFlush", Description = "Enable automatic log file flushing")]
        public void SetLogAutoFlush(bool logAutoFlush)
        {
            _logAutoFlush = logAutoFlush;
        }

        /// <summary>
        ///     Self explanatory.
        /// </summary>
        [Global("LogDirectoryPath", Description = "Set log directory path")]
        public void SetLogDirectoryPath(string path)
        {
            _logDirectoryPath = path;
        }

        /// <summary>
        ///     Set log rolling max file size.
        /// </summary>
        [Global("LogMaxFileSizeMB", Description = "Set log rolling max file size in MB")]
        public void SetLogMaxFileSizeMB(long value)
        {
            _logMaxFileSize = value * 1024 * 1024;
        }

        /// <summary>
        ///     Set CSV log rolling max file size.
        /// </summary>
        [Global("CsvLogMaxFileSizeMB", Description = "Set CSV log (used only when remote telemetry is enabled) rolling max file size in MB")]
        public void SetCsvLogMaxFileSizeMB(long value)
        {
            _csvLogMaxFileSize = value * 1024 * 1024;
        }

        /// <summary>
        ///     Set log rolling max file count.
        /// </summary>
        [Global("LogMaxFileCount", Description = "Set log rolling max file count")]
        public void SetLogMaxFileCount(int value)
        {
            _logMaxFileCount = value;
        }
        
        /// <summary>
        ///     Set option to pause process before exiting.
        /// </summary>
        [Global("Pause", Description = "Pause before exit")]
        public void Pause(bool pause)
        {
            _pause = pause;
        }

        /// <summary>
        ///     Set alternate CASaaS scenario name.
        /// </summary>
        [Global("Scenario", Description = "Alternate CASaaS scenario name")]
        public void Scenario(string scenario)
        {
            _scenario = scenario;
        }

        /// <summary>
        ///     Set level of sensitivity to CASaaS system resource usage. Set to ensure purging occurs offline.
        /// </summary>
        [Global("Sensitivity", Description = "Level of sensitivity to system resource usage")]
        [Obsolete]
        public void Sensitivity(Sensitivity sensitivity)
        {
        }

        /// <summary>
        ///     Set number of pipe connections to use per session to service.
        /// </summary>
        [Global("ConnectionsPerSession", Description = "Number of pipe connections to use per session to service")]
        public void ConnectionsPerSession(uint value)
        {
            _connectionsPerSession = value;
        }

        /// <summary>
        ///     Set number of seconds between each client retry.
        /// </summary>
        [Global("RetryIntervalSeconds", Description = "Number of seconds between each client retry to service")]
        public void RetryIntervalSeconds(uint value)
        {
            _retryIntervalSeconds = value;
        }

        /// <summary>
        ///     Set maximum number of client retries to service before giving up.
        /// </summary>
        [Global("RetryCount", Description = "Maximum number of client retries to service before giving up")]
        public void RetryCount(uint value)
        {
            _retryCount = value;
        }

        /// <summary>
        ///     Whether or not to enable remote telemetry.
        /// </summary>
        [Global("RemoteTelemetry", Description = "Enable remote telemetry")]
        public void EnableRemoteTelemetry(bool enableRemoteTelemetry)
        {
            _enableRemoteTelemetry = enableRemoteTelemetry;
        }

        private static void SetThreadPoolSizes()
        {
            ThreadPool.GetMaxThreads(out var workerThreads, out var completionPortThreads);
            workerThreads = Math.Max(workerThreads, Environment.ProcessorCount * 16);
            completionPortThreads = workerThreads;
            ThreadPool.SetMaxThreads(workerThreads, completionPortThreads);

            ThreadPool.GetMinThreads(out workerThreads, out completionPortThreads);
            workerThreads = Math.Max(workerThreads, Environment.ProcessorCount * 16);
            completionPortThreads = workerThreads;
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        }

        private static HashType GetHashTypeByNameOrDefault(string name)
        {
            return name?.FindHashTypeByName() ?? HashType.Vso0;
        }

        private void PauseUntilKeyboardHit()
        {
            if (!_pause)
            {
                return;
            }

            Console.WriteLine("Press a key to continue");
            Console.ReadKey(true);
        }

        private void Initialize()
        {
            if (_waitForDebugger)
            {
                _logger.Warning("Waiting for debugger to attach. Hit any key to bypass.");

                while (!Debugger.IsAttached)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }

                Debugger.Break();
            }

            SetThreadPoolSizes();

            string logFilePath = FileLog.GetLogFilePath(_logDirectoryPath, logFileBaseName: null, dateInFileName: true, processIdInFileName: true);
            if (_fileLog == null)
            {
                _fileLog = new FileLog(logFilePath, _fileLogSeverity, _logAutoFlush, _logMaxFileSize, _logMaxFileCount);
                _logger.AddLog(_fileLog);
            }

            EnableRemoteTelemetryIfNeeded(logFilePath);
        }

        private void Validate()
        {
            if (_enableRemoteTelemetry && _kustoUploader == null)
            {
                throw new CacheException("Remote telemetry is enabled but Kusto uploader was not created");
            }
        }

        private void EnableRemoteTelemetryIfNeeded(string logFilePath)
        {
            if (!_enableRemoteTelemetry)
            {
                return;
            }

            if (_kustoUploader == null)
            {
                _logger.Warning
                    (
                    "Remote telemetry flag is enabled but no Kusto connection string was found in environment variable '{0}'",
                    KustoConnectionStringEnvVarName
                    );
                return;
            }

            _csvFileLog = new CsvFileLog
                (
                logFilePath: logFilePath + TmpCsvLogFileExt,
                serviceName: ServiceName,
                schema: KustoTableCsvSchema,
                severity: _fileLogSeverity,
                maxFileSize: _csvLogMaxFileSize
                );

            // Every time a log file written to disk and closed, we rename it and upload it to Kusto.
            // The last log file will be produced when _csvFileLog is disposed, so _kustUploader better
            // not be disposed before _csvFileLog.
            _csvFileLog.OnLogFileProduced += (path) =>
            {
                string newPath = Path.ChangeExtension(path, CsvLogFileExt);
                File.Move(path, newPath);
                _kustoUploader.PostFileForUpload(newPath, _csvFileLog.BuildId);
            };

            _logger.AddLog(_csvFileLog);
            _logger.Always("Remote telemetry enabled");
        }

        private void RunFileSystemContentStoreInternal(AbsolutePath rootPath, System.Func<Context, FileSystemContentStoreInternal, Task> funcAsync)
        {
            Initialize();

            try
            {
                var context = new Context(_logger);
                Validate();

                using (var store = CreateInternal(rootPath))
                {
                    try
                    {
                        var result = store.StartupAsync(context).Result;
                        if (!result)
                        {
                            Trace(result, context, "Failed to start store");
                            return;
                        }

                        funcAsync(context, store).Wait();
                    }
                    finally
                    {
                        var result = store.ShutdownAsync(context).Result;
                        if (!result)
                        {
                            context.Error($"Failed to shutdown store, error=[{result.ErrorMessage}]");
                        }
                    }
                }
            }
            catch (AggregateException exception)
            {
                _logger.Error(exception.InnerException?.Message);
            }
        }

        private FileSystemContentStoreInternal CreateInternal(AbsolutePath rootPath)
        {
            return new FileSystemContentStoreInternal(
                _fileSystem, SystemClock.Instance, rootPath, new ConfigurationModel(ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(Constants.OneMB)));
        }

        private void RunContentStore(string cacheName, string cachePath, ServiceClientRpcConfiguration rpcConfiguration, Func<Context, IContentSession, Task> funcAsync)
        {
            VerifyCachePathOrNameProvided(cacheName, cachePath);

            if (cacheName != null)
            {
                RunServiceClientContentStore(cacheName, rpcConfiguration, funcAsync);
            }
            else
            {
                RunFileSystemContentStore(new AbsolutePath(cachePath), funcAsync);
            }

            PauseUntilKeyboardHit();
        }

        private void RunFileSystemContentStore(AbsolutePath rootPath, System.Func<Context, IContentSession, Task> funcAsync)
        {
            System.Func<IContentStore> createFunc = () => new FileSystemContentStore(
                _fileSystem, SystemClock.Instance, rootPath, new ConfigurationModel(ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(Constants.OneMB)));
            RunContentStore(createFunc, funcAsync);
        }

        private void RunServiceClientContentStore(string cacheName, ServiceClientRpcConfiguration rpcConfiguration, Func<Context, IContentSession, Task> funcAsync)
        {
            System.Func<IContentStore> createFunc = () => new ServiceClientContentStore(
                _logger, _fileSystem, new ServiceClientContentStoreConfiguration(cacheName, rpcConfiguration, _scenario)
                                      {
                                          RetryCount = _retryCount,
                                          RetryIntervalSeconds = _retryIntervalSeconds,
                                      });
            RunContentStore(createFunc, funcAsync);
        }

        private void RunContentStore(
            Func<IContentStore> createStoreFunc,
            Func<Context, IContentSession, Task> funcAsync)
        {
            Initialize();
            var context = new Context(_logger);

            try
            {
                Validate();

                using (var store = createStoreFunc())
                {
                    try
                    {
                        var startupResult = store.StartupAsync(new Context(_logger)).Result;
                        if (!startupResult.Succeeded)
                        {
                            Trace(startupResult, context, "Failed to start store");
                            return;
                        }

                        var createSessionResult = store.CreateSession(new Context(_logger), "tool", ImplicitPin.None);
                        if (!createSessionResult.Succeeded)
                        {
                            Trace(createSessionResult, context, "Failed to create session");
                            return;
                        }

                        using (var session = createSessionResult.Session)
                        {
                            try
                            {
                                var sessionBoolResult = session.StartupAsync(new Context(_logger)).Result;
                                if (!sessionBoolResult.Succeeded)
                                {
                                    Trace(sessionBoolResult, context, "Failed to start session");
                                    return;
                                }

                                funcAsync(new Context(_logger), session).Wait();
                            }
                            finally
                            {
                                var sessionBoolResult = session.ShutdownAsync(new Context(_logger)).Result;
                                if (!sessionBoolResult.Succeeded)
                                {
                                    _tracer.Error(context, $"Failed to shutdown session, error=[{sessionBoolResult.ErrorMessage}]");
                                }
                            }
                        }
                    }
                    finally
                    {
                        var r = store.ShutdownAsync(new Context(_logger)).Result;
                        if (!r.Succeeded)
                        {
                            _tracer.Error(context, $"Failed to shutdown store, error=[{r.ErrorMessage}]");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                _tracer.Error(context, exception, "Unhandled exception in RunContentStore.");
            }
        }

        private void Trace(ResultBase result, Context context, string message)
        {
            _tracer.Error(context, $"{message}, result=[{result}]");
            _tracer.Debug(context, $"{result.Diagnostics}");
        }

        private void VerifyCachePathOrNameProvided(string name, string path)
        {
            if ((string.IsNullOrEmpty(path) && string.IsNullOrEmpty(name)) ||
                (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(name)))
            {
                throw new CacheException($"Cache {nameof(path)} or {nameof(name)} must be provided, but not both");
            }
        }

        internal DistributedCacheServiceArguments CreateDistributedCacheServiceArguments(
            IAbsolutePathFileCopier copier,
            IAbsolutePathTransformer pathTransformer,
            DistributedContentSettings dcs,
            HostInfo host,
            string cacheName,
            string cacheRootPath,
            uint grpcPort,
            int maxSizeQuotaMB,
            string dataRootPath,
            CancellationToken ct,
            int? bufferSizeForGrpcCopies = null,
            int? gzipBarrierSizeForGrpcCopies = null)
        {
            var distributedCacheServiceHost = new EnvironmentVariableHost();

            var localCasSettings = LocalCasSettings.Default(
                maxSizeQuotaMB: maxSizeQuotaMB,
                cacheRootPath: cacheRootPath,
                cacheName: cacheName,
                grpcPort: grpcPort,
                grpcPortFileName: _scenario);
            localCasSettings.PreferredCacheDrive = Path.GetPathRoot(cacheRootPath);
            localCasSettings.ServiceSettings = new LocalCasServiceSettings(60, scenarioName: _scenario, grpcPort: grpcPort, grpcPortFileName: _scenario, bufferSizeForGrpcCopies: bufferSizeForGrpcCopies, gzipBarrierSizeForGrpcCopies: gzipBarrierSizeForGrpcCopies);

            var config = new DistributedCacheServiceConfiguration(localCasSettings, dcs);

            return new DistributedCacheServiceArguments(_logger, copier, pathTransformer, distributedCacheServiceHost, host, ct, dataRootPath, config, null);
        }
    }
}
