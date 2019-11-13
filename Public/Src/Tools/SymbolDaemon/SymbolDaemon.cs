// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Tasks;
using Microsoft.VisualStudio.Services.Symbol.App.Core.Tracing;
using Microsoft.VisualStudio.Services.Symbol.Common;
using Microsoft.VisualStudio.Services.Symbol.WebApi;
using Newtonsoft.Json;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Daemon responsible for handling symbol-related requests.
    /// </summary>
    public sealed class SymbolDaemon : ServicePipDaemon.ServicePipDaemon, IDisposable, IIpcOperationExecutor
    {
        private const string LogFileName = "SymbolDaemon";
        private const int s_servicePointParallelism = 200;
        private const char s_debugEntryDataFieldSeparator = '|';

        /// <nodoc/>
        public const string SymbolDLogPrefix = "(SymbolD) ";

        private static readonly int s_minIoThreads = Environment.ProcessorCount * 10;
        private static readonly int s_minWorkerThreads = Environment.ProcessorCount * 10;

        private Task<ISymbolClient> m_symbolServiceClientTask;
        private readonly ISymbolIndexer m_symbolIndexer;

        /// <summary>
        /// Configuration used to initialize this daemon
        /// </summary>
        public SymbolConfig SymbolConfig { get; }

        /// <nodoc />
        public string RequestName => SymbolConfig.Name;

        # region Symbol daemon options and commands

        internal static readonly List<Option> SymbolConfigOptions = new List<Option>();

        private static T RegisterSymbolConfigOption<T>(T option) where T : Option => RegisterOption(SymbolConfigOptions, option);

        internal static readonly StrOption SymbolRequestNameOption = RegisterSymbolConfigOption(new StrOption("name")
        {
            ShortName = "n",
            HelpText = "Request name",
            IsRequired = true,
        });

        internal static readonly UriOption ServiceEndpoint = RegisterSymbolConfigOption(new UriOption("service")
        {
            ShortName = "s",
            HelpText = "Symbol service endpoint URI",
            IsRequired = true,
        });

        internal static readonly StrOption DebugEntryCreateBehavior = RegisterSymbolConfigOption(new StrOption("debugEntryCreateBehavior")
        {
            ShortName = "de",
            HelpText = "Debug Entry Create Behavior in case of a collision",
            IsRequired = false,
            DefaultValue = Microsoft.VisualStudio.Services.Symbol.WebApi.DebugEntryCreateBehavior.ThrowIfExists.ToString(),
        });

        internal static readonly IntOption RetentionDays = RegisterSymbolConfigOption(new IntOption("retentionDays")
        {
            ShortName = "rt",
            HelpText = "Symbol retention time in days",
            IsRequired = false,
            DefaultValue = (int)SymbolConfig.DefaultRetention.TotalDays,
        });

        internal static readonly IntOption HttpSendTimeoutMillis = RegisterSymbolConfigOption(new IntOption("httpSendTimeoutMillis")
        {
            HelpText = "Timeout for http requests",
            IsRequired = false,
            DefaultValue = (int)SymbolConfig.DefaultHttpSendTimeout.TotalMilliseconds,
        });

        internal static readonly BoolOption EnableTelemetry = RegisterSymbolConfigOption(new BoolOption("enableTelemetry")
        {
            ShortName = "t",
            HelpText = "Verbose logging",
            IsRequired = false,
            DefaultValue = SymbolConfig.DefaultEnableTelemetry,
        });

        internal static readonly StrOption SymbolMetadataFile = new StrOption("symbolMetadata")
        {
            ShortName = "sm",
            HelpText = "Path to file with symbols metadata.",
            IsRequired = false,
            IsMultiValue = false,
        };

        internal static SymbolConfig CreateSymbolConfig(ConfiguredCommand conf)
        {
            return new SymbolConfig(
                requestName: conf.Get(SymbolRequestNameOption),
                serviceEndpoint: conf.Get(ServiceEndpoint),
                debugEntryCreateBehaviorStr: conf.Get(DebugEntryCreateBehavior),
                retention: TimeSpan.FromDays(conf.Get(RetentionDays)),
                httpSendTimeout: TimeSpan.FromMilliseconds(conf.Get(HttpSendTimeoutMillis)),
                verbose: conf.Get(Verbose),
                enableTelemetry: conf.Get(EnableTelemetry),
                logDir: conf.Get(LogDir));
        }

        private static Client CreateClient(string serverMoniker, IClientConfig config)
        {
            return serverMoniker != null
                ? Client.Create(serverMoniker, config)
                : null;
        }

        internal static readonly Command StartNoServiceCmd = RegisterCommand(
            name: "start-noservice",
            description: @"Starts a server process without a backing symbol service client (useful for testing/pinging the daemon).",
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                var symbolConfig = new SymbolConfig(string.Empty, new Uri("file://xyz"));
                var daemonConfig = CreateDaemonConfig(conf);
                var vsoClientTask = TaskSourceSlim.Create<ISymbolClient>();
                vsoClientTask.SetException(new NotSupportedException());
                using (var daemon = new SymbolDaemon(conf.Config.Parser, daemonConfig, symbolConfig, vsoClientTask.Task))
                {
                    daemon.Start();
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        internal static readonly Command StartCmd = RegisterCommand(
           name: "start",
           description: "Starts the server process.",
           options: SymbolConfigOptions.Union(new[] { IpcServerMonikerOptional }),
           needsIpcClient: false,
           clientAction: (conf, _) =>
           {
               // This command is used when BXL creates a ServicePip for SymbolDaemon.
               SetupThreadPoolAndServicePoint(s_minWorkerThreads, s_minIoThreads, s_servicePointParallelism);
               var symbolConfig = CreateSymbolConfig(conf);
               var daemonConf = CreateDaemonConfig(conf);

               if (daemonConf.MaxConcurrentClients <= 1)
               {
                   conf.Logger.Error($"Must specify at least 2 '{nameof(DaemonConfig.MaxConcurrentClients)}' when running SymbolDaemon to avoid deadlock when stopping this daemon from a different client");
                   return -1;
               }

               using (var client = CreateClient(conf.Get(IpcServerMonikerOptional), daemonConf))
               using (var daemon = new SymbolDaemon(
                   parser: conf.Config.Parser,
                   daemonConfig: daemonConf,
                   symbolConfig: symbolConfig,
                   symbolServiceClientTask: null,
                   bxlClient: client))
               {
                   daemon.Start();
                   // We are blocking the thread here and waiting for the SymbolDaemon to process all the requests.
                   // Once the daemon receives 'stop' command, GetResult will return, and we'll leave this method
                   // (i.e., ServicePip will finish).
                   daemon.Completion.GetAwaiter().GetResult();
                   return 0;
               }
           });

        internal static readonly Command CreateSymbolRequestCmd = RegisterCommand(
           name: "create",
           description: "[RPC] Invokes the 'create' operation.",
           options: SymbolConfigOptions,
           clientAction: SyncRPCSend,
           serverAction: async (conf, daemon) =>
           {
               var symbolDaemon = daemon as SymbolDaemon;
               symbolDaemon.Logger.Info("[CREATE]: Started at " + symbolDaemon.SymbolConfig.Service + "/" + symbolDaemon.SymbolConfig.Name);
               IIpcResult result = await symbolDaemon.CreateAsync();
               daemon.Logger.Info("[CREATE]: " + result);
               return result;
           });

        internal static readonly Command FinalizeSymbolRequestCmd = RegisterCommand(
            name: "finalize",
            description: "[RPC] Invokes the 'finalize' operation.",
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                var symbolDaemon = daemon as SymbolDaemon;
                symbolDaemon.Logger.Info("[FINALIZE] Started at" + symbolDaemon.SymbolConfig.Service + "/" + symbolDaemon.SymbolConfig.Name);
                IIpcResult result = await symbolDaemon.FinalizeAsync();
                daemon.Logger.Info("[FINALIZE] " + result);
                return result;
            });

        internal static readonly Command FinalizeSymbolRequestAndStopDaemonCmd = RegisterCommand(
            name: "finalize-and-stop",
            description: "[RPC] Invokes the 'finalize' operation; then stops the daemon.",
            clientAction: SyncRPCSend,
            serverAction: Command.Compose(FinalizeSymbolRequestCmd.ServerAction, StopDaemonCmd.ServerAction));

        internal static readonly Command AddSymbolFilesCmd = RegisterCommand(
            name: "addsymbolfiles",
            description: "[RPC] invokes the 'addsymbolfiles' operation.",
            options: new Option[] { IpcServerMonikerRequired, File, FileId, HashOptional, SymbolMetadataFile },
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                var symbolDaemon = daemon as SymbolDaemon;
                symbolDaemon.Logger.Verbose("[ADDSYMBOLS] Started");
                var result = await AddSymbolFilesInternalAsync(conf, symbolDaemon);
                symbolDaemon.Logger.Verbose("[ADDSYMBOLS] " + result);
                return result;
            });

        internal static readonly Command IndexFilesCmd = RegisterCommand(
            name: "indexFiles",
            description: "Indexes the specified files and saves SymbolData into a file.",
            options: new Option[] { File, HashOptional, SymbolMetadataFile },
            needsIpcClient: false,
            clientAction: (ConfiguredCommand conf, IClient rpc) =>
            {
                var files = File.GetValues(conf.Config).ToArray();

                // hashes are sent from BXL by serializing FileContentInfo
                var hashesWithLength = HashOptional.GetValues(conf.Config);
                var hashesOnly = hashesWithLength.Select(h => FileContentInfo.Parse(h).Hash).ToArray();

                var outputFile = SymbolMetadataFile.GetValue(conf.Config);

                IndexFilesAndStoreMetadataToFile(files, hashesOnly, outputFile);

                return 0;
            });

        private static void IndexFilesAndStoreMetadataToFile(string[] files, ContentHash[] hashes, string outputFile)
        {
            Contract.Assert(files.Length == hashes.Length);
            Contract.Assert(files.Length > 0);
            Contract.Assert(!string.IsNullOrEmpty(outputFile));
            Contract.Assert(!hashes.Any(hash => hash.HashType != HashType.Vso0), "Unsupported hash type");

            var indexer = new SymbolIndexer(SymbolAppTraceSource.SingleInstance);
            var symbolsMetadata = new Dictionary<ContentHash, HashSet<DebugEntryData>>(files.Length);

            for (int i = 0; i < files.Length; i++)
            {
                HashSet<DebugEntryData> symbols;
                if (!symbolsMetadata.TryGetValue(hashes[i], out symbols))
                {
                    symbols = new HashSet<DebugEntryData>(DebugEntryDataComparer.Instance);
                    symbolsMetadata.Add(hashes[i], symbols);
                }

                // Index the file. It might not contain any symbol data. In this case, we will have an empty set.
                symbols.UnionWith(indexer.GetDebugEntries(new System.IO.FileInfo(files[i]), calculateBlobId: false));
            }

            SerializeSymbolsMetadata(symbolsMetadata, outputFile);
        }

        /// <summary>
        /// Serializes DebugEntryData's into a file.
        /// </summary>        
        public static void SerializeSymbolsMetadata(Dictionary<ContentHash, HashSet<DebugEntryData>> symbolsMetadata, string outputFile)
        {
            /*                
                <number of hashes>
                for each hash:
                    <hash>
                    <number of debug entries>
                    [<debug entry>]
             */

            using (var writer = new StreamWriter(outputFile))
            {
                writer.WriteLine(symbolsMetadata.Count);

                foreach (var kvp in symbolsMetadata)
                {
                    writer.WriteLine(kvp.Key.Serialize());
                    writer.WriteLine(kvp.Value.Count);

                    foreach (var debugEntry in kvp.Value)
                    {
                        writer.WriteLine(SerializeDebugEntryData(debugEntry));
                    }
                }
            }
        }

        /// <summary>
        /// Deserializes DebugEntryData's from a file.
        /// </summary>
        public static Dictionary<ContentHash, HashSet<DebugEntryData>> DeserializeSymbolsMetadata(string fileName)
        {
            Dictionary<ContentHash, HashSet<DebugEntryData>> result;
            using (var reader = new StreamReader(fileName))
            {
                int hashCount = int.Parse(reader.ReadLine());
                result = new Dictionary<ContentHash, HashSet<DebugEntryData>>(hashCount);

                for (int i = 0; i < hashCount; i++)
                {
                    ContentHash.TryParse(reader.ReadLine(), out var hash);
                    Contract.Assert(hash.HashType == HashType.Vso0);
                    var blobIdentifier = new Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier(hash.ToHashByteArray());
                    int debugEntryCount = int.Parse(reader.ReadLine());

                    var symbols = new HashSet<DebugEntryData>(debugEntryCount, DebugEntryDataComparer.Instance);
                    result.Add(hash, symbols);

                    for (int j = 0; j < debugEntryCount; j++)
                    {
                        var entry = DeserializeDebugEntryData(reader.ReadLine());
                        if (entry.BlobIdentifier == null)
                        {
                            entry.BlobIdentifier = blobIdentifier;
                        }
                        symbols.Add(entry);
                    }
                }
            }

            return result;
        }

        private static string SerializeDebugEntryData(DebugEntryData entry)
        {
            var sb = new StringBuilder(entry.ClientKey.Length * 2);

            sb.Append(entry.BlobIdentifier == null ? "" : entry.BlobIdentifier.ValueString);
            sb.Append(s_debugEntryDataFieldSeparator);
            sb.Append(entry.ClientKey);
            sb.Append(s_debugEntryDataFieldSeparator);
            sb.Append((int)entry.InformationLevel);

            return sb.ToString();
        }

        private static DebugEntryData DeserializeDebugEntryData(string serializedEntry)
        {
            const int DebugEntryFieldCount = 3;

            var blocks = serializedEntry.Split(s_debugEntryDataFieldSeparator);
            if (blocks.Length != DebugEntryFieldCount)
            {
                Contract.Assert(false, I($"Expected to find {DebugEntryFieldCount} fields in the serialized string, but found {blocks.Length}."));
            }

            var result = new DebugEntryData()
            {
                BlobIdentifier = blocks[0].Length == 0 ? null : Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier.Deserialize(blocks[0]),
                ClientKey = string.IsNullOrEmpty(blocks[1]) ? null : blocks[1],
                InformationLevel = (DebugInformationLevel)int.Parse(blocks[2])
            };

            return result;
        }

        private static async Task<IIpcResult> AddSymbolFilesInternalAsync(ConfiguredCommand conf, SymbolDaemon daemon)
        {
            var files = File.GetValues(conf.Config).ToArray();
            var fileIds = FileId.GetValues(conf.Config).ToArray();
            var hashes = HashOptional.GetValues(conf.Config).ToArray();
            var symbolMetadataFile = SymbolMetadataFile.GetValue(conf.Config);

            if (files.Length != fileIds.Length || files.Length != hashes.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"File counts don't match: #files = {files.Length}, #fileIds = {fileIds.Length}, #hashes = {hashes.Length}"));
            }

            if (string.IsNullOrEmpty(symbolMetadataFile))
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    "Invalid path to symbol metadata file.");
            }

            Dictionary<ContentHash, HashSet<DebugEntryData>> symbolMetadata;

            try
            {
                symbolMetadata = DeserializeSymbolsMetadata(symbolMetadataFile);
            }
            catch (Exception e)
            {
                return new IpcResult(
                   IpcResultStatus.GenericError,
                   I($"Failed to deserialize symbol metadata file: {e.DemystifyToString()}"));
            }

            List<SymbolFile> symbolFiles = new List<SymbolFile>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    var hash = FileContentInfo.Parse(hashes[i]).Hash;
                    if (!symbolMetadata.TryGetValue(hash, out var debugEntries))
                    {
                        daemon.Logger.Verbose("Symbol metadata file - {0}{1}{2}",
                            symbolMetadataFile,
                            Environment.NewLine,
                            System.IO.File.ReadAllText(symbolMetadataFile));

                        return new IpcResult(
                            IpcResultStatus.GenericError,
                            I($"Hash '{hash.ToString()}' (file: '{files[i]}') is not found in the metadata file '{symbolMetadataFile}'."));
                    }

                    symbolFiles.Add(new SymbolFile(
                        daemon.ApiClient,
                        files[i],
                        fileIds[i],
                        hash,
                        debugEntries));
                }
                catch (Exception e)
                {
                    return new IpcResult(
                        IpcResultStatus.GenericError,
                        e.DemystifyToString());
                }
            }

            var result = await daemon.AddSymbolFilesAsync(symbolFiles);

            return result;
        }

        #endregion

        static SymbolDaemon()
        {
            // noop
        }

        /// <nodoc/>
        public SymbolDaemon(
            IParser parser,
            DaemonConfig daemonConfig,
            SymbolConfig symbolConfig,
            Task<ISymbolClient> symbolServiceClientTask,
            IIpcProvider rpcProvider = null,
            Client bxlClient = null)
                : base(parser,
                      daemonConfig,
                      !string.IsNullOrWhiteSpace(symbolConfig.LogDir) ? new FileLogger(symbolConfig.LogDir, LogFileName, daemonConfig.Moniker, symbolConfig.Verbose, SymbolDLogPrefix) : daemonConfig.Logger,
                      rpcProvider,
                      bxlClient)
        {
            Contract.Requires(symbolConfig != null);

            SymbolConfig = symbolConfig;
            m_logger.Info(I($"Using {nameof(DaemonConfig)}: {JsonConvert.SerializeObject(daemonConfig)}"));
            m_logger.Info(I($"Using {nameof(SymbolConfig)}: {JsonConvert.SerializeObject(symbolConfig)}"));

            // if no ISymbolServiceClient has been provided, create VsoSymbolClient using the provided SymbolConfig
            m_symbolServiceClientTask = symbolServiceClientTask ?? Task.Run(() => (ISymbolClient)new VsoSymbolClient(m_logger, symbolConfig, bxlClient));

            m_symbolIndexer = new SymbolIndexer(SymbolAppTraceSource.SingleInstance);
        }

        internal static void EnsureCommandsInitialized()
        {
            Contract.Assert(Commands != null);

            // these operations are quite expensive, however, we expect to call this method only once per symbol request, so it should cause any perf downgrade
            var numCommandsBase = typeof(ServicePipDaemon.ServicePipDaemon).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();
            var numCommandsSymbolD = typeof(SymbolDaemon).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();

            if (Commands.Count != numCommandsBase + numCommandsSymbolD)
            {
                Contract.Assert(false, $"The list of commands was not properly initialized (# of initialized commands = {Commands.Count}; # of ServicePipDaemon commands = {numCommandsBase}; # of SymbolDaemon commands = {numCommandsSymbolD})");
            }
        }

        /// <summary>
        /// Synchronous version of <see cref="CreateAsync"/>
        /// </summary>
        public IIpcResult Create()
        {
            return CreateAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a symbol request.
        /// </summary>
        public async Task<IIpcResult> CreateAsync()
        {
            // TODO(olkonone): add logging
            Request createRequestResult;

            try
            {
                createRequestResult = await InternalCreateAsync();
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.DemystifyToString());
            }

            // get and return RequestId ?
            return IpcResult.Success(I($"Symbol request '{RequestName}' created (assigned request ID: '{createRequestResult.Id}')."));
        }

        /// <summary>
        /// Indexes the files and adds symbol data to the request.
        /// </summary>       
        public async Task<IIpcResult> AddSymbolFilesAsync(List<SymbolFile> files)
        {
            var addFileTasks = files.Select(f => AddSymbolFileAsync(f));
            var ipcResults = await TaskUtilities.SafeWhenAll(addFileTasks);

            return IpcResult.Merge(ipcResults);
        }

        private async Task<IIpcResult> AddSymbolFileAsync(SymbolFile file)
        {
            try
            {
                var symbolClient = await m_symbolServiceClientTask;
                var result = await symbolClient.AddFileAsync(file);

                return IpcResult.Success(I($"File '{file.FullFilePath}' {result} in request '{RequestName}'."));
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.DemystifyToString());
            }
        }

        /// <summary>
        /// Synchronous version of <see cref="FinalizeAsync"/>
        /// </summary>
        public IIpcResult Finalize()
        {
            return FinalizeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finalizes the symbol request. 
        /// </summary>
        public async Task<IIpcResult> FinalizeAsync()
        {
            // TODO(olkonone): add logging
            Request finalizeRequestResult;

            try
            {
                finalizeRequestResult = await InternalFinalizeAsync();
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.DemystifyToString());
            }

            return IpcResult.Success(I($"Symbol request '{RequestName}' finalized; the request expires on '{finalizeRequestResult.ExpirationDate}'."));
        }

        /// <nodoc />
        public new void Dispose()
        {
            if (m_symbolServiceClientTask.IsCompleted && !m_symbolServiceClientTask.IsFaulted)
            {
                m_symbolServiceClientTask.Result.Dispose();
            }

            base.Dispose();
        }

        private async Task<Request> InternalCreateAsync()
        {
            var symbolClient = await m_symbolServiceClientTask;
            var result = await symbolClient.CreateAsync();

            Contract.Assert(result.Status == RequestStatus.Created);

            return result;
        }

        private async Task<Request> InternalFinalizeAsync()
        {
            var symbolClient = await m_symbolServiceClientTask;
            var result = await symbolClient.FinalizeAsync();

            Contract.Assert(result.Status == RequestStatus.Sealed);
            Contract.Assert(result.ExpirationDate.HasValue);

            return result;
        }
    }
}
