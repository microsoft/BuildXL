using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BuildXL;
using BuildXL.Engine;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.Bxl
{
    public class ServerDeploymentTests : TemporaryStorageTestBase
    {
        [Fact]
        public void TestMissingManifestDirectory()
        {
            string deploymentDir;
            XAssert.IsTrue(ServerDeployment.IsServerDeploymentOutOfSync(TemporaryDirectory, null, out deploymentDir));
        }

        [Fact]
        public void TestMissingManifestFile()
        {
            string manifestPath = Path.Combine(TemporaryDirectory, AppDeployment.DeploymentManifestFileName);

            File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(manifestPath), AppDeployment.DeploymentManifestFileName),
                    AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));

            var appDeployment = AppDeployment.ReadDeploymentManifest(
                Path.GetDirectoryName(manifestPath),
                AppDeployment.DeploymentManifestFileName,
                skipManifestCheckTestHook: true);

            string deploymentDir = ServerDeployment.ComputeDeploymentDir(TemporaryDirectory);
            Directory.CreateDirectory(deploymentDir);

            XAssert.IsTrue(ServerDeployment.IsServerDeploymentOutOfSync(TemporaryDirectory, appDeployment, out deploymentDir));
        }

    }
}
