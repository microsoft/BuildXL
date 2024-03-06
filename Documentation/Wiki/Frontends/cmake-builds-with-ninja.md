# Building CMake-based repos with BuildXL using the Ninja frontend
BuildXL provides support for CMake-based repositories. 
This is achieved with a particular [frontend](../Frontends.md) type that knows how to translate a Ninja specification into a graph that BuildXL can then schedule and execute.
Thus, the main requirement is that the CMake build works with [the Ninja generator](https://cmake.org/cmake/help/latest/generator/Ninja.html). 

Thus, the way to build with CMake and BuildXL is to generate the Ninja specification with `cmake -GNinja`, which will output a `build.ninja` file into the build tree, and then use the [Ninja frontend](Ninja.md) in your configuration to run the build.