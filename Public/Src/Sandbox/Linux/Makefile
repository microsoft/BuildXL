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

CXXFLAGS = -c -fPIC --std=c++17 $(INC_FLAGS) 
DBGFLAGS = -g -Og -D_DEBUG
RELFLAGS = -O3 -D_NDEBUG

commonSrc = \
	$(wildcard ../MacOs/Interop/Sandbox/Data/*.cpp) \
	$(wildcard ../MacOs/Interop/Sandbox/Handlers/*.cpp) \
	../MacOs/Interop/Sandbox/Sandbox.cpp \
	../MacOs/Sandbox/Src/FileAccessManifest/FileAccessManifestParser.cpp \
	../MacOs/Sandbox/Src/Kauth/Checkers.cpp \
	../MacOs/Sandbox/Src/Kauth/OpNames.cpp \
	../Windows/DetoursServices/PolicyResult_common.cpp \
	../Windows/DetoursServices/PolicySearch.cpp \
	../Windows/DetoursServices/StringOperations.cpp

detoursSrc = \
	bxl_observer.cpp \
	detours.cpp

auditSrc = \
	bxl_observer.cpp \
	audit.cpp

commonObj = $(commonSrc:.cpp=.d.o) $(commonSrc:.cpp=.r.o)
detoursObj = $(detoursSrc:.cpp=.detours.d.o) $(detoursSrc:.cpp=.detours.r.o)
auditObj = $(auditSrc:.cpp=.d.o) $(auditSrc:.cpp=.r.o)
allObj = $(detoursObj) $(auditObj) $(commonObj)
allDep = $(allObj:.o=.deps)

%.deps: %.cpp
	@$(CPP) $(CXXFLAGS) $< -MM -MT $(@:.deps=.o) > $@

%.detours.d.o: %.cpp
	$(CXX) -DENABLE_INTERPOSING $(CXXFLAGS) $(DBGFLAGS) -o $@ $<

%.detours.r.o: %.cpp
	$(CXX) -DENABLE_INTERPOSING $(CXXFLAGS) $(RELFLAGS) -o $@ $<

%.d.o: %.cpp
	$(CXX) $(CXXFLAGS) $(DBGFLAGS) -o $@ $<

%.r.o: %.cpp
	$(CXX) $(CXXFLAGS) $(RELFLAGS) -o $@ $<

all: debug release
debug: prep bin/debug/libDetours.so bin/debug/libBxlAudit.so
release: prep bin/release/libDetours.so bin/release/libBxlAudit.so

prep:
	@mkdir -p bin/debug bin/release

bin/release/libDetours.so: $(filter %.r.o, $(commonObj) $(detoursObj))
	$(CXX) -shared $^ -o bin/release/libDetours.so -ldl -lpthread

bin/debug/libDetours.so: $(filter %.d.o, $(commonObj) $(detoursObj))
	$(CXX) -shared $^ -o bin/debug/libDetours.so -ldl -lpthread

bin/release/libBxlAudit.so: $(filter %.r.o, $(commonObj) $(auditObj))
	$(CXX) -shared $^ -o bin/release/libBxlAudit.so -ldl

bin/debug/libBxlAudit.so: $(filter %.d.o, $(commonObj) $(auditObj))
	$(CXX) -shared $^ -o bin/debug/libBxlAudit.so -ldl

-include $(allDep)

.PHONY: clean
clean:
	rm -rf $(allObj) bin/*

.PHONY: cleandep
cleandep:
	rm -f $(allDep)

.PHONY: cleanall
cleanall: cleandep clean