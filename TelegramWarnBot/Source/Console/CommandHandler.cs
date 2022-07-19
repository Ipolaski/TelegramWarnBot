namespace TelegramWarnBot;

public interface IConsoleCommandHandler
{
    void PrintAvailableCommands();
    bool Register(List<string> parameters);
    Task<bool> Send(TelegramBotClient client, List<string> parameters, CancellationToken cancellationToken);
    void Start(CancellationToken cancellationToken);
}

public class ConsoleCommandHandler : IConsoleCommandHandler
{
    private readonly IBot bot;
    private readonly IConfigurationContext configurationContext;
    private readonly ICachedDataContext cachedDataContext;
    private readonly ILogger<ConsoleCommandHandler> logger;

    public ConsoleCommandHandler(IBot bot,
                                 IConfigurationContext configurationContext,
                                 ICachedDataContext cachedDataContext,
                                 ILogger<ConsoleCommandHandler> logger)
    {
        this.bot = bot;
        this.configurationContext = configurationContext;
        this.cachedDataContext = cachedDataContext;
        this.logger = logger;
    }

    public void Start(CancellationToken cancellationToken)
    {
        Task.Run(() =>
        {
            // Display registered chats
            //Register(new List<string> { "-l" });

            while (true)
            {
                var command = Console.ReadLine();

                if (command is null) continue;

                var parts = Regex.Matches(command, @"[\""].+?[\""]|[^ ]+")
                                    .Cast<Match>()
                                    .Select(m => m.Value)
                                    .ToArray();

                if (parts.Length == 0)
                    continue;

                switch (parts[0])
                {
                    case "send":
                        if (!Send(bot.Client, parts.Skip(1).ToList(), cancellationToken).GetAwaiter().GetResult())
                            goto default; // if not succeed => show available commands 
                        break;

                    case "register":
                        if (!Register(parts.Skip(1).ToList()))
                            goto default;
                        break;

                    case "reload":
                        configurationContext.ReloadConfiguration();
                        WriteColor("[Configuration reloaded successfully!]", ConsoleColor.Green, true);
                        break;

                    case "leave":
                        if (long.TryParse(parts[1], out var chatId))
                            try
                            {
                                bot.Client.LeaveChatAsync(chatId, cancellationToken: cancellationToken).GetAwaiter().GetResult();
                            }
                            catch (Exception e)
                            {
                                WriteColor("[Error]: " + e.Message, ConsoleColor.Red, false);
                            }
                        break;

                    case "save":
                        cachedDataContext.SaveData();
                        logger.LogInformation("Data saved successfully!");
                        break;
                    case "info":
                        WriteInfo();
                        break;
                    case "version":
                        WriteColor($"[Version: {Assembly.GetEntryAssembly().GetName().Version}]", ConsoleColor.Yellow, false);
                        break;

                    case "l": goto case "leave";
                    case "r": goto case "reload";
                    case "s": goto case "save";
                    case "i": goto case "info";
                    case "v": goto case "version";

                    default:
                        WriteColor("Not recognized...", ConsoleColor.Gray, false);
                        PrintAvailableCommands();
                        break;
                }
            }
        }, cancellationToken);
    }

    public bool Register(List<string> parameters)
    {
        if (parameters.Count == 1)
        {
            if (long.TryParse(parameters[0], out var newChatId))
            {
                configurationContext.BotConfiguration.RegisteredChats.Add(newChatId);
                cachedDataContext.SaveRegisteredChatsAsync(configurationContext.BotConfiguration.RegisteredChats).GetAwaiter().GetResult();

                WriteColor("[Chat registered successfully]", ConsoleColor.Green, true);
                return true;
            }

            if (parameters[0] == "-l")
            {
                Console.WriteLine("\nRegistered chats:");
                foreach (var chatId in configurationContext.BotConfiguration.RegisteredChats)
                {
                    WriteColor("\t[" + (cachedDataContext.Chats.Find(c => c.Id == chatId)?.Name ?? "Chat not cached yet") + "]: " + chatId,
                                     ConsoleColor.Blue, false);
                }

                var notRegistered = cachedDataContext.Chats.Where(cached => !configurationContext.BotConfiguration.RegisteredChats.Contains(cached.Id));
                if (!notRegistered.Any())
                    return true;

                Console.WriteLine("\nNot registered chats:");
                foreach (var chat in notRegistered)
                {
                    WriteColor("\t[" + chat.Name + "]: " + chat.Id, ConsoleColor.Red, false);
                }

                return true;
            }
        }
        else if (parameters.Count == 2)
        {
            if (parameters[0] == "-rm" && long.TryParse(parameters[1], out var removedChatId))
            {
                if (configurationContext.BotConfiguration.RegisteredChats.Remove(removedChatId))
                {
                    cachedDataContext.SaveRegisteredChatsAsync(configurationContext.BotConfiguration.RegisteredChats);
                    WriteColor("[Chat removed successfully]", ConsoleColor.Green, true);
                }
                else
                    WriteColor("[Chat not found...]", ConsoleColor.Red, true);

                return true;
            }
        }

        return false;
    }

