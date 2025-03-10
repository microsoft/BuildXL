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
    CAP_SYS_ADMIN
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
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown capability")
        };
    }
}