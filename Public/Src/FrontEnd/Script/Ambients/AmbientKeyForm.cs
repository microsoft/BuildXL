// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.FrontEnd.Script.Ambients.Exceptions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for KeyForm namespace.
    /// </summary>
    public sealed class AmbientKeyForm : AmbientDefinitionBase
    {
        private const string KeyFormFileName = "keyform.dll";
        private const string KeyFormFunction = "GetKeyForm";

        private readonly ConcurrentDictionary<AbsolutePath, GetKeyFormHandler> m_handlers = new ConcurrentDictionary<AbsolutePath, GetKeyFormHandler>();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void GetKeyFormHandler(string arch, string name, string version, string publicKeyToken, string versionScope, string culture, string type, StringBuilder output, uint outputBytes);

        /// <nodoc />
        public AmbientKeyForm(PrimitiveTypes knownTypes)
            : base("KeyForm", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("KeyForm"),
                new[]
                {
                    Function("getKeyForm", GetKeyForm, GetKeyFormSignature),
                });
        }

        /// <summary>
        /// Special pool for <see cref="StringBuilder"/> used by <see cref="GetKeyForm"/> function.
        /// </summary>
        /// <remarks>
        /// <see cref="GetKeyForm"/> has a special requirements for the size of the builder that prevents us from using regular pool for <see cref="StringBuilder"/>.
        /// </remarks>
        private readonly ObjectPool<StringBuilder> m_stringBuilderCache = new ObjectPool<StringBuilder>(() => new StringBuilder(2048), sb => sb.Clear());

        private EvaluationResult GetKeyForm(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var keyFormDll = Args.AsFile(args, 0);
            var arch = Args.AsString(args, 1);
            var name = Args.AsString(args, 2);
            var version = Args.AsString(args, 3);
            var publicKeyToken = Args.AsString(args, 4);
            var versionScope = Args.AsStringOptional(args, 5);
            var culture = Args.AsStringOptional(args, 6);
            var type = Args.AsStringOptional(args, 7);

            var handler = m_handlers.GetOrAdd(
                keyFormDll.Path,
                _ =>
                {
                    var keyFormDllPath = keyFormDll.Path.ToString(context.PathTable);
                    var fileName = Path.GetFileName(keyFormDllPath);
                    if (!string.Equals(KeyFormFileName, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new KeyFormDllWrongFileNameException(keyFormDllPath, KeyFormFileName, new ErrorContext(pos: 1));
                    }

                    if (!File.Exists(keyFormDllPath))
                    {
                        throw new KeyFormDllNotFoundException(keyFormDllPath, new ErrorContext(pos: 1));
                    }

                    IntPtr moduleHandle = NativeMethods.LoadLibraryW(keyFormDllPath);
                    if (moduleHandle == IntPtr.Zero)
                    {
                        var lasterror = Marshal.GetLastWin32Error();
                        var ex = new Win32Exception(lasterror);
                        throw new KeyFormDllLoadException(keyFormDllPath, lasterror, ex.Message, new ErrorContext(pos: 1));
                    }

                    IntPtr procHandle = NativeMethods.GetProcAddress(moduleHandle, KeyFormFunction);
                    if (procHandle == IntPtr.Zero)
                    {
                        var lasterror = Marshal.GetLastWin32Error();
                        var ex = new Win32Exception(lasterror);
                        throw new KeyFormDllLoadException(keyFormDllPath, lasterror, ex.Message, new ErrorContext(pos: 1));
                    }

                    return Marshal.GetDelegateForFunctionPointer<GetKeyFormHandler>(procHandle);
                });

            using (var pooledBuilder = m_stringBuilderCache.GetInstance())
            {
                var builder = pooledBuilder.Instance;
                try
                {
                    // Since this is native code hardening our process against threading issues
                    // in the native code by ensuring we only access from single thread.

                    // WIP: experimental. Removing locking for key form.
                    // lock (m_keyFormLock)
                    {
                        handler(arch, name, version, publicKeyToken, versionScope, culture, type, builder, (uint)builder.Capacity);
                    }
                }
                catch (Exception e)
                {
                    // I know it is bad to catch all exceptions, but this is going into native code of which we
                    // don't have control and this code doesn't handle weird input properly.
                    var keyFormDllPath = keyFormDll.Path.ToString(context.PathTable);
                    throw new KeyFormNativeFailureException(keyFormDllPath, e, new ErrorContext(pos: 1));
                }

                return EvaluationResult.Create(builder.ToString());
            }
        }

        private CallSignature GetKeyFormSignature => CreateSignature(
            required: RequiredParameters(
                AmbientTypes.FileType, // keyFormDll
                AmbientTypes.StringType, // arch
                AmbientTypes.StringType, // name
                AmbientTypes.StringType, // version
                AmbientTypes.StringType), // publicKeyToken
            optional: OptionalParameters(
                AmbientTypes.StringType, // versionScope
                AmbientTypes.StringType, // culture
                AmbientTypes.StringType), // type
            returnType: AmbientTypes.StringType);
    }
}
