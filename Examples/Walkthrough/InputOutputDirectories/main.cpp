#include <fstream>
#include <string>

int main(int argc, char **argv)
{
    std::ifstream in_file1(std::string(argv[1]) + "/" + "1.txt");
    std::ifstream in_file2(std::string(argv[1]) + "/" + "2.txt");

    std::ofstream out_file1(std::string(argv[2]) + "/" + "1.txt");
    std::ofstream out_file2(std::string(argv[2]) + "/" + "2.txt");

    out_file1 << in_file1.rdbuf();
    out_file2 << in_file2.rdbuf();
    return 0;
} 