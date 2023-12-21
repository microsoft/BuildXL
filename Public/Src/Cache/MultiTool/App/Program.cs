// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Tracing;
using CLAP;

namespace BuildXL.Cache.MultiTool.App
{
    /// <summary>
    /// Provides verbs that are useful for internal usage of cache developers.
    /// </summary>
    internal sealed partial class Program
    {
        private static Tracer Tracer { get; } = new Tracer(name: "MultiTool");

        private static void Main(string[] args)
        {
            Parser.Run<Program>(args);
        }

    }
}
