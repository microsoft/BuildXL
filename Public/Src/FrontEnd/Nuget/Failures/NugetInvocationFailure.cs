// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.Core.FormattableStringEx;
namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Nuget invocation failed.
    /// </summary>
    public sealed class NugetInvocationFailure : NugetFailure
    {
        private readonly INugetPackage m_package;
        private readonly string m_message;

        /// <nodoc />
        public NugetInvocationFailure(INugetPackage package, string message)
            : base(FailureType.PackageNotFound)
        {
            m_package = package;
            
            m_message = message?.Trim();
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return I($"Package nuget://{m_package.Id}/{m_package.Version} could not be restored. {m_message}.");
        }
    }
}
