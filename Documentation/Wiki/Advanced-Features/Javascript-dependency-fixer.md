# JavaScript Dependency Fixer
One of the most common problems when onboarding a repo to BuildXL is undeclared dependencies. A missing dependency can cause a scheduling problem, since if the scheduler is unaware of a dependency it can schedule a dependency to run after a dependent, causing failures or undesired behavior in a non-deterministic way. So BuildXL is pretty strict when it detects that a tool accesses an output from another tool without properly declaring a dependency to it.

For JavaScript projects, in particular projects scheduled by any [JavaScript resolver](../Frontends/js-onboarding.md), a JavaScript execution analyzer can be run that analyzes dependency violations and adds additional entries in the corresponding `package.json` files in an attempt to fix them. 

Let's say you did a build with BuildXL that failed because of missing dependencies. E.g.:

```
[1:25.936] error DX0500: [Pip5F85CD981F01C36F, cmd.exe (midgard - @msfast/search-components-carousel), midgard, _msfast_search_components_carousel_build, {}] - Disallowed file accesses were detected (R = read, W = write):
Disallowed file accesses performed by: C:\WINDOWS\system32\cmd.exe
 W  G:\src\Midgard\packages\search-components-carousel\src\string-resources\en-US\search-components-carousel_strings.json

W  = Write

Violations related to pip(s):
Pip804AF1206E2DC8B7, cmd.exe (midgard - @msfast/equation-editor-uwp), midgard, _msfast_equation_editor_uwp_build, {}
```

Violations like the one above gets recorded in the BuildXL [binary execution log](../How-To-Run-BuildXL/Log-Files/BuildXL.xlg.md). After the failed build is done, you can run:

```
> BuildXLAnalyzer.exe /m:JavaScriptDependencyFixer /executionLog:<path-to-last-buildxl.xlg>
```
The analyzer will look into every missing dependency violation, locate the corresponding package.json and try to add the missing dependency:

```
Analyzing G:\src\Midgard\packages\search-components-carousel\package.json
--- Adding dependency @msfast/equation-editor-uwp: 0.0.1
Done with G:\src\Midgard\packages\search-components-carousel\package.json
```

The new dependency is added under `'devDependencies'`. E.g.:

```json
{
  "name": "@msfast/search-components-carousel",
   ...
}
"devDependencies": {
    "@msfast/equation-editor-uwp": "0.0.1",
    "@msfast/midgard-scripts": "*",
    "@msfast/tsconfig": "*",
    "typescript": "npm:@msfast/typescript-platform-resolution@3.8.2"
  },
```

Note that this analyzer will always add detected missing dependencies under `devDependencies`. Dependencies can be manually moved under `dependencies` if deemed appropriate.

**Tip**: Several iterations of this process may be needed since a dependency violation blocks downstream pips from running, which may contain violations as well. You can try passing `/unsafe_UnexpectedFileAccessesAreErrors-` to bxl to discover more violations and try to fix them in one go. Observe this flag is unsafe, and its use is not recommended beyond non-production scenarios.