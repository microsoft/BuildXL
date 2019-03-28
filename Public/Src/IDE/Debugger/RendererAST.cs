// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using VSCode.DebugProtocol;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    ///     Represents a property name-value pair.
    ///
    ///     If a and object of type <see cref="Lazy{T}"/> is set as <see cref="Value"/>,
    ///     the getter for <see cref="Value"/> will automatically return the value of the lazy
    ///     object (<see cref="Lazy{T}.Value"/>).
    /// </summary>
    public sealed class Property
    {
        internal static readonly Property[] Empty = new Property[0];

        private readonly Lazy<object> m_valueAsLazy;

        /// <summary>Property name.</summary>
        public string Name { get; }

        /// <summary>
        ///     Property value.  When set to an instance of <see cref="Lazy{T}"/>, the getter
        ///     returns the value of that lazy object (<see cref="Lazy{T}.Value"/>.
        /// </summary>
        public object Value => m_valueAsLazy.Value;

        /// <summary>Property kind.</summary>
        public CompletionItemType Kind { get; }

        /// <nodoc />
        public Property(string name, object value, CompletionItemType kind = CompletionItemType.property)
        {
            Name = name;
            m_valueAsLazy = value as Lazy<object> ?? Lazy.Create(() => value);
            Kind = kind;
        }
    }

    /// <summary>
    ///     Contains some basic object meta-information suitable for lazy rendering in a tree viewer.
    /// </summary>
    internal sealed class ObjectInfo
    {
        private readonly Lazy<IReadOnlyList<Property>> m_lazyProperties;

        /// <summary>Short preview as a plain string.</summary>
        public string Preview { get; }

        /// <summary>Original object (used when converting to ObjectLiteral)</summary>
        public object Original { get; }

        /// <summary>List of properties (as name-value pairs, <see cref="Property"/>)</summary>
        public IReadOnlyList<Property> Properties => m_lazyProperties.Value;

        /// <nodoc />
        public ObjectInfo(string preview, object original)
            : this(preview, original, Lazy.Create<IReadOnlyList<Property>>(() => Property.Empty)) { }

        /// <nodoc />
        public ObjectInfo(string preview = "", IReadOnlyList<Property> properties = null)
            : this(preview, null, Lazy.Create(() => properties ?? Property.Empty)) { }

        /// <nodoc />
        public ObjectInfo(string preview, object original, Lazy<IReadOnlyList<Property>> properties)
        {
            Contract.Requires(properties != null);

            Preview = preview;
            Original = original;
            m_lazyProperties = properties;
        }

        /// <nodoc />
        public ObjectInfo WithPreview(string preview)
        {
            return new ObjectInfo(preview, Original, m_lazyProperties);
        }

        /// <nodoc />
        public ObjectInfo WithOriginal(object original)
        {
            return new ObjectInfo(Preview, original, m_lazyProperties);
        }
    }

    /// <summary>
    /// Simple memento class for storing an object context, consisting of a <see cref="Context"/> and an object.
    ///
    /// An ObjectContext represents a compound object. Compound objects are scopes on their own,
    /// because their properties are rendered as variables too, but are not associated with stack frame.
    /// </summary>
    internal sealed class ObjectContext
    {
        /// <summary>The context is needed only for "to string" conversion.</summary>
        public Context Context { get; }

        /// <summary>Parent object whose properties are to be rendered.</summary>
        public object Object { get; }

        public ObjectContext(Context context, object obj)
        {
            Contract.Requires(context != null);

            Context = context;
            Object = obj;
        }
    }

    // ===============================================================================
    // == Some special scope objects used as "object" in ObjectContext
    // ===============================================================================
    internal sealed class ScopeLocals
    {
        internal EvaluationState EvalState { get; }

        internal int FrameIndex { get; }

        internal ScopeLocals(EvaluationState evalState, int frameIndex)
        {
            EvalState = evalState;
            FrameIndex = frameIndex;
        }
    }

    internal sealed class ScopeCurrentModule
    {
        internal ModuleLiteral Env { get; }

        internal ScopeCurrentModule(ModuleLiteral env)
        {
            Env = env;
        }
    }

    internal sealed class ScopePipGraph
    {
        internal IPipGraph Graph { get; }

        internal ScopePipGraph(IPipGraph graph)
        {
            Graph = graph;
        }
    }

    internal sealed class ScopeAllModules
    {
        internal IReadOnlyList<IModuleAndContext> EvaluatedModules { get; }

        internal ScopeAllModules(IReadOnlyList<IModuleAndContext> evaluatedModules)
        {
            EvaluatedModules = evaluatedModules;
        }
    }
}
