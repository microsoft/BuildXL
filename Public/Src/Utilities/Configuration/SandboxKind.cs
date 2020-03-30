// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Different kinds of sandboxes
    /// </summary>
    public enum SandboxKind : byte
    {
        /// <summary>
        /// No sandboxing
        /// </summary>
        None,

        /// <summary>
        /// Default sandboxing for the current platform.
        /// </summary>
        Default,

        /// <summary>
        /// Windows-specific: using Detours
        /// </summary>
        WinDetours,

        /// <summary>
        /// macOs-specifc: using kernel extension
        /// </summary>
        MacOsKext,

        /// <summary>
        /// Like <see cref="MacOsKext"/> except that it gnores all reported file accesses.
        /// </summary>
        MacOsKextIgnoreFileAccesses,

        /// <summary>
        /// macOs-specifc: Using the EndpointSecurity subsystem for sandboxing (available from 10.15+)
        /// </summary>
        MacOsEndpointSecurity,

        /// <summary>
        /// macOs-specifc: Using DYLD interposing for sandboxing
        /// </summary>
        MacOsDetours,

        /// <summary>
        /// macOs-specifc: Using the EndpointSecurity subsystem (available from 10.15+) and DYLD interposing together for sandboxing
        /// </summary>
        MacOsHybrid
    }
}
