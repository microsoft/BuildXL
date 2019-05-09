// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace Interop {
    @@public
    export const opNamesAutoGen = genOpNamesCSharpFile(f`${Context.getMount("Sandbox").path}/MacOs/Sandbox/Src/Kauth/OpNames.hpp`);

    const program = `
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

            var regex = new Regex(@"macro_to_apply\\((.*),\\s*(.*)\\)");
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

namespace BuildXL.Interop.MacOS
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
`;

    const exe = BuildXLSdk.nativeExecutable({
        assemblyName: "BuildXL.Interop.TmpOpNameGenerator",
        sources: [
            Transformer.writeAllText({
                outputPath: p`${Context.getNewOutputDirectory("op-name-gen")}/Program.cs`, 
                text: program
            })
        ],
        allowUnsafeBlocks: true,
    });

    export const deployed = BuildXLSdk.deployManagedTool({
        tool: exe,
        options: {
            prepareTempDirectory: true,
        },
    });

    function genOpNamesCSharpFile(inputHppFile: SourceFile): DerivedFile {
        const tool = Interop.withQualifier(BuildXLSdk.TargetFrameworks.currentMachineQualifier).deployed;
        const outDir = Context.getNewOutputDirectory("op-name-out");
        const consoleOutPath = p`${outDir}/FileOperation.g.cs`;
        const result = Transformer.execute({
            tool: tool,
            arguments: [
                Cmd.argument(Artifact.input(inputHppFile))
            ],
            workingDirectory: outDir,
            consoleOutput: consoleOutPath
        });

        return result.getOutputFile(consoleOutPath);
    }
}
