# Как arm-uwp, но библиотеки статические: libwebp/opus/ogg вшиваются прямо в PagegramNative.dll.
# Отдельные DLL от vcpkg на Windows 10 Mobile не загружались (CO_E_ERRORINDLL 0x800401F9),
# а при статической линковке с WindowsApp.lib недоступный API всплывёт ещё при сборке.
set(VCPKG_TARGET_ARCHITECTURE arm)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_CMAKE_SYSTEM_NAME WindowsStore)
set(VCPKG_CMAKE_SYSTEM_VERSION 10.0)
set(VCPKG_BUILD_TYPE release)
set(VCPKG_ENV_PASSTHROUGH PKG_CONFIG)
