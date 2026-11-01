namespace Pagegram
{
    internal static class Config
    {
        // Получи свои ключи на https://my.telegram.org -> API development tools
        public const int ApiId = 0;
        public const string ApiHash = "";

        public const string AppVersion = "0.1";

        // Лимиты, чтобы не жрать память на 512 МБ-1 ГБ
        public const int ChatsPageSize = 40;       // сколько чатов подгружать за раз
        public const int HistoryPageSize = 30;     // сколько сообщений подгружать за раз
        public const int MaxMessagesInMemory = 150; // больше в UI-коллекции не держим
        public const int LowMemoryKeepMessages = 40; // столько оставляем при нехватке памяти
        public const int PreviewMaxLength = 70;    // длина превью в списке чатов
        public const int MaxPhotoSide = 1280;      // максимальный размер фото, которое качаем
    }
}
