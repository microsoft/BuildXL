// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    [Trait("Category", "SandboxedLinuxUtilsTest")]
    [TestClassIfSupported(requiresUnixBasedOperatingSystem: true)]
    public sealed class SandboxedLinuxUtilsTest
    {
        private const string LibBxlUtils = "libBxlUtils";

        // CODESYNC: Public\Src\Sandbox\Linux\utils.c
        private const char EnvSeparator = ';';

        [DllImport(LibBxlUtils, EntryPoint = "add_value_to_env_for_test")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool AddValueToEnv(
            [MarshalAs(UnmanagedType.LPStr)] string envKvp,
            [MarshalAs(UnmanagedType.LPStr)] string valueToAdd,
            [MarshalAs(UnmanagedType.LPStr)] string envPrefix,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder buf);

        [DllImport(LibBxlUtils, EntryPoint = "ensure_1_path_included_in_env_for_test")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool Ensure1PathIncludedInEnv(string[] env,
            [MarshalAs(UnmanagedType.LPStr)] string envPrefix,
            [MarshalAs(UnmanagedType.LPStr)] string path,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder buf);

        [DllImport(LibBxlUtils, EntryPoint = "ensure_2_paths_included_in_env_for_test")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool Ensure2PathsIncludedInEnv(string[] env,
            [MarshalAs(UnmanagedType.LPStr)] string envPrefix,
            [MarshalAs(UnmanagedType.LPStr)] string path0,
            [MarshalAs(UnmanagedType.LPStr)] string path1,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder buf);

        [DllImport(LibBxlUtils, EntryPoint = "ensure_env_value_for_test")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool EnsureEnvValue(string[] env,
            [MarshalAs(UnmanagedType.LPStr)] string Name,
            [MarshalAs(UnmanagedType.LPStr)] string value,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder buf);

        [Theory]
        // no 'valueToAdd' specified --> no change
        [InlineData("")]
        [InlineData("PATH=a:b:c d")]
        [InlineData("LD_DEBUG=libs")]
        [InlineData("ENV_TO_UPDATE=")]
        [InlineData("ENV_TO_UPDATE=/other/lib")]
        // 'valueToAdd' specified but it either already in 'source' or 'source' does not start with ENV_TO_UPDATE= --> no adding
        [InlineData("SOME_VAR=/my/lib", "/my/lib")]
        [InlineData("ENV_TO_UPDATE=/before:/my/lib:/after", "/my/lib")]
        [InlineData("ENV_TO_UPDATE=/before:/my/lib", "/my/lib")]
        [InlineData("ENV_TO_UPDATE=/my/lib:/after:", "/my/lib")]
        [InlineData("ENV_TO_UPDATE=::", "")]
        // some Adding happening
        [InlineData("ENV_TO_UPDATE=", "/my/lib", "ENV_TO_UPDATE=/my/lib", false)]
        [InlineData("ENV_TO_UPDATE=/before", "/my/lib", "ENV_TO_UPDATE=/before:/my/lib", false)]
        [InlineData("ENV_TO_UPDATE=/before:", "/my/lib", "ENV_TO_UPDATE=/before:/my/lib", false)]
        [InlineData("ENV_TO_UPDATE= /my/lib", "/my/lib", "ENV_TO_UPDATE= /my/lib:/my/lib", false)]
        public void TestAddValueToEnv(string source, string valueToAdd = "", string expected = null, bool shouldBeSameEnvp = true)
        {
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return;
            }

            expected = expected ?? source;

            int n = source.Length + valueToAdd.Length + 1;
            var buffer = Enumerable
                .Range(0, n)
                .Aggregate(new StringBuilder(n), (acc, i) => acc.Append('*')); // add some bogus values to buffer to correctly test that a terminating \0 is set.

            var sameEvnp = AddValueToEnv(source, valueToAdd, "ENV_TO_UPDATE=", buffer);
            XAssert.AreEqual(shouldBeSameEnvp, sameEvnp);
            XAssert.AreEqual(expected, buffer.ToString());
        }

        [Theory]
        // envp contains ENV_TO_UPDATE, ENV_TO_UPDATE contains "/my/lib"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/after", null }, "ENV_TO_UPDATE=", new string[1] { "/my/lib" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/after" })]
        // envp contains ENV_TO_UPDATE, ENV_TO_UPDATE doesn't contain "/my/lib"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before", null }, "ENV_TO_UPDATE=", new string[1] { "/my/lib" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib" }, false)]
        // envp doesn't contain ENV_TO_UPDATE
        [InlineData(new string[3] { "HOME=/User/home", "PATH=a:b:c d", null }, "ENV_TO_UPDATE=", new string[1] { "/my/lib" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/my/lib" }, false)]
        // envp doesn't contain ENV_TO_UPDATE
        [InlineData(new string[3] { "HOME=/User/home", "PATH=a:b:c d", null }, "ENV_TO_UPDATE=", new string[2] { "/my/lib", "/my/lib1" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/my/lib:/my/lib1" }, false)]
        // envp contains ENV_TO_UPDATE, ENV_TO_UPDATE contains "/my/lib" and "/my/lib1"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/my/lib1:/after", null }, "ENV_TO_UPDATE=", new string[2] { "/my/lib", "/my/lib1" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/my/lib1:/after" })]
        // envp contains ENV_TO_UPDATE, ENV_TO_UPDATE contains "/my/lib" but not "/my/lib1"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/after", null }, "ENV_TO_UPDATE=", new string[2] { "/my/lib", "/my/lib1" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/after:/my/lib1" }, false)]
        // envp contains ENV_TO_UPDATE, ENV_TO_UPDATE contains neither "/my/lib" nor "/my/lib1"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before", null }, "ENV_TO_UPDATE=", new string[2] { "/my/lib", "/my/lib1" }, new string[3] { "HOME=/User/home", "PATH=a:b:c d", "ENV_TO_UPDATE=/before:/my/lib:/my/lib1" }, false)]
        // envp is null
        [InlineData(null, "ENV_TO_UPDATE=", new string[1] { "/my/lib" }, new string[1] { "ENV_TO_UPDATE=/my/lib" }, false)]
        public void TestEnsurePathsIncludedInEnv(string[] envp, string envPrefix, string[] paths, string[] expectedEnvp, bool shouldBeSameEnvp = true)
        {
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return;
            }

            XAssert.IsTrue(paths.Length <= 2, "Add a EnsureXPathsIncludedInEnv function if you need to test paths has X(X>2) elements.");

            int pathsLenInTotal = 0;
            foreach (var path in paths)
            {
                pathsLenInTotal += path.Length + 1;
            }

            // allocate large enough buffers for each env var
            var buffer = new StringBuilder(capacity: 1000);

            bool sameEvnp = false;
            if (paths.Length == 1)
            {
                sameEvnp = Ensure1PathIncludedInEnv(envp, envPrefix, paths[0], buffer);
            }
            else if (paths.Length == 2)
            {
                sameEvnp = Ensure2PathsIncludedInEnv(envp, envPrefix, paths[0], paths[1], buffer);
            }

            XAssert.AreEqual(shouldBeSameEnvp, sameEvnp);

            var newEnvp = buffer.ToString().Split(EnvSeparator);
            XAssert.IsTrue(newEnvp.SequenceEqual(expectedEnvp));      
        }

        [Theory]
        // Case1: envp contains __BUILDXL_FAM_PATH, __BUILDXL_FAM_PATH equals to "/my/fam"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "__BUILDXL_FAM_PATH=/my/fam", null }, "__BUILDXL_FAM_PATH", "/my/fam")]
        // Case2: envp contains __BUILDXL_FAM_PATH, __BUILDXL_FAM_PATH doesn't equal to "/my/fam"
        [InlineData(new string[4] { "HOME=/User/home", "PATH=a:b:c d", "__BUILDXL_FAM_PATH=/before", null }, "__BUILDXL_FAM_PATH", "/my/fam", false)]
        // Case3: envp doesn't contain __BUILDXL_FAM_PATH
        [InlineData(new string[3] { "HOME=/User/home", "PATH=a:b:c d", null }, "__BUILDXL_FAM_PATH", "/my/fam", false)]
        public void TestEnsureEnvValue(string[] envp, string name, string value, bool shouldBeSameEnvp = true)
        {
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return;
            }

            // allocate large enough buffers for each env var
            var buffer = new StringBuilder(capacity: 1000);

            bool sameEvnp = EnsureEnvValue(envp, name, value, buffer);
            XAssert.AreEqual(shouldBeSameEnvp, sameEvnp);

            var expected = new string[3] { "HOME=/User/home", "PATH=a:b:c d", "__BUILDXL_FAM_PATH=/my/fam" };
            var newEnvp = buffer.ToString().Split(EnvSeparator);
            XAssert.IsTrue(newEnvp.SequenceEqual(expected));
        }

    }
}
