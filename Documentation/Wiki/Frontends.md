BuildXL supports a wide variety of build languages and packaging systems. The engine is architected by allowing multiple frontends to coordinate and collaborate on constructing a build graph. Examples of frontends are:

* [DScript](DScript/Introduction.md)
* [JavaScript](Frontends/js-onboarding.md)
* The [Ninja frontend](Frontends/Ninja.md), which can be used to [build CMake projects](Frontends/cmake-builds-with-ninja.md) (experimental)
* MsBuild (in development)
* [Download](Frontends/Download.md)
* NuGet


The main `config.dsc` file has a field `resolvers` that provides configuration settings for each resolver.

Each frontend has two responsibilities:

* Provide a list of modules and their types that are declared in the resolver settings and provide exported values for each of those modules that other frontends can consume
* Provide the evaluation of those public values and construct pips for the PipGraph when asked to.