    public Task<bool> Send(TelegramBotClient client, List<string> parameters, CancellationToken cancellationToken)
    {
        int chatIndex = parameters.FindIndex(p => p.ToLower() == "-c");

        if (chatIndex < 0)
            return Task.FromResult(false);

        string chatParameter = parameters.ElementAtOrDefault(chatIndex + 1);

        bool broadcast = chatParameter == ".";

        long chatId = 0;

        if (!broadcast && !long.TryParse(chatParameter, out chatId))
            return Task.FromResult(false);

        int messageIndex = parameters.FindIndex(p => p.ToLower() == "-m");

        if (messageIndex < 0)
            return Task.FromResult(false);

        string message = parameters.ElementAtOrDefault(messageIndex + 1);

        if (message is null || !message.StartsWith("\"") || !message.EndsWith("\"") || message.Length == 1)
            return Task.FromResult(false);

        message = message[1..^1];

        var chats = new List<ChatDTO>();

        if (broadcast)
        {
            chats = cachedDataContext.Chats;
        }
        else
        {
            chats.Add(new()
            {
                Id = chatId
            });
        }

        var tasks = new List<Task>();

        int sentCount = 0;
        for (int i = 0; i < chats.Count; i++)
        {
            try
            {
                if (chats[i].Id != 0)
                {
                    tasks.Add(client.SendTextMessageAsync(chats[i].Id,
                                                          message,
                                                          cancellationToken: cancellationToken,
                                                          parseMode: ParseMode.Markdown));
                    sentCount++;
                }
            }
            catch (Exception) { }
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();

        WriteColor($"[Messages sent: {sentCount}]", ConsoleColor.Yellow, true);

        return Task.FromResult(true);
    }

    private void WriteInfo()
    {
        if (cachedDataContext.Chats.Count > 0)
        {
            WriteColor($"\nCached Chats: [{cachedDataContext.Chats.Count}]", ConsoleColor.DarkYellow, true);

            string userName;

            foreach (var chat in cachedDataContext.Chats)
            {
                WriteColor($"\t[{chat.Name}]", ConsoleColor.DarkMagenta, false);

                WriteColor($"\tAdmins: [{chat.Admins.Count}]", ConsoleColor.DarkYellow, false);

                foreach (var admin in chat.Admins)
                {
                    userName = cachedDataContext.Users.Find(u => u.Id == admin)?.GetName() ?? $"Not found - {admin}";

                    WriteColor($"\t\t[{userName}]", ConsoleColor.DarkMagenta, false);
                }
                Console.WriteLine();
            }
        }

        WriteColor($"\nCached Users: [{cachedDataContext.Users.Count}]", ConsoleColor.DarkYellow, false);

        //if (IOHandler.Users.Count > 0)
        //{
        //    foreach (var user in IOHandler.Users)
        //    {
        //        WriteColor($"\t[{user.Name}]", ConsoleColor.DarkMagenta, false);
        //    }
        //}

        if (cachedDataContext.Warnings.Count > 0)
        {
            WriteColor($"\nWarnings: [{cachedDataContext.Warnings.SelectMany(w => w.WarnedUsers).Select(u => u.Warnings).Sum()}]", ConsoleColor.DarkYellow, false);

            string chatName, userName;

            foreach (var warning in cachedDataContext.Warnings)
            {
                chatName = cachedDataContext.Chats.Find(c => c.Id == warning.ChatId)?.Name ?? $"Not found - {warning.ChatId}";

                WriteColor($"\t[{chatName}]:", ConsoleColor.DarkMagenta, false);

                foreach (var user in warning.WarnedUsers)
                {
                    userName = cachedDataContext.Users.Find(u => u.Id == user.Id)?.GetName() ?? $"Not found - {user.Id}";

                    WriteColor($"\t\t[{userName}] - [{user.Warnings}]", ConsoleColor.DarkMagenta, false);
                }
                Console.WriteLine();
            }
        }
    }

    public void PrintAvailableCommands()
    {
        WriteColor(
         "\nAvailable commands:\n"

         + "\n[send] \t=> Send message:"
             + "\n\t[-c] => Chat with according chat ID. Use . to send to all chats"
             + "\n\t[-m] => Message to send. Please use \"\" to indicate message. Markdown formatting allowed"
         + "\nExample: send -c 123456 -m \"Example message\"\n"

         + "\n[register] => Register new chat:"
             + "\n\t[-l] => List of registered chats"
             + "\n\t[-rm] => Remove one specific chat\n"

         + "\n[leave]/[l] => Leave a chat\n"
         + "\n[reload]/[r] => Reload configurations\n"
         + "\n[save]/[s] \t=> Save last data\n"
         + "\n[info]/[i] \t=> Show info about cached chats and users\n"
         + "\n[version]/[v]=> Version of bot"
         , ConsoleColor.Red, false);
    }

    // https://stackoverflow.com/questions/2743260/is-it-possible-to-write-to-the-console-in-colour-in-net
    // usage: WriteColor("This is my [message] with inline [color] changes.", ConsoleColor.Yellow);
    public static void WriteColor(string message, ConsoleColor color, bool logDateTime)
    {
        if (logDateTime)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"[{DateTime.Now}] ");
            Console.ResetColor();
        }

        var pieces = Regex.Split(message, @"(\[[^\]]*\])");

        for (int i = 0; i < pieces.Length; i++)
        {
            string piece = pieces[i];

            if (piece.StartsWith("[") && piece.EndsWith("]"))
            {
                Console.ForegroundColor = color;
                piece = piece[1..^1];
            }

            Console.Write(piece);
            Console.ResetColor();
        }

        Console.WriteLine();
    }
}