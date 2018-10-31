// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Xunit.Abstractions;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Base class for test containers that need to write temporary files.
    /// This class inherits <see cref="XunitBuildXLTest" />, and so BuildXL events
    /// are additionally directed to the console for capture.
    /// </summary>
    /// <remarks>
    /// On construction, a <see cref="TemporaryDirectory" /> is created to house all needed files.
    /// It is guaranteed to be initially empty. The temporary directory may be cleaned up upon test completion.
    /// Note that the current working directory is left unchanged,
    /// and so full paths should be used when accessing or referring to particular files.
    ///
    /// No two tests inheriting from <see cref="TemporaryStorageTestBase"/> may be run at the same time in
    /// the same process
    /// </remarks>
    public abstract class TemporaryStorageTestBase : XunitBuildXLTest
    {
        private readonly string m_tempBase;
        private static readonly Dictionary<string, int> s_temporaryNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Temporary directory to which the test can write files.
        /// </summary>
        /// <seealso cref="WriteFile" />
        public string TemporaryDirectory
        {
            get
            {
                Contract.Assume(m_tempBase != null, "TestInitialize not yet called; can't access TemporaryDirectory");
                return m_tempBase;
            }
        }

        /// <summary>
        /// Can be passed into <see cref="FileUtilities"/> functions to enable move-deleting directories and files.
        /// This is particularly useful for reducing directory deletion errors in test classes that repeatedly
        /// create and delete the same directories between tests.
        /// </summary>
        public TestMoveDeleteCleaner MoveDeleteCleaner { get; private set; }

        /// <summary>
        /// Please use the overload that takes a <see cref="ITestOutputHelper"/>
        /// </summary>
        protected TemporaryStorageTestBase()
            : this(null)
        {
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Usage", "CA2214")]
        protected TemporaryStorageTestBase(ITestOutputHelper output)
            : base(output)
        {
            try
            {
                string testClassName = LimitPathLength(GetType().Name);

                // In Xunit we can't really get the test method name. So we just pick an increasing integer.
                int testMethodNumber = 0;
                lock (s_temporaryNames)
                {
                    if (s_temporaryNames.TryGetValue(testClassName, out testMethodNumber))
                    {
                        testMethodNumber++;
                        s_temporaryNames[testClassName] = testMethodNumber;
                    }
                    else
                    {
                        s_temporaryNames.Add(testClassName, testMethodNumber);
                    }
                }

                // C# identifiers are valid path atoms. See IsValidPathAtom and  http://msdn.microsoft.com/en-us/library/aa664670(v=vs.71).aspx
                Contract.Assume(testClassName != null && PathAtom.Validate((StringSegment)testClassName));

                m_tempBase = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("TEMP"), testClassName, testMethodNumber.ToString(CultureInfo.InvariantCulture)));

                if (Directory.Exists(m_tempBase))
                {
                    FileUtilities.DeleteDirectoryContents(m_tempBase);
                }

                Directory.CreateDirectory(m_tempBase);

                string moveDeleteDirectory = Path.Combine(m_tempBase, TestMoveDeleteCleaner.MoveDeleteDirectoryName);
                MoveDeleteCleaner = new TestMoveDeleteCleaner(moveDeleteDirectory);
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }

        private static string LimitPathLength(string s)
        {
            Contract.Requires(s != null);

            // arbitrary limit; motivation is to keep paths short enough to not hit 260 character overall path limit
            // (this actually happened, not too surprising since test names can be very long)
            // caller should be aware that uniqueness is not guaranteed
            return s.Length > 20 ? s.Substring(0, 12) + s.GetHashCode().ToString("X", CultureInfo.InvariantCulture) : s;
        }

        /// <summary>
        /// Writes a temporary file under <see cref="TemporaryDirectory" /> root with the given string contents.
        /// Parent directories are created as needed. The contents are encoded as UTF-8.
        /// </summary>
        protected string WriteFile(string relativePath, string contents)
        {
            Contract.Requires(!string.IsNullOrEmpty(relativePath));
            Contract.Requires(contents != null);

            string fullPath = GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(contents));

            return fullPath;
        }

        /// <summary>
        /// Returns an absolute path to the given file under <see cref="TemporaryDirectory" />.
        /// </summary>
        protected string GetFullPath(string relativePath)
        {
            Contract.Requires(!string.IsNullOrEmpty(relativePath));

            Contract.Assume(!Path.IsPathRooted(relativePath));
            return Path.Combine(m_tempBase, relativePath);
        }

        /// <summary>
        /// Returns an absolute path to the given file under <see cref="TemporaryDirectory" />.
        /// </summary>
        protected AbsolutePath GetFullPath(PathTable pathTable, string relativePath)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(!string.IsNullOrEmpty(relativePath));

            return AbsolutePath.Create(pathTable, GetFullPath(relativePath));
        }

        /// <summary>
        /// Helper to get where the test files are deployed
        /// </summary>
        protected string GetTestExecutionLocation()
        {
            return Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetCallingAssembly()));
        }
    }
}
