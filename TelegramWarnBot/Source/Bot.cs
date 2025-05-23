﻿namespace TelegramWarnBot;

public interface IBot
{
    User BotUser { get; }

    Task StartAsync(CancellationToken cancellationToken);
    Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken cancellationToken);
    Task PollingErrorHandler(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken);
}

public class Bot : IBot
{
    private readonly ICachedDataContext cachedDataContext;
    private readonly IConfigurationContext configurationContext;
    private readonly ILogger<Bot> logger;
    private readonly IServiceProvider serviceProvider;
    private readonly ITelegramBotClientProvider telegramBotClientProvider;
    private readonly IUpdateContextBuilder updateContextBuilder;
    private readonly IStatsController statsController;
    private List<long> cachedUsers = new();
    private Func<UpdateContext, Task> pipe;
    private Timer _timerUnmuteUsers;
    public Bot(IServiceProvider serviceProvider,
               ITelegramBotClientProvider telegramBotClientProvider,
               IConfigurationContext configurationContext,
               ICachedDataContext cachedDataContext,
               IUpdateContextBuilder updateContextBuilder,
               ILogger<Bot> logger,
               IStatsController statsController)
    {
        this.serviceProvider = serviceProvider;
        this.telegramBotClientProvider = telegramBotClientProvider;
        this.configurationContext = configurationContext;
        this.cachedDataContext = cachedDataContext;
        this.updateContextBuilder = updateContextBuilder;
        this.logger = logger;
        this.statsController = statsController;
    }

    public User BotUser { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        pipe = AppConfiguration.GetPipeBuilder(serviceProvider).Build();

        logger.LogInformation("Starting receiving updates");

        telegramBotClientProvider.StartReceiving(UpdateHandler, PollingErrorHandler, cancellationToken);

        BotUser = await telegramBotClientProvider.GetMeAsync(cancellationToken);

        // Register bot itself to recognize when someone mentions it with @
        cachedDataContext.CacheUser(BotUser);
        cachedDataContext.BeginUpdate(configurationContext.Configuration.UpdateDelay, cancellationToken);

        statsController.StartTrace(cancellationToken);

        logger.LogInformation("Bot {botName} running", BotUser.FirstName);
        logger.LogInformation("Version: {version}", Assembly.GetEntryAssembly()!.GetName().Version);

        Console.Title = BotUser.FirstName;

        TimerCallback tm = new TimerCallback((o) =>
        {
            cachedUsers.Clear();
            cachedDataContext.UnmuteWarnUsers();
        });

        _timerUnmuteUsers = new Timer(tm, null, TimeSpan.FromMinutes(configurationContext.Configuration.UpdateDelay), TimeSpan.FromMinutes(configurationContext.Configuration.UpdateDelay));
    }

    public Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        try
        {
            // Update must be a valid message with a From-user
            if (!update.Validate())
                return Task.CompletedTask;

            var context = updateContextBuilder.Build(update, BotUser, cancellationToken);

            //if ( !context.IsJoinedLeftUpdate && context.ChatDTO is null )
            //    throw new Exception("Message from uncached chat!");
            if (context.Update.Message != null)
            {
                context.AllowPost = !cachedUsers.Contains(context.Update.Message.From.Id);

                if (context.AllowPost)
                    try
                    {
                        cachedUsers.Add(context.Update.Message.From.Id);
                    }
                    catch (Exception e)
                    {
                        var chat = context.Update.GetChat();
                        logger.LogError(e, $"Handler error on update {update} in chat {chat}", update, $"{chat?.Title}: {chat?.Id}, messagge: {update.Message}");
                    }
            }
            return pipe(context);
        }
        catch (Exception exception)
        {
            var chat = update.GetChat();
            // Update that raised exception will be saved in Logs.json (and sent to tech support in private messages)
            logger.LogError(exception, $"Handler error on update {update} in chat {chat}", update, $"{chat?.Title}: {chat?.Id}, messagge: {update.Message}");
            return Task.CompletedTask;
        }
    }

    public Task PollingErrorHandler(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogCritical(exception, "Fatal error occured. Restart required!");
        return Task.CompletedTask;
    }
}