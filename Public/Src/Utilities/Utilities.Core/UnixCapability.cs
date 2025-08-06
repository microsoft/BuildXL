// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Set of capabilities supported by this tool.
/// </summary>
public enum UnixCapability
{
    /// <summary>
    /// Add capability to perform a range of system administration operations.
    /// </summary>
    CAP_SYS_ADMIN,
    /// <summary>
    /// Processes which have it in their effective capability set, DAC (read/write/execute) permission checks are bypassed completely.
    /// </summary>
    CAP_DAC_OVERRIDE,

    /// <summary>
    /// Allows a process to change the priority of processes that it owns.
    /// </summary>
    CAP_SYS_NICE,
}

/// <summary>
/// Extensions for the <see cref="UnixCapability"/> enum.
/// </summary>
public static class UnixCapabilityExtensions
{
    /// <summary>
    /// Converts a <see cref="UnixCapability"/> to it's string representation that is passed to getcap/setcap.
    /// </summary>
    public static string CapabilityString(this UnixCapability capability)
    {
        return capability switch
        {
            UnixCapability.CAP_SYS_ADMIN => "cap_sys_admin=ep",
            UnixCapability.CAP_DAC_OVERRIDE => "cap_dac_override=ep",
            UnixCapability.CAP_SYS_NICE => "cap_sys_nice=ep",
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown capability")
        };
    }
}