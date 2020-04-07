SHELL   = /bin/bash

CC      = gcc
CXX     = g++

INC     = \
    ./                                      \
	../MacOs/Interop/Sandbox                \
    ../MacOs/Interop/Sandbox/Data           \
	../MacOs/Interop/Sandbox/Handlers       \
    ../MacOs/Sandbox/Src                    \
    ../MacOs/Sandbox/Src/FileAccessManifest \
	../MacOs/Sandbox/Src/Kauth              \
    ../Windows/DetoursServices

INC_FLAGS = $(foreach d, $(INC), -I$d)

CXXFLAGS = -c -fPIC --std=c++14 $(INC_FLAGS) 
DBGFLAGS = -g -D_DEBUG
RELFLAGS = -o3 -D_NDEBUG
LDFLAGS  = -ldl -lpthread

src = \
	$(wildcard *.cpp) \
	$(wildcard ../MacOs/Interop/Sandbox/Data/*.cpp) \
	$(wildcard ../MacOs/Interop/Sandbox/Handlers/*.cpp) \
	../MacOs/Interop/Sandbox/Sandbox.cpp \
	../MacOs/Sandbox/Src/FileAccessManifest/FileAccessManifestParser.cpp \
	../MacOs/Sandbox/Src/Kauth/Checkers.cpp \
	../MacOs/Sandbox/Src/Kauth/OpNames.cpp \
	../Windows/DetoursServices/PolicyResult_common.cpp \
	../Windows/DetoursServices/PolicySearch.cpp \
	../Windows/DetoursServices/StringOperations.cpp

dbgobj = $(src:.cpp=.d.o)
relobj = $(src:.cpp=.r.o)
dbgdep = $(dbgobj:.o=.deps)
reldep = $(dbgobj:.o=.deps)
dep = $(dbgdep) $(reldep)

%.d.deps: %.cpp
	@$(CPP) $(CXXFLAGS) $(DBGFLAGS) $< -MM -MT $(@:.deps=.o) > $@

%.r.deps: %.cpp
	@$(CPP) $(CXXFLAGS) $(RELFLAGS) $< -MM -MT $(@:.deps=.o) > $@

%.d.o: %.cpp
	$(CXX) $(CXXFLAGS) $(DBGFLAGS) -o $@ $<

%.r.o: %.cpp
	$(CXX) $(CXXFLAGS) $(RELFLAGS) -o $@ $<

all: debug release
debug: prep bin/debug/libDetours.so
release: prep bin/release/libDetours.so

prep:
	@mkdir -p bin/debug bin/release

main: $(dbgobj)
	$(CXX) $^ -o bin/debug/main $(LDFLAGS)

bin/release/libDetours.so: $(relobj)
	$(CXX) -shared $^ -o bin/release/libDetours.so $(LDFLAGS)

bin/debug/libDetours.so: $(dbgobj)
	$(CXX) -shared $^ -o bin/debug/libDetours.so $(LDFLAGS)

-include $(dep)

.PHONY: clean
clean:
	rm -rf $(dbgobj) $(relobj) bin/*

.PHONY: cleandep
cleandep:
	rm -f $(dep)

.PHONY: cleanall
cleanall: cleandep clean