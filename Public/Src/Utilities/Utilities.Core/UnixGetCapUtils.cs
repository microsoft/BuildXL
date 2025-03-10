// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Contains helper methods to examine file capabilities in Unix
/// </summary>
public class UnixGetCapUtils : UnixUtilsBase
{
    /// <nodoc/>
    protected UnixGetCapUtils() : base(GetCapPath) { }

    /// <summary>
    /// Indicates whether getcap is installed so it can be used by this class.
    /// </summary>
    public static Lazy<bool> IsGetCapInstalled = new(() => OperatingSystemHelper.IsLinuxOS && File.Exists(GetCapPath));

    /// <summary>
    /// Path to getcap utility
    /// </summary>
    private const string GetCapPath = "/usr/sbin/getcap";

    /// <nodoc />
    public static UnixGetCapUtils CreateGetCap() => new();
    
    /// <summary>
    /// Returns true if the provided binary contains any capabilities set
    /// </summary>
    /// <param name="binaryPath">Path for executable to be tested.</param>
    /// <remarks>getcap returns an empty string unless capabilities are found or the file being checked does not exist.</remarks>
    public bool BinaryContainsCapabilities(string binaryPath) =>
        CheckConditionAgainstStandardOutput(binaryPath, binaryPath, (stdout) => !string.IsNullOrEmpty(stdout));
    
    /// <summary>
    /// Returns true if the provided binary contains the specified capability
    /// </summary>
    public bool BinaryContainsCapabilities(string binaryPath, UnixCapability capability) =>
        CheckConditionAgainstStandardOutput(binaryPath, binaryPath, (stdout) => stdout.Contains(capability.CapabilityString()));
}
