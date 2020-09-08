// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public interface IPluginFactory
    {
        /// <nodoc />
        IPlugin CreatePlugin(PluginCreationArgument pluginCreationArgument);
    }
}
