// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Newtonsoft.Json;

namespace SBOMConverter
{
    /// <summary>
    /// SBOMConverter to convert from TypedComponent to SBOMPackage
    /// </summary>
    public static class Program
    {
        /// <nodoc/>
        public static void Main(string[] arguments)
        {
            var args = new Args(arguments);

            try
            {
                var result = ComponentDetectionConverter.TryConvert(args.BcdeOutputPath, m => Console.Error.Write(m), out var packages);

                if (!result)
                {
                    Console.Error.Write($"[SBOMConverter] SBOM package conversion failed for components at path '{args.BcdeOutputPath}'.");
                    Environment.Exit(1);
                }

                // Serialize result to file
                var jsonResult = JsonConvert.SerializeObject(packages, Formatting.Indented);
                File.WriteAllText(args.ResultPath, jsonResult);
            }
            catch (Exception ex)
            {
                Console.Error.Write($"[SBOMConverter] Failed with exception: {ex}");
                Environment.Exit(1);
            }
        }
    }
}
