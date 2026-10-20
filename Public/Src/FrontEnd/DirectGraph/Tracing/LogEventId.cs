// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.DirectGraph.Tracing
{
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />.
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // Reserved 13000 .. 13099 for the DirectGraph front-end.
        ResolverMissingModuleNames = 13000,
        ResolverMissingTool = 13001,
        ResolverEmptyModuleName = 13002,
        ResolverDuplicateModuleNames = 13003,
        ResolverInvalidIdleTimeout = 13004,
    }
}
