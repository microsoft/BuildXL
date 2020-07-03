The concept of qualifiers is explained [here](Qualifiers.md), where sample usages can be found too.  This section only goes over the `QualifierConfiguration` interface (and related abstractions) used to configure them.

A qualifier configuration consists of three parts:
* `qualifierSpace`: a collection of key-value pairs, where a key is a string (think of it as "a name of a build parameter") and a value is a collection of strings (think of it as "allowed values of the corresponding build parameter").
* `defaultQualifier`: a collection of string-string key-value pairs which specifies a default value for every build parameter defined in `qualifierSpace`.
* `namedQualifiers`: allows the user to define symbolic names for concrete qualifier instances (those symbolic names can then be specified via the command line).

```ts
interface QualifierInstance {
    [name: string]: string;
}

interface QualifierSpace {
    [name: string]: string[];
}

interface QualifierConfiguration {
    /** The default qualifier space for this build */
    qualifierSpace?: QualifierSpace;

    /** The default qualifier to use when none specified on the commandline */
    defaultQualifier?: QualifierInstance;

    /** A list of alias for qualifiers that can be used on the commandline */
    namedQualifiers?: {
        [name: string]: QualifierInstance;
    };
}
```