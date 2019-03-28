// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Extension methods for BuildXL tables to convert ids to and from strings
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// Converts a StringId to a string
        /// </summary>
        /// <param name="stringTable">StringTable to use to convert the StringId</param>
        /// <param name="stringId">StringId to convert to string</param>
        /// <returns>The result of converting the provided StringId to a string</returns>
        public static string IdToString(this StringTable stringTable, StringId stringId)
        {
            return stringId.ToString(stringTable);
        }

        /// <summary>
        /// Converts a string to a StringId
        /// </summary>
        /// <param name="stringTable">StringTable to use to convert the string</param>
        /// <param name="str">String to convert to StringId</param>
        /// <returns>The result of converting the provided string to a StrindId</returns>
        public static StringId StringToId(this StringTable stringTable, string str)
        {
            // Create will mutate the StringTable if str is not already in the table
            // At some point, we should add a TryGet method to StringId because StringTable
            // should not mutate as a result of a call to StringToId
            return StringId.Create(stringTable, str);
        }

        /// <summary>
        /// Converts an AbsolutePath to a string
        /// </summary>
        /// <param name="pathTable">PathTable to use to convert the AbsolutePath</param>
        /// <param name="absolutePath">AbsolutePath to convert to string</param>
        /// <returns>The result of converting the provided AbsolutePath to a string</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        public static string AbsolutePathToString(this PathTable pathTable, AbsolutePath absolutePath)
        {
            if (absolutePath.IsValid)
            {
                return absolutePath.ToString(pathTable, PathFormat.Windows).ToLowerInvariant();
            }
            else
            {
                // Normally, an invalid AbsolutePath gets ToString'd to '{Invalid}' but we want an empty string instead
                return string.Empty;
            }
        }

        /// <summary>
        /// Converts a string to an AbsolutePath
        /// </summary>
        /// <param name="pathTable">PathTable to use to convert the string</param>
        /// <param name="str">String to convert to AbsolutePath</param>
        /// <returns>The result of converting the provided string to an AbsolutePath</returns>
        public static AbsolutePath StringToAbsolutePath(this PathTable pathTable, string str)
        {
            AbsolutePath absolutePath;
            if (AbsolutePath.TryGet(pathTable, str, out absolutePath))
            {
                return absolutePath;
            }
            else
            {
                throw new ArgumentException("Unable to find AbsolutePath for string: '" + str + "'");
            }
        }

        /// <summary>
        /// Converts a FullSymbol to a string
        /// </summary>
        /// <param name="symbolTable">SymbolTable to use to convert the FullSymbol</param>
        /// <param name="fullSymbol">FullSymbol to convert to string</param>
        /// <returns>The result of converting the provided FullSymbol to a string</returns>
        public static string FullSymbolToString(this SymbolTable symbolTable, FullSymbol fullSymbol)
        {
            return fullSymbol.ToString(symbolTable);
        }

        /// <summary>
        /// Converts a string to a FullSymbol
        /// </summary>
        /// <param name="symbolTable">SymbolTable to use to convert the string</param>
        /// <param name="str">String to convert to FullSymbol</param>
        /// <returns>The result of converting the provided string to a FullSymbol</returns>
        public static FullSymbol StringToFullSymbol(this SymbolTable symbolTable, string str)
        {
            FullSymbol fullSymbol;
            if (FullSymbol.TryGet(symbolTable, str, out fullSymbol))
            {
                return fullSymbol;
            }
            else
            {
                throw new ArgumentException("Unable to find FullSymbol for string: '" + str + "'");
            }
        }
    }
}
