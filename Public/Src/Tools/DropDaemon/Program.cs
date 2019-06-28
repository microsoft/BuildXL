// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Newtonsoft.Json.Linq;
using static Tool.DropDaemon.Statics;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     DropDaemon entry point.
    /// </summary>
    public static class Program
    {
        internal static IIpcProvider IpcProvider = IpcFactory.GetProvider();
        internal static readonly List<Option> DaemonConfigOptions = new List<Option>();
        internal static readonly List<Option> DropConfigOptions = new List<Option>();

        private const int ServicePointParallelismForDrop = 200;
        private static readonly int s_minIoThreadsForDrop = Environment.ProcessorCount * 10;
        private static readonly int s_minWorkerThreadsForDrop = Environment.ProcessorCount * 10;

        private const string IncludeAllFilter = ".*";

        // ==============================================================================
        // Daemon config
        // ==============================================================================
        internal static readonly StrOption Moniker = RegisterDaemonConfigOption(new StrOption("moniker")
        {
            ShortName = "m",
            HelpText = "Moniker to identify client/server communication",
        });

        internal static readonly IntOption MaxConcurrentClients = RegisterDaemonConfigOption(new IntOption("maxConcurrentClients")
        {
            HelpText = "OBSOLETE due to the hardcoded config. (Maximum number of clients to serve concurrently)",
            DefaultValue = DaemonConfig.DefaultMaxConcurrentClients,
        });

        internal static readonly IntOption MaxConnectRetries = RegisterDaemonConfigOption(new IntOption("maxConnectRetries")
        {
            HelpText = "Maximum number of retries to establish a connection with a running daemon",
            DefaultValue = DaemonConfig.DefaultMaxConnectRetries,
        });

        internal static readonly IntOption ConnectRetryDelayMillis = RegisterDaemonConfigOption(new IntOption("connectRetryDelayMillis")
        {
            HelpText = "Delay between consecutive retries to establish a connection with a running daemon",
            DefaultValue = (int)DaemonConfig.DefaultConnectRetryDelay.TotalMilliseconds,
        });

        internal static readonly BoolOption ShellExecute = RegisterDaemonConfigOption(new BoolOption("shellExecute")
        {
            HelpText = "Use shell execute to start the daemon process (a shell window will be created and displayed)",
            DefaultValue = false,
        });

        internal static readonly BoolOption StopOnFirstFailure = RegisterDaemonConfigOption(new BoolOption("stopOnFirstFailure")
        {
            HelpText = "Daemon process should terminate after first failed operation (e.g., 'drop create' fails because the drop already exists).",
            DefaultValue = DaemonConfig.DefaultStopOnFirstFailure,
        });

        internal static readonly BoolOption EnableCloudBuildIntegration = RegisterDaemonConfigOption(new BoolOption("enableCloudBuildIntegration")
        {
            ShortName = "ecb",
            HelpText = "Enable logging ETW events for CloudBuild to pick up",
            DefaultValue = DaemonConfig.DefaultEnableCloudBuildIntegration,
        });

        internal static readonly BoolOption Verbose = RegisterDaemonConfigOption(new BoolOption("verbose")
        {
            ShortName = "v",
            HelpText = "Verbose logging",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultVerbose,
        });

        internal static readonly StrOption DropServiceConfigFile = RegisterDaemonConfigOption(new StrOption("dropServiceConfigFile")
        {
            ShortName = "c",
            HelpText = "Drop service configuration file",
            DefaultValue = null,
            Expander = (fileName) =>
            {
                var json = System.IO.File.ReadAllText(fileName);
                var jObject = JObject.Parse(json);
                return jObject.Properties().Select(prop => new ParsedOption(PrefixKind.Long, prop.Name, prop.Value.ToString()));
            },
        });

        internal static readonly StrOption LogDir = RegisterDaemonConfigOption(new StrOption("logDir")
        {
            ShortName = "log",
            HelpText = "Log directory",
            IsRequired = false
        });

        // ==============================================================================
        // Drop config
        // ==============================================================================
        internal static readonly StrOption DropName = RegisterDropConfigOption(new StrOption("name")
        {
            ShortName = "n",
            HelpText = "Drop name",
            IsRequired = true,
        });

        internal static readonly UriOption DropEndpoint = RegisterDropConfigOption(new UriOption("service")
        {
            ShortName = "s",
            HelpText = "Drop endpoint URI",
            IsRequired = true,
        });

        internal static readonly IntOption BatchSize = RegisterDropConfigOption(new IntOption("batchSize")
        {
            ShortName = "bs",
            HelpText = "OBSOLETE due to the hardcoded config. (Size of batches in which to send 'associate' requests)",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultBatchSizeForAssociate,
        });

        internal static readonly IntOption MaxParallelUploads = RegisterDropConfigOption(new IntOption("maxParallelUploads")
        {
            ShortName = "mpu",
            HelpText = "Maximum number of uploads to issue to drop service in parallel",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultMaxParallelUploads,
        });

        internal static readonly IntOption NagleTimeMillis = RegisterDropConfigOption(new IntOption("nagleTimeMillis")
        {
            ShortName = "nt",
            HelpText = "OBSOLETE due to the hardcoded config. (Maximum time in milliseconds to wait before triggering a batch 'associate' request)",
            IsRequired = false,
            DefaultValue = (int)DropConfig.DefaultNagleTimeForAssociate.TotalMilliseconds,
        });

        internal static readonly IntOption RetentionDays = RegisterDropConfigOption(new IntOption("retentionDays")
        {
            ShortName = "rt",
            HelpText = "Drop retention time in days",
            IsRequired = false,
            DefaultValue = (int)DropConfig.DefaultRetention.TotalDays,
        });

        internal static readonly IntOption HttpSendTimeoutMillis = RegisterDropConfigOption(new IntOption("httpSendTimeoutMillis")
        {
            HelpText = "Timeout for http requests",
            IsRequired = false,
            DefaultValue = (int)DropConfig.DefaultHttpSendTimeout.TotalMilliseconds,
        });

        internal static readonly BoolOption EnableTelemetry = RegisterDropConfigOption(new BoolOption("enableTelemetry")
        {
            ShortName = "t",
            HelpText = "Verbose logging",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultEnableTelemetry,
        });

        internal static readonly BoolOption EnableChunkDedup = RegisterDropConfigOption(new BoolOption("enableChunkDedup")
        {
            ShortName = "cd",
            HelpText = "Chunk level dedup",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultEnableChunkDedup,
        });

        // ==============================================================================
        // 'addfile' and 'addartifacts' parameters
        // ==============================================================================
        internal static readonly StrOption RelativeDropPath = new StrOption("dropPath")
        {
            ShortName = "d",
            HelpText = "Relative drop path",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption File = new StrOption("file")
        {
            ShortName = "f",
            HelpText = "File path",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption HashOptional = new StrOption("hash")
        {
            ShortName = "h",
            HelpText = "VSO file hash",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption FileId = new StrOption("fileId")
        {
            ShortName = "fid",
            HelpText = "BuildXL file identifier",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption IpcServerMonikerRequired = new StrOption("ipcServerMoniker")
        {
            ShortName = "dm",
            HelpText = "IPC moniker identifying a running BuildXL IPC server",
            IsRequired = true,
        };

        internal static readonly StrOption HelpNoNameOption = new StrOption(string.Empty)
        {
            HelpText = "Command name",
        };

        internal static readonly StrOption Directory = new StrOption("directory")
        {
            ShortName = "dir",
            HelpText = "Directory path",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption DirectoryId = new StrOption("directoryId")
        {
            ShortName = "dirid",
            HelpText = "BuildXL directory identifier",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption RelativeDirectoryDropPath = new StrOption("directoryDropPath")
        {
            ShortName = "dird",
            HelpText = "Relative drop path for directory",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption DirectoryContentFilter = new StrOption("directoryFilter")
        {
            ShortName = "dcfilter",
            HelpText = "Directory content filter (only files that match the filter will be added to drop).",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = false,
        };

        // ==============================================================================
        // Commands
        // ==============================================================================
        internal static readonly Dictionary<string, Command> Commands = new Dictionary<string, Command>();

        /// <remarks>
        ///     The <see cref="DaemonConfigOptions"/> options are added to every command.
        ///     A non-mandatory string option "name" is added as well, which drop operation
        ///     commands may want to use to explicitly specify the target drop name.
        /// </remarks>
        private static Command RegisterCommand(
            string name,
            IEnumerable<Option> options = null,
            ServerAction serverAction = null,
            ClientAction clientAction = null,
            string description = null,
            bool needsIpcClient = true,
            bool addDaemonConfigOptions = true)
        {
            var opts = (options ?? new Option[0]).ToList();
            if (addDaemonConfigOptions)
            {
                opts.AddRange(DaemonConfigOptions);
            }

            if (!opts.Exists(opt => opt.LongName == "name"))
            {
                opts.Add(new Option(longName: DropName.LongName)
                {
                    ShortName = DropName.ShortName,
                });
            }

            var cmd = new Command(name, opts, serverAction, clientAction, description, needsIpcClient);
            Commands[cmd.Name] = cmd;
            return cmd;
        }

        internal static readonly Command HelpCmd = RegisterCommand(
            name: "help",
            description: "Prints a help message (usage).",
            options: new[] { HelpNoNameOption },
            needsIpcClient: false,
            clientAction: (conf, rpc) =>
            {
                string cmdName = conf.Get(HelpNoNameOption);
                bool cmdNotSpecified = string.IsNullOrWhiteSpace(cmdName);
                if (cmdNotSpecified)
                {
                    Console.WriteLine(Usage());
                    return 0;
                }

                Command requestedHelpForCommand;
                var requestedCommandFound = Commands.TryGetValue(cmdName, out requestedHelpForCommand);
                if (requestedCommandFound)
                {
                    Console.WriteLine(requestedHelpForCommand.Usage(conf.Config.Parser));
                    return 0;
                }
                else
                {
                    Console.WriteLine(Usage());
                    return 1;
                }
            });

        internal static readonly Command StartNoDropCmd = RegisterCommand(
            name: "start-nodrop",
            description: @"Starts a server process without a backing VSO drop client (useful for testing/pinging the daemon).",
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                var dropConfig = new DropConfig(string.Empty, new Uri("file://xyz"));
                var daemonConfig = CreateDaemonConfig(conf);
                var vsoClientTask = TaskSourceSlim.Create<IDropClient>();
                vsoClientTask.SetException(new NotSupportedException());
                using (var daemon = new Daemon(conf.Config.Parser, daemonConfig, dropConfig, vsoClientTask.Task))
                {
                    daemon.Start();
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        internal static readonly StrOption IpcServerMonikerOptional = new StrOption(
            longName: IpcServerMonikerRequired.LongName)
        {
            ShortName = IpcServerMonikerRequired.ShortName,
            HelpText = IpcServerMonikerRequired.HelpText,
            IsRequired = false,
        };

        internal static readonly Command StartCmd = RegisterCommand(
            name: "start",
            description: "Starts the server process.",
            options: DropConfigOptions.Union(new[] { IpcServerMonikerOptional }),
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                SetupThreadPoolAndServicePoint();
                var dropConfig = CreateDropConfig(conf);
                var daemonConf = CreateDaemonConfig(conf);

                if (daemonConf.MaxConcurrentClients <= 1)
                {
                    conf.Logger.Error($"Must specify at least 2 '{nameof(DaemonConfig.MaxConcurrentClients)}' when running DropDaemon to avoid deadlock when stopping this daemon from a different client");
                    return -1;
                }

                using (var client = CreateClient(conf.Get(IpcServerMonikerOptional), daemonConf))
                using (var daemon = new Daemon(
                    parser: conf.Config.Parser,
                    daemonConfig: daemonConf,
                    dropConfig: dropConfig,
                    dropClientTask: null,
                    client: client))
                {
                    daemon.Start();
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        private static void SetupThreadPoolAndServicePoint()
        {
            int workerThreads, ioThreads;
            ThreadPool.GetMinThreads(out workerThreads, out ioThreads);

            workerThreads = Math.Max(workerThreads, s_minWorkerThreadsForDrop);
            ioThreads = Math.Max(ioThreads, s_minIoThreadsForDrop);
            ThreadPool.SetMinThreads(workerThreads, ioThreads);

            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointParallelismForDrop, ServicePointManager.DefaultConnectionLimit);
        }

        internal static readonly Command StartDaemonCmd = RegisterCommand(
            name: "start-daemon",
            description: "Starts the server process in background (as daemon).",
            options: DropConfigOptions,
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                using (var daemon = new Process())
                {
                    bool shellExecute = conf.Get(ShellExecute);
                    daemon.StartInfo.FileName = AssemblyHelper.GetAssemblyLocation(System.Reflection.Assembly.GetEntryAssembly());
                    daemon.StartInfo.Arguments = "start " + conf.Config.Render();
                    daemon.StartInfo.LoadUserProfile = false;
                    daemon.StartInfo.UseShellExecute = shellExecute;
                    daemon.StartInfo.CreateNoWindow = !shellExecute;
                    daemon.Start();
                }

                return 0;
            });

        internal static readonly Command StopDaemonCmd = RegisterCommand(
            name: "stop",
            description: "[RPC] Stops the daemon process running on specified port; fails if no such daemon is running.",
            clientAction: AsyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                conf.Logger.Info("[STOP] requested");
                daemon.RequestStop();
                return Task.FromResult(IpcResult.Success());
            });

        internal static readonly Command CrashDaemonCmd = RegisterCommand(
            name: "crash",
            description: "[RPC] Stops the server process by crashing it.",
            clientAction: AsyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                daemon.Logger.Info("[CRASH] requested");
                Environment.Exit(-1);
                return Task.FromResult(IpcResult.Success());
            });

        internal static readonly Command PingDaemonCmd = RegisterCommand(
            name: "ping",
            description: "[RPC] Pings the daemon process.",
            clientAction: SyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                daemon.Logger.Info("[PING] received");
                return Task.FromResult(IpcResult.Success("Alive!"));
            });

        internal static readonly Command CreateDropCmd = RegisterCommand(
            name: "create",
            description: "[RPC] Invokes the 'create' operation.",
            options: DropConfigOptions,
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                daemon.Logger.Info("[CREATE]: Started at " + daemon.DropConfig.Service + "/" + daemon.DropName);
                IIpcResult result = await daemon.CreateAsync();
                daemon.Logger.Info("[CREATE]: " + result);
                return result;
            });

        internal static readonly Command FinalizeDropCmd = RegisterCommand(
            name: "finalize",
            description: "[RPC] Invokes the 'finalize' operation.",
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                daemon.Logger.Info("[FINALIZE] Started at" + daemon.DropConfig.Service + "/" + daemon.DropName);
                IIpcResult result = await daemon.FinalizeAsync();
                daemon.Logger.Info("[FINALIZE] " + result);
                return result;
            });

        internal static readonly Command FinalizeDropAndStopDaemonCmd = RegisterCommand(
            name: "finalize-and-stop",
            description: "[RPC] Invokes the 'finalize' operation; then stops the daemon.",
            clientAction: SyncRPCSend,
            serverAction: Command.Compose(FinalizeDropCmd.ServerAction, StopDaemonCmd.ServerAction));

        internal static readonly Command AddFileToDropCmd = RegisterCommand(
            name: "addfile",
            description: "[RPC] invokes the 'addfile' operation.",
            options: new Option[] { File, RelativeDropPath, HashOptional },
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                daemon.Logger.Verbose("[ADDFILE] Started");
                string filePath = conf.Get(File);
                string hashValue = conf.Get(HashOptional);
                var contentInfo = string.IsNullOrEmpty(hashValue) ? null : (FileContentInfo?)FileContentInfo.Parse(hashValue);
                var dropItem = new DropItemForFile(filePath, conf.Get(RelativeDropPath), contentInfo);
                IIpcResult result = System.IO.File.Exists(filePath)
                    ? await daemon.AddFileAsync(dropItem)
                    : new IpcResult(IpcResultStatus.ExecutionError, "file '" + filePath + "' does not exist");
                daemon.Logger.Verbose("[ADDFILE] " + result);
                return result;
            });

        internal static readonly Command AddArtifactsToDropCmd = RegisterCommand(
            name: "addartifacts",
            description: "[RPC] invokes the 'addartifacts' operation.",
            options: new Option[] { IpcServerMonikerRequired, File, FileId, HashOptional, RelativeDropPath, Directory, DirectoryId, RelativeDirectoryDropPath, DirectoryContentFilter },
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                daemon.Logger.Verbose("[ADDARTIFACTS] Started");
                
                var result = await AddArtifactsToDropInternalAsync(conf, daemon);

                daemon.Logger.Verbose("[ADDARTIFACTS] " + result);
                return result;
            });

        private static async Task<IIpcResult> AddArtifactsToDropInternalAsync(ConfiguredCommand conf, Daemon daemon)
        {
            var files = File.GetValues(conf.Config).ToArray();
            var fileIds = FileId.GetValues(conf.Config).ToArray();
            var hashes = HashOptional.GetValues(conf.Config).ToArray();
            var dropPaths = RelativeDropPath.GetValues(conf.Config).ToArray();

            if (files.Length != fileIds.Length || files.Length != hashes.Length || files.Length != dropPaths.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    Inv(
                        "File counts don't match: #files = {0}, #fileIds = {1}, #hashes = {2}, #dropPaths = {3}",
                        files.Length, fileIds.Length, hashes.Length, dropPaths.Length));
            }

            var directoryPaths = Directory.GetValues(conf.Config).ToArray();
            var directoryIds = DirectoryId.GetValues(conf.Config).ToArray();
            var directoryDropPaths = RelativeDirectoryDropPath.GetValues(conf.Config).ToArray();
            var directoryFilters = DirectoryContentFilter.GetValues(conf.Config).ToArray();

            if (directoryPaths.Length != directoryIds.Length || directoryPaths.Length != directoryDropPaths.Length || directoryPaths.Length != directoryFilters.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    Inv(
                        "Directory counts don't match: #directories = {0}, #directoryIds = {1}, #dropPaths = {2}, #directoryFilters = {3}",
                        directoryPaths.Length, directoryIds.Length, directoryDropPaths.Length, directoryFilters.Length));
            }

            (Regex[] initializedFilters, string filterInitError) = InitializeDirectoryFilters(directoryFilters);
            if (filterInitError != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, filterInitError);
            }

            var dropFileItemsKeyedByIsAbsent = Enumerable
                .Range(0, files.Length)
                .Select(i => new DropItemForBuildXLFile(
                    daemon.ApiClient,
                    chunkDedup: conf.Get(EnableChunkDedup),
                    filePath: files[i],
                    fileId: fileIds[i],
                    fileContentInfo: FileContentInfo.Parse(hashes[i]),
                    relativeDropPath: dropPaths[i])).ToLookup(f => WellKnownContentHashUtilities.IsAbsentFileHash(f.Hash));

            // If a user specified a particular file to be added to drop, this file must be a part of drop.
            // The missing files will not get into the drop, so we emit an error.
            if (dropFileItemsKeyedByIsAbsent[true].Any())
            {
                return new IpcResult(
                    IpcResultStatus.InvalidInput, 
                    Inv("The following files are missing, but they are a part of the drop command:{0}{1}", 
                        Environment.NewLine,
                        string.Join(Environment.NewLine, dropFileItemsKeyedByIsAbsent[true])));
            }

            (IEnumerable<DropItemForBuildXLFile> dropDirectoryMemberItems, string error) = await CreateDropItemsForDirectoriesAsync(
                conf,
                daemon,
                directoryPaths,
                directoryIds,
                directoryDropPaths,
                initializedFilters);

            if (error != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, error);
            }

            var groupedDirectoriesContent = dropDirectoryMemberItems.ToLookup(f => WellKnownContentHashUtilities.IsAbsentFileHash(f.Hash));

            // we allow missing files inside of directories only if those files are output files (e.g., optional or temporary files) 
            if (groupedDirectoriesContent[true].Any(f => !f.IsOutputFile))
            {
                return new IpcResult(
                    IpcResultStatus.InvalidInput, 
                    Inv("Uploading missing source file(s) is not supported:{0}{1}", 
                        Environment.NewLine,
                        string.Join(Environment.NewLine, groupedDirectoriesContent[true].Where(f => !f.IsOutputFile))));
            }
            
            // return early if there is nothing to upload
            if (!dropFileItemsKeyedByIsAbsent[false].Any() && !groupedDirectoriesContent[false].Any())
            {
                return new IpcResult(IpcResultStatus.Success, string.Empty);
            }
            
            return await AddDropItemsAsync(daemon, dropFileItemsKeyedByIsAbsent[false].Concat(groupedDirectoriesContent[false]));
        }

        private static (Regex[], string error) InitializeDirectoryFilters(string[] filters)
        {
            try
            {
                var initializedFilters = filters.Select(
                    filter => filter == IncludeAllFilter
                        ? null
                        : new Regex(filter, RegexOptions.Compiled | RegexOptions.IgnoreCase));

                return (initializedFilters.ToArray(), null);
            }
            catch (Exception e)
            {
                return (null, e.ToString());
            }
        }

        internal static readonly Command TestReadFile = RegisterCommand(
            name: "test-readfile",
            description: "[RPC] Sends a request to the daemon to read a file.",
            options: new Option[] { File },
            clientAction: SyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                daemon.Logger.Info("[READFILE] received");
                var result = IpcResult.Success(System.IO.File.ReadAllText(conf.Get(File)));
                daemon.Logger.Info("[READFILE] succeeded");
                return Task.FromResult(result);
            });

        private static Client CreateClient(string serverMoniker, IClientConfig config)
        {
            return serverMoniker != null
                ? Client.Create(serverMoniker, config)
                : null;
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA2204:Spelling of DropD")]
        public static int Main(string[] args)
        {
            // TODO:# 1208464- this can be removed once DropDaemon targets .net or newer 4.7 where TLS 1.2 is enabled by default
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            if (args.Length > 0 && args[0] == "listen")
            {
                return SubscribeAndProcessCloudBuildEvents();
            }

            try
            {
                Console.WriteLine("DropDaemon started at " + DateTime.UtcNow);
                Console.WriteLine(Daemon.DropDLogPrefix + "Command line arguments: ");
                Console.WriteLine(string.Join(Environment.NewLine + Daemon.DropDLogPrefix, args));
                Console.WriteLine();

                ConfiguredCommand conf = ParseArgs(args, new UnixParser());
                if (conf.Command.NeedsIpcClient)
                {
                    using (var rpc = CreateClient(conf))
                    {
                        var result = conf.Command.ClientAction(conf, rpc);
                        rpc.RequestStop();
                        rpc.Completion.GetAwaiter().GetResult();
                        return result;
                    }
                }
                else
                {
                    return conf.Command.ClientAction(conf, null);
                }
            }
            catch (ArgumentException e)
            {
                Error(e.Message);
                return 3;
            }
        }

        internal static ConfiguredCommand ParseArgs(string allArgs, IParser parser, ILogger logger = null, bool ignoreInvalidOptions = false)
        {
            return ParseArgs(parser.SplitArgs(allArgs), parser, logger, ignoreInvalidOptions);
        }

        internal static ConfiguredCommand ParseArgs(string[] args, IParser parser, ILogger logger = null, bool ignoreInvalidOptions = false)
        {
            var usageMessage = Lazy.Create(() => "Usage:" + Environment.NewLine + Usage());

            if (args.Length == 0)
            {
                throw new ArgumentException(Inv("Command is required. {0}", usageMessage.Value));
            }

            var argsQueue = new Queue<string>(args);
            string cmdName = argsQueue.Dequeue();
            if (!Commands.TryGetValue(cmdName, out Command cmd))
            {
                throw new ArgumentException(Inv("No command '{0}' is found. {1}", cmdName, usageMessage.Value));
            }

            var sw = Stopwatch.StartNew();
            Config conf = Config.ParseCommandLineArgs(cmd.Options, argsQueue, parser, caseInsensitive: true, ignoreInvalidOptions: ignoreInvalidOptions);
            var parseTime = sw.Elapsed;

            logger = logger ?? new ConsoleLogger(Verbose.GetValue(conf), Daemon.DropDLogPrefix);
            logger.Verbose("Parsing command line arguments done in {0}", parseTime);
            return new ConfiguredCommand(cmd, conf, logger);
        }

        internal static DropConfig CreateDropConfig(ConfiguredCommand conf)
        {
            return new DropConfig(
                dropName: conf.Get(DropName),
                serviceEndpoint: conf.Get(DropEndpoint),
                maxParallelUploads: conf.Get(MaxParallelUploads),
                retention: TimeSpan.FromDays(conf.Get(RetentionDays)),
                httpSendTimeout: TimeSpan.FromMilliseconds(conf.Get(HttpSendTimeoutMillis)),
                verbose: conf.Get(Verbose),
                enableTelemetry: conf.Get(EnableTelemetry),
                enableChunkDedup: conf.Get(EnableChunkDedup),
                logDir: conf.Get(LogDir));
        }

        internal static DaemonConfig CreateDaemonConfig(ConfiguredCommand conf)
        {
            return new DaemonConfig(
                logger: conf.Logger,
                moniker: conf.Get(Moniker),
                maxConnectRetries: conf.Get(MaxConnectRetries),
                connectRetryDelay: TimeSpan.FromMilliseconds(conf.Get(ConnectRetryDelayMillis)),
                stopOnFirstFailure: conf.Get(StopOnFirstFailure),
                enableCloudBuildIntegration: conf.Get(EnableCloudBuildIntegration));
        }

        internal static IClient CreateClient(ConfiguredCommand conf)
        {
            var daemonConfig = CreateDaemonConfig(conf);
            return IpcProvider.GetClient(daemonConfig.Moniker, daemonConfig);
        }

        private static async Task<(DropItemForBuildXLFile[], string error)> CreateDropItemsForDirectoryAsync(
            ConfiguredCommand conf, 
            Daemon daemon,
            string directoryPath,
            string directoryId,
            string dropPath,
            Regex contentFilter)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));
            Contract.Requires(!string.IsNullOrEmpty(directoryId));
            Contract.Requires(dropPath != null);

            if (daemon.ApiClient == null)
            {
                return (null, "ApiClient is not initialized");
            }

            DirectoryArtifact directoryArtifact = BuildXL.Ipc.ExternalApi.DirectoryId.Parse(directoryId);

            var maybeResult = await daemon.ApiClient.GetSealedDirectoryContent(directoryArtifact, directoryPath);
            if (!maybeResult.Succeeded)
            {
                return (null, "could not get the directory content from BuildXL server: " + maybeResult.Failure.Describe());
            }

            var directoryContent = maybeResult.Result;
            daemon.Logger.Verbose($"(dirPath'{directoryPath}', dirId='{directoryId}') contains '{directoryContent.Count}' files:{Environment.NewLine}{string.Join(Environment.NewLine, directoryContent.Select(f => f.Render()))}");

            if (contentFilter != null)
            {
                var filteredContent = directoryContent.Where(file => contentFilter.IsMatch(file.FileName)).ToList();
                daemon.Logger.Verbose("[dirId='{0}'] Filter '{1}' excluded {2} file(s) out of {3}", directoryId, contentFilter, directoryContent.Count - filteredContent.Count, directoryContent.Count);
                directoryContent = filteredContent;
            }
            
            return (directoryContent.Select(file =>
            {
                var remoteFileName = Inv(
                    "{0}/{1}",
                    dropPath,
                    // we need to convert '\' into '/' because this path would be a part of a drop url
                    GetRelativePath(directoryPath, file.FileName).Replace('\\', '/'));

                return new DropItemForBuildXLFile(
                    daemon.ApiClient,
                    file.FileName,
                    BuildXL.Ipc.ExternalApi.FileId.ToString(file.Artifact),
                    conf.Get(EnableChunkDedup),
                    file.ContentInfo,
                    remoteFileName);
            }).ToArray(), null);
        }

        private static async Task<(IEnumerable<DropItemForBuildXLFile>, string error)> CreateDropItemsForDirectoriesAsync(
            ConfiguredCommand conf,
            Daemon daemon,
            string[] directoryPaths,
            string[] directoryIds,
            string[] dropPaths,
            Regex[] contentFilters)
        {
            Contract.Requires(directoryPaths != null);
            Contract.Requires(directoryIds != null);
            Contract.Requires(dropPaths != null);
            Contract.Requires(contentFilters != null);
            Contract.Requires(directoryPaths.Length == directoryIds.Length);
            Contract.Requires(directoryPaths.Length == dropPaths.Length);
            Contract.Requires(directoryPaths.Length == contentFilters.Length);

            var createDropItemsTasks = Enumerable
                .Range(0, directoryPaths.Length)
                .Select(i => CreateDropItemsForDirectoryAsync(conf, daemon, directoryPaths[i], directoryIds[i], dropPaths[i], contentFilters[i])).ToArray();

            var createDropItemsResults = await TaskUtilities.SafeWhenAll(createDropItemsTasks);

            if (createDropItemsResults.Any(r => r.error != null))
            {
                return (null, string.Join("; ", createDropItemsResults.Where(r => r.error != null).Select(r => r.error)));
            }

            return (createDropItemsResults.SelectMany(r => r.Item1), null);
        }

        private static string GetRelativePath(string root, string file)
        {
            var rootEndsWithSlash =
                root[root.Length - 1] == System.IO.Path.DirectorySeparatorChar
                || root[root.Length - 1] == System.IO.Path.AltDirectorySeparatorChar;
            return file.Substring(root.Length + (rootEndsWithSlash ? 0 : 1));
        }

        private static (IEnumerable<DropItemForBuildXLFile>, string error) DedupeDropItems(IEnumerable<DropItemForBuildXLFile> dropItems)
        {
            var dropItemsByDropPaths = new Dictionary<string, DropItemForBuildXLFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var dropItem in dropItems)
            {
                if (dropItemsByDropPaths.TryGetValue(dropItem.RelativeDropPath, out var existingDropItem))
                {
                    if (!string.Equals(dropItem.FullFilePath, existingDropItem.FullFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return (
                          null,
                          Inv(
                              "'{0}' cannot be added to drop because it has the same drop path '{1}' as '{2}'",
                              dropItem.FullFilePath,
                              dropItem.RelativeDropPath,
                              existingDropItem.FullFilePath));
                    }
                }
                else
                {
                    dropItemsByDropPaths.Add(dropItem.RelativeDropPath, dropItem);
                }
            }

            return (dropItemsByDropPaths.Select(kvp => kvp.Value).ToArray(), null);
        }

        private static async Task<IIpcResult> AddDropItemsAsync(Daemon daemon, IEnumerable<DropItemForBuildXLFile> dropItems)
        {
            (IEnumerable<DropItemForBuildXLFile> dedupedDropItems, string error) = DedupeDropItems(dropItems);

            if (error != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, error);
            }

            var ipcResultTasks = dedupedDropItems.Select(d => daemon.AddFileAsync(d)).ToArray();
            var ipcResults = await TaskUtilities.SafeWhenAll(ipcResultTasks);

            return IpcResult.Merge(ipcResults);
        }

        private static string Usage()
        {
            var builder = new StringBuilder();
            var len = Commands.Keys.Max(cmdName => cmdName.Length);
            foreach (var cmd in Commands.Values)
            {
                builder.AppendLine(Inv("  {0,-" + len + "} : {1}", cmd.Name, cmd.Description));
            }

            return builder.ToString();
        }

        private static int SyncRPCSend(ConfiguredCommand conf, IClient rpc) => RPCSend(conf, rpc, true);

        private static int AsyncRPCSend(ConfiguredCommand conf, IClient rpc) => RPCSend(conf, rpc, false);

        private static int RPCSend(ConfiguredCommand conf, IClient rpc, bool isSync)
        {
            var rpcResult = RPCSendCore(conf, rpc, isSync);
            conf.Logger.Info(
                "Command '{0}' {1} (exit code: {2}). {3}",
                conf.Command.Name,
                rpcResult.Succeeded ? "succeeded" : "failed",
                (int)rpcResult.ExitCode,
                rpcResult.Payload);
            return (int)rpcResult.ExitCode;
        }

        private static IIpcResult RPCSendCore(ConfiguredCommand conf, IClient rpc, bool isSync)
        {
            string operationPayload = ToPayload(conf);
            var operation = new IpcOperation(operationPayload, waitForServerAck: isSync);
            return rpc.Send(operation).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Reconstructs a full command line from a command name (<paramref name="commandName"/>)
        ///     and a configuration (<paramref name="config"/>).
        /// </summary>
        internal static string ToPayload(string commandName, Config config)
        {
            return commandName + " " + config.Render();
        }

        /// <summary>
        ///     Reconstructs a full command line corresponding to a <see cref="ConfiguredCommand"/>.
        /// </summary>
        private static string ToPayload(ConfiguredCommand cmd) => ToPayload(cmd.Command.Name, cmd.Config);

        private static T RegisterOption<T>(List<Option> options, T option) where T : Option
        {
            options.Add(option);
            return option;
        }

        private static T RegisterDaemonConfigOption<T>(T option) where T : Option => RegisterOption(DaemonConfigOptions, option);

        private static T RegisterDropConfigOption<T>(T option) where T : Option => RegisterOption(DropConfigOptions, option);

        private static int SubscribeAndProcessCloudBuildEvents()
        {
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Error("Not elevated; exiting");
                return -1;
            }

            // BuildXL.Tracing.ETWLogger guid
            if (!Guid.TryParse("43b71382-88db-5427-89d5-0b46476f8ef4", out Guid guid))
            {
                Error("Could not parse guid; exiting");
                return -1;
            }

            Console.WriteLine("Listening for cloud build events");

            // Create an unique session
            string sessionName = "DropDaemon ETW Session";

            // the null second parameter means 'real time session'
            using (TraceEventSession traceEventSession = new TraceEventSession(sessionName, null))
            {
                // Note that sessions create a OS object (a session) that lives beyond the lifetime of the process
                // that created it (like Files), thus you have to be more careful about always cleaning them up.
                // An importantly way you can do this is to set the 'StopOnDispose' property which will cause
                // the session to
                // stop (and thus the OS object will die) when the TraceEventSession dies.   Because we used a 'using'
                // statement, this means that any exception in the code below will clean up the OS object.
                traceEventSession.StopOnDispose = true;
                traceEventSession.EnableProvider(guid, matchAnyKeywords: (ulong)Keywords.CloudBuild);

                // Prepare to read from the session, connect the ETWTraceEventSource to the session
                using (ETWTraceEventSource etwTraceEventSource = new ETWTraceEventSource(
                    sessionName,
                    TraceEventSourceType.Session))
                {
                    DynamicTraceEventParser dynamicTraceEventParser = new DynamicTraceEventParser(etwTraceEventSource);
                    dynamicTraceEventParser.All += traceEvent =>
                        {
                            Possible<CloudBuildEvent, Failure> possibleEvent = CloudBuildEvent.TryParse(traceEvent);

                            if (!possibleEvent.Succeeded)
                            {
                                Error(possibleEvent.Failure.ToString());
                                return;
                            }

                            CloudBuildEvent eventObj = possibleEvent.Result;
                            Console.WriteLine("*** Event received: " + eventObj.Kind);
                        };

                    etwTraceEventSource.Process();
                }
            }

            Console.WriteLine("FINISHED");
            Thread.Sleep(100000);
            return 0;
        }
    }
}
