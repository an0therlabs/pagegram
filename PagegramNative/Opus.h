#pragma once

namespace PagegramNative
{
    /// <summary>
    /// Голосовые Telegram — OGG/Opus, а у Windows 10 Mobile нет декодера Opus.
    /// Разбираем OGG через libogg и декодируем libopus сразу в 16 кГц моно:
    /// для голоса хватает, а памяти в 3 раза меньше, чем 48 кГц (минута ≈ 1,9 МБ).
    /// </summary>
    public ref class Opus sealed
    {
    public:
        static property int SampleRate { int get() { return 16000; } }

        /// <summary>PCM 16 бит, моно, 16 кГц. Пустой массив — если не получилось.</summary>
        static Platform::Array<int16>^ DecodeOggToPcm(const Platform::Array<uint8>^ data);

    private:
        Opus() {}
    };
}
