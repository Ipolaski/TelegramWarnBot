﻿using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TelegramWarnBot;

public interface ICommandService
{
    ChatWarnings ResolveChatWarning(long chatId);
    WarnedUser ResolveWarnedUser(long userId, ChatWarnings chatWarning);
    ResolveMentionedUserResult TryResolveMentionedUser(UpdateContext context, out UserDTO userDto);

    public bool TryResolveWarnedUser(UpdateContext context,
                                     bool isWarn,
                                     [NotNullWhen(true)] out WarnedUser warnedUser,
                                     [NotNullWhen(false)] out string errorMessage);

    public bool TryResolveAllowUser(UpdateContext context,
                                     bool isAllow,
                                     [NotNullWhen(true)] out WarnedUser warnedUser,
                                     [NotNullWhen(false)] out string errorMessage);

    Task<bool> Warn(WarnedUser warnedUser, long chatId, bool tryBanUser, UpdateContext context);
}

public class CommandService : ICommandService
{
    private readonly ICachedDataContext cachedDataContext;
    private readonly IChatHelper chatHelper;
    private readonly IResponseHelper responseHelper;
    private readonly IConfigurationContext configurationContext;
    private readonly ITelegramBotClientProvider telegramBotClientProvider;

    public CommandService(ITelegramBotClientProvider telegramBotClientProvider,
                          IConfigurationContext configurationContext,
                          ICachedDataContext cachedDataContext,
                          IResponseHelper responseHelper,
                          IChatHelper chatHelper)
    {
        this.telegramBotClientProvider = telegramBotClientProvider;
        this.configurationContext = configurationContext;
        this.cachedDataContext = cachedDataContext;
        this.chatHelper = chatHelper;
        this.responseHelper = responseHelper;
        _warnMessageTimeout = new();
    }
    private Dictionary<long, DateTime> _warnMessageTimeout;
    /// <summary>
    /// Выдача предупреждений всем, кроме админов и автобан, если предупреждения доходят до порогового значения "MaxWarnings"
    /// </summary>
    /// <returns>Был ли забанен пользователь</returns>
    public async Task<bool> Warn(WarnedUser warnedUser, long chatId, bool tryBanUser, UpdateContext context)
    {

        string name = (context.UserDTO.Username == "@") ? context.UserDTO.GetName() : context.UserDTO.Username;

        // Max warnings reached

        if (!_warnMessageTimeout.ContainsKey(context.UserDTO.Id))
        {
            _warnMessageTimeout.Add(context.UserDTO.Id, DateTime.Now.AddSeconds(configurationContext.Configuration.TimeMinuteSendUnWarnMessages));

            warnedUser.Warnings = Math.Clamp(warnedUser.Warnings + 1, 0,
                                         configurationContext.Configuration.MaxWarnings);

            warnedUser.Unmute = DateTime.Now.AddDays(configurationContext.Configuration.UnmuteUsersDelay);

            // If not reached max warnings 
            if (warnedUser.Warnings < configurationContext.Configuration.MaxWarnings)
                return false;

            
            await responseHelper.SendToHiddenChatMessageAsync(new ResponseContext
            {
                Message = configurationContext.Configuration.Captions.ServiceMessage.Replace("%1", name).Replace("%2", warnedUser.Warnings.ToString()).Replace("%3", warnedUser.Unmute.ToString())
        });
        }

        else if (_warnMessageTimeout[context.UserDTO.Id].AddMinutes(configurationContext.Configuration.TimeMinuteSendUnWarnMessages) < DateTime.Now)
        {
            _warnMessageTimeout[context.UserDTO.Id] = DateTime.Now.AddSeconds(configurationContext.Configuration.TimeMinuteSendUnWarnMessages);

            warnedUser.Warnings = Math.Clamp(warnedUser.Warnings + 1, 0,
                                         configurationContext.Configuration.MaxWarnings);

            warnedUser.Unmute = DateTime.Now.AddDays(configurationContext.Configuration.UnmuteUsersDelay);

            // If not reached max warnings 
            if (warnedUser.Warnings < configurationContext.Configuration.MaxWarnings)
                return false;

            await responseHelper.SendToHiddenChatMessageAsync(new ResponseContext
            {
                Message = configurationContext.Configuration.Captions.ServiceMessage.Replace("%1", name).Replace("%2", warnedUser.Warnings.ToString()).Replace("%3", warnedUser.Unmute.ToString())
            });
        }

        if (tryBanUser)
        {
            // await telegramBotClientProvider.BanChatMemberAsync(chatId, warnedUser.Id,
            //                                                    context.CancellationToken);
            warnedUser.Warnings = 0;
            return true;
        }

        // This reaches only when admin got max warnings but bot cannot ban him...
        return false;
    }

