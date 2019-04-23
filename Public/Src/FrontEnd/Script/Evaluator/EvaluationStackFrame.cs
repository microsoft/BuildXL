// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Stack frame that contains all the closures, arguments, locals and the marker about return statement, required for function evaluation.
    /// </summary>
    public sealed class EvaluationStackFrame : IDisposable
    {
        // This includes locals and arguments.
        private const int MaxFrameSizeForPooledStackFrames = 20;

        private static readonly ObjectPool<EvaluationStackFrame>[] s_framePools = InitializePool(MaxFrameSizeForPooledStackFrames);

        /// <summary>
        /// True if the instance is came from the object's pool.
        /// </summary>
        private bool m_fromThePool;
        
        /// <summary>
        /// Offset in the evaluation stack frame that separates captured variables from the arguments of the function.
        /// </summary>
        private int m_lambdaParamsOffset;

        private PooledObjectWrapper<EvaluationStackFrame> m_instanceWrapper;

        /// <summary>
        /// Stack frame data that includes locals, captures and parameters.
        /// </summary>
        public EvaluationResult[] Frame { get; }

        /// <summary>
        /// Marker that indicates that the return statement for a current stack frame was reached and evaluated.
        /// The marker can be used to stop evaluation and not evaluate the rest of a method body.
        /// </summary>
        /// TODO: potentially we can use the same logic for 'ErrorValue' as well.
        public bool ReturnStatementWasEvaluated { get; set; }

        /// <summary>
        /// The length of the stack frame that includes locals, captures and parameters.
        /// </summary>
        public int Length => Frame.Length;

        /// <summary>
        /// Number of parameters of a function.
        /// </summary>
        public int ParametersCount { get; private set; }

        /// <summary>
        /// Gets or sets the value of the stack frame.
        /// </summary>
        public EvaluationResult this[int index]
        {
            get => Frame[index];
            set => Frame[index] = value;
        }

        private EvaluationStackFrame(int frameSize, bool fromPool)
        {
            Frame = new EvaluationResult[frameSize];
            m_fromThePool = fromPool;
        }

        private void Initialize(PooledObjectWrapper<EvaluationStackFrame> poolWrapper, int paramsCount, int paramsOffset, EvaluationResult[] capturedValues)
        {
            m_instanceWrapper = poolWrapper;
            ParametersCount = paramsCount;
            m_lambdaParamsOffset = paramsOffset;
            ReturnStatementWasEvaluated = false;

            if (capturedValues != null && Length != 0)
            {
                Array.Copy(capturedValues, Frame, Math.Min(capturedValues.Length, paramsOffset));
            }
        }

        /// <summary>
        /// Creates a frame from an array of evaluated values.
        /// Used only by the debugger.
        /// </summary>
        public static EvaluationStackFrame UnsafeFrom([NotNull]EvaluationResult[] args)
        {
            var result = new EvaluationStackFrame(args.Length, fromPool:false);
            result.Initialize(default(PooledObjectWrapper<EvaluationStackFrame>), args.Length, args.Length, args);
            return result;
        }

        /// <summary>
        /// Allocates a stack frame required for the DScript evaluation.
        /// </summary>
        /// <remarks>
        /// Allocates invocation frame for the 'lambda' expression.  The length of the frame will be equal to the
        /// sum of the captured and local variables in the given lambda.
        ///
        /// If an array of captured values ("capturedValues") is provided, they are copied into the allocated
        /// frame from position <code>0</code> up to position <code>this.Captures - 1</code>.
        ///
        /// If an array of actual arguments ("actualArguments") is provided, they are copied into the allocated
        /// frame from position <code>this.Captures</code> up to position <code>frame.Length - 1</code>.
        /// </remarks>
        public static EvaluationStackFrame Create(FunctionLikeExpression lambda, EvaluationResult[] capturedValues)
        {
            Contract.Requires(lambda.Locals >= lambda.Params);

            int frameSize = lambda.Captures + lambda.Locals;
            if (frameSize == 0)
            {
                return Empty();
            }

            var wrapper = default(PooledObjectWrapper<EvaluationStackFrame>);
            EvaluationStackFrame frame;
            if (frameSize < s_framePools.Length)
            {
                wrapper = s_framePools[frameSize].GetInstance();
                frame = wrapper.Instance;
            }
            else
            {
                frame = new EvaluationStackFrame(frameSize, fromPool: false);
            }

            frame.Initialize(wrapper, lambda.Params, lambda.Captures, capturedValues);
            frame.ReturnStatementWasEvaluated = false;
            return frame;
        }

        /// <summary>
        /// Special factory that creates a frame with 0 arguments/locals.
        /// </summary>
        public static EvaluationStackFrame Empty()
        {
            PooledObjectWrapper<EvaluationStackFrame> wrapper = s_framePools[0].GetInstance();

            var frame = wrapper.Instance;
            frame.Initialize(wrapper, 0, 0, CollectionUtilities.EmptyArray<EvaluationResult>());
            frame.ReturnStatementWasEvaluated = false;

            return frame;
        }

        /// <summary>
        /// Detaches a current stack frame from the object pool.
        /// This method is required if a local/parameter is captured by the closure and should live in the heap.
        /// </summary>
        public EvaluationStackFrame Detach()
        {
            m_fromThePool = false;

            return this;
        }

        /// <summary>
        /// Sets a given argument to a frame with a given position.
        /// </summary>
        /// <remarks>
        /// Method requires that the <paramref name="argumentPosition"/> is less then <see cref="ParametersCount"/>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetArgument(int argumentPosition, EvaluationResult argument0)
        {
#if DEBUG
            Contract.Requires(argumentPosition < ParametersCount, $"Can't set an excessive argument to a frame. Position: {argumentPosition}, Arguments: {ParametersCount}");
#endif
            Frame[m_lambdaParamsOffset + argumentPosition] = argument0;
        }

        /// <summary>
        /// Sets arguments to a frame up to <paramref name="paramsCount"/>.
        /// </summary>
        /// <remarks>
        /// TypeScript/DScript has slightly unusual assignability for local functions.
        /// If the lambda-expression takes 3 parameters, it is possible to specify a lambda expression with just two parameters.
        /// It means that all 4 cases are valid:
        /// <code>
        /// const a = [1,2,3];
        /// const b = a.map((element, index, array) => element);
        /// const c = a.map((element, index) => element);
        /// const d = a.map(element => element);
        /// const e = a.map(() => 42);
        /// </code>
        /// This method intentionally takes an integer as the <paramref name="argument2"/>, not an object to avoid redundant heap allocation if the called member
        /// only has 1 argument.
        /// </remarks>
        public void TrySetArguments(int paramsCount, EvaluationResult argument1, int argument2, EvaluationResult argument3)
        {
            switch (paramsCount)
            {
                case 0:
                    return;
                case 1:
                    SetArguments(argument1);
                    return;
                case 2:
                    SetArguments(argument1, NumberLiteral.Box(argument2));
                    return;
                case 3:
                    SetArguments(argument1, NumberLiteral.Box(argument2), argument3);
                    return;
                default:
                    throw Contract.AssertFailure($"Invalid number of arguments ({paramsCount}).");
            }
        }

        /// <summary>
        /// Sets arguments to a frame up to <paramref name="paramsCount"/>.
        /// </summary>
        /// <remarks>
        /// This method intentionally takes an integer as the <paramref name="argument3"/>, not an object to avoid redundant heap allocation if the called member
        /// only has 1 argument.
        /// </remarks>
        public void TrySetArguments(int paramsCount, EvaluationResult argument1, EvaluationResult argument2, int argument3, EvaluationResult argument4)
        {
            switch (paramsCount)
            {
                case 0:
                    return;
                case 1:
                    SetArguments(argument1);
                    return;
                case 2:
                    SetArguments(argument1, argument2);
                    return;
                case 3:
                    SetArguments(argument1, argument2, NumberLiteral.Box(argument3));
                    return;
                case 4:
                    SetArguments(argument1, argument2, NumberLiteral.Box(argument3), argument4);
                    return;
                default:
                    throw Contract.AssertFailure($"Invalid number of arguments ({paramsCount}).");
            }
        }

        /// <summary>
        /// Sets arguments to a frame up to <paramref name="paramsCount"/>.
        /// </summary>
        public void TrySetArguments(int paramsCount, EvaluationResult argument1, EvaluationResult argument2)
        {
            switch (paramsCount)
            {
                case 0:
                    return;
                case 1:
                    SetArguments(argument1);
                    return;
                case 2:
                    SetArguments(argument1, argument2);
                    return;
                default:
                    throw Contract.AssertFailure($"Invalid number of arguments ({paramsCount}).");
            }
        }

        /// <summary>
        /// Sets arguments to a frame up to <paramref name="paramsCount"/>.
        /// </summary>
        public void TrySetArguments(int paramsCount, EvaluationResult argument1)
        {
            switch (paramsCount)
            {
                case 0:
                    return;
                case 1:
                    SetArguments(argument1);
                    return;
                default:
                    throw Contract.AssertFailure($"Invalid number of arguments ({paramsCount}).");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_fromThePool)
            {
                m_instanceWrapper.Dispose();
            }
        }

        private static ObjectPool<EvaluationStackFrame>[] InitializePool(int poolSize)
        {
            var result = new ObjectPool<EvaluationStackFrame>[poolSize];

            for (int i = 0; i < result.Length; i++)
            {
                int frameSize = i;
                result[i] = new ObjectPool<EvaluationStackFrame>(() => new EvaluationStackFrame(frameSize, fromPool: true), cleanup: null);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetArguments(EvaluationResult argument0)
        {
            SetArgument(0, argument0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetArguments(EvaluationResult argument0, EvaluationResult argument1)
        {
            SetArgument(0, argument0);
            SetArgument(1, argument1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetArguments(EvaluationResult argument0, EvaluationResult argument1, EvaluationResult argument2)
        {
            SetArgument(0, argument0);
            SetArgument(1, argument1);
            SetArgument(2, argument2);
        }

        private void SetArguments(EvaluationResult argument0, EvaluationResult argument1, EvaluationResult argument2, EvaluationResult argument3)
        {
            SetArgument(0, argument0);
            SetArgument(1, argument1);
            SetArgument(2, argument2);
            SetArgument(3, argument3);
        }
    }
}
