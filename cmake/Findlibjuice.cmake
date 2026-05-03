find_path(libjuice_INCLUDE_DIR
    NAMES juice/juice.h
    HINTS
        ENV LIBJUICE_ROOT
        ENV libjuice_ROOT
    PATH_SUFFIXES include
)

find_library(libjuice_LIBRARY
    NAMES juice libjuice
    HINTS
        ENV LIBJUICE_ROOT
        ENV libjuice_ROOT
    PATH_SUFFIXES lib lib64
)

include(FindPackageHandleStandardArgs)
find_package_handle_standard_args(libjuice
    REQUIRED_VARS libjuice_INCLUDE_DIR libjuice_LIBRARY
)

if(libjuice_FOUND AND NOT TARGET libjuice::libjuice)
    add_library(libjuice::libjuice UNKNOWN IMPORTED)
    set_target_properties(libjuice::libjuice PROPERTIES
        IMPORTED_LOCATION "${libjuice_LIBRARY}"
        INTERFACE_INCLUDE_DIRECTORIES "${libjuice_INCLUDE_DIR}"
    )
endif()

mark_as_advanced(libjuice_INCLUDE_DIR libjuice_LIBRARY)
