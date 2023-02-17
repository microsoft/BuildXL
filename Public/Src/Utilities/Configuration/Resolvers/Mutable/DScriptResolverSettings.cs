// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class SourceResolverSettings : DScriptResolverSettings
    {
        /// <nodoc />
        public SourceResolverSettings()
            : base()
        {
        }

        /// <nodoc />
        public SourceResolverSettings(IDScriptResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
        }
    }

    /// <nodoc />
    public class DScriptResolverSettings : ResolverSettings, IDScriptResolverSettings
    {
        /// <nodoc />
        public DScriptResolverSettings()
        {
            Modules = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
            Packages = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
        }

        /// <nodoc />
        public DScriptResolverSettings(IDScriptResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Root = pathRemapper.Remap(template.Root);
            Modules = template.Modules?
                .Select(fileOrInlineModule => RemapModule(fileOrInlineModule, pathRemapper))
                .ToList();
            Packages = template.Packages?.Select(pathRemapper.Remap).ToList();
        }

        /// <inheritdoc />
        public AbsolutePath Root { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>> Modules { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<AbsolutePath> Packages { get; set; }

        private static DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition> RemapModule(
            DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition> fileOrInlineModule, 
            PathRemapper pathRemapper)
        {
            var fileOrInlineModuleValue = fileOrInlineModule?.GetValue();

            if (fileOrInlineModuleValue == null)
            {
                return null;
            }

            if (fileOrInlineModuleValue is AbsolutePath path)
            {
                return new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(pathRemapper.Remap(path));
            }

            var inlineModuleDefinition = (IInlineModuleDefinition)fileOrInlineModuleValue;

            var remappedInlineModuleDefinition = new InlineModuleDefinition
            {
                ModuleName = inlineModuleDefinition.ModuleName,
                Projects = inlineModuleDefinition.Projects?.Select(project => pathRemapper.Remap(project)).ToList()
            };

            return new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(remappedInlineModuleDefinition);
        }
    }
}
