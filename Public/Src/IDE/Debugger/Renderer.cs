// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;
using static BuildXL.FrontEnd.Script.Debugger.Matcher;
using EvaluationContext = BuildXL.FrontEnd.Script.Evaluator.Context;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    ///     Responsible for "rendering" objects to be displayed as "VSCode variables".
    ///
    ///     To render an object means to return a VSCode variable (<see cref="IVariable"/>).
    ///
    ///     Each compound object must be assigned a "variable reference" number, which a client debugger (VSCode)
    ///     can use to refer to it and request its properties.
    /// </summary>
    public sealed class Renderer
    {
        private static readonly ObjectInfo s_nullObj = new ObjectInfo("undefined", null);

        /// <summary>The label used for the Locals scope.</summary>
        public const string LocalsScopeName = "Locals";

        /// <summary>The label used for the Current Module scope.</summary>
        public const string CurrentModuleScopeName = "Current Module";

        /// <summary>The label used for the Pip Graph scope.</summary>
        public const string PipGraphScopeName = "Pip Graph";

        /// <summary>The label used for the Evaluated Modules scope.</summary>
        public const string EvaluatedModulesScopeName = "All Evaluated Modules";

        private sealed class AmbientPlaceholder
        {
            public object Value { get; }

            internal AmbientPlaceholder(object value)
            {
                Value = value;
            }
        }

        private readonly Handles<ObjectContext> m_handles = new Handles<ObjectContext>();
        private readonly DebuggerState m_state;

        /// <nodoc />
        public Renderer(DebuggerState state)
        {
            m_state = state;
        }

        /// <summary>
        ///     A shortcut for creating a <code cref="Scope"/> from an <code cref="ObjectContext"/>.
        /// </summary>
        internal Scope CreateScope(ObjectContext objContext)
        {
            return new Scope(GetObjectInfo(objContext).Preview, m_handles.Create(objContext));
        }

        /// <summary>
        ///     Returns a collection of "VSCode Protocol" variables (<see cref="IVariable"/>) for
        ///     a given scope (represented by an integer handle).
        /// </summary>
        internal IEnumerable<IVariable> GetVariablesForScope(int handle)
        {
            var objectContext = m_handles.Get(handle, null);
            var objInfo = GetObjectInfo(objectContext);
            return objInfo.Properties.Select(prop => ObjectToVariable(objectContext.Context, prop.Value, prop.Name)).ToArray();
        }

        /// <summary>
        ///     Returns a "VSCode Protocol" variable representing a given object.  For the name of the returned
        ///     variable, the <paramref name="variableName"/> argument is used verbatim; the value of the returned
        ///     variable is the preview of the <paramref name="value"/> argument, exactly the same as it would be
        ///     rendered in the "variables" pane in VSCode; finally, the a handle is created and the
        ///     <see cref="IVariable.VariablesReference"/> field is set to non-zero if the object is compound
        ///     (i.e., can be drilled down into).
        /// </summary>
        internal IVariable ObjectToVariable(EvaluationContext context, object value, string variableName)
        {
            // fetch info for the property to see if it's compound or not
            var propObjInfo = GetObjectInfo(context, value);
            var varRef = propObjInfo.Properties.Any()
                ? m_handles.Create(new ObjectContext(context, value))
                : 0; // the VSCode debug protocol specifies 0 to mean "non-compound object" (one that has no properties)
            return new Variable(variableName, propObjInfo.Preview, varRef);
        }

        /// <summary>
        ///     <see cref="GetObjectInfo(EvaluationContext, object)"/>
        /// </summary>
        private ObjectInfo GetObjectInfo(ObjectContext objectContext) => GetObjectInfo(objectContext.Context, objectContext.Object);

        /// <summary>
        ///     Returns an <see cref="ObjectInfo"/> for a given object.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1015:ClosingGenericBracketsMustBeSpacedCorrectly", Justification = "Looks better this way.")]
        [SuppressMessage("Microsoft.Globalization", "CA1305:CultureInfo.InvariantCulture", Justification = "Much more readable this way")]
        [SuppressMessage("Microsoft.Maintainability", "CA1505:ModerateMaintainabilityIndex", Justification = "Despite its size, it's very linear and straightforward")]
        internal ObjectInfo GetObjectInfo(EvaluationContext context, object obj)
        {
            obj = obj is EvaluationResult evalResult ? evalResult.Value : obj;

            var result = IsInvalid(obj)
                ? s_nullObj
                : Match(obj, new CaseMatcher<ObjectInfo>[]
                {
                    Case<ScopeLocals>(scope => new ObjectInfo(LocalsScopeName, null, Lazy.Create(() => GetLocalsForStackEntry(scope.EvalState, scope.FrameIndex)))),
                    Case<ScopeCurrentModule>(scope => ModuleLiteralInfo(context, scope.Env).WithPreview(CurrentModuleScopeName)),
                    Case<ScopePipGraph>(scope => PipGraphInfo(scope.Graph).WithPreview(PipGraphScopeName)),
                    Case<ScopeAllModules>(scope => ArrayObjInfo(scope.EvaluatedModules).WithPreview(EvaluatedModulesScopeName)),
                    Case<IModuleAndContext>(mc => GetObjectInfo(mc.Tree.RootContext, mc.Module)),
                    Case<ObjectInfo>(objInf => objInf),
                    Case<AmbientPlaceholder>(amb => new ObjectInfo(string.Empty, GetAmbientProperties(context, amb.Value).ToList())),
                    Case<IPipGraph>(graph => PipGraphInfo(graph)),
                    Case<Pip>(pip => GenericObjectInfo(pip, $"<{pip.PipType}>")),
                    Case<PipProvenance>(prov => ProvenanceInfo(prov)),
                    Case<EnvironmentVariable>(envVar => EnvironmentVariableInfo(envVar)),
                    Case<PipFragment>(pipFrag => PipFragmentInfo(context, pipFrag)),
                    Case<Thunk>(thunk => thunk.Value != null ? GetObjectInfo(context, thunk.Value) : new ObjectInfo("<not evaluated>")),
                    Case<FunctionLikeExpression>(lambda => LambdaInfo(lambda)),
                    Case<Closure>(cls => LambdaInfo(cls.Function)),
                    Case<FullSymbol>(sym => new ObjectInfo(sym.ToString(context.FrontEndContext.SymbolTable))),
                    Case<SymbolAtom>(sym => new ObjectInfo(sym.ToString(context.StringTable))),
                    Case<StringId>(id => new ObjectInfo(id.ToString(context.StringTable))),
                    Case<PipId>(id => new ObjectInfo($"{id.Value}")),
                    Case<UndefinedLiteral>(_ => new ObjectInfo("undefined", UndefinedLiteral.Instance)),
                    Case<UndefinedValue>(_ => new ObjectInfo("undefined", UndefinedValue.Instance)),
                    Case<AbsolutePath>(path => new ObjectInfo($"p`{path.ToString(context.PathTable)}`", path)),
                    Case<RelativePath>(path => new ObjectInfo($"r`{path.ToString(context.StringTable)}`", path)),
                    Case<PathAtom>(atom => new ObjectInfo($"a`{atom.ToString(context.StringTable)}`", atom)),
                    Case<FileArtifact>(file => new ObjectInfo($"f`{file.Path.ToString(context.PathTable)}`", file)),
                    Case<DirectoryArtifact>(dir => new ObjectInfo($"d`{dir.Path.ToString(context.PathTable)}`", dir)),
                    Case<uint>(num => new ObjectInfo($"{num}")),
                    Case<short>(num => new ObjectInfo($"{num}", (int)num)),
                    Case<long>(num => new ObjectInfo($"{num}")),
                    Case<char>(ch => new ObjectInfo($"'{ch}'", ch.ToString())),
                    Case<string>(str => new ObjectInfo($"\"{str}\"", str)),
                    Case<Enum>(e => new ObjectInfo($"{e.GetType().Name}.{e}", e)),
                    Case<NumberLiteral>(numLit => new ObjectInfo(numLit.UnboxedValue.ToString(), numLit)),
                    Case<Func<object>>(func => FuncObjInfo(func)),
                    Case<IEnumerable>(arr => ArrayObjInfo(arr.Cast<object>())),
                    Case<ArrayLiteral>(arrLit => ArrayObjInfo(arrLit.Values.Select(v => v.Value)).WithOriginal(arrLit)),
                    Case<ModuleBinding>(binding => GetObjectInfo(context, binding.Body)),
                    Case<ModuleLiteral>(modLit => ModuleLiteralInfo(context, modLit)),
                    Case<CallableValue>(cv => CallableValueInfo(context, cv).WithOriginal(cv)),
                    Case<ErrorValue>(error => ErrorValueInfo(context)),
                    Case<Package>(package => PackageInfo(context, package)),
                    Case<ObjectLiteral>(objLit => ObjectLiteralInfo(context, objLit).WithOriginal(objLit)),
                    Case<object>(o => o.GetType().IsArray
                        ? ArrayObjInfo(((IEnumerable)o).Cast<object>())
                        : PrimitiveObjInfo(context, o)),
                },
                defaultResult: s_nullObj);

            var ambientProperties = obj is AmbientPlaceholder
                ? new Property[0]
                : new[] { new Property("__prototype__", new AmbientPlaceholder(obj)) };

            return new ObjectInfo(result.Preview, result.Properties.Concat(ambientProperties).ToArray());
        }

        internal IEnumerable<Property> GetAmbientProperties(EvaluationContext context, object obj)
        {
            if (obj == null)
            {
                return Property.Empty;
            }

            AmbientDefinitionBase ambientDefinition;
            if (!((ModuleRegistry)context.FrontEndHost.ModuleRegistry).PredefinedTypes.AllAmbientDefinitions.TryGetValue(obj.GetType(), out ambientDefinition))
            {
                return Property.Empty;
            }

            return ambientDefinition.GetCallableMembers(context.StringTable)

                // sort by: properties first, functions next; in both groups, sort by name
                .OrderBy(kvp => Invariant("{0}_{1}", kvp.Value.IsProperty ? 1 : 2, kvp.Key))
                .Select(kvp => new Property(
                    name: kvp.Key,
                    value: TryBind(kvp.Value, obj) ?? "<error: couldn't bind callable member to receiver>",
                    kind: kvp.Value.IsProperty ? CompletionItemType.property : CompletionItemType.method));
        }

        internal static ObjectInfo ObjectLiteralInfo(EvaluationContext context, ObjectLiteral objLit)
        {
            return new ObjectInfo(
                "object{" + Invariant(objLit.Count) + "}",
                objLit.Members.Select(kvp => new Property(kvp.Key.ToString(context.StringTable), kvp.Value)).ToArray());
        }

        private static void PopulatePredefinedModuleLiteralProperties(ModuleLiteral env, List<Property> properties, EvaluationContext context)
        {
            Contract.Requires(env != null);
            Contract.Requires(context != null);

            // TODO: path, package, and parent are not projectable from a module literal, but very useful for debugging.
            if (env.IsFileModule)
            {
                // TODO:ST: hide module instantiation from the clients!
                // This is last case when we need Id!
                UninstantiatedModuleInfo moduleInfo = context.ModuleRegistry.GetUninstantiatedModuleInfoByModuleId(env.Id);

                properties.AddRange(new[]
                {
                    new Property(":path", env.Path.ToDisplayString(context)),
                    new Property(":package", env.Package),
                    new Property(":qualifierSpace", GetQualifierSpaceValue(context, moduleInfo.QualifierSpaceId)),
                });
            }

            properties.Add(new Property(":parent", env.OuterScope));

            if (env.IsFileModule)
            {
                properties.Add(new Property("qualifier", env.Qualifier.Qualifier));
            }
        }

        private ObjectInfo PipFragmentInfo(EvaluationContext context, PipFragment pipFrag)
        {
            return pipFrag.FragmentType == PipFragmentType.StringLiteral ? GetObjectInfo(context, pipFrag.GetStringIdValue()) :
                   pipFrag.FragmentType == PipFragmentType.AbsolutePath ? GetObjectInfo(context, pipFrag.GetPathValue()) :
                   pipFrag.FragmentType == PipFragmentType.NestedFragment ? GetObjectInfo(context, pipFrag.GetNestedFragmentValue()) :
                   new ObjectInfo("{invalid}");
        }

        private static ObjectInfo EnvironmentVariableInfo(EnvironmentVariable envVar)
        {
            return new ObjectInfo(envVar.ToString(), new[] { new Property("Name", envVar.Name), new Property("Value", envVar.Value) });
        }

        private static ObjectInfo ProvenanceInfo(PipProvenance prov)
        {
            return new ObjectInfo(
                prov.ToString(),
                new[]
                {
                    new Property("Qualifier", prov.QualifierId),
                    new Property("Output", prov.OutputValueSymbol),
                    new Property("Usage", prov.Usage),
                });
        }

        private static ObjectInfo GenericObjectInfo(object obj, string preview = null)
        {
            return new ObjectInfo(preview ?? obj?.ToString(), ExtractObjectProperties(obj));
        }

        private static ObjectInfo PipGraphInfo(IPipGraph graph)
        {
            return new ObjectInfo(
                "PipGraph",
                null,
                Lazy.Create<IReadOnlyList<Property>>(() => new[]
                {
                    new Property("Process Pips", Lazy.Create<object>(() => graph.RetrieveScheduledPips().Where(pip => pip.PipType == PipType.Process))),
                    new Property("Seal Directory Pips", Lazy.Create<object>(() => graph.RetrieveScheduledPips().Where(pip => pip.PipType == PipType.SealDirectory))),
                    new Property("Copy Pips", Lazy.Create<object>(() => graph.RetrieveScheduledPips().Where(pip => pip.PipType == PipType.CopyFile))),
                }));
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ParameterNeverUsed", Justification = "Parameter 'lambda' may be used in the future, e.g., to render function signature.")]
        private static ObjectInfo LambdaInfo(FunctionLikeExpression lambda)
        {
            return FuncObjInfo(null);
        }

        private static ObjectInfo FuncObjInfo(Func<object> func, string paramsSignature = null, string returnType = null)
        {
            paramsSignature = paramsSignature != null ? "(" + paramsSignature + ")" : string.Empty;
            returnType = returnType != null ? ": " + returnType : string.Empty;
            var evaluateOrNot = func != null ? " [click to evaluate]" : string.Empty;
            return new ObjectInfo(
                Invariant("function{0}{1}{2}", paramsSignature, returnType, evaluateOrNot),
                func != null
                    ? new[] { new Property("result", Lazy.Create<object>(func)) }
                    : Property.Empty);
        }

        private object TryBind(CallableMember member, object receiver)
        {
            Contract.Requires(member != null && receiver != null);

            try
            {
                var memberType = member.GetType();
                var method = memberType.GetMethod("Bind"); // this method only exists on CallableMember<T> but not on CallableMember
                return method.Invoke(member, new[] { receiver }); // returns CallableValue<T>
            }
            catch (Exception e)
            {
                // should not happen, because all our CallableMember objects are also CallableMember<T>,
                // and the only client of this method ('GetAmbientProperties') ensures that 'receiver' is of type T.
                m_state.Logger.ReportDebuggerRendererFailedToBindCallableMember(m_state.LoggingContext, member.GetType().FullName, receiver.GetType().FullName, e.GetLogEventMessage());
                return null;
            }
        }

        private ObjectInfo CallableValueInfo(EvaluationContext context, CallableValue cv)
        {
            if (cv.IsProperty)
            {
                return GetObjectInfo(context, cv.Apply(context, EvaluationStackFrame.Empty()));
            }

            var captures = EvaluationStackFrame.Empty();
            var @null = UndefinedValue.Instance;
            Func<object> func = CreateFunc();

            return func != null
                ? FuncObjInfo(func, RenderCallableSignature(cv.CallableMember))
                : new ObjectInfo("function(" + RenderCallableSignature(cv.CallableMember) + ")");

            // Local functions
            Func<object> CreateFunc()
            {
                var undefined = EvaluationResult.Undefined;
                switch (cv.CallableMember.Kind)
                {
                    case SyntaxKind.Function0:
                        return () => cv.Apply(context, captures).Value;
                    case SyntaxKind.Function1 when cv.CallableMember.MinArity == 0:
                        return () => cv.Apply(context, undefined, captures).Value;
                    case SyntaxKind.Function2 when cv.CallableMember.MinArity == 0:
                        return () => cv.Apply(context, undefined, undefined, captures).Value;
                    case SyntaxKind.FunctionN when cv.CallableMember.MinArity == 0:
                        return () => cv.Apply(context, BuildXL.Utilities.Collections.CollectionUtilities.EmptyArray<EvaluationResult>(), captures).Value;
                }

                return null;
            }
        }

        private static string RenderCallableSignature(CallableMember cm)
        {
            if (cm.MaxArity == short.MaxValue)
            {
                return cm.Rest ? "...args" : "args[]";
            }
            else
            {
                return string.Join(", ", Enumerable
                    .Range(0, cm.MaxArity)
                    .Select(idx => Invariant(
                        "{0}arg{1}{2}",
                        idx == cm.MaxArity - 1 && cm.Rest ? "..." : string.Empty,
                        idx,
                        idx >= cm.MinArity ? "?" : string.Empty)));
            }
        }

        private static ObjectInfo ErrorValueInfo(EvaluationContext context)
        {
            Contract.Requires(context != null);
            return new ObjectInfo("<error>");
        }

        private static ObjectInfo ModuleLiteralInfo(EvaluationContext context, ModuleLiteral modLit)
        {
            Contract.Requires(context != null);
            Contract.Requires(modLit != null);

            string preview = GetModuleKind(modLit);

            var properties = new List<Property>();
            PopulatePredefinedModuleLiteralProperties(modLit, properties, context);

            properties.AddRange(DictToProps(modLit.GetAllBindings(context)));

            return new ObjectInfo(preview, properties);
        }

        private static string GetModuleKind(ModuleLiteral moduleLiteral)
        {
            return moduleLiteral.Kind == SyntaxKind.TypeOrNamespaceModuleLiteral ? "namespace" : "module";
        }

        private static ObjectInfo PackageInfo(EvaluationContext context, Package package)
        {
            return new ObjectInfo("package", GetPackageProperties(context, package));
        }

        private static List<Property> GetPackageProperties(EvaluationContext context, Package package)
        {
            Contract.Requires(context != null);
            Contract.Requires(package != null);

            return new List<Property>
            {
                new Property(":name", package.Id.Name.ToString(context.StringTable)),
                new Property(":version", package.Id.Version.ToString(context.StringTable)),
            };
        }

        private static ObjectLiteral GetQualifierSpaceValue(EvaluationContext context, QualifierSpaceId qualifierSpaceId)
        {
            Contract.Requires(context != null);
            Contract.Requires(context.FrontEndContext.QualifierTable.IsValidQualifierSpaceId(qualifierSpaceId));

            var qualifierSpace = context.FrontEndContext.QualifierTable.GetQualifierSpace(qualifierSpaceId);
            var bindings = new List<Binding>(qualifierSpace.Keys.Count);
            foreach (var kvp in qualifierSpace.AsDictionary)
            {
                var values = ArrayLiteral.CreateWithoutCopy(kvp.Value.Select(s => EvaluationResult.Create(s.ToString(context.StringTable))).ToArray(), default(LineInfo), AbsolutePath.Invalid);
                bindings.Add(new Binding(kvp.Key, values, default(LineInfo)));
            }

            return ObjectLiteral.Create(bindings, default(LineInfo), AbsolutePath.Invalid);
        }

        /// <summary>
        ///     Heuristic for determining if an object is "IsValid" in the BuildXL sense.
        ///
        ///     BuildXL objects that are not "IsValid" are very finicky, and thus not amenable
        ///     to generic processing (e.g., just looking up their public property values).
        /// </summary>
        private static bool IsInvalid(object obj, PropertyInfo[] propertiesToInclude = null)
        {
            return obj != null &&
                (propertiesToInclude ?? GetPublicProperties(obj))
                .Any(p => p.Name == "IsValid" &&
                          p.PropertyType == typeof(bool) &&
                          (bool)p.GetValue(obj) == false);
        }

        private static IReadOnlyList<Property> ExtractObjectProperties(object obj, PropertyInfo[] propertiesToInclude = null)
        {
            propertiesToInclude = propertiesToInclude ?? GetPublicProperties(obj);
            return IsInvalid(obj, propertiesToInclude)
                ? Property.Empty
                : propertiesToInclude.Select(p => new Property(p.Name, p.GetValue(obj))).ToArray();
        }

        private static PropertyInfo[] GetPublicProperties(object obj)
        {
            return obj?.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public) ?? new PropertyInfo[0];
        }

        private static ObjectInfo ArrayObjInfo(IEnumerable<object> values)
        {
            return new ObjectInfo(
                Invariant("array[{0}]", values.Count()),
                values.Select((elem, i) => new Property(i.ToString(CultureInfo.InvariantCulture), elem)).ToArray());
        }

        private static ObjectInfo PrimitiveObjInfo(EvaluationContext context, object obj)
        {
            return new ObjectInfo(ToStringConverter.ObjectToString(context, obj));
        }

        private static IReadOnlyList<Property> GetLocalsForStackEntry(EvaluationState evalState, int frameIndex)
        {
            var stackEntry = evalState.GetStackEntryForFrame(frameIndex);
            return DebugInfo.ComputeCurrentLocals(stackEntry)
                .Select(lvar => new Property(lvar.Name.ToDisplayString(evalState.Context), lvar.Value))
                .ToArray();
        }

        private static IReadOnlyList<Property> DictToProps(IEnumerable<KeyValuePair<string, ModuleBinding>> dict)
        {
            return dict?.Select(kvp => new Property(kvp.Key, kvp.Value.Body)).ToArray() ?? Property.Empty;
        }

        private static CaseMatcher<T, ObjectInfo> Case<T>(Func<T, ObjectInfo> func)
        {
            return Case<T, ObjectInfo>(func);
        }

        private static string Invariant(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }

        private static string Invariant(object obj)
        {
            return Invariant("{0}", obj);
        }
    }
}
