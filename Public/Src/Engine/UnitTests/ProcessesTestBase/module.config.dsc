// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// This module exists as a separate module (not part of BuildXL.Core.UnitTests) to break a
// cyclic evaluation dependency in DScript. Both Test.BuildXL.Processes and Test.BuildXL.Scheduler
// need ProcessesTestBase, but they are in the same module (BuildXL.Core.UnitTests). If
// ProcessesTestBase lived in either spec, the other would create a circular reference within
// the module. By extracting it into its own module, both can import it cleanly.
//
// This also enables PipTestBase (in Scheduler) to inherit from ProcessesTestBase, eliminating
// ~400 lines of duplicated test setup code (BuildXLContext creation, TestProcess discovery,
// pip construction helpers, untracked scopes, etc.).
//
// Other test assemblies that need lightweight process-pip construction without pulling in
// Scheduler dependencies (e.g., Detours tests, future sandbox tests) could also reference
// this module directly.
module({
    name: "BuildXL.Engine.ProcessesTestBase",
});
