Currently, the best way to debug DScript is to strategically sprinkle writeline functions throughout the code. 
Here are a few functions that might be useful

```ts
    /** Prints out (to stdout) each given object on its own line. */
    export declare function writeLine(...strings: any[]);

    /** Dumps the current callstack with the given message. */
    export declare function dumpCallStack(message: string) : void;

    /** Dumps data into a string. */
    export declare function dumpData(data: Transformer.Data): string;
   
    /** Launches the debugger. */
    export declare function launch(): void;

    /** Returns string representation of given command-line arguments. This does not print to console*/
    export declare function dumpArgs(args: Argument[]): string;
```