// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
