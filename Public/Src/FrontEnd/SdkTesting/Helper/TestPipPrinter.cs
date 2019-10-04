// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <summary>
    /// Helper that prints a pip graph
    /// </summary>
    public sealed class TestPipPrinter
    {
        private readonly PathTable m_pathTable;
        private readonly StringTable m_stringTable;
        private readonly TestPathExpander m_pathExpander;

        private readonly StringId m_semiColon;
        private readonly StringId m_emptyString;

        /// <nodoc />
        public TestPipPrinter(PathTable pathTable, StringTable stringTable, AbsolutePath testFolder)
        {
            m_pathTable = pathTable;
            m_stringTable = stringTable;

            m_pathExpander = new TestPathExpander(pathTable);
            m_pathExpander.AddReplacement(testFolder, ".");

            AddPath("Windows", Environment.SpecialFolder.Windows);
            AddPath("ProgramFiles", Environment.SpecialFolder.ProgramFiles);
            AddPath("ProgramFilesX86", Environment.SpecialFolder.ProgramFilesX86);
            AddPath("CommonProgramFiles", Environment.SpecialFolder.CommonProgramFiles);
            AddPath("CommonProgramFilesX86", Environment.SpecialFolder.CommonProgramFilesX86);
            AddPath("UserProfile", Environment.SpecialFolder.UserProfile);
            AddPath("InternetCache", Environment.SpecialFolder.InternetCache);
            AddPath("InternetHistory", Environment.SpecialFolder.History);
            AddPath("AppData", Environment.SpecialFolder.ApplicationData);
            AddPath("LocalAppData", Environment.SpecialFolder.LocalApplicationData);
            AddPath("ProgramData", Environment.SpecialFolder.CommonApplicationData);
            AddPath("LocalLow", FileUtilities.GetKnownFolderPath(FileUtilities.KnownFolderLocalLow));

            m_semiColon = StringId.Create(m_stringTable, ";");
            m_emptyString = StringId.Create(m_stringTable, string.Empty);
        }

        private void AddPath(string name, Environment.SpecialFolder specialFolder)
        {
            AddPath(name, Environment.GetFolderPath(specialFolder));
        }

        private void AddPath(string name, string folder)
        {
            m_pathExpander.AddReplacement(
                AbsolutePath.Create(m_pathTable, folder),
                "${Context.getMount('" + name + "').path}");
        }

        /// <summary>
        /// Prints a pipGraph. Relies on the TypeScript.Net pretty printer
        /// </summary>
        public string Print(IPipGraph pipGraph)
        {
            var sourceFile = Generate(pipGraph);
            return sourceFile.ToDisplayString();
        }

        /// <summary>
        /// Generates a SourceFile from a PipGraph
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public ISourceFile Generate(IPipGraph pipGraph)
        {
            var statements = new List<IStatement>();
            foreach (var pip in pipGraph.RetrieveScheduledPips())
            {
                statements.Add(Generate(pip));
            }

            return new TypeScript.Net.Types.SourceFile(statements.ToArray());
        }

        private IStatement Generate(Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    return new ExpressionStatement(Generate((CopyFile)pip));
                case PipType.SealDirectory:
                    return new ExpressionStatement(Generate((SealDirectory)pip));
                case PipType.Process:
                    return new ExpressionStatement(Generate((Process)pip));
                case PipType.Ipc:
                    return new ExpressionStatement(Generate((IpcPip)pip));
                case PipType.WriteFile:
                    return new ExpressionStatement(Generate((WriteFile)pip));
                case PipType.HashSourceFile:
                case PipType.Module:
                case PipType.SpecFile:
                case PipType.Value:
                    // No need to track these.
                    return new EmptyStatement();
                case PipType.Max: // Placeholder enum value
                default:
                    throw Contract.AssertFailure($"Unexpected PipType from Pip.PipType: {pip.PipType}");
            }
        }

        private IExpression Generate(CopyFile pip)
        {
            var arguments = new List<IExpression>(3);
            arguments.Add(Generate(pip.Source));
            arguments.Add(Generate(pip.Destination));
            if (pip.Tags.Length > 0)
            {
                arguments.Add(Generate(pip.Tags));
            }

            // Skip Description
            return new CallExpression(
                new PropertyAccessExpression("Transformer", "copyFile"),
                arguments.ToArray());
        }

        private IExpression Generate(WriteFile pip)
        {
            var arguments = new List<IExpression>(3);
            arguments.Add(Generate(pip.Destination));
            arguments.Add(Generate(pip.Contents));
            if (pip.Tags.Length > 0)
            {
                arguments.Add(Generate(pip.Tags));
            }

            // Skip Description
            return new CallExpression(
                new PropertyAccessExpression("Transformer", "writeFile"),
                arguments.ToArray());
        }

        private IExpression Generate(SealDirectory pip)
        {
            string functionName = null;
            switch (pip.Kind)
            {
                case SealDirectoryKind.Full:
                    functionName = "sealDirectory";
                    break;
                case SealDirectoryKind.Partial:
                    functionName = "sealPartialDirectory";
                    break;
                case SealDirectoryKind.SourceAllDirectories:
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    functionName = "sealSourceDirectory";
                    break;
                case SealDirectoryKind.Opaque:
                    functionName = "sealDynamicDirectory";
                    break;
                case SealDirectoryKind.SharedOpaque:
                    functionName = "sealSharedDynamicDirectory";
                    break;
                default:
                    throw Contract.AssertFailure($"Unexpected SealDirectoryKind from pip.Kind: {pip.Kind}");
            }

            var args = new List<IObjectLiteralElement>(6);
            args.Add(new PropertyAssignment("root", Generate(pip.Directory)));

            switch (pip.Kind)
            {
                case SealDirectoryKind.Full:
                case SealDirectoryKind.Partial:
                    args.Add(new PropertyAssignment("files", Generate(pip.Contents)));
                    break;
                case SealDirectoryKind.SourceAllDirectories:
                    args.Add(new PropertyAssignment("include", new LiteralExpression("allDirectories")));
                    break;
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    args.Add(new PropertyAssignment("include", new LiteralExpression("topDirectoryOnly")));
                    break;
                case SealDirectoryKind.Opaque:
                case SealDirectoryKind.SharedOpaque:
                    break;
                default:
                    throw Contract.AssertFailure($"Unexpected SealDirectoryKind from pip.Kind: {pip.Kind}");
            }

            if (pip.Tags.Length > 0)
            {
                args.Add(new PropertyAssignment("tags", Generate(pip.Tags)));
            }

            if (pip.Provenance.Usage.IsValid)
            {
                var description = pip.Provenance.Usage.ToString(m_pathTable);
                if (!description.EndsWith(" files]"))
                {
                    args.Add(new PropertyAssignment("description", new LiteralExpression(pip.Provenance.Usage.ToString(m_pathTable))));
                }
            }

            if (pip.Scrub)
            {
                args.Add(new PropertyAssignment("scrub", new PrimaryExpression(true)));
            }

            // Skip Description
            return new CallExpression(
                new PropertyAccessExpression("Transformer", functionName),
                new ObjectLiteralExpression(args));
        }

        private IExpression Generate(Process pip)
        {
            var properties = new List<IObjectLiteralElement>();
            properties.Add(new PropertyAssignment("tool", new ObjectLiteralExpression(new PropertyAssignment("exe", Generate(pip.Executable)))));
            if (pip.Tags.Length > 0)
            {
                properties.Add(new PropertyAssignment("tags", Generate(pip.Tags)));
            }

            // Skip Description
            properties.Add(new PropertyAssignment("arguments", Generate(pip.Arguments)));
            properties.Add(new PropertyAssignment("workingDirectory", Generate(DirectoryArtifact.CreateWithZeroPartialSealId(pip.WorkingDirectory))));

            if (pip.Dependencies.Length > 0 || pip.DirectoryDependencies.Length > 0)
            {
                var elements = new List<IExpression>();
                foreach (var file in pip.Dependencies)
                {
                    elements.Add(Generate(file));
                }

                foreach (var directory in pip.DirectoryDependencies)
                {
                    elements.Add(Generate(directory));
                }

                properties.Add(new PropertyAssignment("dependencies", new ArrayLiteralExpression(elements)));
            }

            var nonTemporaryOutputs = pip.FileOutputs.Where(output => !output.IsTemporaryOutputFile).ToArray();
            if (nonTemporaryOutputs.Length > 0 || pip.DirectoryOutputs.Length > 0)
            {
                var elements = new List<IExpression>();
                foreach (var nonTemporaryOutput in nonTemporaryOutputs)
                {
                    elements.Add(Generate(nonTemporaryOutput.ToFileArtifact()));
                }

                foreach (var directory in pip.DirectoryOutputs)
                {
                    elements.Add(Generate(directory));
                }

                properties.Add(new PropertyAssignment("implicitOutputs", new ArrayLiteralExpression(elements)));
            }

            var optionalImplicitOutputs = pip
                .FileOutputs
                .Where(output => output.IsTemporaryOutputFile)
                .Select(output => output.ToFileArtifact())
                .ToArray();
            if (optionalImplicitOutputs.Length > 0)
            {
                properties.Add(new PropertyAssignment(
                    "optionalImplicitOutputs",
                    Generate(optionalImplicitOutputs, Generate)));
            }

            if (pip.StandardInput.IsValid)
            {
                if (pip.StandardInput.IsFile)
                {
                    properties.Add(new PropertyAssignment(
                       "consoleInput",
                       Generate(pip.StandardInput.File)));
                }
                else if (pip.StandardInput.IsData)
                {
                    properties.Add(new PropertyAssignment(
                        "consoleInput",
                        Generate(pip.StandardInput.Data)));
                }
                else
                {
                    throw Contract.AssertFailure($"A pip's StandardInput is valid, yet neither the underlying file nor data are valid. This should never happen!");
                }
            }

            if (pip.StandardOutput.IsValid)
            {
                properties.Add(new PropertyAssignment(
                    "consoleOutput",
                    Generate(pip.StandardOutput.Path)));
            }

            if (pip.StandardError.IsValid)
            {
                properties.Add(new PropertyAssignment(
                    "consoleError",
                    Generate(pip.StandardError.Path)));
            }

            if (pip.EnvironmentVariables.Length > 0)
            {
                properties.Add(new PropertyAssignment("environmentVariables", Generate(pip.EnvironmentVariables.OrderBy(kv => kv.Name, m_pathTable.StringTable.OrdinalComparer).ToArray(), Generate)));
            }

            if (pip.WarningRegex != null && pip.WarningRegex.Pattern.ToString(m_stringTable) != RegexDescriptor.DefaultWarningPattern)
            {
                properties.Add(new PropertyAssignment("warningRegex", Generate(pip.WarningRegex.Pattern)));
            }

            if (pip.Semaphores.Length > 0)
            {
                properties.Add(new PropertyAssignment("acquireSemaphores", Generate(pip.Semaphores, Generate)));
            }

            // The mutexes are covered by semaphores
            if (pip.SuccessExitCodes.Length > 0)
            {
                properties.Add(new PropertyAssignment("successExitCodes", Generate(pip.SuccessExitCodes, code => Generate(code))));
            }

            if (pip.TempDirectory.IsValid)
            {
                properties.Add(new PropertyAssignment("tempDirectory", Generate(pip.TempDirectory, "d")));
            }

            if (pip.AdditionalTempDirectories.Length > 0)
            {
                properties.Add(new PropertyAssignment("additionalTempDirectories", Generate(pip.AdditionalTempDirectories, tempDir => Generate(tempDir, "d"))));
            }

            if (pip.UntrackedPaths.Length > 0 || pip.UntrackedScopes.Length > 0 || pip.AllowPreserveOutputs || pip.HasUntrackedChildProcesses)
            {
                var unsafeProperties = new List<IObjectLiteralElement>();
                if (pip.UntrackedPaths.Length > 0)
                {
                    unsafeProperties.Add(new PropertyAssignment("untrackedPaths", Generate(pip.UntrackedPaths, Generate)));
                }

                if (pip.UntrackedScopes.Length > 0)
                {
                    unsafeProperties.Add(new PropertyAssignment("untrackedScopes", Generate(pip.UntrackedScopes, Generate)));
                }

                if (pip.HasUntrackedChildProcesses)
                {
                    unsafeProperties.Add(new PropertyAssignment("hasUntrackedChildProcesses", Generate(pip.HasUntrackedChildProcesses)));
                }

                if (pip.AllowPreserveOutputs)
                {
                    unsafeProperties.Add(new PropertyAssignment("allowPreservedOutputs", Generate(pip.AllowPreserveOutputs)));
                }

                if (pip.RequireGlobalDependencies)
                {
                    unsafeProperties.Add(new PropertyAssignment("requireGlobalDependencies", Generate(pip.RequireGlobalDependencies)));
                }

                properties.Add(new PropertyAssignment("unsafe", new ObjectLiteralExpression(unsafeProperties)));
            }

            if (pip.OutputsMustRemainWritable)
            {
                properties.Add(new PropertyAssignment("keepOutputsWritable", Generate(pip.OutputsMustRemainWritable)));
            }

            if (pip.AllowUndeclaredSourceReads)
            {
                properties.Add(new PropertyAssignment("allowUndeclaredSourceReads", Generate(pip.AllowUndeclaredSourceReads)));
            }

            if (pip.AllowedSurvivingChildProcessNames.Length > 0)
            {
                properties.Add(new PropertyAssignment("allowedSurvivingChildProcessNames", Generate(pip.AllowedSurvivingChildProcessNames)));
            }

            if (pip.NestedProcessTerminationTimeout.HasValue)
            {
                properties.Add(new PropertyAssignment("nestedProcessTerminationTimeoutMs", Generate((int)pip.NestedProcessTerminationTimeout.Value.TotalMilliseconds)));
            }

            return new CallExpression(
                new PropertyAccessExpression("Transformer", pip.IsService ? "createService" : "execute"),
                new ObjectLiteralExpression(properties));
        }

        private IExpression Generate(IpcPip pip)
        {
            var properties = new List<IObjectLiteralElement>
            {
                new PropertyAssignment("connectRetryDelayMillis", Generate((int)pip.IpcInfo.IpcClientConfig.ConnectRetryDelay.TotalMilliseconds)),
                new PropertyAssignment("maxConnectRetries", Generate(pip.IpcInfo.IpcClientConfig.MaxConnectRetries)),
                new PropertyAssignment("fileDependencies", new ArrayLiteralExpression(pip.FileDependencies.Select(Generate))),
                new PropertyAssignment("lazilyMaterializedDependencies", new ArrayLiteralExpression(pip.LazilyMaterializedDependencies.Select(Generate))),
                new PropertyAssignment("messageBody", Generate(pip.MessageBody)),
                new PropertyAssignment("outputFile", Generate(pip.OutputFile)),
                new PropertyAssignment("isServiceFinalization", Generate(pip.IsServiceFinalization)),
                new PropertyAssignment("mustRunOnMaster", Generate(pip.MustRunOnMaster)),
            };

            return new CallExpression(
                new PropertyAccessExpression("Transformer", "ipcSend"),
                new ObjectLiteralExpression(properties));
        }

        private IExpression Generate(ProcessSemaphoreInfo arg)
        {
            return new ObjectLiteralExpression(
                new PropertyAssignment("name", Generate(arg.Name)),
                new PropertyAssignment("incrementBy", Generate(arg.Value)),
                new PropertyAssignment("limit", Generate(arg.Limit)));
        }

        private IExpression Generate(EnvironmentVariable arg)
        {
            Contract.Requires(arg.IsPassThrough || arg.Value.FragmentEscaping == PipDataFragmentEscaping.NoEscaping); // This should not be controllable

            if (arg.IsPassThrough)
            {
                return new ObjectLiteralExpression(
                    new PropertyAssignment("name", Generate(arg.Name)),
                    new PropertyAssignment("isPassThrough", Generate(true)));
            }

            var pipData = arg.Value;

            if (pipData.FragmentSeparator == m_emptyString && pipData.FragmentCount == 1)
            {
                return new ObjectLiteralExpression(
                    new PropertyAssignment("name", Generate(arg.Name)),
                    new PropertyAssignment("value", Generate(pipData.First())));
            }

            if (pipData.FragmentSeparator == m_semiColon && pipData.FragmentCount > 1)
            {
                return new ObjectLiteralExpression(
                    new PropertyAssignment("name", Generate(arg.Name)),
                    new PropertyAssignment("value", Generate(pipData.ToList(), Generate)));
            }

            return new ObjectLiteralExpression(
                new PropertyAssignment("name", Generate(arg.Name)),
                new PropertyAssignment("value", Generate(pipData.ToList(), Generate)),
                new PropertyAssignment("separator", Generate(pipData.FragmentSeparator)));
        }

        private IExpression Generate(PipData pipData)
        {
            return new ObjectLiteralExpression(
                new PropertyAssignment("escaping", new LiteralExpression(pipData.FragmentEscaping.ToString())),
                new PropertyAssignment("separator", Generate(pipData.FragmentSeparator)),
                new PropertyAssignment("items", Generate(pipData.ToList(), Generate)));
        }

        private IExpression Generate(PipFragment fragment)
        {
            switch (fragment.FragmentType)
            {
                case PipFragmentType.Invalid:
                    return Identifier.CreateUndefined();
                case PipFragmentType.StringLiteral:
                    return Generate(fragment.GetStringIdValue());
                case PipFragmentType.AbsolutePath:
                    return Generate(fragment.GetPathValue());
                case PipFragmentType.NestedFragment:
                    return Generate(fragment.GetNestedFragmentValue());
                default:
                    throw Contract.AssertFailure($"Unexpected PipFragmentType from PipFragment.FragmentType: {fragment.FragmentType}");
            }
    }

        private IExpression Generate(IReadOnlyList<StringId> tags)
        {
            return Generate(tags, Generate);
        }

        private IExpression Generate(IReadOnlyList<FileArtifact> tags)
        {
            return Generate(tags, Generate);
        }

        private IExpression Generate(IReadOnlyList<PathAtom> atoms)
        {
            return Generate(atoms, Generate);
        }

        private static IExpression Generate<T>(IReadOnlyList<T> list, Func<T, IExpression> generateElement)
        {
            if (list == null)
            {
                // TODO: What is the literal for null
                return null;
            }

            var elements = new IExpression[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                elements[i] = generateElement(list[i]);
            }

            return new ArrayLiteralExpression(elements);
        }

        private static IExpression Generate(bool value)
        {
            return new PrimaryExpression(value);
        }

        private static IExpression Generate(int value)
        {
            return new LiteralExpression(value);
        }

        private IExpression Generate(StringId stringId)
        {
            if (stringId.IsValid)
            {
                return new LiteralExpression(stringId.ToString(m_stringTable));
            }

            return Identifier.CreateUndefined();
        }

        private IExpression Generate(FileArtifact file)
        {
            return Generate(file.Path, "f");
        }

        private IExpression Generate(DirectoryArtifact directory)
        {
            return Generate(directory.Path, "d");
        }

        private IExpression Generate(FileOrDirectoryArtifact artifact)
        {
            return Generate(artifact.Path, artifact.IsFile ? "f" : "d");
        }

        private IExpression Generate(AbsolutePath path)
        {
            return Generate(path, "p");
        }

        private IExpression Generate(PathAtom pathAtom)
        {
            if (pathAtom.IsValid)
            {
                return new TaggedTemplateExpression("a", pathAtom.ToString(m_stringTable));
            }

            return Identifier.CreateUndefined();
        }

        private IExpression Generate(AbsolutePath path, string type)
        {
            if (path.IsValid)
            {
                return new TaggedTemplateExpression(
                    type,
                    m_pathTable.ExpandName(path.Value, m_pathExpander, '/'));
            }

            return Identifier.CreateUndefined();
        }
    }
}
