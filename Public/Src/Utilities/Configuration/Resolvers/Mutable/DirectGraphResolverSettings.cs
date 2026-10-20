// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Mutable settings for the DirectGraph resolver.
    /// </summary>
    public sealed class DirectGraphResolverSettings : ResolverSettings, IDirectGraphResolverSettings
    {
        /// <nodoc />
        public DirectGraphResolverSettings()
        {
        }

        /// <nodoc />
        public DirectGraphResolverSettings(IDirectGraphResolverSettings template, PathRemapper pathRemapper) : base(template, pathRemapper)
        {
            WorkingDirectory = pathRemapper.Remap(template.WorkingDirectory);
            ModuleNames = template.ModuleNames?.Select(pathRemapper.Remap).ToList();
            Tool = pathRemapper.Remap(template.Tool);
            Arguments = template.Arguments;
            IdleTimeoutMin = template.IdleTimeoutMin;
            Environment = template.Environment;
            EnableProjectGraphVerboseLogging = template.EnableProjectGraphVerboseLogging;
        }

        /// <inheritdoc />
        public AbsolutePath WorkingDirectory { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<PathAtom> ModuleNames { get; set; }

        /// <inheritdoc />
        public FileArtifact Tool { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<string> Arguments { get; set; }

        /// <inheritdoc />
        public int? IdleTimeoutMin { get; set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, Resolvers.EnvironmentData> Environment { get; set; }

        /// <inheritdoc />
        public bool? EnableProjectGraphVerboseLogging { get; set; }
    }
}
