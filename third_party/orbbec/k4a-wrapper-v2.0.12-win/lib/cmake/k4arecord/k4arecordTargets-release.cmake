#----------------------------------------------------------------
# Generated CMake target import file for configuration "Release".
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "k4a::k4arecord" for configuration "Release"
set_property(TARGET k4a::k4arecord APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(k4a::k4arecord PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/lib/k4arecord.lib"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/bin/k4arecord.dll"
  )

list(APPEND _cmake_import_check_targets k4a::k4arecord )
list(APPEND _cmake_import_check_files_for_k4a::k4arecord "${_IMPORT_PREFIX}/lib/k4arecord.lib" "${_IMPORT_PREFIX}/bin/k4arecord.dll" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
