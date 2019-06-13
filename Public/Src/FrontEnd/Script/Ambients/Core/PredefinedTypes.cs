// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Script.Core;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Set of predefined types with behavior that the DScript interpreter knows about.
    /// </summary>
    public sealed class PredefinedTypes
    {
        private sealed class Dummy { }

        private readonly AmbientArgumentKind m_ambientArgumentKind;
        private readonly AmbientArtifactKind m_ambientArtifactKind;

        private readonly AmbientContext m_ambientContext;
        private readonly AmbientDebug m_ambientDebug;
        private readonly AmbientContract m_ambientContract;
        private readonly AmbientDirectory m_ambientDirectory;
        private readonly AmbientEnum m_ambientEnum;
        private readonly AmbientEnvironment m_ambientEnvironment;
        private readonly AmbientFile m_ambientFile;
        private readonly AmbientGlobal m_ambientGlobal;
        private readonly AmbientMath m_ambientMath;
        private readonly AmbientMap m_ambientMap;
        private readonly AmbientNumber m_ambientNumber;
        private readonly AmbientUnit m_ambientUnit;
        private readonly AmbientBoolean m_ambientBoolean;
        private readonly AmbientKeyForm m_ambientKeyForm;
        private readonly AmbientPath m_ambientPath;
        private readonly AmbientPathAtom m_ambientPathAtom;
        private readonly AmbientRelativePath m_ambientRelativePath;
        private readonly AmbientSet m_ambientSet;
        private readonly AmbientMutableSet m_ambientMutableSet;
        private readonly AmbientStaticDirectory m_ambientStaticDirectory;
        private readonly AmbientString m_ambientString;
        private readonly AmbientStringBuilder m_ambientStringBuilder;
        private readonly AmbientTransformerOriginal m_ambientTransformerOriginal;
        private readonly AmbientTransformerHack m_ambientTransformerHack;
        private readonly AmbientTextEncoding m_ambientTextEncoding;
        private readonly AmbientNameResolutionSemantics m_ambientNameResolutionSemantics;
        private readonly AmbientSealSourceDirectoryOption m_ambientSealSourceDirectoryOption;
        private readonly AmbientHashing m_ambientHashHelper;
        private readonly AmbientValueCache m_ambientValueCacheHelper;
        private readonly AmbientJson m_ambientJsonHelper;
        private readonly AmbientXml m_ambientXmlHelper;
        private readonly AmbientContainerIsolationLevel m_ambientContainerIsolationLevel;

        /// <summary>Returns all ambient definitions keyed by <see cref="Type"/>.</summary>
        public IReadOnlyDictionary<Type, AmbientDefinitionBase> AllAmbientDefinitions { get; }

        /// <nodoc />
        public PredefinedTypes(PrimitiveTypes knownTypes)
        {
            Contract.Requires(knownTypes != null);

            AllAmbientDefinitions = new Dictionary<Type, AmbientDefinitionBase>
            {
                [typeof(ArrayLiteral)] = AmbientArray = new AmbientArray(knownTypes),
                [typeof(DirectoryArtifact)] = m_ambientDirectory = new AmbientDirectory(knownTypes),
                [typeof(EnumValue)] = m_ambientEnum = new AmbientEnum(knownTypes),
                [typeof(FileArtifact)] = m_ambientFile = new AmbientFile(knownTypes),
                [typeof(ObjectLiteral)] = AmbientObject = new AmbientObject(knownTypes),
                [typeof(ObjectLiteral0)] = AmbientObject,
                [typeof(ObjectLiteralSlim)] = AmbientObject,
                [typeof(ObjectLiteralN)] = AmbientObject,
                [typeof(AbsolutePath)] = m_ambientPath = new AmbientPath(knownTypes),
                [typeof(StaticDirectory)] = m_ambientStaticDirectory = new AmbientStaticDirectory(knownTypes),
                [typeof(string)] = m_ambientString = new AmbientString(knownTypes),
                [typeof(AmbientStringBuilder.StringBuilderWrapper)] = m_ambientStringBuilder = new AmbientStringBuilder(knownTypes),
                [typeof(int)] = m_ambientNumber = new AmbientNumber(knownTypes),
                [typeof(UnitValue)] = m_ambientUnit = new AmbientUnit(knownTypes),
                [typeof(bool)] = m_ambientBoolean = new AmbientBoolean(knownTypes),
                [typeof(OrderedMap)] = m_ambientMap = new AmbientMap(knownTypes),
                [typeof(OrderedSet)] = m_ambientSet = new AmbientSet(knownTypes),
                [typeof(MutableSet)] = m_ambientMutableSet = new AmbientMutableSet(knownTypes),
                [typeof(PathAtom)] = m_ambientPathAtom = new AmbientPathAtom(knownTypes),
                [typeof(RelativePath)] = m_ambientRelativePath = new AmbientRelativePath(knownTypes),

                // the ones below don't define any 'instance' methods, thus are not associated with a "real" object type,
                // i.e., a type that is going to be encountered during a DScript evaluation.
                [typeof(Dummy)] = m_ambientContext = new AmbientContext(knownTypes),
                [typeof(Dummy)] = m_ambientDebug = new AmbientDebug(knownTypes),
                [typeof(Dummy)] = m_ambientContract = new AmbientContract(knownTypes),
                [typeof(Dummy)] = m_ambientGlobal = new AmbientGlobal(knownTypes),
                [typeof(Dummy)] = m_ambientMath = new AmbientMath(knownTypes),
                [typeof(Dummy)] = m_ambientTransformerOriginal = new AmbientTransformerOriginal(knownTypes),
                [typeof(Dummy)] = m_ambientTransformerHack = new AmbientTransformerHack(knownTypes),
                [typeof(Dummy)] = m_ambientEnvironment = new AmbientEnvironment(knownTypes),
                [typeof(Dummy)] = m_ambientArgumentKind = new AmbientArgumentKind(knownTypes),
                [typeof(Dummy)] = m_ambientArtifactKind = new AmbientArtifactKind(knownTypes),
                [typeof(Dummy)] = m_ambientTextEncoding = new AmbientTextEncoding(knownTypes),
                [typeof(Dummy)] = m_ambientNameResolutionSemantics = new AmbientNameResolutionSemantics(knownTypes),
                [typeof(Dummy)] = m_ambientKeyForm = new AmbientKeyForm(knownTypes),
                [typeof(Dummy)] = m_ambientSealSourceDirectoryOption = new AmbientSealSourceDirectoryOption(knownTypes),
                [typeof(Dummy)] = m_ambientHashHelper = new AmbientHashing(knownTypes),
                [typeof(Dummy)] = m_ambientValueCacheHelper = new AmbientValueCache(knownTypes),
                [typeof(Dummy)] = m_ambientJsonHelper = new AmbientJson(knownTypes),
                [typeof(Dummy)] = m_ambientXmlHelper = new AmbientXml(knownTypes),
                [typeof(Dummy)] = m_ambientContainerIsolationLevel = new AmbientContainerIsolationLevel(knownTypes),
            };
        }

        /// <summary>
        ///     Ambient array.
        /// </summary>
        public AmbientArray AmbientArray { get; }

        /// <summary>
        ///     Ambient object.
        /// </summary>
        public AmbientObject AmbientObject { get; }

        /// <summary>
        ///     Registers all ambients to the global module literal.
        /// </summary>
        /// <param name="global">The global module literal.</param>
        public void Register(GlobalModuleLiteral global)
        {
            Contract.Requires(global != null);

            AmbientArray.Initialize(global);
            m_ambientContext.Initialize(global);
            m_ambientDebug.Initialize(global);
            m_ambientContract.Initialize(global);
            m_ambientDirectory.Initialize(global);
            m_ambientEnum.Initialize(global);
            m_ambientFile.Initialize(global);
            m_ambientGlobal.Initialize(global);
            m_ambientMath.Initialize(global);
            AmbientObject.Initialize(global);
            m_ambientPath.Initialize(global);
            m_ambientStaticDirectory.Initialize(global);
            m_ambientString.Initialize(global);
            m_ambientStringBuilder.Initialize(global);
            m_ambientTransformerOriginal.Initialize(global);
            m_ambientTransformerHack.Initialize(global);
            m_ambientNumber.Initialize(global);
            m_ambientBoolean.Initialize(global);
            m_ambientUnit.Initialize(global);
            m_ambientEnvironment.Initialize(global);
            m_ambientArgumentKind.Initialize(global);
            m_ambientArtifactKind.Initialize(global);
            m_ambientMap.Initialize(global);
            m_ambientSet.Initialize(global);
            m_ambientMutableSet.Initialize(global);
            m_ambientKeyForm.Initialize(global);
            m_ambientPathAtom.Initialize(global);
            m_ambientRelativePath.Initialize(global);
            m_ambientTextEncoding.Initialize(global);
            m_ambientNameResolutionSemantics.Initialize(global);
            m_ambientSealSourceDirectoryOption.Initialize(global);
            m_ambientHashHelper.Initialize(global);
            m_ambientValueCacheHelper.Initialize(global);
            m_ambientJsonHelper.Initialize(global);
            m_ambientXmlHelper.Initialize(global);
            m_ambientContainerIsolationLevel.Initialize(global);
        }

        /// <summary>
        ///     Gets an ambient function given an object receiver and the function name.
        /// </summary>
        /// <param name="receiver">The object receiver.</param>
        /// <param name="name">The ambient name.</param>
        /// <returns>The ambient function or value of property, or null if non-existent.</returns>
        public CallableValue ResolveMember(object receiver, SymbolAtom name)
        {
            Contract.Requires(receiver != null);
            Contract.Requires(name.IsValid);

            // TODO: Some microbenchmarking experiments.
            // - Replacing the if's with Dictionary<Type, Ambient> results in 4x slow down.
            // - Below, the types are ordered based on the frequency of occurrences in our self-host.
            // - Further investigation with different scenarios/benchmarks is necessary, but don't replace is with the above dictionary.
            switch (receiver)
            {
                case AbsolutePath p:
                    return p.IsValid ? m_ambientPath.ResolveMember(p, name) : null;
                case FileArtifact f:
                    return f.IsValid ? m_ambientFile.ResolveMember(f, name) : null;
                case PathAtom a:
                    return a.IsValid ? m_ambientPathAtom.ResolveMember(a, name) : null;
                case RelativePath r:
                    return r.IsValid ? m_ambientRelativePath.ResolveMember(r, name) : null;
                case string s:
                    return m_ambientString.ResolveMember(s, name);
                case DirectoryArtifact d:
                    return d.IsValid ? m_ambientDirectory.ResolveMember(d, name) : null;
                case StaticDirectory staticDirectory:
                    return m_ambientStaticDirectory.ResolveMember(staticDirectory, name);
                case int i:
                    return m_ambientNumber.ResolveMember(i, name);
                case bool b:
                    return m_ambientBoolean.ResolveMember(b, name);
                case OrderedMap map:
                    return m_ambientMap.ResolveMember(map, name);
                case OrderedSet set:
                    return m_ambientSet.ResolveMember(set, name);
                case MutableSet mutableSet:
                    return m_ambientMutableSet.ResolveMember(mutableSet, name);
                case EnumValue enumValue:
                    return m_ambientEnum.ResolveMember(enumValue, name);
                case AmbientStringBuilder.StringBuilderWrapper stringBuilder:
                    return m_ambientStringBuilder.ResolveMember(stringBuilder, name);
            }

            return null;
        }
    }
}
