// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Evaluate request; value of the <see cref="IRequest.Command"/> field is "evaluate".
    ///
    /// Evaluates the given expression in the context of the top most stack frame.
    /// The expression has access to any variables and arguments that are in scope.
    /// </summary>
    public interface IEvaluateCommand : ICommand<IEvaluateResult>
    {
        /// <summary>
        /// The expression to evaluate.
        /// </summary>
        string Expression { get; }

        /// <summary>
        /// Evaluate the expression in the scope of this stack frame. If not specified, the expression is evaluated in the global scope.
        /// </summary>
        int? FrameId { get; }

        /// <summary>
        /// The context in which the evaluate request is run. Possible values are:
        ///   - 'watch' if evaluate is run in a watch,
        ///   - 'repl' if run from the REPL console, or
        ///   - 'hover' if run from a data hover.
        /// </summary>
        string Context { get; }
    }

    /// <summary>
    /// Response to <code cref="IEvaluateCommand"/>.
    /// </summary>
    public interface IEvaluateResult
    {
        /// <summary>
        /// The result of the evaluate.
        /// </summary>
        string Result { get; }

        /// <summary>
        /// If <code cref="VariablesReference"/> is > 0, the evaluate result is structured and its children
        /// can be retrieved by passing variablesReference to the VariablesRequest.
        /// </summary>
        int VariablesReference { get; }
    }
}
