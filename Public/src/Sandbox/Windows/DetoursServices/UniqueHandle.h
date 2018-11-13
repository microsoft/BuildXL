#pragma once

template <HANDLE Invalid = INVALID_HANDLE_VALUE>
class unique_handle
{
private:
    HANDLE _handle;
    void close()
    {
        if (_handle != Invalid)
        {
            CloseHandle(_handle);
            _handle = Invalid;
        }
    }

    HANDLE replace(HANDLE newHandle)
    {
        HANDLE oldHandle = _handle;
        _handle = newHandle;
        return oldHandle;
    }

public:
    explicit unique_handle(HANDLE value = Invalid) : _handle(value) {}
    unique_handle(unique_handle &&right) : _handle(right.release()) {}
    ~unique_handle() { close(); }

    explicit operator bool() const{ return _value != Invalid; }
    unique_handle & operator=(unique_handle const &&anotherHandle)
    {
        if (anotherHandle._handle != _handle)
        {
            reset(anotherHandle.release());
        }
        return *this;
    }

    HANDLE get() const { return _handle; }
    void reset(HANDLE handle = Invalid)
    {
        HANDLE oldHandle = replace(handle);
        if (handle != oldHandle && oldHandle != Invalid)
        {
            CloseHandle(oldHandle);
        }
    }
    HANDLE release() { return replace(Invalid); }
    bool duplicate(HANDLE handle)
    {
        if (handle != Invalid)
        {
            HANDLE targetValue;
            HANDLE currentProcess = GetCurrentProcess();
            if (!DuplicateHandle(currentProcess, handle, currentProcess, &targetValue, 0, TRUE, DUPLICATE_SAME_ACCESS)) {
                return false;
            }

            reset(targetValue);
        }
        return true;
    }
    bool isValid() const { return _handle != Invalid; }

    unique_handle(unique_handle const &) = delete;
    unique_handle & operator=(unique_handle const &) = delete;
};
