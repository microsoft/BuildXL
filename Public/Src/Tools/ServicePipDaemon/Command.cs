// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;

namespace Tool.ServicePipDaemon
{
    /// <nodoc/>
    public delegate int ClientAction(ConfiguredCommand conf, IClient rpc);

    /// <nodoc/>
    public delegate Task<IIpcResult> ServerAction(ConfiguredCommand conf, ServicePipDaemon daemon);

    /// <summary>
    /// A command has a name, description, a list of options it supports, and  two actions:
    /// one for executing this command on the client, and one for executing it on the server.
    ///
    /// When an instance of a daemon (e.g., DropDaemon) is invoked, command line arguments
    /// are parsed to determine the command specified by the user. That command is then 
    /// interpreted by executing its <see cref="ClientAction"/>. 
    ///     
    /// Most of the daemon commands will be RPC calls, i.e., when a command is received via 
    /// the command line, it is to be marshaled and sent over to a running daemon server via an RPC.
    /// In such a case, the client action simply invokes <see cref="IClient.Send"/>. 
    /// 
    /// When an RPC is received by a daemon server (<see cref="ServicePipDaemon.ParseAndExecuteCommand"/>),
    /// a <see cref="Command"/> is unmarshaled from the payload of the RPC operation and 
    /// is interpreted on the server by executing its <see cref="ServerAction"/>.
    /// </summary>
    /// <remarks>
    /// Immutable.
    /// </remarks>
    public sealed class Command
    {
        /// <summary>A unique command name.</summary>
        public string Name { get; }

        /// <summary>Arbitrary description.</summary>
        public string Description { get; }

        /// <summary>Options that may/must be passed to this command.</summary>
        public IReadOnlyCollection<Option> Options { get; }

        /// <summary>Action to be executed when this command is received via the command line.</summary>
        public ClientAction ClientAction { get; }

        /// <summary>Action to be executed when this command is received via an RPC call.</summary>
        public ServerAction ServerAction { get; }

        /// <summary>Whether this command requires an IpcClient; defaults to true.</summary>
        public bool NeedsIpcClient { get; }

        /// <nodoc />
        public Command(
            string name,
            IEnumerable<Option> options = null,
            ServerAction serverAction = null,
            ClientAction clientAction = null,
            string description = null,
            bool needsIpcClient = true)
        {
            Contract.Requires(name != null);

            Name = name;
            ServerAction = serverAction;
            ClientAction = clientAction;
            Description = description;
            Options = options.ToList();
            NeedsIpcClient = needsIpcClient;
        }

        /// <summary>
        /// Performs a functional composition of a number of <see cref="ServerAction"/> functions,
        /// where the results are merged by calling <see cref="IpcResult.Merge(IIpcResult, IIpcResult)"/>.
        /// </summary>
        public static ServerAction Compose(params ServerAction[] actions)
        {
            Contract.Requires(actions != null);
            Contract.Requires(actions.Length > 0);

            var first = actions.First();
            return actions.Skip(1).Aggregate(first, (accumulator, currentAction) => new ServerAction(async (conf, daemon) =>
            {
                var lhsResult = await accumulator(conf, daemon);
                var rhsResult = await currentAction(conf, daemon);
                return IpcResult.Merge(lhsResult, rhsResult);
            }));
        }

        /// <summary>
        /// Returns the usage information for this command. 
        /// </summary>        
        public string Usage(IParser parser)
        {
            var result = new StringBuilder();
            var tab = "    ";
            result.AppendLine("NAME");
            result.Append(tab).Append(Name).Append(" - ").AppendLine(Description);
            result.AppendLine();
            result.AppendLine("SWITCHES");
            var optsSorted = Options
                .OrderBy(o => o.IsRequired ? 0 : 1)
                .ThenBy(o => o.LongName)
                .ToArray();
            result.AppendLine(parser.Usage(optsSorted, tab, tab));
            return result.ToString();
        }
    }

    /// <summary>
    /// Simple wrapper class that holds a <see cref="Command"/> and a <see cref="Config"/>
    /// containing actual values for the command's <see cref="Command.Options"/>.
    /// </summary>
    public sealed class ConfiguredCommand
    {
        /// <summary>
        /// The command.
        /// </summary>
        public Command Command { get; }

        /// <summary>
        /// Configured values for command's options
        /// </summary>
        public Config Config { get; }

        /// <nodoc/>
        public ILogger Logger { get; }

        /// <nodoc/>
        public ConfiguredCommand(Command command, Config config, ILogger logger)
        {
            Command = command;
            Config = config;
            Logger = logger;
        }

        /// <summary>
        /// Returns the configured value of a particular command option.
        /// </summary>        
        public T Get<T>(Option<T> option) => option.GetValue(Config);
    }
}
