// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace BuildXL.Explorer.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("ServerDebugOnStart") == "1")
            {
                Debugger.Launch();
            }

            // Globally this is needed to initialize the hash code
            BuildXL.Storage.ContentHashingUtilities.SetDefaultHashType();

            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) {
            string url = null;
            foreach (string arg in args)
            {
                if (arg.Contains("http://localhost")) {
                    url = arg;
                    break;
                }
            }

            if (string.IsNullOrEmpty(url)) {
                return WebHost.CreateDefaultBuilder(args)
                       .UseStartup<Startup>();
            } else
            {
                return WebHost.CreateDefaultBuilder(args)
                       .UseStartup<Startup>()
                       .UseUrls(url);
            }

        }

    }
}
