// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Delegate that is invoked when a command is executed.
    /// </summary>
    public delegate Result<dynamic, ResponseError> ExecuteCommand(
        ProviderContext providerContext,
        dynamic[] arguments);

    /// <summary>
    /// Allows for addition and execution of commands through workspace/ExecuteCommand protocol.
    /// </summary>
    public interface IExecuteCommandProvider
    {
        /// <summary>
        /// Adds a command to the command execution provider.
        /// </summary>
        void AddCommand(string command, ExecuteCommand executeDelegate);

        /// <summary>
        /// Called from workspace/ExecuteCommand to locate and execute a command understood by this provider.
        /// </summary>
        Result<dynamic, ResponseError> ExecuteCommand(ExecuteCommandParams commandParams, CancellationToken token);
    }

    /// <nodoc/>
    public sealed class ExecuteCommandProvider : IdeProviderBase, IExecuteCommandProvider
    {
        private sealed class ExecuteCommandInformation
        {
            /// <summary>
            /// The name of the command (verb) that is passed to "workspace/executeCommand"
            /// </summary>
            public string Command;

            /// <summary>
            /// Delegate to call when the function is executed.
            /// </summary>
            public ExecuteCommand ExecuteCommand;
        }

        /// <nodoc/>
        internal ExecuteCommandProvider(ProviderContext providerContext)
            : base(providerContext)
        {
        }

        /// <summary>
        /// Holds the commands registered with the command provider through <see cref="AddCommand(string, Providers.ExecuteCommand)"/>
        /// </summary>
        private static readonly List<ExecuteCommandInformation> s_executeCommandInformation = new List<ExecuteCommandInformation>();

        /// <summary>
        /// Creates the list of commands this provider is capable of executing
        /// </summary>
        /// <remarks>
        /// Used to fill out the server capabilities ServerCapabilities/executeCommandProvider
        /// </remarks>
        public static IEnumerable<string> GetCommands()
        {
            return s_executeCommandInformation.Select(info => info.Command);
        }

        /// <summary>
        /// Adds a command to the command execution provider.
        /// </summary>
        public void AddCommand(string command, ExecuteCommand executeDelegate)
        {
            s_executeCommandInformation.Add(
                new ExecuteCommandInformation
                {
                    Command = command,
                    ExecuteCommand = executeDelegate,
                });
        }

        /// <summary>
        /// Called from workspace/ExecuteCommand to attempt to locate and execute a command understood by this provider.
        /// </summary>
        public Result<dynamic, ResponseError> ExecuteCommand(ExecuteCommandParams commandParams, CancellationToken token)
        {
            // TODO: support cancellation
            foreach (var command in s_executeCommandInformation)
            {
                if (command.Command.Equals(commandParams.Command, StringComparison.Ordinal))
                {
                    return command.ExecuteCommand(ProviderContext, commandParams.Arguments);
                }
            }

            throw new InvalidOperationException($"Unknown command '{commandParams.Command}'. Supported commands are {GetSupportedCommands()}.");

            string GetSupportedCommands()
            {
                return string.Join(", ", s_executeCommandInformation.Select(c => $"'{c.Command}'"));
            }
        }
    }
}
