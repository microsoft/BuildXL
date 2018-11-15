//
//  arg_parse.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef arg_parse_hpp
#define arg_parse_hpp

#include <iostream>

using namespace std;

#define _log(format, type, ...) printf("[%s] ", type); printf(format, __VA_ARGS__); printf("\n");
#define error(format, ...) _log(format, "ERROR", __VA_ARGS__)
#define info(format, ...)  _log(format, "INFO", __VA_ARGS__)
#define debug(format, ...) _log(format, "DEBUG", __VA_ARGS__)

// forward declaration
class Config;

typedef bool (*Parser)(Config *config, const char *value);

class ArgMeta
{
    const char *longName_;
    const char *shortName_;
    const char *description_;
    bool isRequired_ = false;

public:
    ArgMeta() {}

    inline const char* LongName() const { return longName_; }
    inline const char* ShortName() const { return shortName_; }
    inline const char* Description() const { return description_; }
    inline bool IsRequired() const { return isRequired_; }

    inline ArgMeta* LongName(const char *longName)
    {
        longName_ = longName;
        return this;
    }
    
    inline ArgMeta* ShortName(const char *shortName)
    {
        shortName_ = shortName;
        return this;
    }
    
    inline ArgMeta* Description(const char *description)
    {
        description_ = description;
        return this;
    }

    inline ArgMeta* Required()
    {
        isRequired_ = true;
        return this;
    }
};

typedef struct {
    const char *Name;
    const type_info *Type;
    const char *Default;
    const Parser Parser;
    ArgMeta Meta;
    
    bool IsFlag() const { return *Type == typeid(bool); }
} Arg;

class ConfigImpl
{
private:
    Config *config_;
    const Arg *args_;
    const int argCount_;

public:
    ConfigImpl(Config *config, const Arg *args, const int argCount)
        : config_(config), args_(args), argCount_(argCount) {}

    bool parse(int argc, const char * argv[]) const;
    void printUsage() const;
    
    static bool parse(const type_info *ti, const char *value, void *result);
};

// ============================================================
#pragma mark Public macros for generating the Config class
// ============================================================

#define GEN_ARGTYPE_ENUM(ENUM_ARGS) \
typedef enum {                      \
    ENUM_ARGS(_ENUM_CONST_DECL)     \
    kArgMax                         \
} ArgType;

/*
 * Generates the Config class declaration.
 */
#define GEN_CONFIG_DECL(ENUM_ARGS)  \
GEN_ARGTYPE_ENUM(ENUM_ARGS)         \
class Config                        \
{                                   \
private:                            \
    const ConfigImpl impl_;         \
                                    \
public:                             \
    ENUM_ARGS(_CONFIG_FIELD_DECL)   \
                                    \
    static Arg Args[];              \
    static const int ArgCount;      \
                                    \
    Config() : impl_(this, Args, ArgCount) {}                                           \
    inline static ArgMeta* argMeta(ArgType type)    { return &Args[type].Meta; }        \
    inline void printUsage()                        { impl_.printUsage(); }             \
    inline bool parse(int argc, const char *argv[]) { return impl_.parse(argc, argv); } \
};

/*
 * Generates the Config class definition.
 */
#define GEN_CONFIG_DEF(ENUM_ARGS) \
Arg Config::Args[] =              \
{                                 \
    ENUM_ARGS(_ARG_DECL)          \
};                                \
                                  \
const int Config::ArgCount =      \
    sizeof(Args)/sizeof(Args[0]);

// ============================================================
#pragma mark Private macros used below to generate Config class
// ============================================================

#define _ENUM_CONST_DECL(name, type, defaultValue) kArg_##name,

#define _CONFIG_FIELD_DECL(name, type, defaultValue) type name = defaultValue;

#define _ARG_DECL(name, type, defaultValue) \
{                                           \
    .Name    = #name,                       \
    .Type    = &typeid(type),               \
    .Default = #defaultValue,               \
    .Parser  = [](Config *config, const char *value) { return ConfigImpl::parse(&typeid(type), value, &config->name);} \
},

#endif /* arg_parse_hpp */
