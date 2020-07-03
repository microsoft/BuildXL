// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Writes a module file
    /// </summary>
    public abstract class ModuleWriter : BuildXLFileWriter
    {
        protected readonly string Identity;
        protected readonly IEnumerable<string> LogicAssemblies;

        protected ModuleWriter(string absolutePath, string identity, IEnumerable<string> logicAssemblies)
            : base(absolutePath)
        {
            Identity = identity;
            LogicAssemblies = logicAssemblies;
        }

        public abstract void AddSpec(string specRelativePath, SpecWriter specWriter);
    }
}
