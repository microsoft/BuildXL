// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
using Tool.ServicePipDaemon;
using static Tool.ServicePipDaemon.Statics;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// SymbolDaemon entry point.
    /// </summary>
    public static class Program
    {
        private const string ResponseFilePrefix = "@";

        /// <nodoc/>        
        public static int Main(string[] args)
        {
            // TODO:#1208464 - this can be removed once SymbolDaemon targets .net 4.7 or newer where TLS 1.2 is enabled by default
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            try
            {
                Console.WriteLine(nameof(SymbolDaemon) + " started at " + DateTime.UtcNow);
                Console.WriteLine(SymbolDaemon.SymbolDLogPrefix + "Command line arguments: ");
                Console.WriteLine(SymbolDaemon.SymbolDLogPrefix + string.Join(Environment.NewLine + SymbolDaemon.SymbolDLogPrefix, args));
                Console.WriteLine();

                SymbolDaemon.EnsureCommandsInitialized();

                var confCommand = ServicePipDaemon.ServicePipDaemon.ParseArgs(args, new UnixParser());
                if (confCommand.Command.NeedsIpcClient)
                {
                    // Even though NeedsIpcClient is 'true' for the majority of commands,
                    // we should not step into this block when the code runs in production.
                    // Main method will be called once per symbol request (part of starting
                    // a service pip), and the 'start' command will be passed as an argument
                    // (it will fall into 'else' branch).
                    //
                    // The main reason we have this code is to facilitate testing/debugging
                    // without spinning up bxl.exe.
                    using (var rpc = CreateClient(confCommand))
                    {
                        var result = confCommand.Command.ClientAction(confCommand, rpc);
                        rpc.RequestStop();
                        rpc.Completion.GetAwaiter().GetResult();
                        return result;
                    }
                }
                else
                {
                    return confCommand.Command.ClientAction(confCommand, null);
                }
            }
            catch (ArgumentException e)
            {
                Error(e.Message);
                return 3;
            }
        }

        internal static IClient CreateClient(ConfiguredCommand conf)
        {
            var daemonConfig = ServicePipDaemon.ServicePipDaemon.CreateDaemonConfig(conf);
            return IpcFactory.GetProvider().GetClient(daemonConfig.Moniker, daemonConfig);
        }
    }
}
