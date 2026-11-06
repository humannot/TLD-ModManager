using Il2Cpp;
using UnityEngine;

namespace ModManager;

/// <summary>UI text in the game's language: Russian when the game runs in Russian, English otherwise. Keys are the English strings.</summary>
static class L
{
    static float _checked = -10;
    static bool _ru;

    public static bool Russian
    {
        get
        {
            if (Time.realtimeSinceStartup - _checked < 0.25f) return _ru;
            _checked = Time.realtimeSinceStartup;
            try { var l = Localization.Language ?? ""; _ru = l.StartsWith("Russian", StringComparison.OrdinalIgnoreCase) || l.Equals("ru", StringComparison.OrdinalIgnoreCase); }
            catch { _ru = false; }
            return _ru;
        }
    }

    public static string T(string en) => Russian && Ru.TryGetValue(en, out var ru) ? ru : en;

    static readonly Dictionary<string, string> Ru = new()
    {
        ["MOD MANAGER"] = "МЕНЕДЖЕР МОДОВ",
        ["Mod Manager"] = "Мод Менеджер",
        ["In-game mod manager: download and update mods from https://tldmods.net/"] =
            "Менеджер модов в игре: скачивание и обновление модов с источника https://tldmods.net/",
        ["UPDATE ON LAUNCH:  "] = "ОБНОВЛЯТЬ ПРИ ЗАПУСКЕ:  ",
        ["YES"] = "ДА",
        ["NO"] = "НЕТ",
        ["INSTALLED"] = "УСТАНОВЛЕННЫЕ",
        ["AVAILABLE"] = "ДОСТУПНЫЕ",
        ["UPDATES"] = "ОБНОВЛЕНИЯ",
        ["SEARCH"] = "ПОИСК",
        ["SORT BY"] = "СОРТИРОВКА",
        ["NAME"] = "ИМЯ",
        ["UPDATED"] = "ОБНОВЛЕНИЕ",
        ["AUTHOR"] = "АВТОР",
        ["Game version: "] = "Версия игры: ",
        ["TRANSLATE DESCRIPTION"] = "ПЕРЕВЕСТИ ОПИСАНИЕ",
        ["SHOW ORIGINAL"] = "ПОКАЗАТЬ ОРИГИНАЛ",
        ["Translating description"] = "Перевод описания",
        ["Description translated (machine translation by MyMemory)"] = "Описание переведено (машинный перевод MyMemory)",
        ["VIEW: TILES"] = "ВИД: ПЛИТКИ",
        ["VIEW: LIST"] = "ВИД: СПИСОК",
        ["Catalog not loaded - no network?"] = "Каталог не загружен — нет сети?",
        ["Nothing found"] = "Ничего не найдено",
        ["this plugin"] = "этот плагин",
        ["not in the tldmods.net catalog"] = "нет в каталоге tldmods.net",
        ["INSTALL"] = "УСТАНОВИТЬ",
        ["UPDATE"] = "ОБНОВИТЬ",
        ["MANUAL"] = "ВРУЧНУЮ",
        ["Select a mod from the list"] = "Выберите мод в списке",
        ["Author: "] = "Автор: ",
        ["Version: "] = "Версия: ",
        ["installed: "] = "установлена: ",
        ["no"] = "нет",
        ["! Marked as broken on tldmods.net"] = "! На tldmods.net помечен как неработающий",
        ["DEPENDENCIES"] = "ЗАВИСИМОСТИ",
        ["(not in catalog)"] = "(нет в каталоге)",
        ["DOWNLOAD MISSING"] = "ДОКАЧАТЬ НЕДОСТАЮЩИЕ",
        ["REINSTALL"] = "ПЕРЕУСТАНОВИТЬ",
        ["MOD PAGE"] = "СТРАНИЦА МОДА",
        ["REMOVE"] = "УДАЛИТЬ",
        ["This is Mod Manager itself: it updates your other mods and installs new ones from tldmods.net."] =
            "Это сам менеджер модов: он обновляет ваши моды и ставит новые с tldmods.net.",
        ["Files: "] = "Файлы: ",
        ["UPDATE ALL"] = "ОБНОВИТЬ ВСЁ",
        ["OPEN MODS FOLDER"] = "ОТКРЫТЬ ПАПКУ МОДОВ",
        ["REFRESH LIST"] = "ОБНОВИТЬ СПИСОК",
        ["RESTART GAME"] = "ПЕРЕЗАПУСТИТЬ ИГРУ",
        ["CHECK MANAGER UPDATE"] = "ОБНОВЛЕНИЕ МЕНЕДЖЕРА",
        ["please wait..."] = "подождите...",
        ["Changes apply after a restart"] = "Изменения применятся после перезапуска",
        ["Error: "] = "Ошибка: ",
        ["done"] = "готово",
        ["done, restart the game"] = "готово, перезапустите игру",
        ["Installing "] = "Установка ",
        ["Updating "] = "Обновление ",
        ["Removing "] = "Удаление ",
        ["Installing dependencies of "] = "Установка зависимостей ",
        ["Updating all mods"] = "Обновление всех модов",
        ["Loading catalog"] = "Загрузка каталога",
        ["Checking for a Mod Manager update"] = "Проверка обновления менеджера",
        ["Mod Manager is up to date: "] = "Менеджер модов актуален: ",
        ["Mod Manager updated to "] = "Менеджер модов обновлён до ",
        [", restart the game to use it"] = ", перезапустите игру, чтобы применить",
        ["MOD MANAGER   AUTO-UPDATE: ON"] = "МЕНЕДЖЕР МОДОВ   АВТООБНОВЛЕНИЕ: ВКЛ",
        ["MOD MANAGER   AUTO-UPDATE: OFF"] = "МЕНЕДЖЕР МОДОВ   АВТООБНОВЛЕНИЕ: ВЫКЛ",
        ["Updated at launch: "] = "Обновлено при запуске: ",
        [" update available"] = " обновление доступно",
        [" updates available"] = " обновлений доступно",
        ["All mods are up to date"] = "Все моды актуальны",
        ["Click to open  (F8)"] = "Нажмите, чтобы открыть  (F8)",
        ["AUTO-UPDATE MODS"] = "АВТООБНОВЛЕНИЕ МОДОВ",
        ["Check tldmods.net and update your installed mods every time the game starts?\n\nUpdating starts from the next launch. You can change this any time in the mod manager (Mod Manager in the main menu, or F8)."] =
            "Проверять tldmods.net и обновлять установленные моды при каждом запуске игры?\n\nОбновление начнётся со следующего запуска. Изменить выбор можно в менеджере модов (кнопка «Мод Менеджер» в главном меню или F8).",
        ["YES, UPDATE"] = "ДА, ОБНОВЛЯТЬ",
    };
}
