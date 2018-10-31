// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities.Diagnostics;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Helper class that provide fail-fast behavior for contract violations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Currently test suite is running under MSTest that has weird issues with exception serialization.
    /// During contract validation ContractException would be thrown that will hang MSTest console runner.
    /// To avoid this issue fail fast approach should be used.
    ///
    /// Before killing the current process, there is a delay to wait for the test runner to run another test or
    /// cleanup the test to indicate that the test runner is still active.
    /// </para>
    /// <para>
    /// If contract violation occurred in the testing code base, entire process would be killed.
    /// To debug the failure local dumps should be saved.
    /// To do this, add following registry keys on your dev machine:
    /// </para>
    /// <para>
    /// Location: HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps
    /// Values:
    /// DumpCount  |   REG_DWORD   | 32 - number of rolling dumps
    /// DumpFolder | REG_EXPAND_SZ | %LOCALAPPDATA%\CrashDumps - folder for local dumps
    /// DumpType   |   REG_DWORD   | 2 - Full dumps (1 for minimal).
    /// </para>
    /// <para>
    /// When the test will fail you would be able to open the proc dump and see the root cause of the failure.
    /// </para>
    /// <para>
    /// To use this feature, add <see cref="RegisterForFailFastContractViolations"/> method call to static constructor
    /// (or to the test base class static constructor).
    /// </para>
    /// </remarks>
    public static class FailFastContractChecker
    {
        private static readonly TimeSpan s_failFastWaitTime = TimeSpan.FromSeconds(5);
        private static readonly object s_syncLock = new object();
        private static bool s_testRunnerActiveSinceContractFailure = false;

        /// <summary>
        /// Registers for <see cref="Contract.ContractFailed"/> event and call FailFast in the handler.
        /// </summary>
        public static void RegisterForFailFastContractViolations()
        {
            Contract.ContractFailed += (sender, args) =>
            {
                Console.WriteLine("Contract exception occurred. Waiting for {0} seconds then killing the app in fail fast way!", s_failFastWaitTime.TotalSeconds);
                Console.WriteLine("Condition: " + args.Condition);
                Console.WriteLine("Message: " + args.Message);

                lock (s_syncLock)
                {
                    s_testRunnerActiveSinceContractFailure = false;

                    Task.Run(async () =>
                        {
                            // Wait two seconds then fail if test runner has not reported activity in
                            // that time period.
                            await Task.Delay(s_failFastWaitTime);

                            lock (s_syncLock)
                            {
                                if (!s_testRunnerActiveSinceContractFailure)
                                {
                                    var msg = "MS Test can't deal with exception serialization. Killing the app in fail fast way!";
                                    ExceptionHandling.OnFatalException(null, msg);
                                }
                            }
                        });
                }
            };
        }

        /// <summary>
        /// Indicates that the test runner is still active and fail fast behavior is not needed.
        /// </summary>
        public static void NotifyTestRunnerActive()
        {
            lock (s_syncLock)
            {
                s_testRunnerActiveSinceContractFailure = true;
            }
        }
    }
}
