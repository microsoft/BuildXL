SHELL   = /bin/bash

CC      = gcc
CXX     = g++

INC     = \
    ./                                      \
    ../MacOs/Interop/Sandbox/Data           \
    ../MacOs/Sandbox/Src                    \
    ../MacOs/Sandbox/Src/FileAccessManifest \
    ../Windows/DetoursServices

INC_FLAGS = $(foreach d, $(INC), -I$d)

CFLAGS   = -c -fPIC $(INC_FLAGS)
CXXFLAGS = -c -fPIC --std=c++14 $(INC_FLAGS) 
LDFLAGS  = -ldl

csrc   = $(wildcard *.c)
cppsrc = \
	$(wildcard *.cpp) \
	$(wildcard ../MacOs/Interop/Sandbox/Data/*.cpp) \
	$(wildcard ../MacOs/Interop/Handlers/*.cpp) \
	$(wildcard ../MacOs/Interop/*.cpp) \
	../MacOs/Sandbox/Src/FileAccessManifest/FileAccessManifestParser.cpp \
	../MacOs/Sandbox/Src/Kauth/Checkers.cpp \
	../MacOs/Sandbox/Src/Kauth/OpNames.cpp \
	../Windows/DetoursServices/PolicyResult_common.cpp \
	../Windows/DetoursServices/PolicySearch.cpp \
	../Windows/DetoursServices/StringOperations.cpp \

obj = $(csrc:.c=.o) $(cppsrc:.cpp=.o)
dep = $(obj:.o=.deps)

define GEN_DEPS
	@$(CPP) $(CFLAGS) $< -MM -MT $(@:.d=.o) > $@
endef

%.deps: %.c
	$(GEN_DEPS)

%.deps: %.cpp
	$(GEN_DEPS)

libDetours: $(obj)
	$(CXX) -shared $^ -o $@.so $(LDFLAGS)

-include $(dep)

.PHONY: clean
clean:
	rm -f $(obj) *.so

.PHONY: cleandep
cleandep:
	rm -f $(dep)

.PHONY: cleanall
cleanall: cleandep clean