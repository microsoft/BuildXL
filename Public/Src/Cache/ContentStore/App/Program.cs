// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using CLAP;

namespace BuildXL.Cache.ContentStore.App
{
    /// <summary>
    ///     Entry point of the application.
    /// </summary>
    public static class Program
    {
        private static int Main(string[] args)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var runAppTask = Task.Run(() => RunApp(args, cancellationTokenSource.Token));

            // handle Ctrl+C (i.e., SIGINT) by requesting cancellation from the app
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                cancellationTokenSource.Cancel();
                // must cancel this event because otherwise CoreCLR will proceed to kill this process
                eventArgs.Cancel = true;
            };

            // handle graceful termination (i.e., SIGTERM) by requesting cancellation from the app
            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
            {
                cancellationTokenSource.Cancel();
                // we cannot cancel this event, so to prevent CoreCLR from killing this process before
                // we are done finishing up we wait here until our app task completes.
                runAppTask.GetAwaiter().GetResult();
            };

            // wait for the app to finish
            return runAppTask.GetAwaiter().GetResult();
        }

        private static int RunApp(string[] args, CancellationToken token)
        {
            using (var app = new Application(token))
            {
                return Parser.Run(args, app);
            }
        }
    }
}
