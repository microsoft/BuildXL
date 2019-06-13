// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Core;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using static BuildXL.Utilities.FormattableStringEx;
using Type = System.Type;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Converts an <see cref="ObjectLiteral" /> object representating a configuration object from DScript
    /// into an equivalent <see cref="IConfiguration" /> object.
    /// </summary>
    public sealed class ConfigurationConverter
    {
        /// <summary>
        /// Fully qualified name of resolver class (or implementation).
        /// </summary>
        /// <remarks>
        /// This name is used to handle conversion on resolver in a special way; see <see cref="FindImplementation"/> function.
        /// </remarks>
        private const string FullyQualifiedResolverClassName = "BuildXL.Utilities.Configuration.Mutable.ResolverSettings";

        /// <summary>
        /// Field name for identifying resolver's kind.
        /// </summary>
        private const string ResolverKindFieldName = "kind";

        private const BindingFlags PermissiveFlags = BindingFlags.Public | BindingFlags.Instance;

        private readonly FrontEndContext m_context;

        private static readonly Lazy<MethodInfo> s_createAndPopulateListMethod =
            Lazy.Create(() => typeof(ConfigurationConverter).GetMethod("CreateAndPopulateList", BindingFlags.Instance | BindingFlags.NonPublic));

        private static readonly Lazy<MethodInfo> s_createAndPopulateDictionaryForObjectLiteralsMethod =
            Lazy.Create(() => typeof(ConfigurationConverter).GetMethod("CreateAndPopulateDictionaryForObjectLiterals", BindingFlags.Instance | BindingFlags.NonPublic));

        private static readonly Lazy<MethodInfo> s_createAndPopulateDictionaryForMapsMethod =
            Lazy.Create(() => typeof(ConfigurationConverter).GetMethod("CreateAndPopulateDictionaryForMaps", BindingFlags.Instance | BindingFlags.NonPublic));

        /// <summary>
        /// Converts an object literal, obtained by parsing DScript, into a corresponding BuildXL object of the given type.
        /// </summary>
        /// <returns>BuildXL object of the given type equivalent to the given object literal.</returns>
        public static T Convert<T>(FrontEndContext context, ObjectLiteral objectLiteral, T targetInstance = default(T))
        {
            Contract.Requires(context != null);
            Contract.Requires(objectLiteral != null);

            // TODO: Check this precondition because now we are converting value from another assembly.
            // Contract.Requires(
            //    typeof(T).Assembly == typeof(IConfiguration).Assembly,
            //    "Currently this class supports only IValues defined in the same assembly as IConfiguration itself.");
            Contract.Ensures(Contract.Result<T>() != null);

            return
                (T)new ConfigurationConverter(context).ConvertAny(
                        objectLiteral,
                        typeof(T),
                        targetInstance,
                        new ConversionContext(objectCtx: objectLiteral));
        }

        /// <summary>
        /// Converts an object literal, obtained by parsing DScript, into an instance of BuildXL's
        /// <see cref="IConfiguration" /> class.
        ///
        /// - Requires that all IValue field types that will be (recursively) set in the resulting IConfiguration
        /// object have an implementation class which can be found in the same DLL where the
        /// <see cref="IConfiguration" /> interface is.
        /// </summary>
        /// <returns>BuildXL IConfiguration instance equivalent to the given object literal.</returns>
        public static IConfiguration ConvertObjectLiteralToConfiguration(FrontEndContext context, ObjectLiteral objectLiteral)
        {
            Contract.Requires(context != null);
            Contract.Requires(objectLiteral != null);
            Contract.Ensures(Contract.Result<IConfiguration>() != null);
            
            return Convert<ConfigurationImpl>(context, objectLiteral);
        }

        /// <summary>
        /// Augments <paramref name="cmdLineConfiguration"/> (which came from command line arguments) with configuration options that were read from a DScript config file.
        /// </summary>
        /// <remarks>
        /// The <paramref name="cmdLineConfiguration"/> is used as a main instance and the configuration converted from a DScript config file provides only missing values.
        /// It means that if the configuration already has a member, this member will stay in the resulting instance.
        /// </remarks>
        public static IConfiguration AugmentConfigurationWith(FrontEndContext context, IConfiguration cmdLineConfiguration, ObjectLiteral configLiteral)
        {
            Contract.Requires(context != null);
            Contract.Requires(configLiteral != null);
            Contract.Requires(cmdLineConfiguration != null);
            Contract.Ensures(Contract.Result<IConfiguration>() != null);

            var targetInstance = new ConfigurationImpl(cmdLineConfiguration);
            return Convert(context, configLiteral, targetInstance);
        }

        private ConfigurationConverter(FrontEndContext context)
        {
            Contract.Requires(context != null);
            m_context = context;
        }

        private object SetProperty(StringId originalPropertyName, string propertyName, object obj, object val, Type type, object targetInstance, in ConversionContext context)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(propertyName));

            if (type == null)
            {
                type = obj.GetType();
            }

            var property = GetProperty(propertyName, type);
            if (property == null)
            {
                throw new ConversionException(
                    I($"Property '{propertyName}' not found in type '{type}'"),
                    context.ErrorContext);
            }

            // check existing property
            var existingValue = targetInstance == null ? null : property.GetValue(targetInstance);

            var targetFldVal = ConvertAny(val, property.PropertyType, existingValue, context.WithNewName(originalPropertyName));
            property.SetValue(obj, targetFldVal);
            return obj;
        }

        private static PropertyInfo GetProperty(string propertyName, Type type)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(propertyName));

            if (type == null)
            {
                return null;
            }

            var fld = type.GetProperty(propertyName, PermissiveFlags);

            if (fld != null)
            {
                return fld;
            }

            return GetProperty(propertyName, type.GetTypeInfo().BaseType);
        }

        /// <summary>
        /// Converts a property name found in an ObjectLiteral to the corresponding
        /// underlying field name in <see cref="BuildXL.Utilities.Configuration" /> class.  It does so
        /// by converting <code>propName</code> to camelCase and prepending "m_" to it.
        /// </summary>
        /// <param name="propName">DScript property name.</param>
        /// <returns>Corresponding DScript field name.</returns>
        private static string ToTargetPropertyName(string propName)
        {
            Contract.Ensures(!string.IsNullOrWhiteSpace(Contract.Result<string>()));

            return propName[0].ToUpperInvariantFast() + propName.Substring(1);
        }

        /// <summary>
        /// <see cref="FindImplementationType" />
        /// </summary>
        public Type FindImplementation(Type type, ObjectLiteral objectLiteral)
        {
            return FindImplementationType(type, objectLiteral,
                () =>
                {
                    // Conversion on resolvers gets a special handling. For specifying resolvers, <see cref="IConfiguration"/> provides a list of
                    // <see cref="IResolver"/>, which is a super interface of all resolvers. However, for conversion, one needs to be able
                    // to convert an object literal to a specific resolver, e.g., source resolver or nuget resolver.
                    var kindSymbol = GetSymbol(ResolverKindFieldName);

                    Contract.Assume(kindSymbol.IsValid);
                    return objectLiteral[kindSymbol].Value as string;
                });
        }

        /// <summary>
        /// If <code>type</code> is an interface, it attempts to find a class implementing
        /// that interface.  It does so by looking for a class whose fully qualified name
        /// is obtained from the fully qualified name of the interface type by dropping the
        /// initial "I" from its name in the same assembly in which the <see cref="BuildXL.Utilities.Configuration" />
        /// class is defined.
        ///
        /// - Asserts that there exists a class following the resolution rules above.
        /// </summary>
        /// <param name="type">Interface type.</param>
        /// <param name="objectLiteral">Object literal representing the target</param>
        /// <param name="getResolverKind">Returns the csharp type of the resolver based on the kind member. This is the only polymorphic field in the whole cofig.</param>
        /// <returns>A class type implementing the given interface type.</returns>
        public static Type FindImplementationType(Type type, ObjectLiteral objectLiteral, Func<string> getResolverKind)
        {
            Contract.Requires(type.IsInterface);
            Contract.Requires(type.Name.ToCharArray()[0] == 'I');
            Contract.Ensures(Contract.Result<Type>() != null);
            Analysis.IgnoreArgument(objectLiteral); // TODO: Why do we even pass in this argument?

            var assemblyName = type.GetTypeInfo().Assembly.FullName;
            var targetAssembly = type.GetTypeInfo().Assembly;

            // hack to find class implementing of configuration interface
            var clsName = type.Namespace + ".Mutable." + type.Name.Substring(1);
            Type clsType;
            if (string.Equals(clsName, FullyQualifiedResolverClassName, StringComparison.Ordinal))
            {
                var resolverKind = getResolverKind();

                if (resolverKind == null)
                {
                    throw new ConversionException(
                        I($"Object literal that represents an object implementing interface '{type.FullName}' does not have field '{ResolverKindFieldName}', ") +
                        "or the field gets evaluated to undefined",
                        new ErrorContext(objectCtx: objectLiteral));
                }

                if (!KnownResolverKind.IsValid(resolverKind))
                {
                    var knownResolvers = string.Join(", ", KnownResolverKind.KnownResolvers);
                    throw new ConversionException(
                        I($"Unknown resolver kind '{resolverKind}'. Known resolvers are: {knownResolvers}"),
                        new ErrorContext(objectCtx: objectLiteral));
                }

                clsName = type.Namespace + ".Mutable." + resolverKind + "Settings";
                clsType = targetAssembly.GetType(clsName);
                if (clsType != null)
                {
                    return clsType;
                }

                // not found, try adding Resolver
                clsName = type.Namespace + ".Mutable." + resolverKind + "ResolverSettings";
            }
            else if (type == typeof(IConfiguration))
            {
                // Can't name it configuration due to namespace.
                clsName = type.Namespace + ".Mutable." + type.Name.Substring(1) + "Impl";
            }

            clsType = targetAssembly.GetType(clsName);

            if (clsType == null)
            {
                throw new ConversionException(
                    I($"Cannot find a class '{clsName}' implementing interface '{type.FullName}' in assembly '{assemblyName}'"),
                    new ErrorContext(objectCtx: objectLiteral));
            }

            return clsType;
        }

        private DiscriminatingUnion ConvertToUnionType(object val, Type resultType, in ConversionContext context)
        {
            var union = val as DiscriminatingUnion;
            if (union != null)
            {
                return union;
            }

            union = Activator.CreateInstance(resultType) as DiscriminatingUnion;
            if (union == null)
            {
                throw new ConversionException(
                    I($"Cannot convert value of type '{resultType}' to union type"),
                    context.ErrorContext);
            }

            if (!union.TrySetValue(val))
            {
                throw new ConversionException(
                    I($"Value of type '{val.GetType()}' is not one of the expected values of the discriminating union {resultType}"),
                    context.ErrorContext);
            }

            return union;
        }

        private object ConvertToObject(object val, Type resultType, object targetInstance, in ConversionContext context)
        {
            Contract.Requires(val != null);

            var objLit = val as ObjectLiteral;
            if (objLit == null)
            {
                throw new ConversionException(
                    I($"Cannot convert value of type '{val.GetType()}' to object; value must be an object literal"),
                    context.ErrorContext);
            }

            var objType = resultType.GetTypeInfo().IsClass ? resultType : FindImplementation(resultType, objLit);

            var instance = targetInstance ?? Activator.CreateInstance(objType, true);
            foreach (var kvPair in objLit.Members)
            {
                SetProperty(
                    kvPair.Key,
                    ToTargetPropertyName(ResolveSymbol(kvPair.Key)),
                    instance,
                    kvPair.Value.Value,
                    objType,
                    targetInstance,
                    new ConversionContext(objectCtx: objLit));
            }

            var resolverSettings = instance as ResolverSettings;
            if (resolverSettings != null)
            {
                resolverSettings.Location = new LineInfo(objLit.Location.Line, objLit.Location.Position);
                resolverSettings.File = objLit.Path;
            }

            return instance;
        }

        // ReSharper disable once UnusedMember.Local (invoked via reflection)
        private List<TL> CreateAndPopulateList<TL, TE>(IEnumerable<TE> elems, IReadOnlyList<TL> existingList, in ConversionContext context)
        {
            var componentType = typeof(TL);
            var listBuilder = new List<TL>();
            if (existingList != null)
            {
                listBuilder.AddRange(existingList);
            }

            int pos = 0;

            foreach (var elem in elems)
            {
                var obj = ConvertAny(elem, componentType, null, context.WithNewPos(pos));
                if (obj != null)
                {
                    listBuilder.Add((TL)obj);
                }

                ++pos;
            }

            return listBuilder;
        }

        private static object InvokeViaReflectionAndRestoreExceptionType(MethodInfo method, object instance, object[] args)
        {
            try
            {
                return method.Invoke(instance, args);
            }
            catch (TargetInvocationException e)
            {
                var di = ExceptionDispatchInfo.Capture(e.InnerException);
                di.Throw();
                return default(object);
            }
        }

        private object ConvertIEnumerableToList(object val, Type listType, object targetInstance, in ConversionContext context)
        {
            var inputComponentType = (val is IEnumerable<object>) ? typeof(object) : typeof(int);
            return InvokeViaReflectionAndRestoreExceptionType(
                s_createAndPopulateListMethod.Value.MakeGenericMethod(listType.GenericTypeArguments[0], inputComponentType),
                this,
                new[] { val, targetInstance, context });
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters")]
        private object ConvertArrayLiteralToList(ArrayLiteral arrayLiteral, Type listType, object targetInstance, in ConversionContext context)
        {
            Contract.Requires(arrayLiteral != null);

            var converted = new List<object>();
            for (var i = 0; i < arrayLiteral.Length; i++)
            {
                converted.Add(arrayLiteral[i].Value);
            }

            return ConvertIEnumerableToList(converted, listType, targetInstance, new ConversionContext(objectCtx: arrayLiteral));
        }

        private object ConvertToList(object val, Type lListType, object targetInstance, in ConversionContext context)
        {
            Contract.Requires(val != null);

            if (val is IEnumerable<object> || val is IEnumerable<int>)
            {
                return ConvertIEnumerableToList(val, lListType, targetInstance, context);
            }

            var valAsArrayLiteral = val as ArrayLiteral;
            if (valAsArrayLiteral != null)
            {
                return ConvertArrayLiteralToList(valAsArrayLiteral, lListType, targetInstance, new ConversionContext(objectCtx: valAsArrayLiteral));
            }

            throw new ConversionException(
                I($"Cannot convert value of type '{val.GetType()}' to list of number; only know how to to convert enumerable of number and array literal"),
                context.ErrorContext);
        }

        private AbsolutePath ConvertToAbsolutePath(object value, object overridingValue, in ConversionContext context)
        {
            var convertedValue = ConvertToAbsolutePath(value, context);
            var convertedOverridingValue = overridingValue == null ? AbsolutePath.Invalid : ConvertToAbsolutePath(overridingValue, context);

            return convertedOverridingValue.IsValid ? convertedOverridingValue : convertedValue;
        }

        private AbsolutePath ConvertToAbsolutePath(object value, in ConversionContext context)
        {
            if (TypeConverter.TryConvertAbsolutePath(value, m_context.PathTable, out AbsolutePath result))
            {
                return result;
            }

            // Make sure not to .ToString() value because it could be of a type without a valid ToString method
            throw new ConversionException(
                I($"Cannot convert value of type '{value.GetType().Name}' to absolute path"),
                context.ErrorContext);
        }

        private PathAtom ConvertToPathAtom(object value, in ConversionContext context)
        {
            if (TypeConverter.TryConvertPathAtom(value, m_context.StringTable, out PathAtom result))
            {
                return result;
            }

            throw new ConversionException(
                I($"Cannot convert '{value}' of type '{value.GetType().Name}' to path atom"),
                context.ErrorContext);
        }

        private RelativePath ConvertToRelativePath(object value, in ConversionContext context)
        {
            if (TypeConverter.TryConvertRelativePath(value, m_context.StringTable, out RelativePath result))
            {
                return result;
            }

            throw new ConversionException(
                I($"Cannot convert '{value}' of type '{value.GetType().Name}' to relative path"),
                context.ErrorContext);
        }

        private static object ConvertToEnum(object value, Type targetType, in ConversionContext context)
        {
            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            // value could be a EnumValue or a boxed integer. Need to extract the number first.
            if (TryGetNumericValueFromEnumOrNumber(value, out int numericValue))
            {
                if (TypeConverter.TryConvertEnumValue(numericValue, targetType, out object result))
                {
                    return result;
                }
            }

            // value could be a string when enums are represented as string literals
            if (TryGetStringValueFromEnum(value, targetType, out object enumResult))
            {
                return enumResult;
            }

            throw new ConversionException(
                I($"Cannot convert '{value}' of type '{value.GetType().FullName}' to '{targetType.Name}'"),
                context.ErrorContext);
        }

        private static bool TryGetStringValueFromEnum(object value, Type targetEnumType, out object result)
        {
            result = null;
            if (!(value is string stringValue))
            {
                return false;
            }

            stringValue = ToUpperCaseFirst(stringValue);

            // Unfortunately TryParse is only available in a generic version
            try
            {
                result = Enum.Parse(targetEnumType, stringValue);
                return true;
            }
            catch(ArgumentException)
            {
                return false;
            }
        }

        private static string ToUpperCaseFirst(string stringValue)
        {
            return char.ToUpperInvariant(stringValue[0]) + (stringValue.Length > 1 ? stringValue.Substring(1) : string.Empty);
        }

        private static bool TryGetNumericValueFromEnumOrNumber(object value, out int result)
        {
            int? valueToConvert = null;

            if (value is EnumValue enumValue)
            {
                valueToConvert = enumValue.Value;
            }
            else
            {
                if (value is string strValue)
                {
                    // Do a quick common check to avoid FormatExceptions being thrown.
                    if (strValue.Length > 0 && char.IsLetter(strValue[0]))
                    {
                        result = 0;
                        return false;
                    }
                }
                if (TypeConverter.TryConvertNumber(value, typeof(int).GetTypeInfo(), out object convertedNumber))
                {
                    valueToConvert = (int)convertedNumber;
                }
            }

            result = valueToConvert.GetValueOrDefault();
            return valueToConvert != null;
        }

        private static bool ConvertToBool(object value, in ConversionContext context)
        {
            Contract.Requires(value != null);

            if (value is bool)
            {
                return (bool)value;
            }

            throw new ConversionException(
                I($"Cannot convert '{value}' to bool; only know how to convert boolean literal"),
                context.ErrorContext);
        }

        private static UnitValue ConvertToUnit(object value, in ConversionContext context)
        {
            Contract.Requires(value != null);

            if (value is UnitValue)
            {
                return (UnitValue)value;
            }

            throw new ConversionException(
                I($"Cannot convert '{value}' to Unit"),
                context.ErrorContext);
        }

        private static string ConvertToString(object value, in ConversionContext context)
        {
            Contract.Requires(value != null);

            if (value is string stringValue)
            {
                return stringValue;
            }

            throw new ConversionException(
                I($"Cannot convert '{value}' of type '{value.GetType()}' to string"),
                context.ErrorContext);
        }

        private static object ConvertToNumber(object value, TypeInfo targetType, in ConversionContext context)
        {
            if (TypeConverter.TryConvertNumber(value, targetType, out object result))
            {
                return result;
            }

            throw new ConversionException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Cannot convert '{0}' of type '{1}' to '{2}'",
                        value,
                        value.GetType().FullName,
                        targetType.Name),
                    context.ErrorContext);
        }

        private object ConvertToDictionary(object val, Type dictionaryType, object targetInstance, in ConversionContext context)
        {
            Contract.Requires(dictionaryType.GenericTypeArguments.Length == 2);

            var valueAsOrderedMap = val as OrderedMap;
            if (valueAsOrderedMap != null)
            {
                var keyType = dictionaryType.GenericTypeArguments[0];
                var valueType = dictionaryType.GenericTypeArguments[1];

                var keyValueType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
                Array resolved;

                resolved = Array.CreateInstance(keyValueType, valueAsOrderedMap.Count);
                // An ordered map has no restrictions about its types for keys/values.
                int i = 0;
                foreach (var pair in valueAsOrderedMap)
                {
                    try
                    {
                        resolved.SetValue(Activator.CreateInstance(keyValueType, ConvertAny(pair.Key.Value, keyType, null, context), ConvertAny(pair.Value.Value, valueType, null, context)), i);
                    }
                    catch (MissingMethodException)
                    {
                        throw new ConversionException(I($"Cannot convert value of type '<{pair.Key.Value.GetType()}, {pair.Value.Value.GetType()}>' to '<{keyType},{valueType}>'."), context.ErrorContext);
                    }
                    i++;
                }

                return InvokeViaReflectionAndRestoreExceptionType(
                    s_createAndPopulateDictionaryForMapsMethod.Value.MakeGenericMethod(keyType, valueType),
                    this,
                    new[] { resolved, targetInstance, new ConversionContext(objectCtx: valueAsOrderedMap) });
            }
            else
            {
                // assert that object literal value is an ObjectLiteral
                // (because object literals are used to represent Dictionary too)
                var valueAsObjectLiteral = val as ObjectLiteral;
                if (valueAsObjectLiteral == null)
                {
                    throw new ConversionException(
                        I($"Cannot convert value of type '{val.GetType()}' to dictionary; only know how to convert object literal"),
                        context.ErrorContext);
                }

                // An ArrayLiteral happens to be an ObjectLiteral as well! (with zero members). TODO: revisit this!
                // So in this case if we don't check it explicitly we will end up with an empty object literal
                // An array literal never makes sense to put into a dictionary
                if (val is ArrayLiteral)
                {
                    throw new ConversionException(
                        I($"Cannot convert an array literal; only know how to convert an object literal"),
                        context.ErrorContext);
                }

                // assert that the first map type argument is string
                if (dictionaryType.GenericTypeArguments[0] != typeof(string))
                {
                    throw new ConversionException(
                        I($"Cannot convert a map with keys of type '{dictionaryType.GenericTypeArguments[0]}'; only know how to convert maps with string keys"),
                        context.ErrorContext);
                }

                var resolved = valueAsObjectLiteral.Members
                    .Select(pair => new KeyValuePair<string, object>(ResolveSymbol(pair.Key), pair.Value.Value))
                    .ToArray();

                return InvokeViaReflectionAndRestoreExceptionType(
                    s_createAndPopulateDictionaryForObjectLiteralsMethod.Value.MakeGenericMethod(dictionaryType.GenericTypeArguments[1]),
                    this,
                    new[] { resolved, targetInstance, new ConversionContext(objectCtx: valueAsObjectLiteral) });
            }
        }

        // ReSharper disable once UnusedMember.Local (used via reflection)
        private Dictionary<string, TValue> CreateAndPopulateDictionaryForObjectLiterals<TValue>(IEnumerable<KeyValuePair<string, object>> collection, IReadOnlyDictionary<string, TValue> targetInstance, in ConversionContext context)
        {
            var dictionary = new Dictionary<string, TValue>();
            foreach (var pair in collection)
            {
                dictionary.Add(
                    pair.Key,
                    (TValue)ConvertAny(
                        pair.Value,
                        typeof(TValue),
                        targetInstance != null && targetInstance.TryGetValue(pair.Key, out TValue existingValue) ? (object)existingValue : null,
                        context.WithNewName(StringId.Create(m_context.StringTable, pair.Key))));
            }

            if (targetInstance == null)
            {
                return dictionary;
            }

            foreach (var kv in targetInstance)
            {
                if (!dictionary.ContainsKey(kv.Key))
                {
                    dictionary[kv.Key] = kv.Value;
                }
            }

            return dictionary;
        }

        // ReSharper disable once UnusedMember.Local (used via reflection)
        private Dictionary<Tkey, TValue> CreateAndPopulateDictionaryForMaps<Tkey, TValue>(IEnumerable<KeyValuePair<Tkey, TValue>> collection, IReadOnlyDictionary<Tkey, TValue> targetInstance, in ConversionContext context) 
        {
            var dictionary = new Dictionary<Tkey, TValue>();
            foreach (var pair in collection)
            {
                dictionary.Add(
                    pair.Key,
                    (TValue)ConvertAny(
                        pair.Value,
                        typeof(TValue),
                        targetInstance != null && targetInstance.TryGetValue(pair.Key, out TValue existingValue) ? existingValue : default(TValue),
                        context.WithNewName(StringId.Create(m_context.StringTable, pair.Key.ToString()))));
            }

            if (targetInstance == null)
            {
                return dictionary;
            }

            foreach (var kv in targetInstance)
            {
                if (!dictionary.ContainsKey(kv.Key))
                {
                    dictionary[kv.Key] = kv.Value;
                }
            }

            return dictionary;
        }

        private object ConvertAny(object valueToConvert, Type resultType, object overridingOrMergeIntoValue, in ConversionContext context)
        {
            Contract.Requires(resultType != null);

            // TODO: Check this precondition because now we are converting value from another assembly.
            // Contract.Requires(!typeof(object).IsAssignableFrom(resultType) || resultType.Assembly == typeof(IConfiguration).Assembly);
            var resultTypeInfo = resultType.GetTypeInfo();

            // First need to check that the input type is correct.
            // If the property is a primitive type, but not a nullable type
            // then proper merging of the values wouldn't be possible.
            //
            // Consider the following case: interface IConfiguration {int RetryCount {get;}}
            // Merge operation will take an istance of the IConfiguration with 0 as a RetryCount property.
            // This function won't be able to recognize, whether the value was actually provided by the user via command line
            // or this was a default value for the instance.
            // To prevent this ambiguity, the function fails and the configuration interface must be changed.

            // TODO: this should be enforced in the future.
            // But this check will require too many changes in one shot.
            // if (resultTypeInfo.IsPrimitiveOrEnum())
            // {
            //    throw new ConversionException(
            //        I($"Cannot convert property of a value type '{resultType}'.\r\nAll primitive types should be nullable to support proper merging."),
            //        context.ErrorContext);
            // }

            // If the value is null or undefined, just using the original one.
            if (TypeExtensions.IsNullOrUndefined(valueToConvert))
            {
                return overridingOrMergeIntoValue;
            }

            // Now, need to unwrap the type if the resulting type is nullable
            var underlyingType = Nullable.GetUnderlyingType(resultType)?.GetTypeInfo();
            if (underlyingType != null)
            {
                resultTypeInfo = underlyingType;
            }

            // If the value is primitive or enum and the original value is provided, then no merge is happening
            // The function just returns the original value.
            // (No conversion is needed in this case, because we know that the type of the value is correct,
            // because it was obtained from the real configuration instance).
            if ((resultTypeInfo.IsPrimitiveOrEnum() || resultTypeInfo.TypeHandle.IsString()) && overridingOrMergeIntoValue != null)
            {
                return overridingOrMergeIntoValue;
            }

            if (resultTypeInfo.TypeHandle.IsUnitType())
            {
                return ConvertToUnit(valueToConvert, context);
            }

            if (resultTypeInfo.TypeHandle.IsBooleanType())
            {
                return ConvertToBool(valueToConvert, context);
            }

            if (resultTypeInfo.TypeHandle.IsNumberType())
            {
                return ConvertToNumber(valueToConvert, resultTypeInfo, context);
            }

            if (resultTypeInfo.IsEnum)
            {
                return ConvertToEnum(valueToConvert, resultTypeInfo, context);
            }

            if (resultTypeInfo.TypeHandle.IsString())
            {
                return ConvertToString(valueToConvert, context);
            }

            // target: List<?> (List<,> is deprecated, thus not handled here)
            if (resultTypeInfo.IsListOfT() || resultTypeInfo.IsReadOnlyListOfT())
            {
                return ConvertToList(valueToConvert, resultType, overridingOrMergeIntoValue, context);
            }

            // target: Dictionary<?,?>
            if (resultTypeInfo.IsDictionaryOfT() || resultTypeInfo.IsReadOnlyDictionaryOfT())
            {
                return ConvertToDictionary(valueToConvert, resultType, overridingOrMergeIntoValue, context);
            }

            // target: FileArtifact
            if (resultTypeInfo.TypeHandle.IsFileArtifact())
            {
                return FileArtifact.CreateSourceFile(ConvertToAbsolutePath(valueToConvert, overridingOrMergeIntoValue, context));
            }

            // target: AbsolutePath
            if (resultTypeInfo.TypeHandle.IsAbsolutePath())
            {
                return ConvertToAbsolutePath(valueToConvert, overridingOrMergeIntoValue, context);
            }

            // target: DirectoryArtifact
            if (resultTypeInfo.TypeHandle.IsDirectoryArtifact())
            {
                return DirectoryArtifact.CreateWithZeroPartialSealId(ConvertToAbsolutePath(valueToConvert, overridingOrMergeIntoValue, context));
            }

            // target: PathAtom
            if (resultTypeInfo.TypeHandle.IsPathAtom())
            {
                return ConvertToPathAtom(overridingOrMergeIntoValue ?? valueToConvert, context);
            }

            // target: RelativePath
            if (resultTypeInfo.TypeHandle.IsRelativePath())
            {
                return ConvertToRelativePath(overridingOrMergeIntoValue ?? valueToConvert, context);
            }

            // target: UnionType
            if (resultTypeInfo.IsUnionType())
            {
                return ConvertToUnionType(overridingOrMergeIntoValue ?? valueToConvert, resultType, context);
            }

            // target: object
            return ConvertToObject(valueToConvert, resultType, overridingOrMergeIntoValue, context);
        }

        private string ResolveSymbol(StringId stringId)
        {
            Contract.Requires(stringId.IsValid);
            return m_context.StringTable.GetString(stringId);
        }

        private SymbolAtom GetSymbol(string name)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            // TODO: For general use, one may want to add proper handling for getting a symbol from a name.
            return SymbolAtom.Create(m_context.StringTable, name);
        }
    }
}
