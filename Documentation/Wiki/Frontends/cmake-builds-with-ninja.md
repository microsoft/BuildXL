# Building CMake-based repos with BuildXL using the Ninja frontend
BuildXL can be used to build CMake-based repositories.
The only requirement is the repositories generate Ninja specifications through CMake's [Ninja generator](https://cmake.org/cmake/help/latest/generator/Ninja.html).
The command `cmake -GNinja` will output a `build.ninja` file containing the generated Ninja specifications.
BuildXL, with its [Ninja frontend](../Frontends/Ninja.md), can translate those Ninja specifications into a build graph that itcan schedule and execute.