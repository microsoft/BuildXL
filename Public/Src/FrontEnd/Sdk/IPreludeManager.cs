// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Responsible for finding built-in prelude files and creating a <see cref="ParsedModule"/> out of them.
    /// </summary>
    public interface IPreludeManager
    {
        /// <summary>
        /// Creates or returns a previously created parsed prelude module.
        /// </summary>
        Task<Possible<ParsedModule>> GetOrCreateParsedPreludeModuleAsync();
    }
}
