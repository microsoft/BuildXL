// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using VSCode.DebugProtocol;

using EvaluationContext = BuildXL.FrontEnd.Script.Evaluator.Context;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Custom renderer for dscript-specific objects.
    /// </summary>
    public class DScriptDebugerRenderer
    {
        private sealed class AmbientPlaceholder
        {
            public object Value { get; }

            internal AmbientPlaceholder(object value)
            {
                Value = value;
            }
        }

        /// <nodoc />
        public static ObjectInfo Render(Renderer renderer, object ctx, object obj)
        {
            var context = ctx as EvaluationContext;
            if (ctx == null)
            {
                return null;
            }

            var result = Match();

            if (result == null)
            {
                return null;
            }

            var ambientProperties = obj is AmbientPlaceholder
                ? new Property[0]
                : new[] { new Property("__proto__", new AmbientPlaceholder(obj)) };

            return new ObjectInfo(result.Preview, result.Properties.Concat(ambientProperties).ToArray());

            ObjectInfo Match()
            {
                switch (obj)
                {
                    case AmbientPlaceholder amb:   return new ObjectInfo(string.Empty, GetAmbientProperties(context, amb.Value).ToArray());
                    case FullSymbol sym:           return new ObjectInfo(sym.ToString(context.FrontEndContext.SymbolTable));
                    case ScopeCurrentModule scope: return ModuleLiteralInfo(context, scope.Env).WithPreview(Renderer.CurrentModuleScopeName);
                    case ModuleLiteral modLit:     return ModuleLiteralInfo(context, modLit);
                    case CallableValue cv:         return CallableValueInfo(renderer, context, cv).WithOriginal(cv);
                    case Package package:          return PackageInfo(context, package);
                    case ObjectLiteral objLit:     return ObjectLiteralInfo(context, objLit).WithOriginal(objLit);
                    default:
                        return null;
                }
            }
        }

        private static IEnumerable<Property> GetAmbientProperties(EvaluationContext context, object obj)
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

        private static object TryBind(CallableMember member, object receiver)
        {
            Contract.Requires(member != null && receiver != null);

            var memberType = member.GetType();
            var method = memberType.GetMethod("Bind"); // this method only exists on CallableMember<T> but not on CallableMember
            return method.Invoke(member, new[] { receiver }); // returns CallableValue<T>
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

        private static IReadOnlyList<Property> DictToProps(IEnumerable<KeyValuePair<string, ModuleBinding>> dict)
        {
            return dict?.Select(kvp => new Property(kvp.Key, kvp.Value.Body)).ToArray() ?? Property.Empty;
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


        private static ObjectInfo CallableValueInfo(Renderer renderer, EvaluationContext context, CallableValue cv)
        {
            if (cv.IsProperty)
            {
                return renderer.GetObjectInfo(context, cv.Apply(context, EvaluationStackFrame.Empty()));
            }

            var captures = EvaluationStackFrame.Empty();
            var @null = UndefinedValue.Instance;
            Func<object> func = CreateFunc();

            return func != null
                ? Renderer.FuncObjInfo(func, RenderCallableSignature(cv.CallableMember))
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

        private static ObjectInfo PrimitiveObjInfo(EvaluationContext context, object obj)
        {
            return new ObjectInfo(ToStringConverter.ObjectToString(context, obj));
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
