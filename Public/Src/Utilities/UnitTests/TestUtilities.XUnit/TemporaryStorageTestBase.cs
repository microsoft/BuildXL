// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Utilities.Configuration;
using Xunit.Abstractions;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Base class for test containers that need to write temporary files.
    /// This class inherits <see cref="Test" />, and so BuildXL events
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
                EngineEnvironmentSettings.SkipExtraneousPins.Value = true;
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

                m_tempBase = Path.GetFullPath(Path.Combine(GetTempDir(), testClassName, testMethodNumber.ToString(CultureInfo.InvariantCulture)));

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
        protected string GetFullPath(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(m_tempBase, path);
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

        /// <summary>
        /// Returns the subst source and subst target under which the test is running.
        /// </summary>
        /// <param name="substSource">Subst source.</param>
        /// <param name="substTarget">Subst target.</param>
        /// <returns>True if the test is running under subst.</returns>
        /// <remarks>
        /// Unit tests are run during BuildXL selfhost. The latter most likely runs under subst, which
        /// in turn, essentially, makes unit tests run under subst as well. If a unit test executes a pip that calls
        /// a function like <code>GetFinalPathNameByHandle</code>, then, without directory translation,
        /// instead of returning the substed path, the function returns the real path. This real path
        /// can cause dependency access violation for the pip because the file access manifest for the pip may only
        /// contain substed paths.
        ///
        /// For example if <code>B:\</code> is the subst target and <code>E:\Repos\BuildXL</code> is the subst source. All paths,
        /// based on <see cref="TemporaryDirectory"/>, that are specified for a pip executed by a unit test have <code>B:\</code> as the root.
        /// If the pip calls <code>GetFinalPathNameByHandle</code> on <code>B:\file</code>, then the resulting path will be
        /// <code>E:\Repos\BuildXL\file</code>. Further, if the pip reads <code>E:\Repos\BuildXL\file</code>, then there will be
        /// read violation because the path <code>E:\Repos\BuildXL\file</code> is not present anywhere in the manifest.
        ///
        /// This method calls <code>GetFinalPathNameByHandle</code> on <see cref="TemporaryDirectory"/> to infer
        /// <paramref name="substSource"/> and <paramref name="substTarget"/>. Since <code>GetFinalPathNameByHandle</code>
        /// is not implemented on non-Windows (and there is currently no subst on non-Windows), this method simply returns
        /// false on non-Windows.
        ///
        /// CAUTION:
        /// This method only works if the unit test itself is called in undetoured environment. Or, in terms of
        /// BuildXL Script for selfhost, the unit test itself is executed using <code>Sdk.Managed.Testing.XUnit.UnsafeUnDetoured</code> framework.
        /// Recall that this method calls <code>GetFinalPathNameByHandle</code> internally. If it is called in a detoured environment,
        /// then the detoured version of <code>GetFinalPathNameByHandle</code> will be called, and that detoured version takes into account
        /// the subst specified by the selfhost via the directory translation. Thus, for the above example, calling the detoured version on
        /// <code>B:\file</code> returns <code>B:\file</code> because the directory translation translated <code>E:\Repos\BuildXL\file</code>
        /// to <code>B:\file</code>.
        ///
        /// BuildXL unit tests that involve running Detours, e.g., integration tests and detours tests, are all run in undetoured environment.
        /// </remarks>
        protected bool TryGetSubstSourceAndTarget(out string substSource, out string substTarget)
        {
            var isSubstUsed = FileUtilities.TryGetSubstSourceAndTarget(TemporaryDirectory, out substSource, out substTarget, out var errorMessage);
            XAssert.IsFalse(!isSubstUsed && errorMessage != null, errorMessage);

            return isSubstUsed;
        }

        /// <summary>
        /// Deletes the symlink at location <paramref name="link"/>.  Return value indicates success.
        /// </summary>
        protected bool TryDeleteSymlink(string link)
        {
            try
            {
                var fullPath = GetFullPath(link);
                FileUtilities.DeleteFile(fullPath);
                return true;
            }
            catch
            {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                return false;
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        }
    }
}
