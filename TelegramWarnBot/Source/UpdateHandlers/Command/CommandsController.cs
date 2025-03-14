﻿namespace TelegramWarnBot;

public interface ICommandsController
{
    Task<Task> AllowWrite(UpdateContext context);
    Task<Task> UnallowWrite(UpdateContext context);
    Task<Task> Unwarn(UpdateContext context);
    Task<Task> Warn(UpdateContext context);
    Task WCount(UpdateContext context);
}

public class CommandsController : ICommandsController
{
    private readonly ICachedDataContext cachedDataContext;
    private readonly IChatHelper chatHelper;
    private readonly ICommandService commandService;
    private readonly IConfigurationContext configurationContext;
    private readonly ILogger<CommandsController> logger;
    private readonly IResponseHelper responseHelper;
    private readonly ITelegramBotClientProvider telegramBotClientProvider;

    public CommandsController(ITelegramBotClientProvider telegramBotClientProvider,
                              IConfigurationContext configurationContext,
                              ICachedDataContext cachedDataContext,
                              IChatHelper chatHelper,
                              IResponseHelper responseHelper,
                              ICommandService commandService,
                              ILogger<CommandsController> logger)
    {
        this.telegramBotClientProvider = telegramBotClientProvider;
        this.configurationContext = configurationContext;
        this.cachedDataContext = cachedDataContext;
        this.chatHelper = chatHelper;
        this.responseHelper = responseHelper;
        this.commandService = commandService;
        this.logger = logger;
    }

    public async Task<Task> AllowWrite(UpdateContext context)
    {
        if (!commandService.TryResolveAllowUser(context, true, out var warnedUser, out var errorMessage))
            return responseHelper.SendMessageAsync(new ResponseContext
            {
                Message = errorMessage
            }, context);

        if (configurationContext.Configuration.DeleteWarnMessage)
            await responseHelper.DeleteMessageAsync(context);

        // Notify in chat that user has been warned or banned
        return responseHelper.SendMessageAsync(new ResponseContext
        {
            Message =
                configurationContext.Configuration.Captions.AllowWriteSuccessfully,
            MentionedUserId = warnedUser.Id
        }, context);
    }

    public async Task<Task> UnallowWrite(UpdateContext context)
    {
        if (!commandService.TryResolveAllowUser(context, false, out var unwarnedUser, out var errorMessage))
            return responseHelper.SendMessageAsync(new ResponseContext
            {
                Message = errorMessage
            }, context);

        if (configurationContext.Configuration.DeleteWarnMessage)
            await responseHelper.DeleteMessageAsync(context);

        unwarnedUser.Warnings = 0;

        await telegramBotClientProvider.UnbanChatMemberAsync(context.Update.Message.Chat.Id,
                                                             unwarnedUser.Id,
                                                             context.CancellationToken);

        logger.LogInformation("[Admin] {admin} unallow write no limit message user {user} in chat {chat}. Warnings: {currentWarns} / {maxWarns}",
                              context.UserDTO.GetName(),
                              cachedDataContext.FindUserById(unwarnedUser.Id).GetName(),
                              context.ChatDTO.Name,
                              unwarnedUser.Warnings,
                              configurationContext.Configuration.MaxWarnings);

        return responseHelper.SendMessageAsync(new ResponseContext
        {
            Message = configurationContext.Configuration.Captions.UnallowWriteSuccessfully,
            MentionedUserId = unwarnedUser.Id
        }, context);
    }

    public async Task<Task> Warn(UpdateContext context)
    {
        if (!commandService.TryResolveWarnedUser(context, true, out var warnedUser, out var errorMessage))
            return responseHelper.SendMessageAsync(new ResponseContext
            {
                Message = errorMessage
            }, context);

        var isBanned = await commandService.Warn(warnedUser, context.ChatDTO.Id,
                                                 !chatHelper.IsAdmin(context.Update.Message.Chat.Id, warnedUser.Id),
                                                 context);

        if (configurationContext.Configuration.DeleteWarnMessage)
            await responseHelper.DeleteMessageAsync(context);

        LogWarned(isBanned, context.ChatDTO, context.UserDTO, warnedUser);

        // Notify in chat that user has been warned or banned
        return responseHelper.SendMessageAsync(new ResponseContext
        {
            Message = isBanned
                ? configurationContext.Configuration.Captions.BannedSuccessfully
                : configurationContext.Configuration.Captions.WarnedSuccessfully,
            MentionedUserId = warnedUser.Id
        }, context);
    }

