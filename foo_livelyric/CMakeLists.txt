cmake_minimum_required(VERSION 3.15)
project(foo_livelyric)

set(CMAKE_CXX_STANDARD 17)

add_library(foo_livelyric SHARED foo_livelyric.cpp)

# Assuming foobar2000 SDK is placed in ../foobar2000
target_include_directories(foo_livelyric PRIVATE ../foobar2000/SDK)
# We would link against foobar2000_SDK.lib and pfc.lib here
# target_link_libraries(foo_livelyric PRIVATE ../foobar2000/SDK/shared.lib)
