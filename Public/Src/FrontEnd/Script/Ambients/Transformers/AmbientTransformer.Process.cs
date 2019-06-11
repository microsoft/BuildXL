// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;
using Type = System.Type;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        private static readonly Type[] s_convertFileContentExpectedTypes = { typeof(string), typeof(PathAtom), typeof(RelativePath), typeof(AbsolutePath) };
        private static readonly Type[] s_convertFileContentExpectedTypesWithArray = { typeof(string), typeof(PathAtom), typeof(RelativePath), typeof(AbsolutePath), typeof(ArrayLiteral) };

        private static readonly Dictionary<string, FileExistence> s_fileExistenceKindMap = new Dictionary<string, FileExistence>(StringComparer.Ordinal)
        {
            ["required"] = FileExistence.Required,
            ["optional"] = FileExistence.Optional,
            ["temporary"] = FileExistence.Temporary,
        };

        private static readonly ISet<string> s_outputDirectoryKind = new HashSet<string>(StringComparer.Ordinal)
        {
            "shared",
            "exclusive",
        };

        // Keep in sync with Transformer.Execute.dsc DoubleWritePolicy definition
        private static readonly Dictionary<string, DoubleWritePolicy> s_doubleWritePolicyMap = new Dictionary<string, DoubleWritePolicy>(StringComparer.Ordinal)
        {
            ["doubleWritesAreErrors"] = DoubleWritePolicy.DoubleWritesAreErrors,
            ["allowSameContentDoubleWrites"] = DoubleWritePolicy.AllowSameContentDoubleWrites,
            ["unsafeFirstDoubleWriteWins"] = DoubleWritePolicy.UnsafeFirstDoubleWriteWins,
        };

        private static readonly Dictionary<string, bool> s_privilegeLevel = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["standard"] = false,
            ["admin"]    = true,
        };

        // these values must be kept in sync with the ones defined on the BuildXL Script side
        private static readonly Dictionary<string, Process.AbsentPathProbeInUndeclaredOpaquesMode> s_absentPathProbeModes = new Dictionary<string, Process.AbsentPathProbeInUndeclaredOpaquesMode>(StringComparer.Ordinal)
        {
            ["unsafe"] = Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe,
            ["strict"] = Process.AbsentPathProbeInUndeclaredOpaquesMode.Strict,
            ["relaxed"] = Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed,
        };

        internal const string ExecuteFunctionName = "execute";
        internal const string CreateServiceFunctionName = "createService";

        private SymbolAtom m_executeTool;
        private SymbolAtom m_executeArguments;
        private SymbolAtom m_executeWorkingDirectory;
        private SymbolAtom m_executeDependencies;
        private SymbolAtom m_executeImplicitOutputs;
        private SymbolAtom m_executeOptionalImplicitOutputs;
        private SymbolAtom m_executeOutputs;
        private SymbolAtom m_executeDirectoryOutputKind;
        private SymbolAtom m_executeDirectoryOutputDirectory;
        private SymbolAtom m_executeFileOrPathOutputExistence;
        private SymbolAtom m_executeFileOrPathOutputArtifact;
        private SymbolAtom m_executeConsoleInput;
        private SymbolAtom m_executeConsoleOutput;
        private SymbolAtom m_executeConsoleError;
        private SymbolAtom m_executeEnvironmentVariables;
        private SymbolAtom m_executeAcquireSemaphores;
        private SymbolAtom m_executeAcquireMutexes;
        private SymbolAtom m_executeSuccessExitCodes;
        private SymbolAtom m_executeRetryExitCodes;
        private SymbolAtom m_executeTempDirectory;
        private SymbolAtom m_executeUnsafe;
        private SymbolAtom m_executeIsLight;
        private SymbolAtom m_executeRunInContainer;
        private SymbolAtom m_executeContainerIsolationLevel;
        private SymbolAtom m_executeDoubleWritePolicy;
        private SymbolAtom m_executeAllowUndeclaredSourceReads;
        private SymbolAtom m_executeKeepOutputsWritable;
        private SymbolAtom m_privilegeLevel;
        private SymbolAtom m_disableCacheLookup;
        private SymbolAtom m_executeWarningRegex;
        private SymbolAtom m_executeErrorRegex;
        private SymbolAtom m_executeTags;
        private SymbolAtom m_executeServiceShutdownCmd;
        private SymbolAtom m_executeServiceFinalizationCmds;
        private SymbolAtom m_executeServicePipDependencies;
        private SymbolAtom m_executeDescription;
        private SymbolAtom m_executeAbsentPathProbeInUndeclaredOpaqueMode;
        private SymbolAtom m_executeAdditionalTempDirectories;
        private SymbolAtom m_executeAllowedSurvivingChildProcessNames;
        private SymbolAtom m_executeNestedProcessTerminationTimeoutMs;
        private SymbolAtom m_executeDependsOnCurrentHostOSDirectories;
        private SymbolAtom m_toolTimeoutInMilliseconds;
        private SymbolAtom m_toolWarningTimeoutInMilliseconds;
        private SymbolAtom m_argN;
        private SymbolAtom m_argV;
        private SymbolAtom m_argValues;
        private SymbolAtom m_argArgs;
        private StringId m_argResponseFileForRemainingArgumentsForce;
        private SymbolAtom m_envName;
        private SymbolAtom m_envValue;
        private SymbolAtom m_envSeparator;
        private SymbolAtom m_priority;
        private SymbolAtom m_toolExe;
        private SymbolAtom m_toolNestedTools;
        private SymbolAtom m_toolRuntimeDependencies;
        private SymbolAtom m_toolRuntimeDirectoryDependencies;
        private SymbolAtom m_toolRuntimeEnvironment;
        private SymbolAtom m_toolUntrackedDirectories;
        private SymbolAtom m_toolUntrackedDirectoryScopes;
        private SymbolAtom m_toolUntrackedFiles;
        private SymbolAtom m_toolDependsOnWindowsDirectories;
        private SymbolAtom m_toolDependsOnCurrentHostOSDirectories;
        private SymbolAtom m_toolDependsOnAppDataDirectory;
        private SymbolAtom m_toolPrepareTempDirectory;
        private SymbolAtom m_toolDescription;
        private SymbolAtom m_weight;
        
        private SymbolAtom m_runtimeEnvironmentMinimumOSVersion;
        private SymbolAtom m_runtimeEnvironmentMaximumOSVersion;
        private SymbolAtom m_runtimeEnvironmentMinimumClrVersion;
        private SymbolAtom m_runtimeEnvironmentMaximumClrVersion;
        private SymbolAtom m_runtimeEnvironmentClrOverride;
        private SymbolAtom m_clrConfigInstallRoot;
        private SymbolAtom m_clrConfigVersion;
        private SymbolAtom m_clrConfigGuiFromShim;
        private SymbolAtom m_clrConfigDbgJitDebugLaunchSetting;
        private SymbolAtom m_clrConfigOnlyUseLatestClr;
        private SymbolAtom m_clrConfigDefaultVersion;
        private StringId m_clrConfigComplusInstallRoot;
        private StringId m_clrConfigComplusVersion;
        private StringId m_clrConfigComplusNoGuiFromShim;
        private StringId m_clrConfigComplusDefaultVersion;
        private StringId m_clrConfigComplusDbgJitDebugLaunchSetting;
        private StringId m_clrConfigComplusOnlyUseLatestClr;
        private SymbolAtom m_versionBuildNumber;
        private SymbolAtom m_versionMajor;
        private SymbolAtom m_versionMinor;
        private SymbolAtom m_versionRevision;
        private SymbolAtom m_unsafeUntrackedPaths;
        private SymbolAtom m_unsafeUntrackedScopes;
        private SymbolAtom m_unsafeHasUntrackedChildProcesses;
        private SymbolAtom m_unsafeAllowPreservedOutputs;
        private SymbolAtom m_unsafePassThroughEnvironmentVariables;
        private SymbolAtom m_unsafePreserveOutputWhitelist;

        private SymbolAtom m_semaphoreInfoLimit;
        private SymbolAtom m_semaphoreInfoName;
        private SymbolAtom m_semaphoreInfoIncrementBy;

        private CallSignature ExecuteSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.ExecuteArgumentsType),
            returnType: AmbientTypes.ExecuteResultType);

        private CallSignature CreateServiceSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.CreateServiceArgumentsType),
            returnType: AmbientTypes.CreateServiceResultType);

        private EvaluationResult Execute(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return ScheduleProcessPip(context, env, args, ServicePipKind.None);
        }

        private EvaluationResult CreateService(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return ScheduleProcessPip(context, env, args, ServicePipKind.Service);
        }

        private EvaluationResult ScheduleProcessPip(Context context, ModuleLiteral env, EvaluationStackFrame args, ServicePipKind serviceKind)
        {
            var obj = Args.AsObjectLiteral(args, 0);

            if (!TryScheduleProcessPip(context, obj, serviceKind, out var processOutputs, out _))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            return EvaluationResult.Create(BuildExecuteOutputs(context, env, processOutputs, serviceKind != ServicePipKind.None));
        }

        private void InitializeProcessNames()
        {
            // Execute.
            m_executeTool = Symbol("tool");
            m_executeArguments = Symbol("arguments");
            m_executeWorkingDirectory = Symbol("workingDirectory");
            m_executeDependencies = Symbol("dependencies");
            m_executeImplicitOutputs = Symbol("implicitOutputs");
            m_executeOptionalImplicitOutputs = Symbol("optionalImplicitOutputs");
            m_executeOutputs = Symbol("outputs");
            m_executeDirectoryOutputKind = Symbol("kind");
            m_executeDirectoryOutputDirectory = Symbol("directory");
            m_executeFileOrPathOutputExistence = Symbol("existence");
            m_executeFileOrPathOutputArtifact = Symbol("artifact");
            m_executeConsoleInput = Symbol("consoleInput");
            m_executeConsoleOutput = Symbol("consoleOutput");
            m_executeConsoleError = Symbol("consoleError");
            m_executeEnvironmentVariables = Symbol("environmentVariables");
            m_executeAcquireSemaphores = Symbol("acquireSemaphores");
            m_executeAcquireMutexes = Symbol("acquireMutexes");
            m_executeSuccessExitCodes = Symbol("successExitCodes");
            m_executeRetryExitCodes = Symbol("retryExitCodes");
            m_executeTempDirectory = Symbol("tempDirectory");
            m_executeUnsafe = Symbol("unsafe");
            m_executeIsLight = Symbol("isLight");
            m_executeRunInContainer = Symbol("runInContainer");
            m_executeContainerIsolationLevel = Symbol("containerIsolationLevel");
            m_executeDoubleWritePolicy = Symbol("doubleWritePolicy");
            m_executeAllowUndeclaredSourceReads = Symbol("allowUndeclaredSourceReads");
            m_executeAbsentPathProbeInUndeclaredOpaqueMode = Symbol("absentPathProbeInUndeclaredOpaquesMode");

            m_executeKeepOutputsWritable = Symbol("keepOutputsWritable");
            m_privilegeLevel = Symbol("privilegeLevel");
            m_disableCacheLookup = Symbol("disableCacheLookup");
            m_executeTags = Symbol("tags");
            m_executeServiceShutdownCmd = Symbol("serviceShutdownCmd");
            m_executeServiceFinalizationCmds = Symbol("serviceFinalizationCmds");
            m_executeServicePipDependencies = Symbol("servicePipDependencies");
            m_executeDescription = Symbol("description");
            m_executeAdditionalTempDirectories = Symbol("additionalTempDirectories");
            m_executeWarningRegex = Symbol("warningRegex");
            m_executeErrorRegex = Symbol("errorRegex");
            m_executeAllowedSurvivingChildProcessNames = Symbol("allowedSurvivingChildProcessNames");
            m_executeNestedProcessTerminationTimeoutMs = Symbol("nestedProcessTerminationTimeoutMs");
            m_executeDependsOnCurrentHostOSDirectories = Symbol("dependsOnCurrentHostOSDirectories");
            m_weight = Symbol("weight");
            m_priority = Symbol("priority");

            m_argN = Symbol("n");
            m_argV = Symbol("v");
            m_argValues = Symbol("values");
            m_argArgs = Symbol("args");
            m_argResponseFileForRemainingArgumentsForce = StringId.Create(StringTable, "__force");

            // Environment variable.
            m_envName = Symbol("name");
            m_envValue = Symbol("value");
            m_envSeparator = Symbol("separator");

            // Tool.
            m_toolExe = Symbol("exe");
            m_toolNestedTools = Symbol("nestedTools");
            m_toolRuntimeDependencies = Symbol("runtimeDependencies");
            m_toolRuntimeDirectoryDependencies = Symbol("runtimeDirectoryDependencies");
            m_toolRuntimeEnvironment = Symbol("runtimeEnvironment");
            m_toolUntrackedDirectories = Symbol("untrackedDirectories");
            m_toolUntrackedDirectoryScopes = Symbol("untrackedDirectoryScopes");
            m_toolUntrackedFiles = Symbol("untrackedFiles");
            m_toolDependsOnWindowsDirectories = Symbol("dependsOnWindowsDirectories");
            m_toolDependsOnAppDataDirectory = Symbol("dependsOnAppDataDirectory");
            m_toolDependsOnCurrentHostOSDirectories = Symbol("dependsOnCurrentHostOSDirectories");
            m_toolPrepareTempDirectory = Symbol("prepareTempDirectory");
            m_toolTimeoutInMilliseconds = Symbol("timeoutInMilliseconds");
            m_toolWarningTimeoutInMilliseconds = Symbol("warningTimeoutInMilliseconds");
            m_toolDescription = Symbol("description");

            // Runtime environment.
            m_runtimeEnvironmentMinimumOSVersion = Symbol("minimumOSVersion");
            m_runtimeEnvironmentMaximumOSVersion = Symbol("maximumOSVersion");
            m_runtimeEnvironmentMinimumClrVersion = Symbol("minimumClrVersion");
            m_runtimeEnvironmentMaximumClrVersion = Symbol("maximumClrVersion");
            m_runtimeEnvironmentClrOverride = Symbol("clrOverride");

            // m_clrConfig
            m_clrConfigInstallRoot = Symbol("installRoot");
            m_clrConfigVersion = Symbol("version");
            m_clrConfigGuiFromShim = Symbol("guiFromShim");
            m_clrConfigDbgJitDebugLaunchSetting = Symbol("dbgJitDebugLaunchSetting");
            m_clrConfigDefaultVersion = Symbol("defaultVersion");
            m_clrConfigOnlyUseLatestClr = Symbol("onlyUseLatestClr");
            m_clrConfigComplusInstallRoot = StringId.Create(StringTable, "COMPLUS_InstallRoot");
            m_clrConfigComplusVersion = StringId.Create(StringTable, "COMPLUS_Version");
            m_clrConfigComplusNoGuiFromShim = StringId.Create(StringTable, "COMPLUS_NoGuiFromShim");
            m_clrConfigComplusDbgJitDebugLaunchSetting = StringId.Create(StringTable, "COMPLUS_DbgJitDebugLaunchSetting");
            m_clrConfigComplusDefaultVersion = StringId.Create(StringTable, "COMPLUS_DefaultVersion");
            m_clrConfigComplusOnlyUseLatestClr = StringId.Create(StringTable, "COMPLUS_OnlyUseLatestClr");

            // Version
            m_versionBuildNumber = Symbol("buildNumber");
            m_versionMajor = Symbol("major");
            m_versionMinor = Symbol("minor");
            m_versionRevision = Symbol("revision");


            // Unsafe.
            m_unsafeUntrackedPaths = Symbol("untrackedPaths");
            m_unsafeUntrackedScopes = Symbol("untrackedScopes");
            m_unsafeHasUntrackedChildProcesses = Symbol("hasUntrackedChildProcesses");
            m_unsafeAllowPreservedOutputs = Symbol("allowPreservedOutputs");
            m_unsafePassThroughEnvironmentVariables = Symbol("passThroughEnvironmentVariables");
            m_unsafePreserveOutputWhitelist = Symbol("preserveOutputWhitelist");

            // Semaphore info.
            m_semaphoreInfoLimit = Symbol("limit");
            m_semaphoreInfoName = Symbol("name");
            m_semaphoreInfoIncrementBy = Symbol("incrementBy");

        }


        private bool TryScheduleProcessPip(Context context, ObjectLiteral obj, ServicePipKind serviceKind, out ProcessOutputs processOutputs, out Process pip)
        {
            using (var processBuilder = ProcessBuilder.Create(context.PathTable, context.FrontEndContext.GetPipDataBuilder()))
            {
                ProcessExecuteArguments(context, obj, processBuilder, serviceKind);

                if (!context.GetPipConstructionHelper().TryAddProcess(processBuilder, out processOutputs, out pip))
                {
                    // Error has been logged
                    return false;
                }

                return true;
            }
        }

        private void ProcessExecuteArguments(Context context, ObjectLiteral obj, ProcessBuilder processBuilder, ServicePipKind serviceKind)
        {
            // Tool.
            var tool = Converter.ExtractObjectLiteral(obj, m_executeTool);
            ProcessTool(context, tool, processBuilder);

            // Description.
            var description = Converter.ExtractString(obj, m_executeDescription, allowUndefined: true);
            processBuilder.Usage = string.IsNullOrEmpty(description)
                ? PipData.Invalid
                : PipDataBuilder.CreatePipData(context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, description);

            // Timeouts.
            var timeout = Converter.ExtractOptionalInt(obj, m_toolTimeoutInMilliseconds);
            if (timeout.HasValue)
            {
                processBuilder.Timeout = TimeSpan.FromMilliseconds(timeout.Value);
            }
            var warningTimeout = Converter.ExtractOptionalInt(obj, m_toolWarningTimeoutInMilliseconds);
            if (warningTimeout.HasValue)
            {
                processBuilder.WarningTimeout = TimeSpan.FromMilliseconds(warningTimeout.Value);
            }

            // Arguments.
            var arguments = Converter.ExtractArrayLiteral(obj, m_executeArguments);
            TransformerExecuteArgumentsProcessor.ProcessArguments(context, processBuilder, arguments);

            // Working directory.
            processBuilder.WorkingDirectory = Converter.ExtractDirectory(obj, m_executeWorkingDirectory);

            // Dependencies.
            var dependencies = Converter.ExtractArrayLiteral(obj, m_executeDependencies, allowUndefined: true);
            if (dependencies != null)
            {
                for (var i = 0; i < dependencies.Length; i++)
                {
                    var value = dependencies[i];
                    if (!value.IsUndefined)
                    {
                        var oneInputFileConvContext = new ConversionContext(pos: i, objectCtx: dependencies);
                        ProcessImplicitDependency(processBuilder, value, convContext: oneInputFileConvContext);
                    }
                }
            }

            // TODO: remove in favor of 'outputs' when the corresponding obsolete field is removed
            // Implicit outputs.
            var implicitOutputs = Converter.ExtractArrayLiteral(obj, m_executeImplicitOutputs, allowUndefined: true);
            if (implicitOutputs != null)
            {
                ProcessImplicitOutputs(processBuilder, implicitOutputs, FileExistence.Required);
            }

            // TODO: remove in favor of 'outputs' when the corresponding obsolete field is removed
            // Optional implicit outputs.
            var optionalImplicitOutputs = Converter.ExtractArrayLiteral(obj, m_executeOptionalImplicitOutputs, allowUndefined: true);
            if (optionalImplicitOutputs != null)
            {
                ProcessImplicitOutputs(processBuilder, optionalImplicitOutputs, FileExistence.Temporary);
            }

            // Tool outputs
            var outputs = Converter.ExtractArrayLiteral(obj, m_executeOutputs, allowUndefined: true);
            if (outputs != null)
            {
                ProcessOutputs(context, processBuilder, outputs);
            }

            // Console input.
            var consoleInput = obj[m_executeConsoleInput];
            if (!consoleInput.IsUndefined)
            {
                if (consoleInput.Value is FileArtifact stdInFile)
                {
                    processBuilder.StandardInput = StandardInput.CreateFromFile(stdInFile);
                    processBuilder.AddInputFile(stdInFile);
                }
                else
                {
                    var pipData = ProcessData(context, consoleInput, new ConversionContext(name: m_executeConsoleInput, allowUndefined: false, objectCtx: obj));
                    processBuilder.StandardInput = StandardInput.CreateFromData(pipData);
                }
            }

            // Console output.
            var consoleOutput = Converter.ExtractPath(obj, m_executeConsoleOutput, allowUndefined: true);
            if (consoleOutput.IsValid)
            {
                processBuilder.SetStandardOutputFile(consoleOutput);
            }

            // Console error
            var consoleErrorOutput = Converter.ExtractPath(obj, m_executeConsoleError, allowUndefined: true);
            if (consoleErrorOutput.IsValid)
            {
                processBuilder.SetStandardErrorFile(consoleErrorOutput);
            }

            // Environment variables.
            var environmentVariables = Converter.ExtractArrayLiteral(obj, m_executeEnvironmentVariables, allowUndefined: true);
            if (environmentVariables != null)
            {
                using (var pipDataBuilderWrapper = context.FrontEndContext.GetPipDataBuilder())
                {
                    var pipDataBuilder = pipDataBuilderWrapper.Instance;

                    for (var i = 0; i < environmentVariables.Length; i++)
                    {
                        var environmentVariable = Converter.ExpectObjectLiteral(
                            environmentVariables[i],
                            new ConversionContext(pos: i, objectCtx: environmentVariables));
                        ProcessEnvironmentVariable(context, processBuilder, pipDataBuilder, environmentVariable);
                        pipDataBuilder.Clear();
                    }
                }
            }

            // TODO: Regex. Should we follow ECMA, C#, JavaScript?

            // Weight.
            var weight = Converter.ExtractOptionalInt(obj, m_weight);
            if (weight != null)
            {
                processBuilder.Weight = weight.Value;
            }

            // Priority.
            var priority = Converter.ExtractOptionalInt(obj, m_priority);
            if (priority != null)
            {
                processBuilder.Priority = priority.Value;
            }

            // Acquired semaphores.
            var acquireSemaphores = Converter.ExtractArrayLiteral(obj, m_executeAcquireSemaphores, allowUndefined: true);
            if (acquireSemaphores != null)
            {
                ProcessAcquireSemaphores(context, processBuilder, acquireSemaphores);
            }

            // Acquired mutexes.
            var acquireMutexes = Converter.ExtractArrayLiteral(obj, m_executeAcquireMutexes, allowUndefined: true);
            if (acquireMutexes != null)
            {
                ProcessAcquireMutexes(processBuilder, acquireMutexes);
            }

            // Exit Codes
            processBuilder.SuccessExitCodes = ProcessOptionalIntArray(obj, m_executeSuccessExitCodes);
            processBuilder.RetryExitCodes = ProcessOptionalIntArray(obj, m_executeRetryExitCodes);

            // Temporary directory.
            var tempDirectory = Converter.ExtractDirectory(obj, m_executeTempDirectory, allowUndefined: true);
            if (tempDirectory.IsValid)
            {
                processBuilder.SetTempDirectory(tempDirectory);
            }
            processBuilder.AdditionalTempDirectories = ProcessOptionalPathArray(obj, m_executeAdditionalTempDirectories, strict: false, skipUndefined: true);

            // Unsafe options.
            var unsafeOptions = Converter.ExtractObjectLiteral(obj, m_executeUnsafe, allowUndefined: true);
            if (unsafeOptions != null)
            {
                ProcessUnsafeOptions(context, processBuilder, unsafeOptions);
            }

            // GlobalUnsafePassthroughEnvironmentVariables
            processBuilder.SetGlobalPassthroughEnvironmentVariable(context.FrontEndHost.Configuration.FrontEnd.GlobalUnsafePassthroughEnvironmentVariables, context.StringTable);

            // Set outputs to remain writable.
            var keepOutputsWritable = Converter.ExtractOptionalBoolean(obj, m_executeKeepOutputsWritable);
            if (keepOutputsWritable == true)
            {
                processBuilder.Options |= Process.Options.OutputsMustRemainWritable;
            }

            // Set outputs to remain writable.
            var privilegeLevel = Converter.ExtractStringLiteral(obj, m_privilegeLevel, s_privilegeLevel.Keys, allowUndefined: true);
            if (privilegeLevel != null && s_privilegeLevel.TryGetValue(privilegeLevel, out bool level) && level)
            {
                processBuilder.Options |= Process.Options.RequiresAdmin;
            }

            var absentPathProbeMode = Converter.ExtractStringLiteral(obj, m_executeAbsentPathProbeInUndeclaredOpaqueMode, s_absentPathProbeModes.Keys, allowUndefined: true);
            if (absentPathProbeMode != null)
            {
                processBuilder.AbsentPathProbeUnderOpaquesMode = s_absentPathProbeModes[absentPathProbeMode];
            }

            // Set custom warning regex.
            var warningRegex = Converter.ExtractString(obj, m_executeWarningRegex, allowUndefined: true);
            if (warningRegex != null)
            {
                processBuilder.WarningRegex = new RegexDescriptor(StringId.Create(context.StringTable, warningRegex), RegexOptions.None);
            }

            var errorRegex = Converter.ExtractString(obj, m_executeErrorRegex, allowUndefined: true);
            if (errorRegex != null)
            {
                processBuilder.ErrorRegex = new RegexDescriptor(StringId.Create(context.StringTable, errorRegex), RegexOptions.None);
            }

            // Tags.
            var tags = Converter.ExtractArrayLiteral(obj, m_executeTags, allowUndefined: true);
            if (tags != null && tags.Count > 0)
            {
                var tagIds = new StringId[tags.Count];
                for (var i = 0; i < tags.Count; i++)
                {
                    var tag = Converter.ExpectString(tags[i], context: new ConversionContext(pos: i, objectCtx: tags));
                    tagIds[i] = StringId.Create(context.StringTable, tag);
                }
                processBuilder.Tags = ReadOnlyArray<StringId>.FromWithoutCopy(tagIds);
            }

            // service pip dependencies (only if this pip is not a service)
            processBuilder.ServiceKind = serviceKind;
            if (serviceKind != ServicePipKind.Service)
            {
                var servicePipDependencies = Converter.ExtractArrayLiteral(obj, m_executeServicePipDependencies, allowUndefined: true);
                if (servicePipDependencies != null)
                {
                    for (var i = 0; i < servicePipDependencies.Length; i++)
                    {
                        var value = servicePipDependencies[i];
                        if (!value.IsUndefined)
                        {
                            var servicePip = Converter.ExpectPipId(value, new ConversionContext(pos: i, objectCtx: servicePipDependencies));
                            processBuilder.AddServicePipDependency(servicePip);
                        }
                    }
                }
            }
            else
            {
                var shutdownCommand = Converter.ExtractObjectLiteral(obj, m_executeServiceShutdownCmd, allowUndefined: false);
                TryScheduleProcessPip(context, shutdownCommand, ServicePipKind.ServiceShutdown, out _, out var shutdownProcessPip);

                processBuilder.ShutDownProcessPipId = shutdownProcessPip.PipId;

                var finalizationCommands = Converter.ExtractArrayLiteral(obj, m_executeServiceFinalizationCmds, allowUndefined: true);
                if (finalizationCommands != null)
                {
                    var finalizationPipIds = new PipId[finalizationCommands.Count];
                    for (var i = 0; i < finalizationCommands.Count; i++)
                    {
                        var executePipArgs = Converter.ExpectObjectLiteral(finalizationCommands[i], new ConversionContext(pos: i, objectCtx: finalizationCommands));
                        finalizationPipIds[i] = InterpretFinalizationPipArguments(context, executePipArgs);
                    }

                    processBuilder.FinalizationPipIds = ReadOnlyArray<PipId>.FromWithoutCopy(finalizationPipIds);
                }
            }

            // Light process flag.
            if (Converter.ExtractOptionalBoolean(obj, m_executeIsLight) == true)
            {
                processBuilder.Options |= Process.Options.IsLight;
            }

            // Run in container flag.
            // The value is set based on the default but overridden if the field is explicitly defined for the pip
            var runInContainer = Converter.ExtractOptionalBoolean(obj, m_executeRunInContainer);
            if (!runInContainer.HasValue)
            {
                runInContainer = context.FrontEndHost.Configuration.Sandbox.ContainerConfiguration.RunInContainer();
            }

            if (runInContainer.Value)
            {
                processBuilder.Options |= Process.Options.NeedsToRunInContainer;
            }

            // Container isolation level
            // The value is set based on the default but overridden if the field is explicitly defined for the pip
            var containerIsolationLevel = Converter.ExtractEnumValue<ContainerIsolationLevel>(obj, m_executeContainerIsolationLevel, allowUndefined: true);
            processBuilder.ContainerIsolationLevel = containerIsolationLevel.HasValue? 
                    containerIsolationLevel.Value : 
                    context.FrontEndHost.Configuration.Sandbox.ContainerConfiguration.ContainerIsolationLevel();

            // Container double write policy
            // The value is set based on the default but overridden if the field is explicitly defined for the pip
            var doubleWritePolicy = Converter.ExtractStringLiteral(obj, m_executeDoubleWritePolicy, s_doubleWritePolicyMap.Keys, allowUndefined: true);
            processBuilder.DoubleWritePolicy = doubleWritePolicy != null?
                    s_doubleWritePolicyMap[doubleWritePolicy] :
                    context.FrontEndHost.Configuration.Sandbox.UnsafeSandboxConfiguration.DoubleWritePolicy();
    
            // Allow undeclared source reads flag
            if (Converter.ExtractOptionalBoolean(obj, m_executeAllowUndeclaredSourceReads) == true)
            {
                processBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;
            }

            // disableCacheLookup flag
            if (Converter.ExtractOptionalBoolean(obj, m_disableCacheLookup) == true)
            {
                processBuilder.Options |= Process.Options.DisableCacheLookup;
            }

            // Surviving process settings.
            var allowedSurvivingChildProcessNames = Converter.ExtractArrayLiteral(obj, m_executeAllowedSurvivingChildProcessNames, allowUndefined: true);
            if (allowedSurvivingChildProcessNames != null && allowedSurvivingChildProcessNames.Count > 0)
            {
                var processNameAtoms = new PathAtom[allowedSurvivingChildProcessNames.Count];
                for (var i = 0; i < allowedSurvivingChildProcessNames.Count; i++)
                {
                    processNameAtoms[i] = Converter.ExpectPathAtomFromStringOrPathAtom(
                        context.StringTable,
                        allowedSurvivingChildProcessNames[i],
                        context: new ConversionContext(pos: i, objectCtx: allowedSurvivingChildProcessNames));
                }
                processBuilder.AllowedSurvivingChildProcessNames = ReadOnlyArray<PathAtom>.FromWithoutCopy(processNameAtoms);
            }

            var nestedProcessTerminationTimeoutMs = Converter.ExtractNumber(obj, m_executeNestedProcessTerminationTimeoutMs, allowUndefined: true);
            if (nestedProcessTerminationTimeoutMs != null)
            {
                processBuilder.NestedProcessTerminationTimeout = TimeSpan.FromMilliseconds(nestedProcessTerminationTimeoutMs.Value);
            }

            var executeDependsOnCurrentHostOSDirectories = Converter.ExtractOptionalBoolean(obj, m_executeDependsOnCurrentHostOSDirectories);
            if (executeDependsOnCurrentHostOSDirectories == true)
            {
                processBuilder.AddCurrentHostOSDirectories();
            }
        }

        private void ProcessTool(Context context, ObjectLiteral tool, ProcessBuilder processBuilder)
        {
            var cachedTool = context.ContextTree.ToolDefinitionCache.GetOrAdd(
                tool,
                obj =>
                {
                    var cacheEntry = new CachedToolDefinition();
                    ProcessCachedTool(context, tool, cacheEntry);
                    return cacheEntry;
                });

            processBuilder.Executable = cachedTool.Executable;
            processBuilder.ToolDescription = cachedTool.ToolDescription;

            foreach (var inputFile in cachedTool.InputFiles)
            {
                processBuilder.AddInputFile(inputFile);
            }

            foreach (var inputDirectory in cachedTool.InputDirectories)
            {
                processBuilder.AddInputDirectory(inputDirectory);
            }

            foreach (var untrackedFile in cachedTool.UntrackedFiles)
            {
                processBuilder.AddUntrackedFile(untrackedFile);
            }

            foreach (var untrackedDirectory in cachedTool.UntrackedDirectories)
            {
                processBuilder.AddUntrackedDirectoryScope(untrackedDirectory);
            }

            foreach (var untracedDirectoryScope in cachedTool.UntrackedDirectoryScopes)
            {
                processBuilder.AddUntrackedDirectoryScope(untracedDirectoryScope);
            }

            foreach (var kv in cachedTool.EnvironmentVariables)
            {
                processBuilder.SetEnvironmentVariable(kv.Key, kv.Value);
            }

            if (cachedTool.DependsOnCurrentHostOSDirectories)
            {
                processBuilder.AddCurrentHostOSDirectories();
            }

            if (cachedTool.UntrackedWindowsDirectories)
            {
                if (!OperatingSystemHelper.IsUnixOS)
                {
                    processBuilder.AddCurrentHostOSDirectories();
                }
            }

            if (cachedTool.UntrackedAppDataDirectories)
            {
                processBuilder.AddUntrackedAppDataDirectories();
            }

            if (cachedTool.EnableTempDirectory)
            {
                processBuilder.EnableTempDirectory();
            }

        }

        private void ProcessCachedTool(Context context, ObjectLiteral tool, CachedToolDefinition cachedTool)
        {
            // TODO: Handle ToolCache again

            // Do the nested tools recursively first, so the outer most tool wins for settings
            var nestedTools = Converter.ExtractArrayLiteral(tool, m_toolNestedTools, allowUndefined: true);
            if (nestedTools != null)
            {
                for (var i = 0; i < nestedTools.Length; i++)
                {
                    var nestedTool = Converter.ExpectObjectLiteral(nestedTools[i], new ConversionContext(pos: i, objectCtx: nestedTools));
                    ProcessCachedTool(context, nestedTool, cachedTool);
                }
            }

            var executable = Converter.ExpectFile(tool[m_toolExe], strict: true, context: new ConversionContext(allowUndefined: false, name: m_toolExe, objectCtx: tool));
            cachedTool.Executable = executable;
            cachedTool.InputFiles.Add(executable);
            var toolDescription = Converter.ExpectString(tool[m_toolDescription], new ConversionContext(allowUndefined: true, name: m_toolDescription, objectCtx: tool));
            if (!string.IsNullOrEmpty(toolDescription))
            {
                cachedTool.ToolDescription = StringId.Create(context.StringTable, toolDescription);
            }

            ProcessOptionalFileArray(tool, m_toolRuntimeDependencies, file => cachedTool.InputFiles.Add(file), skipUndefined: false);
            ProcessOptionalStaticDirectoryArray(tool, m_toolRuntimeDirectoryDependencies, dir => cachedTool.InputDirectories.Add(dir.Root), skipUndefined: false);

            // TODO: Fix all callers, in the api this is limited to Directory.
            ProcessOptionalDirectoryOrPathArray(tool, m_toolUntrackedDirectoryScopes, dir => cachedTool.UntrackedDirectoryScopes.Add(dir), skipUndefined: false);
            ProcessOptionalDirectoryArray(tool, m_toolUntrackedDirectories, dir => cachedTool.UntrackedDirectories.Add(dir), skipUndefined: false);
            ProcessOptionalFileArray(tool, m_toolUntrackedFiles, file => cachedTool.UntrackedFiles.Add(file.Path), skipUndefined: false);

            var runtimeEnvironment = Converter.ExtractObjectLiteral(tool, m_toolRuntimeEnvironment, allowUndefined: true);
            if (runtimeEnvironment != null)
            {
                var clrOverride = Converter.ExtractObjectLiteral(runtimeEnvironment, m_runtimeEnvironmentClrOverride, allowUndefined: true);
                if (clrOverride != null)
                {
                    var installRoot = Converter.ExtractPathLike(clrOverride, m_clrConfigInstallRoot, allowUndefined: true);
                    if (installRoot.IsValid)
                    {
                        cachedTool.EnvironmentVariables[m_clrConfigComplusInstallRoot] = installRoot;
                    }

                    var version = Converter.ExtractString(clrOverride, m_clrConfigVersion, allowUndefined: true);
                    if (version != null)
                    {
                        cachedTool.EnvironmentVariables[m_clrConfigComplusVersion] = version;
                    }

                    var noGuiFromShim = Converter.ExtractOptionalBoolean(clrOverride, m_clrConfigGuiFromShim);
                    if (noGuiFromShim != null)
                    {
                        cachedTool.EnvironmentVariables[m_clrConfigComplusNoGuiFromShim] = noGuiFromShim.Value ? "1" : "0";
                    }

                    var dbgJitDebugLaunch = Converter.ExtractOptionalBoolean(clrOverride, m_clrConfigDbgJitDebugLaunchSetting);
                    if (dbgJitDebugLaunch != null)
                    {
                        cachedTool.EnvironmentVariables[m_clrConfigComplusDbgJitDebugLaunchSetting] = dbgJitDebugLaunch.Value ? "1" : "0";
                    }

                    var defaultVersion = Converter.ExtractString(clrOverride, m_clrConfigDefaultVersion);
                    if (defaultVersion != null)
                    {
                        cachedTool.EnvironmentVariables[m_clrConfigComplusDefaultVersion] = defaultVersion;
                    }

                    var onlyUseLatestClr = Converter.ExtractOptionalBoolean(clrOverride, m_clrConfigOnlyUseLatestClr);
                    if (onlyUseLatestClr != null)
                    {
                        cachedTool.EnvironmentVariables[m_clrConfigComplusOnlyUseLatestClr] = onlyUseLatestClr.Value ? "1" : "0";
                    }
                }
            }

            if (Converter.ExtractOptionalBoolean(tool, m_toolDependsOnWindowsDirectories) == true)
            {
                cachedTool.UntrackedWindowsDirectories = true;
            }

            if (Converter.ExtractOptionalBoolean(tool, m_toolDependsOnCurrentHostOSDirectories) == true)
            {
                cachedTool.DependsOnCurrentHostOSDirectories = true;
            }

            if (Converter.ExtractOptionalBoolean(tool, m_toolDependsOnAppDataDirectory) == true)
            {
                cachedTool.UntrackedAppDataDirectories = true;
            }

            if (Converter.ExtractOptionalBoolean(tool, m_toolPrepareTempDirectory) == true)
            {
                cachedTool.EnableTempDirectory = true;
            }
        }

        private static void ProcessOptionalFileArray(ObjectLiteral obj, SymbolAtom fieldName, Action<FileArtifact> addItem, bool skipUndefined)
        {
            var array = Converter.ExtractArrayLiteral(obj, fieldName, allowUndefined: true);
            if (array != null)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (skipUndefined && array[i].IsUndefined)
                    {
                        continue;
                    }

                    var file = Converter.ExpectFile(array[i], context: new ConversionContext(pos: i, objectCtx: array, name: fieldName));
                    addItem(file);
                }
            }
        }

        private static void ProcessOptionalStaticDirectoryArray(ObjectLiteral obj, SymbolAtom fieldName, Action<StaticDirectory> addItem, bool skipUndefined)
        {
            var array = Converter.ExtractArrayLiteral(obj, fieldName, allowUndefined: true);
            if (array != null)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (skipUndefined && array[i].IsUndefined)
                    {
                        continue;
                    }

                    var staticDir = Converter.ExpectStaticDirectory(array[i], context: new ConversionContext(pos: i, objectCtx: array, name: fieldName));
                    addItem(staticDir);
                }
            }
        }

        private static void ProcessOptionalDirectoryArray(ObjectLiteral obj, SymbolAtom fieldName, Action<DirectoryArtifact> addItem, bool skipUndefined)
        {
            var array = Converter.ExtractArrayLiteral(obj, fieldName, allowUndefined: true);
            if (array != null)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (skipUndefined && array[i].IsUndefined)
                    {
                        continue;
                    }

                    var directory = Converter.ExpectDirectory(array[i], context: new ConversionContext(pos: i, objectCtx: array, name: fieldName));
                    addItem(directory);
                }
            }
        }

        private static void ProcessOptionalDirectoryOrPathArray(ObjectLiteral obj, SymbolAtom fieldName, Action<DirectoryArtifact> addItem, bool skipUndefined)
        {
            var array = Converter.ExtractArrayLiteral(obj, fieldName, allowUndefined: true);
            if (array != null)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (skipUndefined && array[i].IsUndefined)
                    {
                        continue;
                    }

                    Converter.ExpectPathOrDirectory(array[i], out var path, out var directory, new ConversionContext(pos: i, objectCtx: array, name: fieldName));
                    if (directory.IsValid)
                    {
                        addItem(directory);
                    }
                    else if (path.IsValid)
                    {
                        addItem(DirectoryArtifact.CreateWithZeroPartialSealId(path));
                    }
                }
            }
        }

        private static ReadOnlyArray<AbsolutePath> ProcessOptionalPathArray(ObjectLiteral obj, SymbolAtom fieldName, bool strict, bool skipUndefined)
        {
            var array = Converter.ExtractArrayLiteral(obj, fieldName, allowUndefined: true);
            if (array != null && array.Length > 0)
            {
                var items = new AbsolutePath[array.Length];

                for (var i = 0; i < array.Length; i++)
                {
                    if (skipUndefined && array[i].IsUndefined)
                    {
                        continue;
                    }

                    var path = Converter.ExpectPath(array[i], strict: strict, context: new ConversionContext(pos: i, objectCtx: array, name: fieldName));
                    items[i] = path;
                }

                return ReadOnlyArray<AbsolutePath>.FromWithoutCopy(items);
            }

            return ReadOnlyArray<AbsolutePath>.Empty;
        }


        private static ReadOnlyArray<int> ProcessOptionalIntArray(ObjectLiteral obj, SymbolAtom fieldName)
        {
            var array = Converter.ExtractArrayLiteral(obj, fieldName, allowUndefined: true);
            if (array != null && array.Length > 0)
            {
                var items = new int[array.Length];

                for (var i = 0; i < array.Length; i++)
                {
                    var value = Converter.ExpectNumber(array[i], context: new ConversionContext(pos: i, objectCtx: array, name: fieldName));
                    items[i] = value;
                }

                return ReadOnlyArray<int>.FromWithoutCopy(items);
            }

            return ReadOnlyArray<int>.Empty;
        }

        /// <summary>
        /// Process tool outputs. Declared in DScript as:
        /// export type Output = Path | File | Directory | DirectoryOutput | FileOrPathOutput;
        /// </summary>
        private void ProcessOutputs(Context context, ProcessBuilder processBuilder, ArrayLiteral outputs)
        {
            for (var i = 0; i < outputs.Length; ++i)
            {
                var output = outputs[i];
                if (output.IsUndefined)
                {
                    continue;
                }

                Contract.Assert(!output.IsUndefined);
                Contract.Assert(context != null);
                Contract.Assert(processBuilder != null);

                // A file artifact (path or file) is interpreted as a required output by default
                if (output.Value is FileArtifact fileArtifact)
                {
                    processBuilder.AddOutputFile(fileArtifact, FileExistence.Required);
                }
                else if (output.Value is AbsolutePath absolutePath)
                {
                    processBuilder.AddOutputFile(absolutePath, FileExistence.Required);
                }
                // A directory artifact is interpreted as a regular (exclusive) opaque directory
                else if (output.Value is DirectoryArtifact directoryArtifact)
                {
                    processBuilder.AddOutputDirectory(directoryArtifact, SealDirectoryKind.Opaque);
                }
                else
                {
                    // Here we should find DirectoryOutput or FileOrPathOutput, object literals in both cases
                    var objectLiteral = Converter.ExpectObjectLiteral(output, context: new ConversionContext(pos: i, objectCtx: outputs));

                    var artifact = Converter.ExtractFileLike(objectLiteral, m_executeFileOrPathOutputArtifact, allowUndefined: true);
                    // If 'artifact' is defined, then we are in the FileOrPathOutput case, and therefore we expect 'existence' to be defined as well
                    if (artifact.IsValid)
                    {
                        var existence = Converter.ExtractStringLiteral(objectLiteral, m_executeFileOrPathOutputExistence, s_fileExistenceKindMap.Keys, allowUndefined: false);
                        processBuilder.AddOutputFile(artifact, s_fileExistenceKindMap[existence]);
                    }
                    else
                    {
                        // This should be the DirectoryOutput case then, and both fields should be defined
                        var directory = Converter.ExtractDirectory(objectLiteral, m_executeDirectoryOutputDirectory, allowUndefined: false);
                        var outputDirectoryKind = Converter.ExtractStringLiteral(objectLiteral, m_executeDirectoryOutputKind, s_outputDirectoryKind, allowUndefined: false);

                        if (outputDirectoryKind == "shared")
                        {
                            var reservedDirectory = context.GetPipConstructionHelper().ReserveSharedOpaqueDirectory(directory);
                            processBuilder.AddOutputDirectory(reservedDirectory, SealDirectoryKind.SharedOpaque);
                        }
                        else
                        {
                            processBuilder.AddOutputDirectory(directory, SealDirectoryKind.Opaque);
                        }
                    }
                }
            }
        }

        private void ProcessEnvironmentVariable(Context context, ProcessBuilder processBuilder, PipDataBuilder pipDataBuilder, ObjectLiteral obj)
        {
            // Name of the environment variable.
            var n = obj[m_envName].Value as string;

            if (string.IsNullOrWhiteSpace(n))
            {
                throw new InputValidationException(
                    I($"Invalid environment variable name '{n}'"),
                    new ErrorContext(name: m_envName, objectCtx: obj));
            }

            if (BuildParameters.DisallowedTempVariables.Contains(n))
            {
                throw new InputValidationException(
                    I($"Setting the '{n}' environment variable is disallowed"),
                    new ErrorContext(name: m_envName, objectCtx: obj));
            }

            var property = obj[m_envValue];
            if (property.IsUndefined)
            {
                throw new InputValidationException(
                    I($"Value of the '{n}' environment variable is undefined"),
                    new ErrorContext(name: m_envValue, objectCtx: obj));
            }

            var convContext = new ConversionContext(name: m_argV, objectCtx: obj);
            var sepId = context.FrontEndContext.StringTable.Empty;

            var v = property.Value;
            switch (ArgTypeOf(context, property, ref convContext))
            {
                case ArgType.IsString:
                    var vStr = v as string;
                    Contract.Assume(vStr != null);
                    pipDataBuilder.Add(vStr);
                    break;
                case ArgType.IsBoolean:
                    Contract.Assume(v is bool);
                    pipDataBuilder.Add(((bool)v).ToString());
                    break;
                case ArgType.IsNumber:
                    Contract.Assume(v is int);
                    pipDataBuilder.Add(((int)v).ToString(CultureInfo.InvariantCulture));
                    break;
                case ArgType.IsFile:
                    var path = Converter.ExpectPath(property, strict: false, context: convContext);
                    pipDataBuilder.Add(path);
                    break;
                case ArgType.IsArrayOfFile:
                    var pathArr = v as ArrayLiteral;
                    Contract.Assume(pathArr != null);

                    for (var j = 0; j < pathArr.Length; j++)
                    {
                        pipDataBuilder.Add(Converter.ExpectPath(pathArr[j], strict: false, context: new ConversionContext(pos: j, objectCtx: pathArr)));
                    }

                    var sep = obj[m_envSeparator].Value as string;

                    if (sep != null && string.IsNullOrWhiteSpace(sep))
                    {
                        throw new InputValidationException(
                            I($"Path separator for the '{n}' environment variable is empty"),
                            new ErrorContext(name: m_envSeparator, objectCtx: obj));
                    }

                    sep = sep ?? System.IO.Path.PathSeparator.ToString();
                    sepId = StringId.Create(context.FrontEndContext.StringTable, sep);
                    break;
            }

            processBuilder.SetEnvironmentVariable(
                StringId.Create(context.StringTable, n),
                pipDataBuilder.ToPipData(sepId, PipDataFragmentEscaping.NoEscaping));
        }

        private void ProcessAcquireSemaphores(Context context, ProcessBuilder processBuilder, ArrayLiteral semaphores)
        {
            for (var i = 0; i < semaphores.Length; ++i)
            {
                var semaphore = Converter.ExpectObjectLiteral(semaphores[i], new ConversionContext(pos: i, objectCtx: semaphores, name: m_executeAcquireSemaphores));
                var name = Converter.ExpectString(
                    semaphore[m_semaphoreInfoName],
                    new ConversionContext(name: m_semaphoreInfoName, objectCtx: semaphore));
                var limit = Converter.ExpectNumber(
                    semaphore[m_semaphoreInfoLimit],
                    context: new ConversionContext(name: m_semaphoreInfoLimit, objectCtx: semaphore));
                var incrementBy = Converter.ExpectNumber(
                    semaphore[m_semaphoreInfoIncrementBy],
                    context: new ConversionContext(name: m_semaphoreInfoIncrementBy, objectCtx: semaphore));

                processBuilder.SetSemaphore(name, limit, incrementBy);
            }
        }

        private static void ProcessAcquireMutexes(ProcessBuilder processBuilder, ArrayLiteral mutexes)
        {
            for (var i = 0; i < mutexes.Length; ++i)
            {
                var name = Converter.ExpectString(mutexes[i], new ConversionContext(pos: i, objectCtx: mutexes));
                processBuilder.SetSemaphore(name, 1, 1);
            }
        }

        private static void ProcessExitCodes(ProcessBuilder processBuilder, ArrayLiteral exitCodesLiteral, Action<ProcessBuilder, int[]> processBuilderAction)
        {
            var exitCodes = new int[exitCodesLiteral.Length];

            for (var i = 0; i < exitCodesLiteral.Length; ++i)
            {
                var exitCode = Converter.ExpectNumber(
                    exitCodesLiteral[i],
                    strict: true,
                    context: new ConversionContext(pos: i, objectCtx: exitCodesLiteral));
                exitCodes[i] = exitCode;
            }

            processBuilderAction(processBuilder, exitCodes);
        }

        private void ProcessUnsafeOptions(Context context, ProcessBuilder processBuilder, ObjectLiteral unsafeOptionsObjLit)
        {
            // UnsafeExecuteArguments.untrackedPaths
            // TODO: Fix all callers, in the api this is limited to Directory.
            ProcessOptionalDirectoryOrPathArray(unsafeOptionsObjLit, m_unsafeUntrackedScopes, dir => processBuilder.AddUntrackedDirectoryScope(dir), skipUndefined: true);

            var untrackedPaths = Converter.ExtractArrayLiteral(unsafeOptionsObjLit, m_unsafeUntrackedPaths, allowUndefined: true);
            if (untrackedPaths != null)
            {
                for (var i = 0; i < untrackedPaths.Length; i++)
                {
                    var value = untrackedPaths[i];
                    if (!value.IsUndefined)
                    {
                        Converter.ExpectPathOrFileOrDirectory(untrackedPaths[i], out var untrackedFile, out var untrackedDirectory, out var untrackedPath, new ConversionContext(pos: i, objectCtx: untrackedPaths, name: m_unsafeUntrackedPaths));
                        if (untrackedFile.IsValid)
                        {
                            processBuilder.AddUntrackedFile(untrackedFile);
                        }
                        if (untrackedPath.IsValid)
                        {
                            processBuilder.AddUntrackedFile(untrackedPath);
                        }
                        if (untrackedDirectory.IsValid)
                        {
                            processBuilder.AddUntrackedDirectoryScope(untrackedDirectory);
                        }
                    }
                }
            }

            // UnsafeExecuteArguments.hasUntrackedChildProcesses
            if (Converter.ExtractOptionalBoolean(unsafeOptionsObjLit, m_unsafeHasUntrackedChildProcesses) == true)
            {
                processBuilder.Options |= Process.Options.HasUntrackedChildProcesses;
            }

            // UnsafeExecuteArguments.allowPreservedOutputs
            if (Converter.ExtractOptionalBoolean(unsafeOptionsObjLit, m_unsafeAllowPreservedOutputs) == true)
            {
                processBuilder.Options |= Process.Options.AllowPreserveOutputs;
            }

            // UnsafeExecuteArguments.passThroughEnvironmentVariables
            var passThroughEnvironmentVariables = Converter.ExtractArrayLiteral(unsafeOptionsObjLit, m_unsafePassThroughEnvironmentVariables, allowUndefined: true);
            if (passThroughEnvironmentVariables != null)
            {
                for (var i = 0; i < passThroughEnvironmentVariables.Length; i++)
                {
                    var passThroughEnvironmentVariable = Converter.ExpectString(passThroughEnvironmentVariables[i], new ConversionContext(pos: i, objectCtx: passThroughEnvironmentVariables));
                    processBuilder.SetPassthroughEnvironmentVariable(StringId.Create(context.StringTable, passThroughEnvironmentVariable));
                }
            }

            processBuilder.PreserveOutputWhitelist = ProcessOptionalPathArray(unsafeOptionsObjLit, m_unsafePreserveOutputWhitelist, strict: false, skipUndefined: true);
        }

        private PipId InterpretFinalizationPipArguments(Context context, ObjectLiteral obj)
        {
            Contract.Ensures(Contract.Result<Pip>() != null);
            Contract.Ensures(!Contract.Result<Pip>().PipId.IsValid);

            var tool = Converter.ExtractObjectLiteral(obj, m_executeTool, allowUndefined: true);
            var moniker = Converter.ExtractRef<IIpcMoniker>(obj, m_ipcSendMoniker, allowUndefined: true);

            if ((tool == null && moniker == null) || (tool != null && moniker != null))
            {
                throw new InputValidationException(
                    I($"Expected exactly one of the '{m_executeTool.ToString(StringTable)}' and '{m_ipcSendMoniker.ToString(StringTable)}' fields to be defined."),
                    new ErrorContext(objectCtx: obj));
            }

            if (tool != null)
            {
                TryScheduleProcessPip(context, obj, ServicePipKind.ServiceFinalization, out _, out var finalizationPip);
                return finalizationPip.PipId;
            }
            else
            {
                TryScheduleIpcPip(context, obj, allowUndefinedTargetService: true, isServiceFinalization: true, out _, out var ipcPipId);
                return ipcPipId;
            }
        }


        /// <summary>
        /// This function is used for both the scalar case (e.g. a string is passed as the file content) or for the array case element (e.g. a string[] is passed)
        /// The objectContext is passed for the array case only.
        /// </summary>
        private static PipDataAtom ConvertFileContentElement(Context context, EvaluationResult result, int pos, object objectContext)
        {
            // Expected types are AmbientTypes.PathType, AmbientTypes.RelativePathType , AmbientTypes.PathAtomType or PrimitiveType.StringType
            var element = result.Value;
            if (element is string e)
            {
                return e;
            }

            if (element is PathAtom atom)
            {
                return atom;
            }

            if (element is RelativePath relativePath)
            {
                return relativePath.ToString(context.StringTable);
            }

            if (element is AbsolutePath absolutePath)
            {
                return absolutePath;
            }

            // If the object context is null, that means we are in the scalar case, in which case we can also expect an array
            throw Converter.CreateException(
                objectContext == null
                    ? s_convertFileContentExpectedTypesWithArray
                    : s_convertFileContentExpectedTypes,
                result,
                new ConversionContext(false, pos: pos, objectCtx: objectContext));
        }

        private static void IfPropertyDefined<TState>(TState state, ObjectLiteral obj, SymbolAtom propertyName, Action<TState, EvaluationResult, ConversionContext> callback)
        {
            var val = obj[propertyName];
            if (!val.IsUndefined)
            {
                callback(state, val, new ConversionContext(objectCtx: obj, name: propertyName));
            }
        }

        private static void IfIntPropertyDefined<TState>(TState state, ObjectLiteral obj, SymbolAtom propertyName, Action<TState, int> callback)
        {
            IfPropertyDefined(
                (state, callback),
                obj,
                propertyName,
                (tpl, val, convContext) => tpl.Item2(tpl.Item1, Converter.ExpectNumber(val, strict: true, context: convContext)));
        }

        /// <summary>
        /// Argument type.
        /// </summary>
        private enum ArgType
        {
            IsString,
            IsFile,
            IsBoolean,
            IsNumber,
            IsUndefined,

            // IsArray
            IsEmptyArray,
            IsArrayOfString,
            IsArrayOfFile,

            // IsObjectLiteral
            IsNamedArgument, // <string | string[] | File | File[] | boolean | number> |
            IsMultiArgument, // <string|File>
            IsArguments, // <string|File> |
            IsResponseFilePlaceholder,
        }

        private ArgType ArgTypeOf(Context context, EvaluationResult obj, ref ConversionContext convContext)
        {
            if (obj.IsUndefined)
            {
                return ArgType.IsUndefined;
            }

            var objValue = obj.Value;
            if ((objValue as string) != null)
            {
                return ArgType.IsString;
            }

            if (objValue is ArrayLiteral arr)
            {
                if (arr.Length > 0)
                {
                    var value = arr[0].Value;
                    if (value is string)
                    {
                        return ArgType.IsArrayOfString;
                    }

                    if (value is FileArtifact
                        || value is AbsolutePath
                        || value is DirectoryArtifact
                        || value is StaticDirectory)
                    {
                        return ArgType.IsArrayOfFile;
                    }

                    throw Converter.CreateException(
                        new[]
                        {
                            typeof(string),
                            typeof(AbsolutePath),
                            typeof(FileArtifact),
                            typeof(DirectoryArtifact),
                            typeof(StaticDirectory),
                        },
                        arr[0],
                        convContext);
                }

                return ArgType.IsEmptyArray;
            }

            if (objValue is ObjectLiteral me)
            {
                // IsMultiArgument, // <string|File>
                var isMultiArgument = !me[m_argValues].IsUndefined;

                if (isMultiArgument)
                {
                    return ArgType.IsMultiArgument;
                }

                // IsNamedArgument, // <string | string[] | File | File[] | boolean | number> |
                var isNamedArgument = !me[m_argN].IsUndefined;
                if (isNamedArgument)
                {
                    return ArgType.IsNamedArgument;
                }

                // IsArguments, // <string|File> |
                var isArguments = !me[m_argArgs].IsUndefined;
                if (isArguments)
                {
                    return ArgType.IsArguments;
                }

                var isResponseFilePlaceholder = !me[m_argResponseFileForRemainingArgumentsForce].IsUndefined;
                if (isResponseFilePlaceholder)
                {
                    return ArgType.IsResponseFilePlaceholder;
                }

                throw Converter.CreateException(
                    new[] { typeof(MultiArgument), typeof(NamedArgument), typeof(string), typeof(FileArtifact), typeof(ResponseFilePlaceHolder) },
                    obj,
                    convContext);
            }

            if (objValue is AbsolutePath || objValue is FileArtifact || objValue is DirectoryArtifact || objValue is StaticDirectory)
            {
                return ArgType.IsFile;
            }

            if (objValue is bool)
            {
                return ArgType.IsBoolean;
            }

            if (objValue is int)
            {
                return ArgType.IsNumber;
            }

            throw Converter.CreateException(
                new[]
                {
                    typeof(string), typeof(ArrayLiteral), typeof(ObjectLiteral), typeof(AbsolutePath), typeof(FileArtifact),
                    typeof(bool), typeof(int), typeof(DirectoryArtifact), typeof(StaticDirectory),
                },
                obj,
                convContext);
        }

        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        private static void ProcessImplicitDependency(ProcessBuilder processBuilder, EvaluationResult fileOrStaticDirectory, in ConversionContext convContext)
        {
            // In the past we allow AbsolutePath and DirectoryArtifact as well.
            // For AbsolutePath, one doesn't know whether the user intention is File or Directory, but our conversion will treat it as a source file.
            // Then someone who thinks of it as a Directory will wonder how things could work downstream.
            // For DirectoryArtifact, we simply get an exception because the ObservedInputProcessor expects it to be obtained from sealing a directory.
            Converter.ExpectFileOrStaticDirectory(fileOrStaticDirectory, out var file, out var staticDirectory, convContext);

            if (staticDirectory != null)
            {
                processBuilder.AddInputDirectory(staticDirectory.Root);
            }
            else
            {
                processBuilder.AddInputFile(file);
            }
        }

        private static void ProcessImplicitOutputs(ProcessBuilder processBuilder, ArrayLiteral implicitOutputs, FileExistence fileExistence)
        {
            for (var i = 0; i < implicitOutputs.Length; ++i)
            {
                var implicitOutput = implicitOutputs[i];
                if (implicitOutput.IsUndefined)
                {
                    continue;
                }

                if (implicitOutput.Value is DirectoryArtifact)
                {
                    var dir = Converter.ExpectDirectory(implicitOutput, new ConversionContext(pos: i, objectCtx: implicitOutputs));
                    processBuilder.AddOutputDirectory(dir, SealDirectoryKind.Opaque);
                }
                else
                {
                    var file = Converter.ExpectFile(implicitOutput, strict: false, context: new ConversionContext(pos: i, objectCtx: implicitOutputs));
                    processBuilder.AddOutputFile(file, fileExistence);
                }
            }
        }
    }
}
