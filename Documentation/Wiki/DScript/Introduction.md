# Introduction
BuildXL uses the DScript scripting language as one of its front-end build specification languages, used by the BuildXL repo itself plus a few Microsoft teams like Office.

DScript is based on [TypeScript](http://www.typescriptlang.org/) which derives from [JavaScript](https://www.javascript.com/).

Since TypeScript is a strict superset of JavaScript and JavaScript has a lot of dynamic features that hamper a deterministic evaluation, as well as parallel evaluation and for us to predict codeflow to allow for incremental evaluation we have chosen to restrict the features allowed.

As a rule every DScript program is a valid TypeScript program but not the other way around. 
There is one dirty *asterisk* to that statement and that is the added support for windows Slashes `\` in File, Directory and Path literals. That feature broke the compatibility. But if you disable the support for `\` slashes and stick to `/` slashes the specs are compatible.

# BuildXL DScript Design Principles
To ensure we have a holistic design we have defined a set of Design principles which must be used when framing  our language and runtime discussions.

## Principles
* Maintain DScript guiding principles and requirements [details](#DScript-guiding-principles)
* Stay as close to TypeScript as possible [details](#Stay-as-close-to-TypeScript-as-possible)
* Use TypeScript features whenever they match our requirements [details](#Use-TypeScript-features)
* Maintain a clear doc on where what TypeScript is not DScript and vice versa [details](#Document-differences)
* Collaborate to add useful features to TypeScript [details](#Collaborate)

## DScript guiding Principles
We have a few principles that we want to maintain in DScript:
* Pure functions:
  * i.e. functions should not have any side effects.
  * This property will allow us to memoize function invocations
  * This property will allow us to cache evaluation results across builds
* Immutable structures
  * This helps with maintaining pure functions and memory reuse
* Path primitives
  * `Path`, `File` and `Directory` are so pervasive in build specs that they deserve their own primitives.
* Qualified files:
  * A single spec file/value could be evaluated in multiple contexts. Think debug/release and x86/x64/arm32/arm64 etc.
* Package/Module substitutability:
* Strong typing
  * In some scenarios we want structural typing to facilitate extension points.
  * In some scenarios we want nominal typing to facilitate runtime type information or a stronger lock-down of values for large code bases. 

## Stay as close to TypeScript as possible
We want to stay as close to TypeScript as possible. The motivation for this is that the TypeScript team has a lot of experience with language Design which we want to leverage. Not to mention existing implementations, tooling, documentation we can leverage.

This helps us in the following: 
* User education
  * There is already lots of documentation 
* inconsistent 1-off decisions. 
  * TypeScript has a large set of Language experts that ensure the TypeScript language is consistent, learnable and usable.
* IDE
  * Various IDE's, tools (codeflow) already have colorizers, and some have intellisense for TypeScript. We would have to start from scratch
* Type checker
  * We want to do type checking. We can leverage the type checker 
* Runtime
  * If we stay close to typescript we could use the compiler as-is, possibly using some extension points.
  * At the moment javascript engines single-threadedness will not get us the necessary perf above a thousand projects, hence we are investing in our own interpreter for now. But when the javascript ones have proper multithreading and we can memoize properly we should not block ourselves from going to that model. Ensuring that we maintain a possible translation aids us in that regard.
* Debugger
  * When we would use a javascript runtime we'd get a debugger for free. 
  * For now we'll have to implement one to scrape by. Luckily VsCode has a very sane debugger Api.
* Linters 
  * TypeScript already has lots of linters which our users could adopt.
 
## Long term maintenance cost
When making language decisions, the following are not good arguments: 
* "but I already implemented it this (possibly not TypeScript-like) way 
  * That's for free now, everything else would take a day" 
* "I can implement this in some linter" 
  * Linters are nice domain specific policy enforces, but the ultimate truths lies in the actual evaluation engine.
 
## Use TypeScript features
TypeScript is a language that is constantly evolving and adding new features. When these features align with our requirements we should make sure that we force users to use those features if they are not default. 
The motivation for this is that tooling for TypeScript will not break those requirements. An example could be the immutability of a field. If TypeScript has a built-in feature for that requirement, tooling will be specialized already. (i.e. intellisense would not show assignment and refactorings would not emit code that modifies the field.  
 
## Runtime should not influence syntax
In some cases where we need to differ in runtime semantics. This doesn't mean we should also give up on compatibility with the syntax and the type system and we should strive strongly to do so. It should not be an excuse to then differ syntactically, since that will mean giving up more of the benefits we get from basing it on typescript.

## Don't differ with subtle syntax
In some cases where we like to differ, but not in a subset way. I.e Typescript couldn't parse or couldn't typecheck the build spec. Here we should make it blatantly obvious in the syntax that it is different from typescript.  
 
## Translation possible
When we decide to differ anywhere. There MUST be a translation to TypeScript that matches the semantics we chose. The motivation for this is that at least DScript can be a pre-processor for TypeScript and still get the benefit of the entire stack.
 
## Document differences
We will rigorously document all differences and label where the difference is:
 
| Area | Axis |
|---|---|
| Scanner/Parser/Grammar | Strict SubSet (lint), Strict SubSet, Breaking Syntax |
| TypeSystem | Strict SubSet, Breaking Syntax |
| Default Runtime behavior | Strict SubSet, CustomCodeGen required, Breaking |

## Collaborate
The goal is to be as close to 100% typescript as possible. For the places where we have a specific need that could make sense for TypeScript we will collaborate with the TypeScript team to see if the feature could be useful for them and if so we can co-develop the feature in TypeScript.
  