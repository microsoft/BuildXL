// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildXL.OpNameGen
{
    class Program
    {
        static unsafe int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine($"Usage: {Environment.CommandLine} <file-name>");
                return 1;
            }

            string path = args[0];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File doesn't exist: '{path}'");
                return 2;
            }

            var regex = new Regex(@"macro_to_apply\((.*),\s*(.*)\)");
            var matches = File
                .ReadAllLines(path)
                .Select(line => regex.Match(line))
                .Where(match => match.Success)
                .Select(match => (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()))
                .ToArray();
            var enumDecls = matches
                .Select(match => $"        {match.Item1},")
                .ToArray();
            var dictDecls = matches
                .Select(match => $"            [FileOperation.{match.Item1}] = {match.Item2},")
                .ToArray();

            var output = $@"
//
// Auto generated from: {Path.GetFileName(path)}
//
using System.Collections.Generic;

namespace BuildXL.Interop.Unix
{{
#pragma warning disable CS1591 // disabling warning about missing API documentation
    /// <summary>File operations reported by BuildXLSandbox.</summary>
    public enum FileOperation : byte
    {{
{string.Join(Environment.NewLine, enumDecls)}
    }}

    public static class FileOperationExtensions
    {{
        /// <summary>Operation names to use when logging reported file accesses</summary>
        public static readonly IReadOnlyDictionary<FileOperation, string> OpNames = new Dictionary<FileOperation, string>
        {{
{string.Join(Environment.NewLine, dictDecls)}
        }};

        /// <summary>Returns the name associated with the given FileOperation enum constant.</summary>
        public static string GetName(this FileOperation op) => OpNames[op];
    }}
#pragma warning restore CS1591
}}
";
            Console.WriteLine(output);
            return 0;
        }
    }
}