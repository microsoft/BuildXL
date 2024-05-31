function salt() {}
salt.apply("hello", []);
// CodeQL [SM04509] Intentional usage of a function constructor in a test. The executed code is static.
(new Function("return 5"))();
 
 
