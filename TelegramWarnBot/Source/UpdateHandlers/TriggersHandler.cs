﻿namespace TelegramWarnBot;

[RegisteredChat]
[TextMessageUpdate]
public class TriggersHandler : Pipe<UpdateContext>
{
    private readonly IConfigurationContext configurationContext;
    private readonly ILogger<TriggersHandler> logger;
    private readonly IMessageHelper messageHelper;
    private readonly IResponseHelper responseHelper;

    public TriggersHandler(Func<UpdateContext, Task> next,
                           IConfigurationContext configurationContext,
                           IMessageHelper messageHelper,
                           IResponseHelper responseHelper,
                           ILogger<TriggersHandler> logger) : base(next)
    {
        this.configurationContext = configurationContext;
        this.messageHelper = messageHelper;
        this.responseHelper = responseHelper;
        this.logger = logger;
    }

    public override async Task<Task> Handle(UpdateContext context)
    {
        foreach (var trigger in configurationContext.Triggers)
        {
            if (trigger.Chat is not null && trigger.Chat != context.ChatDTO.Id)
                continue;

            if (messageHelper.MatchMessage(trigger.Messages, trigger.MatchWholeMessage, trigger.MatchCase, context.Text))
            {
                //string response = string.Empty;
                //if (trigger.Responses.Length != 0)
                //    // Get random response
                //    response = trigger.Responses[Random.Shared.Next(trigger.Responses.Length)];

                //await responseHelper.SendMessageAsync(new ResponseContext
                //{
                //    Message = response
                //}, context, context.MessageId);

                //logger.LogInformation("Message \"{message}\" from {user} in chat {chat} triggered a Trigger. Bot responded with:\"{response}\"",
                //                      context.Text.Truncate(50),
                //                      context.UserDTO.GetName(),
                //                      context.ChatDTO.Name,
                //                      response.Truncate(50));

                //// Match only 1 trigger
                return next(context);
            }
        }

        return next(context);
    }
}