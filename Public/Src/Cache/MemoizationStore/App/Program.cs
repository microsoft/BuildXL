// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CLAP;

namespace BuildXL.Cache.MemoizationStore.App
{
    /// <summary>
    ///     Entry point of the application.
    /// </summary>
    public static class Program
    {
        private static int Main(string[] args)
        {
            using (var app = new Application())
            {
                return Parser.Run(args, app);
            }
        }
    }
}
