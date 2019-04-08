This project is a port of TypeScript compiler.

Some useful notes:
* TypeScript type system is different from C# type system, so migration require some tricks.
* All TypeScript interfaces are modeled via C# interfaces to support multiple inheritance
* All union types are explicitely modeled using custom classes that explicitely implements unioned interfaces (see TypeUnion.cs file).
* Because union types could be combined using instances of derived interfaces, all casting operations should be performed via Cast<T>/As<T>
  extension methods.
* All types in Types.cs are similar to types in types.ts
* Every interface has appropriate implementation class (in NodeImplementation.cs) even when there is only one concrete implementation.
  This helps to build consisten solution.
* If you want to look at typescript implementation, please look into TypeScriptImpl folder. This folder contains typescript version of this port.