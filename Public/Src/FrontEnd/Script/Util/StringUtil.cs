// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Utilities for strings.
    /// </summary>
    public static class StringUtil
    {
        /// <summary>
        /// Checks if a name is a valid identifier.
        /// </summary>
        public static bool IsValidId(string name)
        {
            Contract.Requires(name != null);

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            for (int i = 0; i < name.Length; ++i)
            {
                if (i == 0)
                {
                    if ((name[i] >= 'a' && name[i] <= 'z') || (name[i] >= 'A' && name[i] <= 'Z') || name[i] == '_')
                    {
                        // Valid first character.
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    if ((name[i] >= 'a' && name[i] <= 'z') || (name[i] >= 'A' && name[i] <= 'Z') ||
                        (name[i] >= '0' && name[i] <= '9') || name[i] == '_')
                    {
                        // Valid character.
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
