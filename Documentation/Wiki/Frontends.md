BuildXL supports a wide variety of build languages and packaging systems. The engine is architected by allowing multiple frontends to coordinate and collaborate on constructing a build graph. Examples of frontends are:

* MsBuild (in development)
* Ninja (in development)
* NuGet
* DScript

The main `config.dsc` file has a field `resolvers` that provides configuration settings for each resolver.

Each frontend has two responsibilities:

* Provide a list of modules and their types that are declared in the resolver settings and provide exported values for each of those modules that other frontends can consume
* Provide the evaluation of those public values and construct pips for the PipGraph when asked to.
