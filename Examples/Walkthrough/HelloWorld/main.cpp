#include <fstream>

int main(int argc, char **argv)
{
    std::ifstream in_file(argv[1]);
    std::ofstream out_file(argv[2]);

    out_file << in_file.rdbuf();
    return 0;
} 