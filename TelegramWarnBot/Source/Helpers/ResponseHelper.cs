using System.Timers;

using TelegramWarnBot.Source.IOTypes.DTOs;

namespace TelegramWarnBot;

public interface IResponseHelper
{
    string FormatResponseVariables(ResponseContext responseContext, UpdateContext updateContext);
    Task DeleteMessageAsync(UpdateContext context);
    Task SendMessageAsync(ResponseContext responseContext, UpdateContext updateContext, int? replyToMessageId = null);
    Task SendToHiddenChatMessageAsync(ResponseContext responseContext);
}

public class ResponseHelper : IResponseHelper
{
    private readonly ICachedDataContext cachedDataContext;
    private readonly IConfigurationContext configurationContext;
    private readonly ISmartFormatterProvider formatterProvider;
    private readonly ITelegramBotClientProvider telegramBotClientProvider;
    private readonly Queue<DeleteMessageModel> _onDeleteQueue;
    private readonly System.Timers.Timer _deleteMessageTimer;

    public ResponseHelper(ITelegramBotClientProvider telegramBotClientProvider,
                          IConfigurationContext configurationContext,
                          ICachedDataContext cachedDataContext,
                          ISmartFormatterProvider formatterProvider)
    {
        this.telegramBotClientProvider = telegramBotClientProvider;
        this.configurationContext = configurationContext;
        this.cachedDataContext = cachedDataContext;
        this.formatterProvider = formatterProvider;
        _deleteMessageTimer = new System.Timers.Timer(TimeSpan.FromSeconds(configurationContext.Configuration.DeleteBotMessageTimeOutInSeconds).TotalMilliseconds);
        _deleteMessageTimer.Elapsed += async (sender, e) => await ProcessDeleteMessage(sender, e);
        _deleteMessageTimer.AutoReset = true;
        _deleteMessageTimer.Enabled = true;
        _onDeleteQueue = new Queue<DeleteMessageModel>();
    }

    public async Task SendMessageAsync(ResponseContext responseContext, UpdateContext updateContext, int? replyToMessageId = null)
    {
            ChatId chatId = new ChatId(updateContext.ChatDTO.Id);
            Message message = await telegramBotClientProvider.SendMessageAsync(chatId,
                                                              FormatResponseVariables(responseContext, updateContext),
                                                              null,
                                                              updateContext.CancellationToken);
            MarkOnDeleteMrssage(message);
            Log.Logger.Debug($"Message: {message.Text}");
    }

    public async Task SendToHiddenChatMessageAsync(ResponseContext responseContext)
    {
        ChatId chatId = new ChatId(long.Parse(configurationContext.Configuration.HiddenChatId));
        Message message = await telegramBotClientProvider.SendMessageAsync(chatId, responseContext.Message);
        Log.Logger.Debug($"Message: {message.Text}");
    }

    public Task DeleteMessageAsync(UpdateContext context)
    {
        ChatId chatId = new ChatId(context.ChatDTO.Id);

        return telegramBotClientProvider.DeleteMessageAsync(chatId,
                                                            context.MessageId!.Value,
                                                            context.CancellationToken);
    }

    public string FormatResponseVariables(ResponseContext responseContext, UpdateContext updateContext)
    {
        var arguments = new
        {
            SenderUser = GetUserObject(updateContext.ChatDTO.Id, updateContext.UserDTO?.Id),
            MentionedUser = GetUserObject(updateContext.ChatDTO.Id, responseContext.MentionedUserId),
            configurationContext.Configuration
        };

        return formatterProvider.Formatter.Format(responseContext.Message, arguments);
    }

    private MentionedUserDTO GetUserObject(long chatId, long? userId)
    {
        MentionedUserDTO answer = new();
        UserDTO user = new();
        if (userId is not null)
            user = cachedDataContext.FindUserById(userId.Value);

        if (user is null)
        {
            answer = null;
        }
        else
        {
            var warnedUser = cachedDataContext.FindWarningByChatId(chatId)?
                .WarnedUsers.Find(u => u.Id == userId);

            answer = new MentionedUserDTO
            {
                Id = user.Id,
                FirstName = user?.FirstName,
                LastName = user.LastName,
                Username = user.Username,
                Warnings = warnedUser?.Warnings
            };
        }

        return answer;
    }

    private async Task ProcessDeleteMessage(object obj, ElapsedEventArgs e)
    {
        while (true)
        {
            if (_onDeleteQueue == null || _onDeleteQueue.Count == 0)
            {
                break;
            }
            DeleteMessageModel deleteMessageModel = _onDeleteQueue.Peek();
            Log.Logger.Information($"Время сообщения: {deleteMessageModel.messageDate} время: {DateTime.Now}");
            if (deleteMessageModel.messageDate + TimeSpan.FromSeconds(configurationContext.Configuration.TimeToAliveMessageInSeconds) < DateTime.Now)
            {
                ChatId chatId = new ChatId(deleteMessageModel.ChatId);
                Log.Logger.Information($"Удаление messageId {deleteMessageModel.MessageId!.Value}, ChatId: {chatId.Identifier} ");
                await telegramBotClientProvider.DeleteMessageAsync(chatId,
                                                            deleteMessageModel.MessageId!.Value,
                                                            new CancellationToken());
                DeleteMessageModel x = _onDeleteQueue.Dequeue();
            }
            else
            {
                break;
            }
        }
    }

    public void MarkOnDeleteMrssage(Message message)
    {
        if (message.From.Username == configurationContext.Configuration.BOTUserName)
        {
            Log.Logger.Information($"Добавление сообщения в  очередь на удаление MessageId: {message.MessageId} messageDate: {DateTime.Now} ChatId: {message.Chat.Id}");
            var deleteMessageModel = new DeleteMessageModel()
            {
                MessageId = message.MessageId,
                messageDate = DateTime.Now,
                ChatId = message.Chat.Id
            };
            _onDeleteQueue.Enqueue(deleteMessageModel);
            Log.Logger.Information($"Пустое Queue. COUNT: {_onDeleteQueue.Count}");
        }
    }
}