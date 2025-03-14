using System.Timers;
using TelegramWarnBot.Source.IOTypes.DTOs;

namespace TelegramWarnBot;

public interface IResponseHelper
{
    string FormatResponseVariables(ResponseContext responseContext, UpdateContext updateContext);
    Task DeleteMessageAsync(UpdateContext context);
    Task SendMessageAsync(ResponseContext responseContext, UpdateContext updateContext, int? replyToMessageId = null);
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
		_deleteMessageTimer = new System.Timers.Timer( TimeSpan.FromSeconds( configurationContext.Configuration.DeleteBotMessageTimeOutInSeconds ).TotalMilliseconds );
		_deleteMessageTimer.Elapsed += async ( sender, e ) => await ProcessDeleteMessage( sender, e );
		_deleteMessageTimer.AutoReset = true;
		_deleteMessageTimer.Enabled = true;
		_onDeleteQueue = new Queue<DeleteMessageModel>();
		Log.Logger.Information( $"ResponseHelper сработал конструктор класса" );
	}

    public async Task SendMessageAsync(ResponseContext responseContext, UpdateContext updateContext, int? replyToMessageId = null)
    {
		
		Message message = await telegramBotClientProvider.SendMessageAsync( updateContext.ChatDTO.Id,
														  FormatResponseVariables( responseContext, updateContext ),
														  replyToMessageId,
														  updateContext.CancellationToken );		
		MarkOnDeleteMrssage( message );
	}

    public Task DeleteMessageAsync(UpdateContext context)
    {
        return telegramBotClientProvider.DeleteMessageAsync(context.ChatDTO.Id,
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
        if (userId is null)
            return null;

        var user = cachedDataContext.FindUserById(userId.Value);

        if (user is null)
            return null;

        var warnedUser = cachedDataContext.FindWarningByChatId(chatId)?
            .WarnedUsers.Find(u => u.Id == userId);

        return new MentionedUserDTO
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Username = user.Username,
            Warnings = warnedUser?.Warnings
        };
    }
	private async Task ProcessDeleteMessage( object obj, ElapsedEventArgs e )
	{
        Log.Logger.Information("Сработал метод удаления сообщения");
		while ( true )
		{
			if ( _onDeleteQueue == null || _onDeleteQueue.Count == 0 )
			{
				Log.Logger.Information( $"Пустое Queue. COUNT {_onDeleteQueue.Count}" );
				break;
			}
			DeleteMessageModel deleteMessageModel = _onDeleteQueue.Peek();
			Log.Logger.Information( $"время сообщения: {deleteMessageModel.messageDate} время: {DateTime.Now}" );
			if ( deleteMessageModel.messageDate + TimeSpan.FromSeconds( configurationContext.Configuration.TimeToAliveMessageInSeconds ) < DateTime.Now  )
			{
                Log.Logger.Information( $"удаление messageId {deleteMessageModel.MessageId!.Value}, ChatId: {deleteMessageModel.ChatId} " );
				await telegramBotClientProvider.DeleteMessageAsync( deleteMessageModel.ChatId,
															deleteMessageModel.MessageId!.Value,
															new CancellationToken() );
				DeleteMessageModel x = _onDeleteQueue.Dequeue();
			}
			else
			{
				break;
			}
		}
    }

	public async Task MarkOnDeleteMrssage( Message message )
	{
        if ( message.From.Username == configurationContext.Configuration.BOTUserName )
        {
            Log.Logger.Information( $"добавление сообщения в  очередьна удаление MessageId: {message.MessageId} messageDate: {DateTime.Now} ChatId: {message.Chat.Id}" );
            var deleteMessageModel = new DeleteMessageModel()
            {
                MessageId = message.MessageId,
                messageDate = DateTime.Now,
                ChatId = message.Chat.Id
            };
            _onDeleteQueue.Enqueue( deleteMessageModel );
			Log.Logger.Information( $"Пустое Queue  . COUNT {_onDeleteQueue.Count}" );
		}
    }
}