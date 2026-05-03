cmake_minimum_required(VERSION 3.20)

project(TPCwei LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")

find_package(Threads REQUIRED)
find_package(libjuice QUIET)
find_package(libdatachannel QUIET)

add_library(p2p_core SHARED
    native/src/p2p_core.cpp
    native/src/gateway_manager.cpp
    native/src/proxy_manager.cpp
)

target_include_directories(p2p_core
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}/native/include"
)

target_compile_definitions(p2p_core
    PRIVATE
        P2P_CORE_EXPORTS
)

if(MSVC)
    target_compile_options(p2p_core PRIVATE /utf-8)
endif()

if(libjuice_FOUND)
    target_compile_definitions(p2p_core PRIVATE P2P_HAS_LIBJUICE=1)
    target_link_libraries(p2p_core PRIVATE libjuice::libjuice)
endif()

if(libdatachannel_FOUND)
    target_compile_definitions(p2p_core PRIVATE P2P_HAS_LIBDATACHANNEL=1)
    target_link_libraries(p2p_core PRIVATE libdatachannel::libdatachannel)
endif()

target_link_libraries(p2p_core PRIVATE Threads::Threads)

if(WIN32)
    target_link_libraries(p2p_core PRIVATE ws2_32 iphlpapi)
elseif(APPLE)
    target_link_libraries(p2p_core PRIVATE "-framework CoreFoundation")
endif()

set_target_properties(p2p_core PROPERTIES
    OUTPUT_NAME "p2p_core"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib"
)

add_executable(tpcwei_gateway
    native/src/tpcwei_gateway.cpp
)

target_include_directories(tpcwei_gateway
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/native/include"
)

target_link_libraries(tpcwei_gateway PRIVATE Threads::Threads)

if(WIN32)
    target_link_libraries(tpcwei_gateway PRIVATE ws2_32)
endif()

if(MSVC)
    target_compile_options(tpcwei_gateway PRIVATE /utf-8)
endif()

set_target_properties(tpcwei_gateway PROPERTIES
    OUTPUT_NAME "tpcwei_gateway"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

install(TARGETS p2p_core
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)

install(TARGETS tpcwei_gateway
    RUNTIME DESTINATION bin
)

install(DIRECTORY native/include/ DESTINATION include)
