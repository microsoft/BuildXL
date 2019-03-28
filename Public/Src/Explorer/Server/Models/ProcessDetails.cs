// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class ProcessDetails : PipDetails
    {
        public ProcessDetails(CachedGraph graph, Process process) 
            : base(graph, process)
        {
            Executable = new FileRef(graph.Context, process.Executable);
            Arguments = new PipData(graph.Context, process.Arguments);
            EnvironmentVariables.AddRange(process.EnvironmentVariables.Select(envVar => new EnvironmentVariable(graph.Context, envVar)));
            WorkingDirectory = new DirectoryRef(graph.Context, process.WorkingDirectory);

            UntrackedScopes.AddRange(process.UntrackedScopes.Select(scope => new DirectoryRef(graph.Context, scope)));
            UntrackedFiles.AddRange(process.UntrackedPaths.Select(path => new FileRef(graph.Context, FileArtifact.CreateSourceFile(path))));

        }

        public FileRef Executable { get; set; }

        public DirectoryRef WorkingDirectory { get; set; }

        public PipData Arguments { get; }

        public List<EnvironmentVariable> EnvironmentVariables { get; } = new List<EnvironmentVariable>();

        public List<DirectoryRef> UntrackedScopes { get; } = new List<DirectoryRef>(0);

        public List<FileRef> UntrackedFiles { get; } = new List<FileRef>(0);

        public class EnvironmentVariable
        {
            public EnvironmentVariable(PipExecutionContext context, Pips.Operations.EnvironmentVariable envVar)
            {
                Name = envVar.Name.ToString(context.StringTable);
                // Passthrough environment variables have invalid values
                if (envVar.Value.IsValid)
                {
                    Value = new PipData(context, envVar.Value);
                    IsPassthrough = false;
                }
                else
                {
                    Value = PipData.EmptyPipData;
                    IsPassthrough = true;
                }
            }

            public string Name { get; set; }
            public PipData Value { get; set; }
            public bool IsPassthrough { get; set; }
        }
    }
}
