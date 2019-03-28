// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Helper static methods for the tests
    /// </summary>
    public static class TestCacheHelpers
    {
        /// <summary>
        /// Convert a string into a stream
        /// </summary>
        /// <param name="data">String to convert</param>
        /// <returns>An in-memory stream of the string contents in UTF-8 encoding</returns>
        public static Stream AsStream(this string data)
        {
            Contract.Requires(data != null);

            return new MemoryStream(Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Convert a StringBuilder into a stream
        /// </summary>
        /// <param name="data">StringBuilder to convert</param>
        /// <returns>An in-memory stream of the StringBuilder contents in UTF-8 encoding</returns>
        public static Stream AsStream(this StringBuilder data)
        {
            Contract.Requires(data != null);

            return data.ToString().AsStream();
        }

        /// <summary>
        /// Convert a stream into a string
        /// </summary>
        /// <param name="data">Stream to convert</param>
        /// <returns>String containing the contents of the stream read as UTF-8 bytes</returns>
        public static string AsString(this Stream data)
        {
            Contract.Requires(data != null);

            return new StreamReader(data, Encoding.UTF8).ReadToEnd();
        }

        /// <summary>
        /// Compute a hash for our test streams
        /// </summary>
        /// <param name="testStream">The test stream to hash</param>
        /// <returns>The Hash of the contents of the stream</returns>
        /// <remarks>
        /// Our test streams can be rewound so this works as a way to get the hash
        /// </remarks>
        public static Hash AsHash(this Stream testStream)
        {
            Contract.Requires(testStream != null);

            byte[] contents = new byte[testStream.Length];

            // We can only be sure we can do this for our test streams
            testStream.Seek(0, SeekOrigin.Begin);

            int read = testStream.Read(contents, 0, contents.Length);

            testStream.Seek(0, SeekOrigin.Begin);

            var contentHash = ContentHashingUtilities.HashBytes(contents);
            return new Hash(contentHash);
        }

        /// <summary>
        /// Simple helper to xunit assert on failures
        /// </summary>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <typeparam name="TFailure">Failure type</typeparam>
        /// <param name="possible">The input possible</param>
        /// <returns>The result if the result was not a failure</returns>
        public static TResult Success<TResult, TFailure>(in this Possible<TResult, TFailure> possible) where TFailure : Failure
        {
            if (!possible.Succeeded)
            {
                XAssert.Fail("{0}", possible.Failure.Describe());
            }

            return possible.Result;
        }

        /// <summary>
        /// Simple helper to xunit assert on failures
        /// </summary>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <typeparam name="TFailure">Failure type</typeparam>
        /// <param name="taskPossible">The input task of possible</param>
        /// <returns>The task of result if the result was not a failure</returns>
        public static async Task<TResult> SuccessAsync<TResult, TFailure>(this Task<Possible<TResult, TFailure>> taskPossible) where TFailure : Failure
        {
            Contract.Requires(taskPossible != null);

            return (await taskPossible).Success();
        }

        /// <summary>
        /// Simple helper to xunit assert on failures
        /// </summary>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <typeparam name="TFailure">Failure type</typeparam>
        /// <param name="possibles">The input possibles array</param>
        /// <returns>The array of results if the result was not a failure</returns>
        public static TResult[] Success<TResult, TFailure>(this Possible<TResult, TFailure>[] possibles) where TFailure : Failure
        {
            Contract.Requires(possibles != null);

            TResult[] results = new TResult[possibles.Length];
            for (int i = 0; i < possibles.Length; i++)
            {
                results[i] = possibles[i].Success();
            }

            return results;
        }

        /// <summary>
        /// Simple helper to xunit assert on failures
        /// </summary>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <typeparam name="TFailure">Failure type</typeparam>
        /// <param name="possible">The input possible</param>
        /// <param name="format">String format for log message if not successful.</param>
        /// <param name="args">Arguements to format string for Assert logging.</param>
        /// <returns>The result type if the result was not a failure</returns>
        public static TResult Success<TResult, TFailure>(in this Possible<TResult, TFailure> possible, string format, params object[] args) where TFailure : Failure
        {
            Contract.Requires(format != null);

            if (!possible.Succeeded)
            {
                if (args == null)
                {
                    args = new object[] { possible.Failure.Describe() };
                }
                else
                {
                    object[] moreArgs = new object[args.Length + 1];
                    System.Array.Copy(args, moreArgs, args.Length);
                    moreArgs[args.Length] = possible.Failure.Describe();
                    args = moreArgs;
                }

                XAssert.Fail(format, args);
            }

            return possible.Result;
        }

        /// <summary>
        /// Simple helper to xunit assert on failures
        /// </summary>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <typeparam name="TFailure">Failure type</typeparam>
        /// <param name="taskPossible">The input task of possible</param>
        /// <param name="format">String format for log message if not successful.</param>
        /// <param name="args">Arguements to format string for Assert logging.</param>
        /// <returns>The task of result if the result was not a failure</returns>
        public static async Task<TResult> SuccessAsync<TResult, TFailure>(this Task<Possible<TResult, TFailure>> taskPossible, string format, params string[] args) where TFailure : Failure
        {
            Contract.Requires(taskPossible != null);
            Contract.Requires(format != null);

            return (await taskPossible).Success(format, args);
        }

        /// <summary>
        /// Simple helper to xunit assert on failures
        /// </summary>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <typeparam name="TFailure">Failure type</typeparam>
        /// <param name="possibles">The input possibles array</param>
        /// <param name="format">String format for log message if not successful.</param>
        /// <param name="args">Arguements to format string for Assert logging.</param>
        /// <returns>The array of results if the result was not a failure</returns>
        public static TResult[] Success<TResult, TFailure>(this Possible<TResult, TFailure>[] possibles, string format, params object[] args) where TFailure : Failure
        {
            Contract.Requires(possibles != null);
            Contract.Requires(format != null);

            TResult[] results = new TResult[possibles.Length];
            for (int i = 0; i < possibles.Length; i++)
            {
                results[i] = possibles[i].Success(format, args);
            }

            return results;
        }

        /// <summary>
        /// Do a build across an array of PipDefinitions
        /// </summary>
        /// <param name="pips">The array of pips</param>
        /// <param name="session">The ICacheSession to do the build in</param>
        /// <returns>A HashSet of the cache records produced</returns>
        /// <remarks>
        /// This tries to run all of the pips in parallel by starting all async
        /// operations and only after starting them all, waiting for the results.
        /// </remarks>
        public static async Task<HashSet<FullCacheRecord>> BuildAsync(this PipDefinition[] pips, ICacheSession session)
        {
            Contract.Requires(pips != null);
            Contract.Requires(session != null);

            Task<FullCacheRecord>[] recordTasks = new Task<FullCacheRecord>[pips.Length];
            for (int i = 0; i < pips.Length; i++)
            {
                recordTasks[i] = pips[i].BuildAsync(session);
            }

            HashSet<FullCacheRecord> records = new HashSet<FullCacheRecord>();
            foreach (var recordTask in recordTasks)
            {
                records.Add(await recordTask);
            }

            return records;
        }
    }
}
