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

namespace Tool.DropDaemon
{
    internal delegate int ClientAction(ConfiguredCommand conf, IClient rpc);

    internal delegate Task<IIpcResult> ServerAction(ConfiguredCommand conf, Daemon daemon);

    /// <summary>
    ///     A command has a name, description, a list of options it supports, and  two actions:
    ///     one for executing this command on the client, and one for executing it on the server.
    ///
    ///     When this program (DropDaemon.exe) is invoked, command line arguments are parsed to
    ///     determine the command specified by the user.  That command is then interpreted by
    ///     executing its <see cref="ClientAction"/>.  Most of DropDaemon's commands will be
    ///     RPC calls, i.e., when a command is received via the command line, it is to be
    ///     marshaled and sent over to a running DropDaemon server via an RPC.  In such a case,
    ///     the client action simply invokes <see cref="IClient.Send"/>.  When an RPC is
    ///     received by a DropDaemon server (<see cref="Daemon.ParseAndExecuteCommand"/>), a
    ///     <see cref="Command"/> is unmarshaled from the payload of the RPC operation and the
    ///     command is interpreted on the server by executing its <see cref="ServerAction"/>.
    /// </summary>
    /// <remarks>
    ///     Immutable.
    /// </remarks>
    internal sealed class Command
    {
        /// <summary>A unique command name.</summary>
        internal string Name { get; }

        /// <summary>Arbitrary description.</summary>
        internal string Description { get; }

        /// <summary>Options that may/must be passed to this command.</summary>
        internal IReadOnlyCollection<Option> Options { get; }

        /// <summary>Action to be executed when this command is received via the command line.</summary>
        internal ClientAction ClientAction { get; }

        /// <summary>Action to be executed when this command is received via an RPC call.</summary>
        internal ServerAction ServerAction { get; }

        /// <summary>Whether this command requires an IpcClient; defaults to true.</summary>
        internal bool NeedsIpcClient { get; }

        /// <nodoc />
        internal Command(
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
        ///     Performs a functional composition of a number of <see cref="ServerAction"/> functions,
        ///     where the results are merged by calling <see cref="IpcResult.Merge(IIpcResult, IIpcResult)"/>.
        /// </summary>
        internal static ServerAction Compose(params ServerAction[] actions)
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

        internal string Usage(IParser parser)
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
    ///     Simple wrapper class that holds a <see cref="Command"/> and a <see cref="Config"/>
    ///     containing actual values for the command's <see cref="Command.Options"/>.
    /// </summary>
    internal sealed class ConfiguredCommand
    {
        internal Command Command { get; }

        internal Config Config { get; }

        internal ILogger Logger { get; }

        internal ConfiguredCommand(Command command, Config config, ILogger logger)
        {
            Command = command;
            Config = config;
            Logger = logger;
        }

        internal T Get<T>(Option<T> option) => option.GetValue(Config);
    }
}
