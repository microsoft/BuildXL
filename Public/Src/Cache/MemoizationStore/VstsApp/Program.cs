// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CLAP;

namespace BuildXL.Cache.MemoizationStore.VstsApp
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
