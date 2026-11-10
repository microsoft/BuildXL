// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Ensures that the filesystem used to pin eBPF maps is mounted.
/// </summary>
public sealed class UnixBpfUtils : UnixUtilsBase
{
    private const string MountPath = "/usr/bin/mount";
    private const string BpfMountPoint = "/sys/fs/bpf";
    private const string MountInfoPath = "/proc/self/mountinfo";

    private UnixBpfUtils() : base(MountPath) { }

    /// <summary>
    /// Mounts bpffs if needed, allowing sudo to prompt only in interactive mode.
    /// Non-interactive builds require bpffs to have already been mounted.
    /// </summary>
    public static bool TryMountBpfFileSystemIfNeeded(bool interactive, out string failure, Action interactivePromptAction = null)
    {
        const string Instructions = "Run 'sudo mount -t bpf bpf /sys/fs/bpf' manually or pass /interactive+ to allow BuildXL to mount it.";

        if (!OperatingSystemHelper.IsLinuxOS)
        {
            failure = "Mounting the BPF filesystem is only supported on Linux.";
            return false;
        }

        try
        {
            if (IsBpfFileSystemMounted(File.ReadLines(MountInfoPath)))
            {
                failure = string.Empty;
                return true;
            }

            if (!interactive)
            {
                failure = $"The BPF filesystem is not mounted at {BpfMountPoint}. {Instructions}";
                return false;
            }

            if (WillSudoPromptForPassword())
            {
                interactivePromptAction?.Invoke();
            }

            var mountUtils = new UnixBpfUtils();
            if (!mountUtils.CheckConditionAgainstStandardOutput(
                MountPath,
                $"-t bpf bpf {BpfMountPoint}",
                condition: _ => true,
                standardError: out failure,
                runAsSudo: true,
                interactive: true))
            {
                failure = $"Could not mount the BPF filesystem at {BpfMountPoint}: {failure}. {Instructions}";
                return false;
            }

            if (!IsBpfFileSystemMounted(File.ReadLines(MountInfoPath)))
            {
                failure = $"The mount command succeeded but the BPF filesystem is not mounted at {BpfMountPoint}. {Instructions}";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception)
        {
            failure = $"Could not prepare the BPF filesystem at {BpfMountPoint}: {ex.Message}. {Instructions}";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    internal static bool IsBpfFileSystemMounted(IEnumerable<string> mountInfo)
    {
        foreach (string entry in mountInfo)
        {
            string[] fields = entry.Split(' ');
            int separator = Array.IndexOf(fields, "-");
            if (separator >= 6
                && separator + 3 < fields.Length
                && fields[4] == BpfMountPoint
                && fields[separator + 1] == "bpf")
            {
                return true;
            }
        }

        return false;
    }
}
