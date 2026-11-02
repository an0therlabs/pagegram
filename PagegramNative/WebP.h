#pragma once

namespace PagegramNative
{
    /// <summary>
    /// Декодер WebP для Windows 10 Mobile: у системы нет встроенного кодека WebP
    /// (он появился только в Windows 10 1809), а стикеры Telegram — это WebP.
    /// Декодируем сразу в буфер WriteableBitmap и сразу в нужном размере
    /// (libwebp масштабирует при декодировании), без промежуточных копий.
    /// </summary>
    public ref class WebP sealed
    {
    public:
        /// <summary>Похоже ли на WebP (RIFF....WEBP).</summary>
        static bool IsWebP(const Platform::Array<uint8>^ data);

        /// <summary>Размер картинки: size[0] = ширина, size[1] = высота.</summary>
        static bool GetSize(const Platform::Array<uint8>^ data, Platform::WriteOnlyArray<int>^ size);

        /// <summary>
        /// Декодирует в target (PixelBuffer у WriteableBitmap: BGRA8, premultiplied)
        /// с масштабированием до width x height.
        /// </summary>
        static bool DecodeInto(const Platform::Array<uint8>^ data, int width, int height,
                               Windows::Storage::Streams::IBuffer^ target);

    private:
        WebP() {}
    };
}
