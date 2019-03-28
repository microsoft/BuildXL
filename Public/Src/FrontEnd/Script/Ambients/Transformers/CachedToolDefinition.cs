// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Object that represents the results of parsing a tooldefinition
    /// Its purpose is to only parse a given objectliteral once into this structure
    /// and then use repeatedly over and over for pips that use that tool.
    /// </summary>
    public class CachedToolDefinition
    {
        /// <nodoc />
        public FileArtifact Executable { get; set; }

        /// <nodoc />
        public StringId ToolDescription { get; set; }

        /// <nodoc />
        public List<FileArtifact> InputFiles { get; } = new List<FileArtifact>();

        /// <nodoc />
        public List<DirectoryArtifact> InputDirectories { get; } = new List<DirectoryArtifact>();

        /// <nodoc />
        public List<AbsolutePath> UntrackedFiles { get; } = new List<AbsolutePath>();

        /// <nodoc />
        public List<DirectoryArtifact> UntrackedDirectories { get; } = new List<DirectoryArtifact>();

        /// <nodoc />
        public List<DirectoryArtifact> UntrackedDirectoryScopes { get; } = new List<DirectoryArtifact>();

        /// <nodoc />
        public Dictionary<StringId, PipDataAtom> EnvironmentVariables { get; } = new Dictionary<StringId, PipDataAtom>();

        /// <nodoc />
        public bool UntrackedWindowsDirectories { get; set; }

        /// <nodoc />
        public bool UntrackedAppDataDirectories { get; set; }

        /// <nodoc />
        public bool EnableTempDirectory { get; set; }
    }
}
