// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Debugger;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class DsDebuggerTest : DsTest
    {
        public const int EvalTaskTimeoutMillis = 300000; // 5 minutes

        private static readonly Regex s_breakpointRegex = new Regex(@"<<\s*breakpoint(:(?<name>[\d]+))?\s*>>");

        protected MockDebugger Debugger { get; }

        protected override int DegreeOfParallelism => 20;

        // TODO: Move these to virtual filesystem. There is something funny with line numbers when usng the virtual filesystem.
        public DsDebuggerTest(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
            Debugger = new MockService(FrontEndContext.PathTable, LoggingContext).Debugger;
        }

        protected override IDecorator<EvaluationResult> DecoratorForEvaluation => Debugger.Decorator;

        protected TestResult DebugSpec(string spec, string[] expressions, Func<ISource, Task> debuggerTest, int evalTaskTimeoutMillis = EvalTaskTimeoutMillis)
        {
            var testWriter = CreateTestWriter();
            testWriter.ConfigWriter.AddBuildSpec(MainSpecRelativePath, spec);
            return Debug(debuggerTest, testWriter, MainSpecRelativePath, expressions, null, evalTaskTimeoutMillis);
        }

        protected TestResult Debug(Func<ISource, Task> debuggerTest, DsTestWriter testWriter, string specRelativePath, string[] expressions, string qualifier = null, int evalTaskTimeoutMillis = EvalTaskTimeoutMillis)
        {
            var sourceBreakPoints = ComputeSourceBreakpoints(testWriter);
            var mainSource = GetSource(testWriter, specRelativePath);

            var evalTask = Task
                .Run(() =>
                {
                    var ans = Evaluate(testWriter, specRelativePath, expressions, qualifier: qualifier, parseOnly: false, isDebugged: true);
                    if (ans.ErrorCount > 0)
                    {
                        Output?.WriteLine("Errors during evaluation:\r\n" + string.Join(Environment.NewLine, ans.Diagnostics));
                    }

                    Debugger.ShutDown();
                    return ans;
                });

            var debuggerTestTask = Task
                .Run(async () =>
                {
                    Debugger.SendRequest(new InitializeCommand("DScript", true, true, "path"));
                    await Debugger.ReceiveEvent<IInitializedEvent>();

                    foreach (var sourceBreakPoint in sourceBreakPoints)
                    {
                        Debugger.SendRequest(new SetBreakpointsCommand(sourceBreakPoint.Item1, sourceBreakPoint.Item2));
                    }

                    Debugger.SendRequest(new AttachCommand());
                    await debuggerTest(mainSource);
                })
                .ContinueWith(prevTask =>
                {
                    // if failed --> shut down debugger (to ensure liveness), rethrow exception
                    if (prevTask.IsFaulted)
                    {
                        Debugger.ShutDown();
                        throw prevTask.Exception.InnerException;
                    }

                    // if didn't fail AND evaluation has stopped threads --> shut down debugger, throw EvaluationBlockedUponTestCompletionException
                    if (!prevTask.IsFaulted && Debugger.State.GetStoppedThreadsClone().Any())
                    {
                        Debugger.ShutDown();
                        string message = "DScript evaluation has stopped threads upon debugger test completion.";
                        if (prevTask.IsFaulted)
                        {
                            message += " Failure: " + prevTask.Exception;
                        }

                        throw new EvaluationBlockedUponTestCompletionException(message);
                    }

                    // if didn't fail AND evaluation doesn't complete within 'evalTaskTimeoutMillis' --> throw EvaluationBlockedUponTestCompletionException
                    if (!prevTask.IsFaulted && !evalTask.Wait(evalTaskTimeoutMillis))
                    {
                        Debugger.ShutDown();
                        throw new EvaluationBlockedUponTestCompletionException("DScript evaluation timed out after debugger test completed.");
                    }
                });

            Task.WhenAll(evalTask, debuggerTestTask).Wait();
            return evalTask.Result;
        }

        /// <summary>
        /// Set a new list breakpoints in the given source file. Overwriting any previously set breakpoints.
        /// </summary>
        protected void SetBreakpoints(ISource mainSource, int[] lineNums)
        {
            var breakpoints = lineNums.Select(lineNum => new SourceBreakpoint(lineNum, column: null, condition: null)).ToArray();
            Debugger.SendRequest(new SetBreakpointsCommand(mainSource, breakpoints));
        }

        private List<Tuple<Source, SourceBreakpoint[]>> ComputeSourceBreakpoints(DsTestWriter testWriter)
        {
            var list = new List<Tuple<Source, SourceBreakpoint[]>>();
            var files = testWriter.GetAllFiles();

            foreach (var file in files)
            {
                var breakpoints = file.Item2
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                    .Select((str, idx) => s_breakpointRegex.Match(str).Success
                        ? new SourceBreakpoint(idx + 1, column: null, condition: null)
                        : null)
                    .Where(b => b != null)
                    .ToArray();

                if (breakpoints.Length > 0)
                {
                    list.Add(Tuple.Create(GetSource(testWriter, file.Item1), breakpoints));
                }
            }

            return list;
        }

        private Source GetSource(DsTestWriter testWriter, string specRelativePath)
        {
            string writerRoot = testWriter.RootPath != null ? Path.Combine(TestRoot, testWriter.RootPath) : TestRoot;

            var pathTable = FrontEndContext.PathTable;

            // We do round trip in path conversion to get canonical representation.
            var sourceFullPath = AbsolutePath.Create(pathTable, Path.Combine(writerRoot, specRelativePath)).ToString(pathTable);
            var source = new Source(sourceFullPath);

            return source;
        }

        protected Task ClearBreakpointsContinueAndAwaitTerminate(ISource source)
        {
            Debugger.SendRequest(new SetBreakpointsCommand(source, new ISourceBreakpoint[0]));
            Debugger.Continue();
            return Debugger.ReceiveEvent<ITerminatedEvent>();
        }

        protected Task ContinueThreadAndAwaitTerminate(int? threadId)
        {
            Debugger.Continue();
            return Debugger.ReceiveEvent<ITerminatedEvent>();
        }

        protected Task DisconnectAndAwaitTerminate()
        {
            Debugger.SendRequest(new DisconnectCommand());
            return Debugger.ReceiveEvent<ITerminatedEvent>();
        }

        protected IReadOnlyList<IThread> GetThreads()
        {
            var res = Debugger.SendRequest(new ThreadsCommand());
            Assert.NotNull(res);
            return res.Threads;
        }

        protected IReadOnlyList<IThread> GetThreadsSortedByName()
        {
            return GetThreads().OrderBy(t => t.Name).ToArray();
        }

        protected IReadOnlyList<IStackFrame> GetStackFrames(int threadId)
        {
            var stackResult = Debugger.SendRequest(new StackTraceCommand(threadId, startFrame: 0, levels: null));
            Assert.NotNull(stackResult);
            return stackResult.StackFrames;
        }

        protected IReadOnlyList<IScope> GetScopes(int threadId, int frameIndex)
        {
            var stackFrames = GetStackFrames(threadId);
            Assert.True(stackFrames.Count > frameIndex);

            var scopesResult = Debugger.SendRequest(new ScopesCommand(stackFrames[frameIndex].Id));
            Assert.NotNull(scopesResult);
            return scopesResult.Scopes;
        }

        protected IScope GetScope(int threadId, int frameIndex, string scopeName)
        {
            return GetScopes(threadId, frameIndex).FirstOrDefault(s => s.Name == scopeName);
        }

        protected IReadOnlyList<IVariable> GetScopeVars(int threadId, int frameIndex, string scopeName)
        {
            var scope = GetScope(threadId, frameIndex, scopeName);
            return GetScopeVars(scope);
        }

        protected IReadOnlyList<IVariable> GetScopeVars(IScope scope)
        {
            Assert.NotNull(scope);
            Assert.NotEqual(0, scope.VariablesReference);
            var result = Debugger.SendRequest(new VariablesCommand(scope.VariablesReference));
            Assert.NotNull(result);
            return result.Variables;
        }

        protected IReadOnlyList<IVariable> GetVar(IVariable variable)
        {
            return Debugger.SendRequest(new VariablesCommand(variable.VariablesReference)).Variables;
        }

        protected IReadOnlyList<IVariable> GetLocalVars(int threadId, int frameIndex, bool excludePrototypeVar = true)
        {
            var allVars = GetScopeVars(threadId, frameIndex, Renderer.LocalsScopeName);
            return excludePrototypeVar
                ? allVars.Where(l => l.Name != "__prototype__").ToArray()
                : allVars;
        }

        protected IReadOnlyList<IVariable> GetPipVars(int threadId, int frameIndex)
        {
            return GetScopeVars(threadId, frameIndex, Renderer.PipGraphScopeName);
        }

        protected Dictionary<string, IVariable> ToDictionary(IEnumerable<IVariable> variables)
        {
            return variables.ToDictionary(k => k.Name, k => k);
        }

        protected IVariable FirstOrDefault(IVariablesResult variables)
        {
            return variables.Variables.FirstOrDefault();
        }

        protected async Task<IStoppedEvent> StopAndValidateLineNumber(int expectedLineNumber)
        {
            var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
            var stack = GetStackFrames(ev.Body.ThreadId);

            var msg = string.Join(
                Environment.NewLine,
                stack.Select(line => $"c:{line.Column}, l:{line.Line}, id:{line.Id}, name:{line.Name} - {line.Source.Path}"));     
            Assert.NotEqual(0, stack.Count);
            XAssert.AreEqual(expectedLineNumber, stack[0].Line, msg);
            return ev;
        }

        protected void AssertExists(Dictionary<string, IVariable> values, string name, int expected)
        {
            AssertExists(values, name);
            AssertAreEqual(expected.ToString(), values[name].Value);
        }

        protected void AssertExists(Dictionary<string, IVariable> values, string name, string expected)
        {
            AssertExists(values, name);
            AssertAreEqual(expected, values[name].Value);
        }

        protected void AssertExists(Dictionary<string, IVariable> values, string name)
        {
            AssertTrue(values.ContainsKey(name));
        }

        protected void AssertNotExist(Dictionary<string, IVariable> values, string name)
        {
            AssertTrue(!values.ContainsKey(name));
        }
    }
}
