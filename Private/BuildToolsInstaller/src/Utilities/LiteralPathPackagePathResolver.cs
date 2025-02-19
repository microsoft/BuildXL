// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace BuildToolsInstaller.Utilities
{
    internal class LiteralPathPackagePathResolver : PackagePathResolver
    {
        private readonly string m_installDirectory;
        public LiteralPathPackagePathResolver(string installDirectory, bool useSideBySidePaths = true) : base(installDirectory, useSideBySidePaths)
        {
            m_installDirectory = installDirectory;
        }

        public override string GetInstallPath(PackageIdentity packageIdentity)
        {
            return m_installDirectory;
        }
    }
}
