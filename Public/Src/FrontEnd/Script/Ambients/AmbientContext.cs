// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Reflection;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for Context namespace.
    /// </summary>
    public sealed class AmbientContext : AmbientDefinitionBase
    {
        internal const string ContextName = "Context";
        internal const string GetNewOutputDirectoryFunctionName = "getNewOutputDirectory";
        internal const string GetTempDirectoryFunctionName = "getTempDirectory";
        internal const string GetLastActiveUseNameFunctionName = "getLastActiveUseName";
        internal const string GetLastActiveUseNamespaceFunctionName = "getLastActiveUseNamespace";
        internal const string GetLastActiveUsePathFunctionName = "getLastActiveUsePath";
        internal const string GetLastActiveUseModuleNameFunctionName = "getLastActiveUseModuleName";
        internal const string IsWindowsOsFunctionName = "isWindowsOS";
        internal const string GetMountFunctionName = "getMount";
        internal const string HasMountFunctionName = "hasMount";
        internal const string GetUserHomeDirectoryFunctionName = "getUserHomeDirectory";
        internal const string GetSpecFileFunctionName = "getSpecFile";
        internal const string GetSpecFileDirectoryFunctionName = "getSpecFileDirectory";
        internal const string GetBuildEngineDirectoryFunctionName = "getBuildEngineDirectory";
        internal const string GetDominoBinDirectoryFunctionName = "getDominoBinDirectory";
        internal const string GetTemplateFunctionName = "getTemplate";
        internal const string GetCurrentHostName = "getCurrentHost";

        private readonly SymbolAtom MountNameObject;
        private readonly SymbolAtom MountPathObject;

        internal static string[] ConfigBlacklist =
        {
            GetNewOutputDirectoryFunctionName,
            GetTempDirectoryFunctionName,
            GetMountFunctionName,
            HasMountFunctionName,
            GetTemplateFunctionName,
            GetBuildEngineDirectoryFunctionName,
            GetDominoBinDirectoryFunctionName,
            GetLastActiveUseModuleNameFunctionName,
        };

        private readonly ObjectLiteral m_currentHost;

        /// <nodoc />
        public AmbientContext(PrimitiveTypes knownTypes)
            : base(ContextName, knownTypes)
        {
            var currentHost = Host.Current;
            var osName = SymbolAtom.Create(StringTable, "os");
            string osValue;
            switch (currentHost.CurrentOS)
            {
                case BuildXL.Interop.OperatingSystem.Win:
                    osValue = "win";
                    break;
                case BuildXL.Interop.OperatingSystem.MacOS:
                    osValue = "macOS";
                    break;
                case BuildXL.Interop.OperatingSystem.Unix:
                    osValue = "unix";
                    break;
                default:
                    throw Contract.AssertFailure("Unhandled HostOS Type");
            }

            var cpuName = SymbolAtom.Create(StringTable, "cpuArchitecture");
            string cpuValue;
            switch (currentHost.CpuArchitecture)
            {
                case HostCpuArchitecture.X86:
                    cpuValue = "x86";
                    break;
                case HostCpuArchitecture.X64:
                    cpuValue = "x64";
                    break;
                default:
                    throw Contract.AssertFailure("Unhandled CpuArchitecture Type");
            }

            var isElevatedName = SymbolAtom.Create(StringTable, "isElevated");
            var isElevatedValue = CurrentProcess.IsElevated;

            m_currentHost = ObjectLiteral.Create(
                new Binding(osName, osValue, default(LineInfo)),
                new Binding(cpuName, cpuValue, default(LineInfo)),
                new Binding(isElevatedName, isElevatedValue, default(LineInfo))
                );

            MountNameObject = Symbol("name");
            MountPathObject = Symbol("path");
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                ContextName,
                new[]
                {
                    Function(GetNewOutputDirectoryFunctionName, GetNewOutputDirectory, GetNewOutputDirectorySignature),
                    Function(GetTempDirectoryFunctionName, GetTempDirectory, GetTempDirectorySignature),
                    Function(GetLastActiveUseNameFunctionName, GetLastActiveUseName, GetLastActiveUseNameSignature),
                    Function(GetLastActiveUseNamespaceFunctionName, GetLastActiveUseNamespace, GetLastActiveUseNamespaceSignature),
                    Function(GetLastActiveUsePathFunctionName, GetLastActiveUsePath, GetLastActiveUsePathSignature),
                    Function(GetLastActiveUseModuleNameFunctionName, GetLastActiveUseModuleName, GetLastActiveUseModuleNameSignature),
                    Function(GetMountFunctionName, GetMount, GetMountSignature),
                    Function(HasMountFunctionName, HasMount, HasMountSignature),
                    Function(GetUserHomeDirectoryFunctionName, GetUserHomeDirectory, GetUserHomeDirectorySignature),
                    Function(
                        GetSpecFileFunctionName, // like getLastActiveUsePath but returns FileArtifact
                        GetSpecFile, GetSpecFileSignature),
                    Function(GetSpecFileDirectoryFunctionName, GetSpecFileDirectory, GetSpecFileDirectorySignature),

                    // Aliased to the same implementation. TODO: 'BuildXL' older naming intended to be deprecated and removed.
                    Function(GetDominoBinDirectoryFunctionName, GetBuildEngineDirectoryToBeDeprecated, GetBuildXLBinDirectorySignature),
                    Function(GetBuildEngineDirectoryFunctionName, GetBuildEngineDirectoryToBeDeprecated, GetBuildXLBinDirectorySignature),

                    Function(GetTemplateFunctionName, GetTemplate, GetTemplateSignature),
                    Function(GetCurrentHostName, GetCurrentHost, GetCurrentHostSignature),

                    // To Be deprecated
                    Function(IsWindowsOsFunctionName, IsWindowsOS, IsWindowsOSSignature),
                });
        }

        private static EvaluationResult GetNewOutputDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            PathAtom hint = Args.AsPathAtom(args, 0, context.FrontEndContext.StringTable);

            var directory = context.GetPipConstructionHelper().GetUniqueObjectDirectory(hint);

            return EvaluationResult.Create(directory);
        }

        private static EvaluationResult GetTempDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var directory = context.GetPipConstructionHelper().GetUniqueTempDirectory();

            return EvaluationResult.Create(directory);
        }

        private static EvaluationResult GetLastActiveUseName(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(context.TopLevelValueInfo.ValueName.ToString(context.FrontEndContext.SymbolTable));
        }

        private static EvaluationResult GetLastActiveUseNamespace(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(context.TopLevelValueInfo.ValueName.GetParent(context.FrontEndContext.SymbolTable).ToString(context.FrontEndContext.SymbolTable));
        }

        private static EvaluationResult GetLastActiveUsePath(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(context.LastActiveUsedPath);
        }

        private static EvaluationResult GetLastActiveUseModuleName(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(context.LastActiveUsedModuleName);
        }

        private static EvaluationResult IsWindowsOS(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(Host.Current.CurrentOS == BuildXL.Interop.OperatingSystem.Win);
        }

        private EvaluationResult GetCurrentHost(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(m_currentHost);
        }

        private EvaluationResult GetMount(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var engine = context.FrontEndHost.Engine;
            var name = Convert.ToString(args[0].Value, CultureInfo.InvariantCulture);
            GetProvenance(context, out AbsolutePath path, out LineInfo lineInfo);

            var result = engine.TryGetMount(name, "Script", AmbientTypes.ScriptModuleId, out IMount mount);
            switch (result)
            {
                case TryGetMountResult.Success:
                    return
                        EvaluationResult.Create(ObjectLiteral.Create(
                            new Binding(MountNameObject, mount.Name, location: lineInfo),
                            new Binding(MountPathObject, mount.Path, location: lineInfo)));

                case TryGetMountResult.NameNullOrEmpty:
                    throw TryGetMountException.MountNameNullOrEmpty(env, new ErrorContext(pos: 1), lineInfo);

                case TryGetMountResult.NameNotFound:
                    // Check for case mismatch.
                    var mountNames = engine.GetMountNames("Script", BuildXL.Utilities.ModuleId.Invalid);
                    foreach (var mountName in mountNames)
                    {
                        if (string.Equals(name, mountName, StringComparison.OrdinalIgnoreCase))
                        {
                            throw TryGetMountException.MountNameCaseMismatch(env, name, mountName, new ErrorContext(pos: 1), lineInfo);
                        }
                    }

                    throw TryGetMountException.MountNameNotFound(env, name, mountNames, new ErrorContext(pos: 1), lineInfo);

                default:
                    Contract.Assume(false, "Unexpected TryGetMountResult");
                    return EvaluationResult.Undefined;
            }
        }

        private EvaluationResult HasMount(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var engine = context.FrontEndHost.Engine;
            var name = Convert.ToString(args[0].Value, CultureInfo.InvariantCulture);
            GetProvenance(context, out AbsolutePath path, out LineInfo lineInfo);

            var result = engine.TryGetMount(name, "Script", AmbientTypes.ScriptModuleId, out IMount mount);
            switch (result)
            {
                case TryGetMountResult.Success:
                    return EvaluationResult.True;

                case TryGetMountResult.NameNullOrEmpty:
                    throw TryGetMountException.MountNameNullOrEmpty(env, new ErrorContext(pos: 1), lineInfo);

                case TryGetMountResult.NameNotFound:
                    return EvaluationResult.False;

                default:
                    Contract.Assume(false, "Unexpected TryGetMountResult");
                    return EvaluationResult.Undefined;
            }
        }

        private static EvaluationResult GetUserHomeDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            string homePath = Host.Current.CurrentOS == BuildXL.Interop.OperatingSystem.Win
                ? I($"{Environment.GetEnvironmentVariable(")HOMEDRIVE") ?? "C:"}{Path.DirectorySeparatorChar}{Environment.GetEnvironmentVariable("HOMEPATH") ?? "Users"}")
                : Environment.GetEnvironmentVariable("HOME");

            var directoryPath = AbsolutePath.Create(context.FrontEndContext.PathTable, homePath);
            return EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(directoryPath));
        }

        private static EvaluationResult GetSpecFile(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(FileArtifact.CreateSourceFile(context.LastActiveUsedPath));
        }

        private static EvaluationResult GetSpecFileDirectory(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(context.LastActiveUsedPath.GetParent(context.FrontEndContext.PathTable)));
        }

        private static EvaluationResult GetBuildEngineDirectoryToBeDeprecated(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var pathTable = context.FrontEndContext.PathTable;
            var executingEnginePath = AbsolutePath.Create(pathTable, AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));

            return EvaluationResult.Create(DirectoryArtifact.CreateWithZeroPartialSealId(executingEnginePath.GetParent(pathTable)));
        }

        private static EvaluationResult GetTemplate(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            if (context.TopLevelValueInfo.CapturedTemplateValue == null)
            {
                GetProvenance(context, out _, out LineInfo lineInfo);

                throw TryGetTemplateException.TemplateNotAvailable(env, new ErrorContext(pos: 1), lineInfo);
            }

            // The capture template is retrieved from the context
            return EvaluationResult.Create(context.TopLevelValueInfo.CapturedTemplateValue);
        }

        private CallSignature GetNewOutputDirectorySignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.StringType),
            returnType: AmbientTypes.DirectoryType);

        private CallSignature GetTempDirectorySignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.StringType),
            returnType: AmbientTypes.DirectoryType);

        private static CallSignature GetLastActiveUseNameSignature => CreateSignature(
            returnType: PrimitiveType.StringType);

        private static CallSignature GetLastActiveUseNamespaceSignature => CreateSignature(
            returnType: PrimitiveType.StringType);

        private CallSignature GetLastActiveUsePathSignature => CreateSignature(
            returnType: AmbientTypes.PathType);

        private CallSignature GetLastActiveUseModuleNameSignature => CreateSignature(
            returnType: AmbientTypes.StringType);

        private CallSignature GetCurrentHostSignature => CreateSignature(
            returnType: AmbientTypes.ObjectType);

        private CallSignature IsWindowsOSSignature => CreateSignature(
            returnType: AmbientTypes.BooleanType);

        private CallSignature GetMountSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.StringType),
            returnType: AmbientTypes.ObjectType);

        private CallSignature HasMountSignature => CreateSignature(
            required: RequiredParameters(PrimitiveType.StringType),
            returnType: AmbientTypes.BooleanType);

        private CallSignature GetUserHomeDirectorySignature => CreateSignature(
            returnType: AmbientTypes.DirectoryType);

        private CallSignature GetSpecFileSignature => CreateSignature(
            returnType: AmbientTypes.FileType);

        private CallSignature GetSpecFileDirectorySignature => CreateSignature(
            returnType: AmbientTypes.DirectoryType);

        private CallSignature GetBuildXLBinDirectorySignature => CreateSignature(
            returnType: AmbientTypes.DirectoryType);

        private static CallSignature GetTemplateSignature => CreateSignature(
            returnType: PrimitiveType.AnyType);
    }
}
