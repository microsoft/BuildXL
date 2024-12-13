// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Contains helper methods to get information on an object in Unix
/// </summary>
public class UnixObjectFileDumpUtils : UnixUtilsBase
{
    /// <nodoc/>
    protected UnixObjectFileDumpUtils() : base(ObjDumpPath) { }

    /// <summary>
    /// Indicates whether binutils is installed so that objdump can be used by this class.
    /// </summary>
    public static Lazy<bool> IsObjDumpInstalled = new(() => OperatingSystemHelper.IsLinuxOS && File.Exists(ObjDumpPath));

    /// <summary>
    /// Path to objdump utility
    /// </summary>
    private const string ObjDumpPath = "/usr/bin/objdump";

    /// <summary>
    /// The output from the objdump utility that indicates that libc is dynamically linked
    /// </summary>
    private const string ObjDumpLibcOutput = "NEEDED               libc.so.";

    /// <nodoc />
    public static UnixObjectFileDumpUtils CreateObjDump() => new();

    /// <summary>
    /// Returns true if the provided binary statically links libc
    /// </summary>
    /// <param name="binaryPath">Path for executable to be tested.</param>
    /// <returns>True if the binary is statically linked, false if not.</returns>
    public bool IsBinaryStaticallyLinked(string binaryPath) => 
        CheckConditionAgainstStandardOutput(binaryPath, $"-p {binaryPath}", (stdout) => !stdout.Contains(ObjDumpLibcOutput));
}
