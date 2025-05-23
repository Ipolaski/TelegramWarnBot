﻿namespace TelegramWarnBot;

public interface ITelegramBotClientProvider
{
    Task<Message> SendMessageAsync(ChatId chatId, string text, int? replyToMessageId = null, CancellationToken cancellationToken = default);
    Task<Message> SendHtmlMessageAsync(ChatId chatId, string text, int? replyToMessageId = null, CancellationToken cancellationToken = default);
    Task DeleteMessageAsync(ChatId chatId, int messageId, CancellationToken cancellationToken = default);
    Task BanChatMemberAsync(ChatId chatId, long userId, CancellationToken cancellationToken = default);
    Task UnbanChatMemberAsync(ChatId chatId, long userId, CancellationToken cancellationToken = default);
    Task<ChatMember[]> GetChatAdministratorsAsync(ChatId chatId, CancellationToken cancellationToken = default);
    Task<Message> ForwardMessageAsync(ChatId chatId, ChatId fromChatId, int messageId, CancellationToken cancellationToken = default);
    Task<User> GetMeAsync(CancellationToken cancellationToken = default);
    Task LeaveChatAsync(ChatId chatId, CancellationToken cancellationToken = default);

    void StartReceiving(
        Func<ITelegramBotClient, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClient, Exception, CancellationToken, Task> pollingErrorHandler,
        CancellationToken cancellationToken = default);
}

public class TelegramBotClientProvider : ITelegramBotClientProvider
{
    private readonly TelegramBotClient client;

    public TelegramBotClientProvider(IConfigurationContext configurationContext)
    {
        client = new TelegramBotClient(configurationContext.BotConfiguration.Token);
        Shared = this;
    }

    public static TelegramBotClientProvider Shared { get; private set; } // Only for TelegramSink logging

    public Task<Message> SendMessageAsync(ChatId chatId, string text, int? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        return client.SendTextMessageAsync(chatId, text, ParseMode.Markdown, replyToMessageId: replyToMessageId, cancellationToken: cancellationToken);
    }

    public Task<Message> SendHtmlMessageAsync(ChatId chatId, string text, int? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        return client.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Html, replyToMessageId: replyToMessageId, cancellationToken: cancellationToken);
    }

    public Task DeleteMessageAsync(ChatId chatId, int messageId, CancellationToken cancellationToken = default)
    {
        return client.DeleteMessageAsync(chatId, messageId, cancellationToken);
    }

    /// <summary>
    /// Бан пользователя на месяц
    /// </summary>
    /// <param name="chatId">Ид чата</param>
    /// <param name="userId">Ид юзера</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task BanChatMemberAsync(ChatId chatId, long userId, CancellationToken cancellationToken = default)
    {
        return client.BanChatMemberAsync(chatId, userId, DateTime.Now.AddMonths(1), cancellationToken: cancellationToken);
    }

    public Task UnbanChatMemberAsync(ChatId chatId, long userId, CancellationToken cancellationToken = default)
    {
        return client.UnbanChatMemberAsync(chatId, userId, true, cancellationToken);
    }

    public Task<ChatMember[]> GetChatAdministratorsAsync(ChatId chatId, CancellationToken cancellationToken = default)
    {
        return client.GetChatAdministratorsAsync(chatId, cancellationToken);
    }

    public Task<Message> ForwardMessageAsync(ChatId chatId, ChatId fromChatId, int messageId, CancellationToken cancellationToken = default)
    {
        return client.ForwardMessageAsync(chatId, fromChatId, messageId, cancellationToken: cancellationToken);
    }

    public Task<User> GetMeAsync(CancellationToken cancellationToken = default)
    {
        return client.GetMeAsync(cancellationToken);
    }

    public void StartReceiving(
        Func<ITelegramBotClient, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClient, Exception, CancellationToken, Task> pollingErrorHandler,
        CancellationToken cancellationToken = default)
    {
        client.DeleteWebhookAsync();
        client.StartReceiving(updateHandler, pollingErrorHandler,
                              new ReceiverOptions
                              {
                                  ThrowPendingUpdates = true,
                                  AllowedUpdates = new[]
                                  {
                                      UpdateType.Message,
                                      UpdateType.ChatMember,
                                      UpdateType.MyChatMember
                                  }
                              }, cancellationToken);
    }

    public Task LeaveChatAsync(ChatId chatId, CancellationToken cancellationToken = default)
    {
        return client.LeaveChatAsync(chatId, cancellationToken);
    }
}