    public async Task<Task> Unwarn(UpdateContext context)
    {
        if (!commandService.TryResolveWarnedUser(context, false, out var unwarnedUser, out var errorMessage))
            return responseHelper.SendMessageAsync(new ResponseContext
            {
                Message = errorMessage
            }, context);

        if (configurationContext.Configuration.DeleteWarnMessage)
            await responseHelper.DeleteMessageAsync(context);

        if (unwarnedUser.Warnings == 0)
            return responseHelper.SendMessageAsync(new ResponseContext
            {
                Message = configurationContext.Configuration.Captions.UnwarnUserNoWarnings,
                MentionedUserId = unwarnedUser.Id
            }, context);

        unwarnedUser.Warnings--;

        await telegramBotClientProvider.UnbanChatMemberAsync(context.Update.Message.Chat.Id,
                                                             unwarnedUser.Id,
                                                             context.CancellationToken);

        logger.LogInformation("[Admin] {admin} unwarned user {user} in chat {chat}. Warnings: {currentWarns} / {maxWarns}",
                              context.UserDTO.GetName(),
                              cachedDataContext.FindUserById(unwarnedUser.Id).GetName(),
                              context.ChatDTO.Name,
                              unwarnedUser.Warnings,
                              configurationContext.Configuration.MaxWarnings);

        return responseHelper.SendMessageAsync(new ResponseContext
        {
            Message = configurationContext.Configuration.Captions.UnwarnedSuccessfully,
            MentionedUserId = unwarnedUser.Id
        }, context);
    }

    public Task WCount(UpdateContext context)
    {
        logger.LogWarning("handle wcount");
        // todo all chats
        var resolveUser = commandService.TryResolveMentionedUser(context, out var mentionedUser);

        if (resolveUser == ResolveMentionedUserResult.UserNotMentioned)
        {
            mentionedUser = context.UserDTO;
        }
        else if (resolveUser != ResolveMentionedUserResult.Resolved)
        {
            var errorResponse = new ResponseContext
            {
                Message = resolveUser switch
                {
                    ResolveMentionedUserResult.UserNotFound => configurationContext.Configuration.Captions.UserNotFound,
                    ResolveMentionedUserResult.BotMention => configurationContext.Configuration.Captions.WCountBotAttempt,
                    ResolveMentionedUserResult.BotSelfMention => configurationContext.Configuration.Captions.WCountBotSelfAttempt,
                    _ => throw new ArgumentException("ResolveMentionedUserResult")
                }
            };
            return responseHelper.SendMessageAsync(errorResponse, context);
        }

        if (!configurationContext.Configuration.AllowAdminWarnings)
        {
            var mentionedUserIsAdmin = chatHelper.IsAdmin(context.ChatDTO.Id, mentionedUser.Id);
            if (mentionedUserIsAdmin)
                return responseHelper.SendMessageAsync(new ResponseContext
                {
                    Message = configurationContext.Configuration.Captions.WCountAdminAttempt
                }, context);
        }

        var warningsCount = cachedDataContext.FindWarningByChatId(context.ChatDTO.Id)?
            .WarnedUsers.Find(u => u.Id == mentionedUser.Id)?.Warnings ?? 0;

        string response;

        if (warningsCount == 0)
            response = configurationContext.Configuration.Captions.WCountUserHasNoWarnings;
        else
            response = configurationContext.Configuration.Captions.WCountMessage;

        return responseHelper.SendMessageAsync(new ResponseContext
        {
            Message = response,
            MentionedUserId = mentionedUser.Id
        }, context);
    }

    private void LogWarned(bool banned, ChatDTO chat, UserDTO admin, WarnedUser warnedUser)
    {
        var userName = cachedDataContext.FindUserById(warnedUser.Id).GetName();

        if (banned)
            logger.LogInformation("[Admin] {admin} banned user by giving a warning {user} from chat {chat}.",
                                  admin.GetName(), userName, chat.Name);
        else
            logger.LogInformation("[Admin] {admin} warned user {user} in chat {chat}. Warnings: {currentWarns} / {maxWarns}",
                                  admin.GetName(), userName, chat.Name, warnedUser.Warnings, configurationContext.Configuration.MaxWarnings);
    }

    public Task Random(UpdateContext context)
    {
        var lines = context.Text.Split('\n').ToArray();

        string response;

        if (lines.Length < 2)
            response = configurationContext.Configuration.Captions.InvalidOperation;
        else
            response = lines[System.Random.Shared.Next(1, lines.Length)];

        return responseHelper.SendMessageAsync(new ResponseContext
        {
            Message = response
        }, context, context.MessageId);
    }
}