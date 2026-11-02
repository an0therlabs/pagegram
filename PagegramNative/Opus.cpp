#include "Opus.h"

#include <cstring>
#include <vector>
#include <ogg/ogg.h>
#include <opus/opus.h>

using namespace PagegramNative;
using namespace Platform;

Array<int16>^ Opus::DecodeOggToPcm(const Array<uint8>^ data)
{
    if (data == nullptr || data->Length < 32) return ref new Array<int16>(0);

    ogg_sync_state oy;
    ogg_sync_init(&oy);
    char* buf = ogg_sync_buffer(&oy, static_cast<long>(data->Length));
    std::memcpy(buf, data->Data, data->Length);
    ogg_sync_wrote(&oy, static_cast<long>(data->Length));

    ogg_stream_state os;
    bool streamInit = false;
    OpusDecoder* dec = nullptr;
    int packetNo = 0;
    int preSkip48k = 0;
    bool ok = true;

    std::vector<int16_t> pcm;
    pcm.reserve(16000 * 30);              // ~30 с на старте, дальше растёт
    std::vector<int16_t> frame(16000 / 1000 * 120); // макс. длина Opus-кадра — 120 мс

    ogg_page og;
    while (ok && ogg_sync_pageout(&oy, &og) == 1)
    {
        if (!streamInit)
        {
            ogg_stream_init(&os, ogg_page_serialno(&og));
            streamInit = true;
        }
        if (ogg_page_serialno(&og) != os.serialno) continue; // чужой поток — пропускаем
        ogg_stream_pagein(&os, &og);

        ogg_packet op;
        while (ogg_stream_packetout(&os, &op) == 1)
        {
            if (packetNo == 0)
            {
                // OpusHead: "OpusHead", версия, каналы, pre-skip (LE16) ...
                if (op.bytes < 19 || std::memcmp(op.packet, "OpusHead", 8) != 0) { ok = false; break; }
                preSkip48k = op.packet[10] | (op.packet[11] << 8);
                int err = 0;
                // 1 канал на выходе: стерео декодер сведёт сам
                dec = opus_decoder_create(SampleRate, 1, &err);
                if (err != OPUS_OK || dec == nullptr) { ok = false; break; }
            }
            else if (packetNo >= 2) // 1 — OpusTags
            {
                int n = opus_decode(dec, op.packet, static_cast<opus_int32>(op.bytes),
                                    frame.data(), static_cast<int>(frame.size()), 0);
                if (n > 0) pcm.insert(pcm.end(), frame.begin(), frame.begin() + n);
            }
            ++packetNo;
        }
    }

    if (dec != nullptr) opus_decoder_destroy(dec);
    if (streamInit) ogg_stream_clear(&os);
    ogg_sync_clear(&oy);

    if (!ok) return ref new Array<int16>(0);

    // pre-skip задан в 48 кГц-сэмплах, у нас 16 кГц
    size_t skip = static_cast<size_t>(preSkip48k) / 3;
    if (skip >= pcm.size()) return ref new Array<int16>(0);
    size_t count = pcm.size() - skip;

    auto result = ref new Array<int16>(static_cast<unsigned int>(count));
    std::memcpy(result->Data, pcm.data() + skip, count * sizeof(int16_t));
    return result;
}
