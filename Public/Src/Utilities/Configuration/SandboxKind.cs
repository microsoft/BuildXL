// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        MacOsEndpointSecurity
    }
}