    /// <returns>
    ///     User or error message that has to be returned
    /// </returns>
    public bool TryResolveWarnedUser(UpdateContext context,
                                     bool isWarn,
                                     [NotNullWhen(true)] out WarnedUser warnedUser,
                                     [NotNullWhen(false)] out string errorMessage)
    {
        warnedUser = null;
        errorMessage = null;

        if (!context.IsSenderAdmin)
        {
            errorMessage = configurationContext.Configuration.Captions.UserNoPermissions;
            return false;
        }

        if (!context.IsBotAdmin)
        {
            errorMessage = configurationContext.Configuration.Captions.BotHasNoPermissions;
            return false;
        }

        var resolveUser = TryResolveMentionedUser(context, out var mentionedUser);

        // Didn't find the user => return reason 
        if (resolveUser != ResolveMentionedUserResult.Resolved)
        {
            errorMessage = resolveUser switch
            {
                ResolveMentionedUserResult.UserNotMentioned => configurationContext.Configuration.Captions.InvalidOperation,
                ResolveMentionedUserResult.UserNotFound => configurationContext.Configuration.Captions.UserNotFound,
                ResolveMentionedUserResult.BotMention => isWarn
                    ? configurationContext.Configuration.Captions.WarnBotAttempt
                    : configurationContext.Configuration.Captions.UnwarnBotAttempt,
                ResolveMentionedUserResult.BotSelfMention => isWarn
                    ? configurationContext.Configuration.Captions.WarnBotSelfAttempt
                    : configurationContext.Configuration.Captions.UnwarnBotSelfAttempt,
                _ => throw new ArgumentException("ResolveMentionedUserResult")
            };
            return false;
        }

        var mentionedUserIsAdmin = chatHelper.IsAdmin(context.Update.Message.Chat.Id, mentionedUser.Id);

        // warn/unwarn admin disabled
        if (mentionedUserIsAdmin && !configurationContext.Configuration.AllowAdminWarnings)
        {
            errorMessage = isWarn
                ? configurationContext.Configuration.Captions.WarnAdminAttempt
                : configurationContext.Configuration.Captions.UnwarnAdminAttempt;
            return false;
        }

        var chatWarnings = ResolveChatWarning(context.Update.Message.Chat.Id);
        warnedUser = ResolveWarnedUser(mentionedUser.Id, chatWarnings);

        return true;
    }

