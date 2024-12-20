// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Inherit from this class to reuse some test utilities
    /// </summary>
    public abstract class TestBase
    {
        protected string TemporaryDirectory => m_tempDir;
        private readonly string m_tempDir;
        private static int s_tempIndex = 0;

        public TestBase()
        {

            string testClassName = GetType().Name;
            if (testClassName.Length > 1024)
            {
                testClassName = testClassName.Substring(0, 1024);
            }

            var index = Interlocked.Increment(ref s_tempIndex);
            m_tempDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), testClassName, index.ToString()));

            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, true);
            }

            Directory.CreateDirectory(m_tempDir);
        }

        /// <nodoc/>
        protected static void AssertContains(string container, params string[] elems)
        {
            foreach (var elem in elems)
            {
                if (!container.Contains(elem))
                {
                    Assert.Fail($"Substring '{elem}' not found in string '{container}'");
                }
            }
        }

        /// <nodoc/>
        protected static void AssertNotContains(string container, params string[] elems)
        {
            foreach (var elem in elems)
            {
                if (container.Contains(elem))
                {
                    Assert.Fail($"Substring '{elem}' found in string '{container}'");
                }
            }
        }

        /// <nodoc/>
        protected static void AssertContains<T>(IEnumerable<T> container, params T[] elems)
        {
            foreach (var elem in elems)
            {
                if (!container.Contains(elem))
                {
                    Assert.Fail($"Element '{elem}' not found in container: {RenderContainer(container)}");
                }
            }
        }

        /// <nodoc/>
        protected static void AssertNotContains<T>(IEnumerable<T> container, params T[] elems)
        {
            foreach (var elem in elems)
            {
                if (container.Contains(elem))
                {
                    Assert.Fail($"Element '{elem}' found in container: {RenderContainer(container)}");
                }
            }
        }

        private static string RenderContainer<T>(IEnumerable<T> container)
        {
            string nl = Environment.NewLine;
            var elems = container.Select(e => $"  '{e}'");
            return $"[{nl}{string.Join("," + nl, elems)}{nl}]";
        }
    }
}