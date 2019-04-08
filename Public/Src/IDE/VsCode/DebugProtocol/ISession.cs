// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Represents all commands that can be received from a client debugger, plus a few more helper methods.
    /// </summary>
    public interface ISession
    {
        /// <see cref="IAttachCommand"/>.
        void Attach(IAttachCommand cmd);

        /// <see cref="IConfigurationDoneCommand"/>.
        void ConfigurationDone(IConfigurationDoneCommand cmd);

        /// <see cref="IContinueCommand"/>.
        void Continue(IContinueCommand cmd);

        /// <see cref="IDisconnectCommand"/>.
        void Disconnect(IDisconnectCommand cmd);

        /// <see cref="IEvaluateCommand"/>.
        void Evaluate(IEvaluateCommand cmd);

        /// <see cref="IInitializeCommand"/>.
        void Initialize(IInitializeCommand cmd);

        /// <see cref="ILaunchCommand"/>.
        void Launch(ILaunchCommand cmd);

        /// <see cref="INextCommand"/>.
        void Next(INextCommand cmd);

        /// <see cref="IPauseCommand"/>.
        void Pause(IPauseCommand cmd);

        /// <see cref="IScopesCommand"/>.
        void Scopes(IScopesCommand cmd);

        /// <see cref="ISetBreakpointsCommand"/>.
        void SetBreakpoints(ISetBreakpointsCommand cmd);

        /// <see cref="ISetExceptionBreakpointsCommand"/>.
        void SetExceptionBreakpoints(ISetExceptionBreakpointsCommand cmd);

        /// <see cref="ISetFunctionBreakpointsCommand"/>.
        void SetFunctionBreakpoints(ISetFunctionBreakpointsCommand cmd);

        /// <see cref="ISourceCommand"/>.
        void Source(ISourceCommand cmd);

        /// <see cref="IStackTraceCommand"/>.
        void StackTrace(IStackTraceCommand cmd);

        /// <see cref="IStepInCommand"/>.
        void StepIn(IStepInCommand cmd);

        /// <see cref="IStepOutCommand"/>.
        void StepOut(IStepOutCommand cmd);

        /// <see cref="IThreadsCommand"/>.
        void Threads(IThreadsCommand cmd);

        /// <see cref="IVariablesCommand"/>.
        void Variables(IVariablesCommand cmd);

        /// <see cref="ICompletionsCommand"/>.
        void Completions(ICompletionsCommand cmd);

        /// <summary>
        /// Blocks the caller thread until this session has been initialized, i.e.,
        /// <see cref="IAttachCommand"/> or <see cref="ILaunchCommand"/> command has been received.
        /// </summary>
        void WaitSessionInitialized();
    }
}
