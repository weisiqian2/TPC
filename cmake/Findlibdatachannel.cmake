find_path(libdatachannel_INCLUDE_DIR
    NAMES rtc/rtc.hpp
    HINTS
        ENV LIBDATACHANNEL_ROOT
        ENV libdatachannel_ROOT
    PATH_SUFFIXES include
)

find_library(libdatachannel_LIBRARY
    NAMES datachannel libdatachannel
    HINTS
        ENV LIBDATACHANNEL_ROOT
        ENV libdatachannel_ROOT
    PATH_SUFFIXES lib lib64
)

include(FindPackageHandleStandardArgs)
find_package_handle_standard_args(libdatachannel
    REQUIRED_VARS libdatachannel_INCLUDE_DIR libdatachannel_LIBRARY
)

if(libdatachannel_FOUND AND NOT TARGET libdatachannel::libdatachannel)
    add_library(libdatachannel::libdatachannel UNKNOWN IMPORTED)
    set_target_properties(libdatachannel::libdatachannel PROPERTIES
        IMPORTED_LOCATION "${libdatachannel_LIBRARY}"
        INTERFACE_INCLUDE_DIRECTORIES "${libdatachannel_INCLUDE_DIR}"
    )
endif()

mark_as_advanced(libdatachannel_INCLUDE_DIR libdatachannel_LIBRARY)
