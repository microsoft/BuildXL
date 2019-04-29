// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using DsMap = BuildXL.FrontEnd.Script.Ambients.Map.OrderedMap;
using DsSet = BuildXL.FrontEnd.Script.Ambients.Set.OrderedSet;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Arguments for evaluator.
    /// </summary>
    public static class Args
    {
        private static readonly string[] s_emptyStringArray = CollectionUtilities.EmptyArray<string>();

        /// <nodoc />
        public static bool AsBool(EvaluationStackFrame args, int index)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return Converter.ExpectBool(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static int AsInt(EvaluationStackFrame args, int index, bool strict = true)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return Converter.ExpectNumber(args[index], strict: strict, context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static int? AsIntOptional(EvaluationStackFrame args, int index, bool strict = true)
        {
            Contract.Requires(args != null);

            if (index >= args.Length || args[index].IsUndefined)
            {
                return null;
            }

            return Converter.ExpectNumber(args[index], strict: strict, context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static PathAtom AsPathAtom(EvaluationStackFrame args, int index, StringTable stringTable, bool canBeFromString = true)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            var context = new ConversionContext(pos: checked(index + 1));

            return canBeFromString
                ? Converter.ExpectPathAtomFromStringOrPathAtom(stringTable, args[index], context: context)
                : Converter.ExpectPathAtom(args[index], context: context);
        }

        /// <nodoc />
        public static RelativePath AsRelativePath(EvaluationStackFrame args, int index)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return Converter.ExpectRelativePath(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static AbsolutePath AsPath(EvaluationStackFrame args, int index, bool strict = true)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return Converter.ExpectPath(args[index], strict: strict, context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static AbsolutePath AsPathOrUndefined(EvaluationStackFrame args, int index, bool strict = true)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);

            if (args[index].IsUndefined)
            {
                return AbsolutePath.Invalid;
            }

            return Converter.ExpectPath(args[index], strict: strict, context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static void AsPathOrDirectory(EvaluationStackFrame args, int index, out AbsolutePath path, out DirectoryArtifact dir)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            Converter.ExpectPathOrDirectory(args[index], out path, out dir, new ConversionContext(pos: checked(index + 1)));
        }
        
        /// <nodoc />
        public static void AsPathOrDirectory(EvaluationResult[] args, int index, out AbsolutePath path, out DirectoryArtifact dir)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            Converter.ExpectPathOrDirectory(args[index], out path, out dir, new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static string AsString(EvaluationStackFrame args, int index)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return Converter.ExpectString(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static string AsStringOrUndefined(EvaluationStackFrame args, int index)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return Converter.ExpectString(args[index], context: new ConversionContext(pos: checked(index + 1), allowUndefined: true));
        }

        /// <nodoc />
        public static string AsStringOptional(EvaluationStackFrame args, int index)
        {
            Contract.Requires(args != null);

            if (index >= args.Length || args[index].IsUndefined)
            {
                return null;
            }

            return Converter.ExpectString(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static FileArtifact AsFile(EvaluationStackFrame args, int index, bool strict = true)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectFile(args[index], strict: strict, context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static DirectoryArtifact AsDirectory(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectDirectory(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static StaticDirectory AsStaticDirectory(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectStaticDirectory(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static ObjectLiteral AsObjectLiteral(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectObjectLiteral(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static ArrayLiteral AsArrayLiteral(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectArrayLiteral(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static string[] AsStringArray(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);

            var array = Converter.ExpectArrayLiteral(args[index], new ConversionContext(allowUndefined: true, pos: index, objectCtx: args));
            return Converter.ExpectStringArray(array);
        }

        /// <nodoc />
        public static string[] AsStringArrayOptional(EvaluationStackFrame args, int index)
        {
            if (args.Length <= index)
            {
                return s_emptyStringArray;
            }

            var array = Converter.ExpectArrayLiteral(args[index], new ConversionContext(allowUndefined: true, pos: index, objectCtx: args));
            return Converter.ExpectStringArray(array);
        }

        /// <nodoc />
        public static EnumValue AsEnumValue(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectEnumValue(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static EnumValue AsEnumValueOptional(EvaluationStackFrame args, int index)
        {
            if (index >= args.Length || args[index].IsUndefined)
            {
                return null;
            }

            return Converter.ExpectEnumValue(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static int? AsNumberOrEnumValueOptional(EvaluationStackFrame args, int index)
        {
            if (index >= args.Length || args[index].IsUndefined)
            {
                return null;
            }

            return Converter.ExpectNumberOrEnum(args[index], index + 1);
        }

        /// <nodoc />
        public static Closure AsClosure(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectClosure(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static DsMap AsMap(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectMap(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static DsSet AsSet(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return Converter.ExpectSet(args[index], context: new ConversionContext(pos: checked(index + 1)));
        }

        /// <nodoc />
        public static bool IsUndefined(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return args[index].IsUndefined;
        }

        /// <nodoc />
        public static object AsIs(EvaluationStackFrame args, int index)
        {
            CheckArgumentIndex(args, index);
            return args[index].Value;
        }
        
        /// <nodoc />
        public static object AsIs(EvaluationResult[] args, int index)
        {
            Contract.Requires(args != null);

            CheckArgumentIndex(args, index);
            return args[index].Value;
        }

        /// <nodoc/>
        public static void CheckArgumentIndex(EvaluationStackFrame args, int index)
        {
            if (index >= args.Length || index < 0)
            {
                throw new ArgumentIndexOutOfBoundException(index, args.Length);
            }
        }
        
        /// <nodoc/>
        public static void CheckArgumentIndex(EvaluationResult[] args, int index)
        {
            if (index >= args.Length || index < 0)
            {
                throw new ArgumentIndexOutOfBoundException(index, args.Length);
            }
        }

        /// <nodoc/>
        public static bool AsBoolOptional(EvaluationStackFrame args, int index)
        {
            if (index >= args.Length || args[index].IsUndefined)
            {
                return false;
            }

            return AsBool(args, index);
        }
    }
}
