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

    /// <summary>
    /// Check if the environment variable TF_BUILD is set, which indicates an Azure DevOps build.
    /// </summary>
    private static bool IsAdoBuild() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));

    /// <summary>
    /// Sets the required EBPF capabilities on the provided binary path.
    /// For now we only want to do this for ADO builds because they won't require an interactive prompt
    /// TODO: for now we are setting DAC_OVERRIDE to allow the executable to pin maps (which requires writing under th BPF file system). Consider
    /// mounting the file system explicitly as a way to avoid setting this cap.
    /// CAP_SYS_NICE is required to allow the ebpf runner to set the nice level of the threads it spawns.
    /// </summary>
    public static void SetEBPFCapabilitiesIfNeeded(string binaryPath)
    {
        if (IsAdoBuild())
        {
            var getCapUtils = UnixGetCapUtils.CreateGetCap();
            if (!getCapUtils.BinaryContainsCapabilities(binaryPath, UnixCapability.CAP_SYS_ADMIN) ||
                !getCapUtils.BinaryContainsCapabilities(binaryPath, UnixCapability.CAP_DAC_OVERRIDE) ||
                !getCapUtils.BinaryContainsCapabilities(binaryPath, UnixCapability.CAP_SYS_NICE))
            {
                var setCapUtils = UnixSetCapUtils.CreateSetCap();
                if (!setCapUtils.SetCapability(binaryPath, UnixCapability.CAP_SYS_ADMIN, UnixCapability.CAP_DAC_OVERRIDE, UnixCapability.CAP_SYS_NICE))
                {
                    throw new BuildXLException($"Failed to set CAP_SYS_ADMIN, CAP_DAC_OVERRIDE and CAP_SYS_NICE capabilities on {binaryPath}");
                }
            }
        }
    }
}
