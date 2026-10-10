# Builds mdk_leia.dll (the MDK port's Simulated Reality host).
#
#   cmake -S . -B build -G "Visual Studio 17 2022" -A x64
#   cmake --build build --config Release
#
#   -DMDK_LEIA_SRLIB_DIR=<checkout>   builds the real weaver: bo3b/SR-lib's
#                                     api_expansion branch (CreateSRInterfaceDX12)
#                                     with its vendored SDK. The SDK is proprietary
#                                     and stays local; without it the stub is built
#                                     and the Leia SR mode presents the plain pair.
#
# The SDK import libraries are linked but every SR DLL is delay-loaded
# (srlib_apply_delayload): a machine that never had the SR Platform can still run
# the game, and the weaver's probe fails gracefully instead of a missing-DLL
# popup before main(). Verify with:
#   dumpbin /imports mdk_leia.dll | findstr .dll

cmake_minimum_required(VERSION 3.16)
project(mdk_leia LANGUAGES CXX)

set(MDK_LEIA_SRLIB_DIR "" CACHE PATH "bo3b/SR-lib checkout (api_expansion): builds the real weaver")

add_library(mdk_leia SHARED mdk_leia.cpp)
set_target_properties(mdk_leia PROPERTIES PREFIX "")
target_compile_features(mdk_leia PRIVATE cxx_std_17)

if (MDK_LEIA_SRLIB_DIR AND EXISTS "${MDK_LEIA_SRLIB_DIR}/CMakeLists.txt")
    add_subdirectory("${MDK_LEIA_SRLIB_DIR}" srlib)
    if (SRLIB_FOUND)
        target_compile_definitions(mdk_leia PRIVATE MDK_LEIA_SR)
        target_link_libraries(mdk_leia PRIVATE SRLib::SR)
        srlib_apply_delayload(mdk_leia)
        message(STATUS "mdk_leia: SR-lib found; building the real weaver")
    else()
        message(STATUS "mdk_leia: SR-lib present but its SDK was not found; building the stub")
    endif()
else()
    message(STATUS "mdk_leia: no SR-lib at MDK_LEIA_SRLIB_DIR; building the stub")
endif()
