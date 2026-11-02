#include "WebP.h"

#include <robuffer.h>
#include <wrl/client.h>
#include <webp/decode.h>

using namespace PagegramNative;
using namespace Platform;
using namespace Windows::Storage::Streams;
using Microsoft::WRL::ComPtr;

bool WebP::IsWebP(const Array<uint8>^ d)
{
    return d != nullptr && d->Length >= 12 &&
           d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' &&
           d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P';
}

bool WebP::GetSize(const Array<uint8>^ d, WriteOnlyArray<int>^ size)
{
    if (d == nullptr || size == nullptr || size->Length < 2) return false;
    int w = 0, h = 0;
    if (!WebPGetInfo(d->Data, d->Length, &w, &h)) return false;
    size[0] = w;
    size[1] = h;
    return true;
}

bool WebP::DecodeInto(const Array<uint8>^ d, int width, int height, IBuffer^ target)
{
    if (d == nullptr || target == nullptr || width <= 0 || height <= 0) return false;

    ComPtr<IBufferByteAccess> access;
    if (FAILED(reinterpret_cast<IInspectable*>(target)->QueryInterface(IID_PPV_ARGS(&access)))) return false;
    byte* pixels = nullptr;
    if (FAILED(access->Buffer(&pixels)) || pixels == nullptr) return false;

    const size_t stride = static_cast<size_t>(width) * 4;
    const size_t total = stride * static_cast<size_t>(height);
    if (target->Capacity < total) return false;

    WebPDecoderConfig config;
    if (!WebPInitDecoderConfig(&config)) return false;

    config.options.use_scaling = 1;
    config.options.scaled_width = width;
    config.options.scaled_height = height;

    // MODE_bgrA = BGRA с premultiplied alpha — ровно то, что ждёт WriteableBitmap
    config.output.colorspace = MODE_bgrA;
    config.output.is_external_memory = 1;
    config.output.u.RGBA.rgba = pixels;
    config.output.u.RGBA.stride = static_cast<int>(stride);
    config.output.u.RGBA.size = total;

    const VP8StatusCode status = WebPDecode(d->Data, d->Length, &config);
    WebPFreeDecBuffer(&config.output);
    return status == VP8_STATUS_OK;
}
