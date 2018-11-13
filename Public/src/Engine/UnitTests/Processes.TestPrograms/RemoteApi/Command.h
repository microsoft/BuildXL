// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

// Support for wrapping functions as named Command objects.
// This allows dispatching a string like "CommandName,a,b" to a call CommandName("a", "b").

enum CommandInvocationResult {
    Success,
    Failure,
    CommandNameDoesNotMatch,
    IncorrectParameterCount
};

// Arity-specific function types. Note that these are not pointers.
typedef bool(SingleParam)(std::wstring const&);
typedef bool(DualParam)(std::wstring const&, std::wstring const&);

// Artiy-specific adapters from a list of size N to fn(1, 2, ...N)
template<typename Fn> bool invokeList(Fn* fn, std::vector<std::wstring> const& parameters);

template<> bool invokeList<SingleParam>(SingleParam* fn, std::vector<std::wstring> const& parameters) {
    return fn(parameters[1]);
}

template<> bool invokeList<DualParam>(DualParam* fn, std::vector<std::wstring> const& parameters) {
    return fn(parameters[1], parameters[2]);
}

// Arity agnostic base type. Dispatch should be through a pointer to CommandBase.
// A program may have some collection of CommandBase pointers, and try to dispatch a command string to each.
class CommandBase {
public:
    size_t requiredParameters;
    std::wstring commandName;

    CommandBase(size_t requiredParameters, std::wstring const& commandName)
        : requiredParameters(requiredParameters), commandName(commandName) {}

    CommandInvocationResult InvokeIfMatches(std::vector<std::wstring> const& parameters) const {
        assert(parameters.size() > 0);

        std::wstring const& nameToInvoke = parameters[0];
        if (nameToInvoke != commandName) {
            return CommandNameDoesNotMatch;
        }

        if (requiredParameters + 1 != parameters.size()) {
            return IncorrectParameterCount;
        }

        return UnpackAndInvoke(parameters);
    }

    virtual ~CommandBase() {}

protected:
    virtual CommandInvocationResult UnpackAndInvoke(std::vector<std::wstring> const& parameters) const = 0;
};

// Command has one type parameter - the (non-pointer) function type.
template<typename Fn> class Command;

// ...but we provide one specialization of it since this lets us unpack to a Result and Args... pack.
// Instantiating Command<bool(int)> works but Command<int> does not (since there's no specialization for int)
// This is the same trickery used by std::function.
template<typename Result, typename ...Args>
class Command<Result(Args...)> : public CommandBase{
public:
    typedef Result(*FnType)(Args...);
    Command(std::wstring const& commandName, FnType fn)
        : CommandBase(sizeof...(Args), commandName), fn(fn) {}

private:
    FnType fn;

protected:
    virtual CommandInvocationResult UnpackAndInvoke(std::vector<std::wstring> const& parameters) const {
        bool succeeded = invokeList(fn, parameters);
        return succeeded ? Success : Failure;
    }
};