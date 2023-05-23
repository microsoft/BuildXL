#include "hello.h"
#include "greetings.h"
#include <iostream>

void SayHello()
{
    std::cout << Greetings() << std::endl;
}

char* Greetings()
{
    return "Hello, World!";
}