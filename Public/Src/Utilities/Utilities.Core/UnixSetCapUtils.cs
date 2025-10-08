// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Contains helper methods to set file capabilities in Unix
/// </summary>
public class UnixSetCapUtils : UnixUtilsBase
{
    /// <nodoc/>
    protected UnixSetCapUtils() : base(SetCapPath) { }

    /// <summary>
    /// Path to setcap utility
    /// </summary>
    private const string SetCapPath = "/usr/sbin/setcap";

    /// <nodoc />
    public static UnixSetCapUtils CreateSetCap() => new();

    /// <summary>
    /// Sets a capability on a binary
    /// </summary>
    /// <remarks>
    /// This requires an interactive prompt for sudo password.
    /// Do not call this if you're not sure whether it's possible to interactively prompt from the engine.
    /// </remarks>
    public bool SetCapability(string binaryPath, bool interactive, out string standardError, params UnixCapability[] capabilities)
        => CheckConditionAgainstStandardOutput(
            binaryPath, 
            $"\"{string.Join(" ", capabilities.Select(cap => cap.CapabilityString()))}\" {binaryPath}", 
            condition: string.IsNullOrEmpty, 
            runAsSudo: true,
            interactive: interactive,
            standardError: out standardError);
}