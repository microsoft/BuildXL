// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace StaticDirectory {}</code> and <code>interface StaticDirectory {}</code>..
    /// </summary>
    public sealed class AmbientStaticDirectory : AmbientBasePathQueries<StaticDirectory>
    {
        private SymbolAtom m_subFolder;

        /// <nodoc />
        public AmbientStaticDirectory(PrimitiveTypes knownTypes)
            : base("StaticDirectory", knownTypes)
        {
            m_subFolder = Symbol("subFolder");
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<StaticDirectory>> CreateSpecificMembers()
        {
            return new Dictionary<StringId, CallableMember<StaticDirectory>>
                   {
                       { NameId("contents"), CreateProperty<StaticDirectory>(AmbientName, Symbol("contents"), GetContent) },
                       { NameId("getFile"), Create<StaticDirectory>(AmbientName, Symbol("getFile"), GetFile) },
                       { NameId("hasFile"), Create<StaticDirectory>(AmbientName, Symbol("hasFile"), HasFile) },
                       { NameId("getFiles"), Create<StaticDirectory>(AmbientName, Symbol("getFiles"), GetFiles) },
                       { NameId("ensureContents"), Create<StaticDirectory>(AmbientName, Symbol("ensureContents"), EnsureContents) },
                       { NameId("root"), CreateProperty<StaticDirectory>(AmbientName, Symbol("root"), GetRoot) },
                       { NameId("kind"), CreateProperty<StaticDirectory>(AmbientName, Symbol("kind"), GetKind) },

                       // TODO: These two methods need to be deprecated.
                       { NameId("getContent"), Create<StaticDirectory>(AmbientName, Symbol("getContent"), GetContent) },
                       { NameId("getSealedDirectory"), Create<StaticDirectory>(AmbientName, Symbol("getSealedDirectory"), GetRoot) },
                   };
        }

        private static EvaluationResult GetRoot(Context context, StaticDirectory receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Root);
        }

        private static EvaluationResult GetKind(Context context, StaticDirectory receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Kind);
        }

        private static EvaluationResult GetContent(Context context, StaticDirectory receiver, EvaluationStackFrame captures)
        {
            GetProvenance(context, out AbsolutePath path, out LineInfo lineInfo);

            // Can't use content directly, because there is no IS-A relationship between collection
            // of value types and collection of objects. So need to box everything any way.
            var content = receiver.Contents.SelectArray(x => EvaluationResult.Create(x));

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(content, lineInfo, path));
        }

        private static EvaluationResult HasFile(Context context, StaticDirectory receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var path = GetPathFromArgument(context, receiver, arg);

            if (receiver.TryGetFileArtifact(path, out var _))
            {
                return EvaluationResult.True;
            }

            return EvaluationResult.False;
        }

        private static EvaluationResult GetFile(Context context, StaticDirectory receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var path = GetPathFromArgument(context, receiver, arg);

            if (receiver.TryGetFileArtifact(path, out FileArtifact file))
            {
                return EvaluationResult.Create(file);
            }

            throw new FileNotFoundInStaticDirectoryException(path.ToString(context.PathTable), new ErrorContext(objectCtx: arg.Value, pos: 0));
        }

        private static AbsolutePath GetPathFromArgument(Context context, StaticDirectory receiver, EvaluationResult arg)
        {
            AbsolutePath path;
            if (arg.Value is AbsolutePath absolutePath)
            {
                path = absolutePath;
            }
            else
            {
                var stringTable = context.FrontEndContext.StringTable;
                var pathTable = context.FrontEndContext.PathTable;

                Converter.ExpectPathFragment(stringTable, arg, out PathAtom pathAtom, out RelativePath relativePath, new ConversionContext(pos: 1));

                path = receiver.Root.Path;
                path = pathAtom.IsValid ? path.Combine(pathTable, pathAtom) : path.Combine(pathTable, relativePath);
            }

            return path;
        }

        private static EvaluationResult GetFiles(Context context, StaticDirectory receiver, EvaluationResult argument, EvaluationStackFrame captures)
        {
            ArrayLiteral args = Converter.ExpectArrayLiteral(argument);
            var result = new EvaluationResult[args.Length];

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                AbsolutePath path;
                if (arg.Value is AbsolutePath absolutePath)
                {
                    path = absolutePath;
                }
                else
                {
                    var stringTable = context.FrontEndContext.StringTable;
                    var pathTable = context.FrontEndContext.PathTable;

                    Converter.ExpectPathFragment(stringTable, arg, out PathAtom pathAtom, out RelativePath relativePath, new ConversionContext(pos: 1));

                    path = receiver.Root.Path;
                    path = pathAtom.IsValid ? path.Combine(pathTable, pathAtom) : path.Combine(pathTable, relativePath);
                }

                if (receiver.TryGetFileArtifact(path, out FileArtifact file))
                {
                    result[i] = EvaluationResult.Create(file);
                }
                else
                {
                    throw new FileNotFoundInStaticDirectoryException(path.ToString(context.PathTable), new ErrorContext(objectCtx: arg.Value, pos: i));
                }
            }

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, context.TopStack.InvocationLocation, context.TopStack.Path));
        }

        private EvaluationResult EnsureContents(Context context, StaticDirectory receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            // Check the kind. For now this only works on Full and Partial Sealed directories
            // This needs to be extended (just like getFile and getFiles) to opaque and sourcesealed directories.

            var stringTable = context.FrontEndContext.StringTable;
            var pathTable = context.FrontEndContext.PathTable;

            switch (receiver.SealDirectoryKind)
            {
                case SealDirectoryKind.Full:
                case SealDirectoryKind.Partial:
                    // Supported since we have static directory.
                    break;
                default:
                    // For the other types we will need to schedule a pip in the graph that actually validates at runtime the file is there
                    // either on disk for sourcesealed, or in the opaque collection by using FileContentManager.ListSealedDirectoryContents
                    throw new DirectoryNotSupportedException(receiver.Root.Path.ToString(pathTable));
            }

            var obj = Converter.ExpectObjectLiteral(arg);
            var subFolder = Converter.ExtractRelativePath(obj, m_subFolder);

            var filterPath = receiver.Root.Path.Combine(pathTable, subFolder);

            var fileContents = new List<FileArtifact>();
            foreach (var sealedFile in receiver.Contents)
            {
                if (sealedFile.Path.IsWithin(pathTable, filterPath))
                {
                    fileContents.Add(sealedFile);
                }
            }

            var sortedFileContents = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(fileContents, OrdinalFileArtifactComparer.Instance);

            if (!context.GetPipConstructionHelper().TrySealDirectory(filterPath, sortedFileContents, SealDirectoryKind.Partial, null, null, null, out var sealedDirectoryArtifact, false))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }

            var result = new StaticDirectory(sealedDirectoryArtifact, SealDirectoryKind.Partial, sortedFileContents.WithCompatibleComparer(OrdinalPathOnlyFileArtifactComparer.Instance));

            return EvaluationResult.Create(result);
        }
        
    }
}
