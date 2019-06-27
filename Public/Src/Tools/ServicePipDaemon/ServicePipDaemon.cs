// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Newtonsoft.Json.Linq;
using static Tool.ServicePipDaemon.Statics;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// Responsible for accepting and handling TCP/IP connections from clients.
    /// </summary>
    public abstract class ServicePipDaemon : IDisposable, IIpcOperationExecutor
    {
        public static readonly IIpcProvider IpcProvider = IpcFactory.GetProvider();
        internal static readonly List<Option> DaemonConfigOptions = new List<Option>();       

        private const string LogFileName = "ServiceDaemon";

        /// <nodoc/>
        public const string LogPrefix = "(SPD) ";

        /// <summary>Daemon configuration.</summary>
        public DaemonConfig Config { get; }

        /// <summary>Task to wait on for the completion result.</summary>
        public Task Completion => m_server.Completion;

        /// <summary>Client for talking to BuildXL.</summary>
        [CanBeNull]
        public Client ApiClient { get; }

        protected readonly ICloudBuildLogger m_etwLogger;
        protected readonly IServer m_server;
        protected readonly IParser m_parser;

        protected readonly ILogger m_logger;

        /// <nodoc />
        public ILogger Logger => m_logger;

        /// <nodoc />
        public ServicePipDaemon(IParser parser, DaemonConfig daemonConfig, ILogger logger, IIpcProvider rpcProvider = null, Client client = null)
        {
            Contract.Requires(daemonConfig != null);

            Config = daemonConfig;
            m_parser = parser;
            ApiClient = client;
            m_logger = logger;

            rpcProvider = rpcProvider ?? IpcFactory.GetProvider();
            m_server = rpcProvider.GetServer(Config.Moniker, Config);

            m_etwLogger = new BuildXLBasedCloudBuildLogger(Config.Logger, Config.EnableCloudBuildIntegration);
        }

        /// <summary>
        /// Starts to listen for client connections.  As soon as a connection is received,
        /// it is placed in an action block from which it is picked up and handled asynchronously
        /// (in the <see cref="ParseAndExecuteCommand"/> method).
        /// </summary>
        public void Start()
        {
            m_server.Start(this);
        }

        /// <summary>
        /// Requests shut down, causing this daemon to immediately stop listening for TCP/IP
        /// connections. Any pending requests, however, will be processed to completion.
        /// </summary>
        public void RequestStop()
        {
            m_server.RequestStop();
        }

        /// <summary>
        /// Calls <see cref="RequestStop"/> then waits for <see cref="Completion"/>.
        /// </summary>
        public Task RequestStopAndWaitForCompletionAsync()
        {
            RequestStop();
            return Completion;
        }    

        /// <inheritdoc />
        public void Dispose()
        {
            m_server.Dispose();
            ApiClient?.Dispose();
            m_logger.Dispose();
        }       

        protected readonly CounterCollection<DaemonCounter> m_counters = new CounterCollection<DaemonCounter>();

        protected enum DaemonCounter
        {
            /// <nodoc/>
            [CounterType(CounterType.Stopwatch)]
            ParseArgsDuration,

            /// <nodoc/>
            [CounterType(CounterType.Stopwatch)]
            ServerActionDuration,

            /// <nodoc/>
            QueueDurationMs,
        }

        private async Task<IIpcResult> ParseAndExecuteCommand(IIpcOperation operation)
        {
            string cmdLine = operation.Payload;
            m_logger.Verbose("Command received: {0}", cmdLine);
            ConfiguredCommand conf;
            using (m_counters.StartStopwatch(DaemonCounter.ParseArgsDuration))
            {
                conf = ParseArgs(cmdLine, m_parser);
            }

            IIpcResult result;
            using (m_counters.StartStopwatch(DaemonCounter.ServerActionDuration))
            {
                 result = await conf.Command.ServerAction(conf, this);
            }

            TimeSpan queueDuration = operation.Timestamp.Daemon_BeforeExecuteTime - operation.Timestamp.Daemon_AfterReceivedTime;
            m_counters.AddToCounter(DaemonCounter.QueueDurationMs, (long)queueDuration.TotalMilliseconds);

            return result;
        }

        Task<IIpcResult> IIpcOperationExecutor.ExecuteAsync(IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            return ParseAndExecuteCommand(operation);
        }

        #region Config options and commands 

        protected static readonly Dictionary<string, Command> Commands = new Dictionary<string, Command>();

        public static readonly StrOption Moniker = RegisterDaemonConfigOption(new StrOption("moniker")
        {
            ShortName = "m",
            HelpText = "Moniker to identify client/server communication",
        });

        public static readonly IntOption MaxConcurrentClients = RegisterDaemonConfigOption(new IntOption("maxConcurrentClients")
        {
            HelpText = "OBSOLETE due to the hardcoded config. (Maximum number of clients to serve concurrently)",
            DefaultValue = DaemonConfig.DefaultMaxConcurrentClients,
        });

        public static readonly IntOption MaxConnectRetries = RegisterDaemonConfigOption(new IntOption("maxConnectRetries")
        {
            HelpText = "Maximum number of retries to establish a connection with a running daemon",
            DefaultValue = DaemonConfig.DefaultMaxConnectRetries,
        });

        public static readonly IntOption ConnectRetryDelayMillis = RegisterDaemonConfigOption(new IntOption("connectRetryDelayMillis")
        {
            HelpText = "Delay between consecutive retries to establish a connection with a running daemon",
            DefaultValue = (int)DaemonConfig.DefaultConnectRetryDelay.TotalMilliseconds,
        });

        public static readonly BoolOption ShellExecute = RegisterDaemonConfigOption(new BoolOption("shellExecute")
        {
            HelpText = "Use shell execute to start the daemon process (a shell window will be created and displayed)",
            DefaultValue = false,
        });

        public static readonly BoolOption StopOnFirstFailure = RegisterDaemonConfigOption(new BoolOption("stopOnFirstFailure")
        {
            HelpText = "Daemon process should terminate after first failed operation (e.g., 'drop create' fails because the drop already exists).",
            DefaultValue = DaemonConfig.DefaultStopOnFirstFailure,
        });

        public static readonly BoolOption EnableCloudBuildIntegration = RegisterDaemonConfigOption(new BoolOption("enableCloudBuildIntegration")
        {
            ShortName = "ecb",
            HelpText = "Enable logging ETW events for CloudBuild to pick up",
            DefaultValue = DaemonConfig.DefaultEnableCloudBuildIntegration,
        });

        public static readonly BoolOption Verbose = RegisterDaemonConfigOption(new BoolOption("verbose")
        {
            ShortName = "v",
            HelpText = "Verbose logging",
            IsRequired = false,
            DefaultValue = false,
        });

        public static readonly StrOption DropServiceConfigFile = RegisterDaemonConfigOption(new StrOption("dropServiceConfigFile")
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

        public static readonly StrOption LogDir = RegisterDaemonConfigOption(new StrOption("logDir")
        {
            ShortName = "log",
            HelpText = "Log directory",
            IsRequired = false
        });

        public static readonly StrOption File = new StrOption("file")
        {
            ShortName = "f",
            HelpText = "File path",
            IsRequired = false,
            IsMultiValue = true,
        };

        public static readonly StrOption IpcServerMonikerRequired = new StrOption("ipcServerMoniker")
        {
            ShortName = "dm",
            HelpText = "IPC moniker identifying a running BuildXL IPC server",
            IsRequired = true,
        };

        public static readonly StrOption HelpNoNameOption = new StrOption(string.Empty)
        {
            HelpText = "Command name",
        };

        public static readonly StrOption IpcServerMonikerOptional = new StrOption(
            longName: IpcServerMonikerRequired.LongName)
        {
            ShortName = IpcServerMonikerRequired.ShortName,
            HelpText = IpcServerMonikerRequired.HelpText,
            IsRequired = false,
        };

        protected static T RegisterOption<T>(List<Option> options, T option) where T : Option
        {
            options.Add(option);
            return option;
        }

        protected static T RegisterDaemonConfigOption<T>(T option) where T : Option => RegisterOption(DaemonConfigOptions, option);

        /// <remarks>
        /// The <see cref="DaemonConfigOptions"/> options are added to every command.
        /// A non-mandatory string option "name" is added as well, which drop operation
        /// commands may want to use to explicitly specify the target drop name.
        /// </remarks>
        public static Command RegisterCommand(
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
                opts.Add(new Option(longName: "name")
                {
                    ShortName = "n",
                });
            }

            var cmd = new Command(name, opts, serverAction, clientAction, description, needsIpcClient);
            Commands[cmd.Name] = cmd;
            return cmd;
        }

        protected static readonly Command HelpCmd = RegisterCommand(
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

        public static readonly Command StopDaemonCmd = RegisterCommand(
            name: "stop",
            description: "[RPC] Stops the daemon process running on specified port; fails if no such daemon is running.",
            clientAction: AsyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                conf.Logger.Info("[STOP] requested");
                daemon.RequestStop();
                return Task.FromResult(IpcResult.Success());
            });

        public static readonly Command CrashDaemonCmd = RegisterCommand(
            name: "crash",
            description: "[RPC] Stops the server process by crashing it.",
            clientAction: AsyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                daemon.Logger.Info("[CRASH] requested");
                Environment.Exit(-1);
                return Task.FromResult(IpcResult.Success());
            });

        public static readonly Command PingDaemonCmd = RegisterCommand(
            name: "ping",
            description: "[RPC] Pings the daemon process.",
            clientAction: SyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                daemon.Logger.Info("[PING] received");
                return Task.FromResult(IpcResult.Success("Alive!"));
            });

        public static readonly Command TestReadFile = RegisterCommand(
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

        #endregion

        public static ConfiguredCommand ParseArgs(string allArgs, IParser parser, ILogger logger = null, bool ignoreInvalidOptions = false)
        {
            return ParseArgs(parser.SplitArgs(allArgs), parser, logger, ignoreInvalidOptions);
        }

        public static ConfiguredCommand ParseArgs(string[] args, IParser parser, ILogger logger = null, bool ignoreInvalidOptions = false)
        {
            var usageMessage = Lazy.Create(() => "Usage:" + Environment.NewLine + Usage());

            if (args.Length == 0)
            {
                throw new ArgumentException(I($"Command is required. {usageMessage.Value}"));
            }

            var argsQueue = new Queue<string>(args);
            string cmdName = argsQueue.Dequeue();
            if (!Commands.TryGetValue(cmdName, out Command cmd))
            {
                throw new ArgumentException(I($"No command '{cmdName}' is found. {usageMessage.Value}"));
            }

            var sw = Stopwatch.StartNew();
            Config conf = BuildXL.Utilities.CLI.Config.ParseCommandLineArgs(cmd.Options, argsQueue, parser, caseInsensitive: true, ignoreInvalidOptions: ignoreInvalidOptions);
            var parseTime = sw.Elapsed;

            logger = logger ?? new ConsoleLogger(Verbose.GetValue(conf), ServicePipDaemon.LogPrefix);
            logger.Verbose("Parsing command line arguments done in {0}", parseTime);
            return new ConfiguredCommand(cmd, conf, logger);
        }

        public static DaemonConfig CreateDaemonConfig(ConfiguredCommand conf)
        {
            return new DaemonConfig(
                logger: conf.Logger,
                moniker: conf.Get(Moniker),
                maxConnectRetries: conf.Get(MaxConnectRetries),
                connectRetryDelay: TimeSpan.FromMilliseconds(conf.Get(ConnectRetryDelayMillis)),
                stopOnFirstFailure: conf.Get(StopOnFirstFailure),
                enableCloudBuildIntegration: conf.Get(EnableCloudBuildIntegration));
        }

        public static IClient CreateClient(ConfiguredCommand conf)
        {
            var daemonConfig = CreateDaemonConfig(conf);
            return IpcProvider.GetClient(daemonConfig.Moniker, daemonConfig);
        }

        private static string Usage()
        {
            var builder = new StringBuilder();
            var len = Commands.Keys.Max(cmdName => cmdName.Length);
            foreach (var cmd in Commands.Values)
            {
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-" + len + "} : {1}", cmd.Name, cmd.Description));
            }

            return builder.ToString();
        }

        protected static int SyncRPCSend(ConfiguredCommand conf, IClient rpc) => RPCSend(conf, rpc, true);

        protected static int AsyncRPCSend(ConfiguredCommand conf, IClient rpc) => RPCSend(conf, rpc, false);

        protected static int RPCSend(ConfiguredCommand conf, IClient rpc, bool isSync)
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
        /// Reconstructs a full command line from a command name (<paramref name="commandName"/>)
        /// and a configuration (<paramref name="config"/>).
        /// </summary>
        internal static string ToPayload(string commandName, Config config)
        {
            return commandName + " " + config.Render();
        }

        /// <summary>
        ///     Reconstructs a full command line corresponding to a <see cref="ConfiguredCommand"/>.
        /// </summary>
        private static string ToPayload(ConfiguredCommand cmd) => ToPayload(cmd.Command.Name, cmd.Config);

        public static int SubscribeAndProcessCloudBuildEvents()
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
