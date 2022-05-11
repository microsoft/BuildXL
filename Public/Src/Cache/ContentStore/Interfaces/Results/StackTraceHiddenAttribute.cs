// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if !NET6_0_OR_GREATER
namespace System.Diagnostics
{
    /// <summary>
    /// Types and Methods attributed with StackTraceHidden will be omitted from the stack trace text shown in StackTrace.ToString()
    /// and Exception.StackTrace
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Struct, Inherited = false)]
    public sealed class StackTraceHiddenAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StackTraceHiddenAttribute"/> class.
        /// </summary>
        public StackTraceHiddenAttribute() { }
    }
}

#endif
