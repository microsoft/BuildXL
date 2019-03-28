// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    public abstract class ProcessReportingTestBase : SchedulerTestBase
    {
        protected ProcessReportingTestBase(ITestOutputHelper output)
            : base(output)
        {
        }

        protected static void AssertEqual(ReportedFileAccess expected, ReportedFileAccess actual)
        {
            AssertEqual(expected, actual,
               r => r.Operation,
               r => r.Process.Path,
               r => r.Process.ProcessId,
               r => r.RequestedAccess,
               r => r.Status,
               r => r.ExplicitlyReported,
               r => r.Error,
               r => r.Usn,
               r => r.DesiredAccess,
               r => r.ShareMode,
               r => r.CreationDisposition,
               r => r.FlagsAndAttributes,
               r => r.ManifestPath,
               r => r.Path,
               r => r.EnumeratePattern
            );
        }

        protected static void AssertSequenceEqual<T>(IReadOnlyCollection<T> expectedList, IReadOnlyCollection<T> actualList, params Action<T, T>[] equalityVerifiers)
        {
            Assert.Equal(expectedList.Count, actualList.Count);

            var zippedLists = expectedList.Zip(actualList, (x, y) => (x, y));

            foreach (var entry in zippedLists)
            {
                var expected = entry.Item1;
                var actual = entry.Item2;

                foreach (var equalityVerifier in equalityVerifiers)
                {
                    equalityVerifier(expected, actual);
                }
            }
        }

        protected static void AssertEqual<T>(T expected, T actual, params Func<T, object>[] getters)
        {
            int i = 0;
            foreach (var getter in getters)
            {
                var expectedValue = getter(expected);
                var actualValue = getter(expected);
                XAssert.AreEqual(expectedValue, actualValue, I($"Unequality value for getter ({i})"));
                i++;
            }
        }

        protected ReportedFileAccess CreateRandomReportedFileAccess(ReportedProcess process = null)
        {
            Random r = new Random(123);

            var manifestFile = CreateOutputFile();
            return new ReportedFileAccess(
                RandomEnum<ReportedFileOperation>(r),
                process ?? new ReportedProcess((uint)r.Next(), X("/x/processPath") + r.Next(), X("/x/processPath") + r.Next() + " args1 args2"),
                RandomEnum<RequestedAccess>(r),
                RandomEnum<FileAccessStatus>(r),
                true,
                (uint)r.Next(),
                new Usn((ulong)r.Next()),
                RandomEnum<DesiredAccess>(r),
                RandomEnum<ShareMode>(r),
                RandomEnum<CreationDisposition>(r),
                RandomEnum<FlagsAndAttributes>(r),
                manifestFile.Path,
                X("/j/accessPath") + r.Next(),
                null);
        }

        protected ReportedProcess CreateRandomReportedProcess()
        {
            Random r = new Random(123);

            return new ReportedProcess((uint)r.Next(), X("/x/processPath") + r.Next(), X("/x/processPath") + r.Next() + " args1 args2");
        }

        protected TEnum RandomEnum<TEnum>(Random r) where TEnum : struct
        {
            TEnum[] values = (TEnum[])Enum.GetValues(typeof(TEnum));
            return values[r.Next(0, values.Length)];
        }
    }
}
