// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CLAP;

namespace BuildXL.Cache.Roxis.App
{
    /// <summary>
    /// Main program entry point
    /// </summary>
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // CLAP will derive processing of the effective subcommand to the classes specified in template parameters.
            Parser.Run<Server, Client>(args);
        }
    }
}
