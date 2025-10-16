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
        CheckConditionAgainstStandardOutput(binaryPath, binaryPath, (stdout) => !string.IsNullOrEmpty(stdout), out _);

    /// <summary>
    /// Returns true if the provided binary contains the specified capability
    /// </summary>
    public bool BinaryContainsCapabilities(string binaryPath, UnixCapability capability) =>
        CheckConditionAgainstStandardOutput(binaryPath, binaryPath, (stdout) => stdout.Contains(capability.CapabilityString()), out _);

    /// <summary>
    /// Check if the environment variable TF_BUILD is set, which indicates an Azure DevOps build.
    /// </summary>
    private static bool IsAdoBuild() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));
    
    /// <summary>
    /// Sets the required EBPF capabilities on the provided binary path.
    /// TODO: for now we are setting DAC_OVERRIDE to allow the executable to pin maps (which requires writing under th BPF file system). Consider
    /// mounting the file system explicitly as a way to avoid setting this cap.
    /// </summary>
    /// <remarks>
    /// CAP_SYS_NICE is required to allow the ebpf runner to set the nice level of the threads it spawns.
    /// sudo is always executed in non-interactive mode unless interactive is set to true. An optional action can be provided to
    /// notify the user that a password prompt is about to be issued.
    /// </remarks>
    public static bool TrySetEBPFCapabilitiesIfNeeded(string binaryPath, bool interactive, out string failure, Action interactivePromptAction = null)
    {
        if (!BinaryHasEBPFCapabilities(binaryPath))
        {
            // If this is running in ADO, we don't need to check for sudo permissions since the ADO agent is expected to run as root. Sometimes checking whether sudo will prompt for a password
            // is flaky in ADO, so we skip it entirely and avoid failing in that case.
            // If this is not running in ADO, check if sudo will prompt for a password
            if (!IsAdoBuild() && WillSudoPromptForPassword())
            {
                if (interactive)
                {
                    // We are going to prompt for a password and interactive mode is enabled. Notify the user.
                    interactivePromptAction?.Invoke();
                }
                else
                {
                    // We are going to prompt for a password but interactive mode is disabled. This is bound to fail.
                    failure = "Interactive sudo access is required but interactive mode is disabled. If this is a developer build, you can pass /interactive+ to enable it.";
                    return false;
                }
            }

            var setCapUtils = UnixSetCapUtils.CreateSetCap();
            return setCapUtils.SetCapability(binaryPath, interactive, out failure, UnixCapability.CAP_SYS_ADMIN, UnixCapability.CAP_DAC_OVERRIDE, UnixCapability.CAP_SYS_NICE);
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// Returns true if the provided binary contains the required EBPF capabilities
    /// </summary>
    public static bool BinaryHasEBPFCapabilities(string binaryPath)
    {
        var getCapUtils = CreateGetCap();
        return getCapUtils.BinaryContainsCapabilities(binaryPath, UnixCapability.CAP_SYS_ADMIN) &&
               getCapUtils.BinaryContainsCapabilities(binaryPath, UnixCapability.CAP_DAC_OVERRIDE) &&
               getCapUtils.BinaryContainsCapabilities(binaryPath, UnixCapability.CAP_SYS_NICE);
    }
}
