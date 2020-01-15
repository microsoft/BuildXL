// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;

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
        private static readonly ObjectInfo s_nullObj = new ObjectInfo("undefined");

        /// <summary>The label used for the Locals scope.</summary>
        public const string LocalsScopeName = "Locals";

        /// <summary>The label used for the Current Module scope.</summary>
        public const string CurrentModuleScopeName = "Current Module";

        /// <summary>The label used for the Pip Graph scope.</summary>
        public const string PipGraphScopeName = "Pip Graph";

        /// <summary>The label used for the Evaluated Modules scope.</summary>
        public const string EvaluatedModulesScopeName = "All Evaluated Modules";

        private PathTable PathTable { get; }
        private StringTable StringTable => PathTable.StringTable;

        private readonly Handles<ObjectContext> m_handles = new Handles<ObjectContext>();
        private readonly Tracing.Logger m_logger = Tracing.Logger.CreateLogger();
        private readonly LoggingContext m_loggingContext;
        private readonly CustomRenderer m_customRenderer;

        /// <summary>
        /// Delegate type for custom renderers
        /// </summary>
        public delegate ObjectInfo CustomRenderer(Renderer renderer, object context, object value);

        /// <nodoc />
        public Renderer(LoggingContext loggingContext, PathTable pathTable, CustomRenderer customRenderer)
        {
            Contract.Requires(pathTable.IsValid);

            m_loggingContext = loggingContext;
            m_customRenderer = customRenderer;
            PathTable = pathTable;
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
            var ctx = objectContext.Context;
            var obj = (objectContext.Object is Property p) ? p.Value : objectContext.Object;
            var objInfo = GetObjectInfo(ctx, obj);
            return objInfo.Properties
                .Select(prop => ObjectToVariable(ctx, prop.Value, prop.Name))
                .ToArray();
        }

        /// <summary>
        ///     Returns a "VSCode Protocol" variable representing a given object.  For the name of the returned
        ///     variable, the <paramref name="variableName"/> argument is used verbatim; the value of the returned
        ///     variable is the preview of the <paramref name="value"/> argument, exactly the same as it would be
        ///     rendered in the "variables" pane in VSCode; finally, a handle is created and the
        ///     <see cref="IVariable.VariablesReference"/> field is set to non-zero if the object is compound
        ///     (i.e., can be drilled down into).
        /// </summary>
        internal IVariable ObjectToVariable(object context, object value, string variableName)
        {
            // fetch info for the property to see if it's compound or not
            var propObjInfo = GetObjectInfo(context, value);
            var varRef = propObjInfo.HasAnyProperties
                ? m_handles.Create(new ObjectContext(context, value))
                : 0; // the VSCode debug protocol specifies 0 to mean "non-compound object" (one that has no properties)
            return new Variable(variableName, propObjInfo.Preview, varRef);
        }

        /// <summary>
        ///     <see cref="GetObjectInfo(object, object)"/>
        /// </summary>
        private ObjectInfo GetObjectInfo(ObjectContext objectContext) => GetObjectInfo(objectContext.Context, objectContext.Object);

        /// <summary>
        ///     Returns an <see cref="ObjectInfo"/> for a given object.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1015:ClosingGenericBracketsMustBeSpacedCorrectly", Justification = "Looks better this way.")]
        [SuppressMessage("Microsoft.Globalization", "CA1305:CultureInfo.InvariantCulture", Justification = "Much more readable this way")]
        [SuppressMessage("Microsoft.Maintainability", "CA1505:ModerateMaintainabilityIndex", Justification = "Despite its size, it's very linear and straightforward")]
        public ObjectInfo GetObjectInfo(object context, object obj)
        {
            obj = obj is EvaluationResult evalResult 
                ? evalResult.Value
                : obj;

            if (obj == null || IsInvalid(obj))
            {
                return s_nullObj;
            }

            if (obj.GetType().IsArray)
            {
                return ArrayObjInfo(((IEnumerable)obj).Cast<object>().ToArray());
            }

            var customResult = m_customRenderer?.Invoke(this, context, obj);
            if (customResult != null)
            {
                return customResult;
            }

            return obj switch
            {
                ScopeLocals scope => new ObjectInfo(LocalsScopeName, null, Lazy.Create(() => GetLocalsForStackEntry(scope.EvalState, scope.FrameIndex))),
                ScopePipGraph scope => PipGraphInfo(scope.Graph).WithPreview(PipGraphScopeName),
                ScopeAllModules scope => ArrayObjInfo(scope.EvaluatedModules.ToArray()).WithPreview(EvaluatedModulesScopeName),
                IModuleAndContext mc => GetObjectInfo(mc.Tree.RootContext, mc.Module),
                ObjectInfo objInf => objInf,
                IPipScheduleTraversal graph => PipGraphInfo(graph),
                Pip pip => GenericObjectInfo(pip, $"<{pip.PipType}>").Build(),
                PipProvenance prov => ProvenanceInfo(prov),
                EnvironmentVariable envVar => EnvironmentVariableInfo(envVar),
                PipFragment pipFrag => PipFragmentInfo(context, pipFrag),
                Thunk thunk => thunk.Value != null ? GetObjectInfo(context, thunk.Value) : new ObjectInfo("<not evaluated>"),
                FunctionLikeExpression lambda => LambdaInfo(lambda),
                Closure cls => LambdaInfo(cls.Function),
                SymbolAtom sym => new ObjectInfo(sym.ToString(StringTable)),
                StringId id => new ObjectInfo(id.ToString(StringTable)),
                PipId id => new ObjectInfo($"{id.Value}"),
                UndefinedLiteral _ => new ObjectInfo("undefined", UndefinedLiteral.Instance),
                UndefinedValue _ => new ObjectInfo("undefined", UndefinedValue.Instance),
                AbsolutePath path => new ObjectInfo($"p`{path.ToString(PathTable)}`", path),
                RelativePath path => new ObjectInfo($"r`{path.ToString(StringTable)}`", path),
                PathAtom atom => new ObjectInfo($"a`{atom.ToString(StringTable)}`", atom),
                FileArtifact file => new ObjectInfo($"f`{file.Path.ToString(PathTable)}`", file),
                DirectoryArtifact dir => new ObjectInfo($"d`{dir.Path.ToString(PathTable)}`", dir),
                int num => new ObjectInfo($"{num}"),
                uint num => new ObjectInfo($"{num}"),
                short num => new ObjectInfo($"{num}", (int)num),
                long num => new ObjectInfo($"{num}"),
                char ch => new ObjectInfo($"'{ch}'", ch.ToString()),
                string str => new ObjectInfo($"\"{str}\"", str),
                Enum e => new ObjectInfo($"{e.GetType().Name}.{e}", e),
                NumberLiteral numLit => new ObjectInfo(numLit.UnboxedValue.ToString(), numLit),
                Func<object> func => FuncObjInfo(func),
                ArraySegment<object> arrSeg => ArrayObjInfo(arrSeg),
                IEnumerable enu => new ObjectInfoBuilder().Preview("IEnumerable").Prop("Result", Lazy.Create<object>(() => enu.Cast<object>().ToArray())).Build(),
                ArrayLiteral arrLit => ArrayObjInfo(arrLit.Values.Select(v => v.Value).ToArray()).WithOriginal(arrLit),
                ModuleBinding binding => GetObjectInfo(context, binding.Body),
                ErrorValue error => ErrorValueInfo(),
                object o => GenericObjectInfo(o).Build(),
                _ => s_nullObj
            };
        }

        private static string TryToString(object obj)
        {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            try
            {
                return obj?.ToString();
            }
            catch (Exception)
            {
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private ObjectInfo PipFragmentInfo(object context, PipFragment pipFrag)
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

        /// <summary>
        /// Extracts values of all public fields and properties.
        /// </summary>
        public static ObjectInfoBuilder GenericObjectInfo(object obj, string preview = null)
        { 
            var builder = new ObjectInfoBuilder().Preview(preview ?? TryToString(obj));
            builder = GetPublicProperties(obj?.GetType()).Aggregate(builder, (acc, pi) => acc.Prop(pi.Name, () => pi.GetValue(obj)));
            builder = GetPublicFields(obj?.GetType()).Aggregate(builder, (acc, fi) => acc.Prop(fi.Name, () => fi.GetValue(obj)));
            return builder;
        }

        private static ObjectInfo PipGraphInfo(IPipScheduleTraversal graph)
        {
            return new ObjectInfo(
                "PipGraph",
                Enum.GetValues(typeof(PipType))
                    .Cast<PipType>()
                    .Select(pipType => new Property(pipType.ToString(), () => graph.RetrieveScheduledPips().Where(pip => pip.PipType == pipType).ToArray()))
                    .ToArray());
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ParameterNeverUsed", Justification = "Parameter 'lambda' may be used in the future, e.g., to render function signature.")]
        private static ObjectInfo LambdaInfo(FunctionLikeExpression lambda)
        {
            return FuncObjInfo(null);
        }

        internal static ObjectInfo FuncObjInfo(Func<object> func, string paramsSignature = null, string returnType = null)
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

        private static ObjectInfo ErrorValueInfo()
        {
            return new ObjectInfo("<error>");
        }

        /// <summary>
        ///     Heuristic for determining if an object is "IsValid" in the BuildXL sense.
        ///
        ///     BuildXL objects that are not "IsValid" are very finicky, and thus not amenable
        ///     to generic processing (e.g., just looking up their public property values).
        /// </summary>
        public static bool IsInvalid(object obj, PropertyInfo[] propertiesToInclude = null)
        {
            return obj != null &&
                (propertiesToInclude ?? GetPublicProperties(obj?.GetType()))
                .Any(p => p.Name == "IsValid" &&
                          p.PropertyType == typeof(bool) &&
                          (bool)p.GetValue(obj) == false);
        }

        /// <summary>
        ///     Returns public properties of a type.
        /// </summary>
        public static PropertyInfo[] GetPublicProperties([CanBeNull]Type objType)
        {
            return objType?.GetProperties(BindingFlags.Instance | BindingFlags.Public) ?? new PropertyInfo[0];
        }

        /// <summary>
        ///     Returns public fields of a type.
        /// </summary>
        public static FieldInfo[] GetPublicFields([CanBeNull]Type objType)
        {
            return objType?.GetFields(BindingFlags.Instance | BindingFlags.Public) ?? new FieldInfo[0];
        }

        private const int MaxArrayLength = 1000;

        private static ObjectInfo ArrayObjInfo(object[] arr)
        {
            return ArrayObjInfo(new ArraySegment<object>(arr));
        }

        private static ObjectInfo ArrayObjInfo(ArraySegment<object> arr)
        {
            var bucketSize = MaxArrayLength;
            var arrLen = arr.Count;

            var builder = new ObjectInfoBuilder();
            if (arrLen <= bucketSize)
            {
                for (int i = 0; i < arrLen; i++)
                {
                    builder = builder.Prop($"{i + arr.Offset}", arr.Array[i + arr.Offset]);
                }
            }
            else
            {
                var bucketCount = (arrLen - 1)/bucketSize + 1;
                for (int bucketIdx = 0; bucketIdx < bucketCount; bucketIdx++)
                {
                    var startIdx = bucketIdx * bucketSize;
                    var endIdx = Math.Min(arrLen - 1, (bucketIdx + 1)*bucketSize - 1);
                    builder = builder.Prop(
                        name: $"[{startIdx}..{endIdx}]", 
                        value: new ArraySegment<object>(arr.Array, arr.Offset + startIdx, count: endIdx - startIdx + 1));
                }
            }

            return builder.Preview($"array[{arrLen}]").Build();
        }

        private static IDictionary<string, Property> GetLocalsForStackEntry(EvaluationState evalState, int frameIndex)
        {
            var stackEntry = evalState.GetStackEntryForFrame(frameIndex);
            return DebugInfo.ComputeCurrentLocals(stackEntry)
                .Select(lvar => new Property(lvar.Name.ToDisplayString(evalState.Context), lvar.Value))
                .ToDictionary(p => p.Name, p => p);
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