    /// <returns>
    ///     Разрешение или запрет бесконечного ввода сообщений
    /// </returns>
    public bool TryResolveAllowUser(UpdateContext context,
                                     bool isAllow,
                                     [NotNullWhen(true)] out WarnedUser warnedUser,
                                     [NotNullWhen(false)] out string errorMessage)
    {
        warnedUser = null;
        errorMessage = null;

        if (!context.IsSenderAdmin)
        {
            errorMessage = configurationContext.Configuration.Captions.UserNoPermissions;
            return false;
        }

        if (!context.IsBotAdmin)
        {
            errorMessage = configurationContext.Configuration.Captions.BotHasNoPermissions;
            return false;
        }

        var resolveUser = TryResolveMentionedUser(context, out var mentionedUser);

        // Didn't find the user => return reason 
        if (resolveUser != ResolveMentionedUserResult.Resolved)
        {
            errorMessage = resolveUser switch
            {
                ResolveMentionedUserResult.UserNotMentioned => configurationContext.Configuration.Captions.InvalidOperation,
                ResolveMentionedUserResult.UserNotFound => configurationContext.Configuration.Captions.UserNotFound,
                ResolveMentionedUserResult.BotMention => isAllow
                    ? configurationContext.Configuration.Captions.WarnBotAttempt
                    : configurationContext.Configuration.Captions.UnwarnBotAttempt,
                ResolveMentionedUserResult.BotSelfMention => isAllow
                    ? configurationContext.Configuration.Captions.WarnBotSelfAttempt
                    : configurationContext.Configuration.Captions.UnwarnBotSelfAttempt,
                _ => throw new ArgumentException("ResolveMentionedUserResult")
            };
            return false;
        }

        var mentionedUserIsAdmin = chatHelper.IsAdmin(context.Update.Message.Chat.Id, mentionedUser.Id);

        // warn/unwarn admin disabled
        if (mentionedUserIsAdmin && !configurationContext.Configuration.AllowAdminWarnings)
        {
            errorMessage = isAllow
                ? configurationContext.Configuration.Captions.WarnAdminAttempt
                : configurationContext.Configuration.Captions.UnwarnAdminAttempt;
            return false;
        }
        //mentionedUser.WriteAllow = isAllow;

        cachedDataContext.Users.Find(a => a.Username == mentionedUser.Username).WriteAllow = isAllow;

        cachedDataContext.SaveData();

        var chatWarnings = ResolveChatWarning(context.Update.Message.Chat.Id);
        warnedUser = ResolveWarnedUser(mentionedUser.Id, chatWarnings);

        return true;
    }

    public WarnedUser ResolveWarnedUser(long userId, ChatWarnings chatWarning)
    {
        var warnedUser = chatWarning.WarnedUsers.FirstOrDefault(u => u.Id == userId);
        if (warnedUser is null)
        {
            warnedUser = new WarnedUser
            {
                Id = userId,
                Warnings = 0
            };
            chatWarning.WarnedUsers.Add(warnedUser);
        }
        return warnedUser;
    }

    public ChatWarnings ResolveChatWarning(long chatId)
    {
        var chatWarning = cachedDataContext.FindWarningByChatId(chatId);
        if (chatWarning is null)
        {
            chatWarning = new ChatWarnings
            {
                ChatId = chatId,
                WarnedUsers = new List<WarnedUser>()
            };
            cachedDataContext.Warnings.Add(chatWarning);
        }
        return chatWarning;
    }

    /// <returns>
    ///     User or error message that has to be responded
    /// </returns>
    public ResolveMentionedUserResult TryResolveMentionedUser(UpdateContext context, out UserDTO userDto)
    {
        userDto = null;
        User user = null;

        if (context.Update.Message.Entities?.Length >= 2)
        {
            if (context.Update.Message.Entities[1].Type == MessageEntityType.Mention
             && context.Update.Message.EntityValues is not null)
            {
                var mentionedUsername = context.Update.Message.EntityValues.ElementAt(1)[1..];
                var mentionedUserDto = cachedDataContext.Users.Find(u => u.Username == mentionedUsername);

                if (mentionedUserDto is not null)
                    user = mentionedUserDto.Map();
            }
            else if (context.Update.Message.Entities[1].Type == MessageEntityType.TextMention
                  && context.Update.Message.Entities[1].User is not null)
            {
                user = context.Update.Message.Entities[1].User;
            }
            // Second entity must be a mention
            else
            {
                return ResolveMentionedUserResult.UserNotMentioned;
            }
        }
        // If didn't mention user in message => look into replied message
        else if (context.Update.Message.ReplyToMessage?.From is not null)
        {
            user = context.Update.Message.ReplyToMessage.From;
        }
        // Didn't mention and didn't replied
        else
        {
            return ResolveMentionedUserResult.UserNotMentioned;
        }

        if (user is null)
            return ResolveMentionedUserResult.UserNotFound;

        // Mentioned bot itself
        if (user.Id == context.Bot.Id)
            return ResolveMentionedUserResult.BotSelfMention;

        // Mentioned other bot
        if (user.IsBot)
            return ResolveMentionedUserResult.BotMention;

        userDto = user.Map();

        return ResolveMentionedUserResult.Resolved;
    }
}