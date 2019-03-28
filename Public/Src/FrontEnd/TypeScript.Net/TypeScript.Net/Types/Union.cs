// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Placeholder type to simplify migration from TypeScript to C# to model union types with 2 cases.
    /// </summary>
    public class Union<TFirst, TSecond>
    { }

    /// <summary>
    /// Placeholder type to simplify migration from TypeScript to C# to model union types with 3 cases.
    /// </summary>
    public class Union<TFirst, TSecond, TThird> : Union<TFirst, TSecond>
    { }

    /// <summary>
    /// Placeholder type to simplify migration from TypeScript to C# to model union types with 4 cases.
    /// </summary>
    public class Union<TFirst, TSecond, TThird, TFourth> : Union<TFirst, TSecond, TThird>
    { }
}
