// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

// DummyWaiter.exe - Executable which launches and blocks until a key is pressed.
//                   This is useful for poking at the draconian access restrictions
//                   on the executable file while it is running (e.g. can a link to it be created? deleted?)

namespace Test.BuildXL.Executables.DummyWaiter
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.ReadLine();
        }
    }
}
