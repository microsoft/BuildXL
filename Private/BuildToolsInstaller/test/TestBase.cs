// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Runtime.CompilerServices;

namespace BuildToolsInstaller.Tests
{
    public abstract class TestBase
    {
        protected string GetTempPathForTest(string suffix = "", [CallerMemberName] string caller = "")
        {
            var path = Path.Combine(Path.GetTempPath(), caller, suffix);
            if (Path.Exists(path))
            {
                Directory.Delete(path, true);
            }
            return path;
        }
    }
}