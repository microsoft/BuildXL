// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
