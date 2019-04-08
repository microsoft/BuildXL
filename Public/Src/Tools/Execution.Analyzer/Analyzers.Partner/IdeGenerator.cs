// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Ide.Generator;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration.Mutable;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeIdeGenerator()
        {
            return new IdeGenerator(GetAnalysisInput());
        }
    }

    internal sealed class IdeGenerator : Analyzer
    {
        private readonly Ide.Generator.IdeGenerator m_ideGenerator;
        private readonly AbsolutePath m_solutionFile;

        internal IdeGenerator(AnalysisInput input)
            : base(input)
        {
            AbsolutePath enlistmentRoot;
            if (!CachedGraph.MountPathExpander.TryGetRootByMountName("SourceRoot", out enlistmentRoot))
            {
                throw new BuildXLException("Source root is not available");
            }

            var config = new CommandLineConfiguration
                {
                    Startup = new StartupConfiguration
                        {
                            ConfigFile = enlistmentRoot.Combine(PathTable, PathAtom.Create(StringTable, Names.ConfigDsc)),
                        },
                    Ide = new IdeConfiguration { IsEnabled = true },
                    Layout =
                        {
                            OutputDirectory = enlistmentRoot.Combine(PathTable, PathAtom.Create(StringTable, "Out")),
                        },
                    Engine =
                    {
                        TrackBuildsInUserFolder = false,
                    }
            };

            Ide.Generator.IdeGenerator.Configure(config, config.Startup, PathTable);
            m_ideGenerator = new Ide.Generator.IdeGenerator(PipGraph.Context, PipGraph, PipGraph.DataflowGraph, config.Startup.ConfigFile, config.Ide);
            m_solutionFile = Ide.Generator.IdeGenerator.GetSolutionPath(config.Ide, PathTable);
        }

        public override int Analyze()
        {
            m_ideGenerator.Generate();
            Console.WriteLine("Solution path: " + m_solutionFile.ToString(PathTable));
            return 0;
        }
    }
}
