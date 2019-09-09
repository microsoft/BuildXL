// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
using Tool.ServicePipDaemon;
using static Tool.ServicePipDaemon.Statics;

namespace Tool.DropDaemon
{
    /// <summary>
    /// DropDaemon entry point.
    /// </summary>
    public static class Program
    {
        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA2204:Spelling of DropD")]
        public static int Main(string[] args)
        {
            // TODO:#1208464- this can be removed once DropDaemon targets .net 4.7 or newer where TLS 1.2 is enabled by default
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            try
            {
                Console.WriteLine(nameof(DropDaemon) + " started at " + DateTime.UtcNow);
                Console.WriteLine(DropDaemon.DropDLogPrefix + "Command line arguments: ");
                Console.WriteLine(string.Join(Environment.NewLine + DropDaemon.DropDLogPrefix, args));
                Console.WriteLine();

                DropDaemon.EnsureCommandsInitialized();

                var confCommand = ServicePipDaemon.ServicePipDaemon.ParseArgs(args, new UnixParser());
                if (confCommand.Command.NeedsIpcClient)
                {
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
