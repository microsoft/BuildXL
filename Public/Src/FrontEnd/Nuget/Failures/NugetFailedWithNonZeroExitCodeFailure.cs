// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;
namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Nuget.exe failed with non-zero exit code.
    /// </summary>
    public sealed class NugetFailedWithNonZeroExitCodeFailure : NugetFailure
    {
        private readonly INugetPackage m_package;
        private readonly int m_exitCode;
        private readonly string m_stdOut;
        private readonly string m_stdErr;

        /// <nodoc />
        public NugetFailedWithNonZeroExitCodeFailure(INugetPackage package, int exitCode, string stdOut, string stdErr)
            : base(FailureType.PackageNotFound)
        {
            m_package = package;
            m_exitCode = exitCode;

            m_stdOut = stdOut?.Trim();
            m_stdErr = stdErr?.Trim();
        }

        /// <inheritdoc />
        public override string Describe()
        {
            var separator = !string.IsNullOrEmpty(m_stdOut) && !string.IsNullOrEmpty(m_stdErr) ? Environment.NewLine : string.Empty;
            var output = I($"{m_stdOut}{separator}{m_stdErr}");
            return I($"Package nuget://{m_package.Id}/{m_package.Version} could not be restored because nuget.exe failed with exit code '{m_exitCode}'. \r\nTools output:\r\n{output}\r\nSee the buildxl log for more details.");
        }
    }
}
