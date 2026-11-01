# arm-uwp из community-триплетов vcpkg + наши правки для CI
set(VCPKG_TARGET_ARCHITECTURE arm)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME WindowsStore)
set(VCPKG_CMAKE_SYSTEM_VERSION 10.0)
# Debug-версии OpenSSL/zlib не нужны — вдвое быстрее
set(VCPKG_BUILD_TYPE release)
# Свой pkg-config: старые пакеты msys2, которые качает vcpkg, удалены с зеркал (404)
set(VCPKG_ENV_PASSTHROUGH PKG_CONFIG)
