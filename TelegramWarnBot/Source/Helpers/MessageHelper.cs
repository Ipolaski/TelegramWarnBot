﻿namespace TelegramWarnBot;

public interface IMessageHelper
{
    bool MatchCardNumber(string message);
    bool MatchLinkMessage(Message message);
    bool MatchForwardedMedia(Message message);
    bool MatchMessage(IEnumerable<string> matchFromMessages, bool matchWholeMessage, bool matchCase, string message);
}

public class MessageHelper : IMessageHelper
{
    public bool MatchForwardedMedia(Message message)
    {
        return message.ForwardFrom is not null && message.Type is MessageType.Photo or MessageType.Video;
    }

    public bool MatchMessage(IEnumerable<string> matchFromMessages, bool matchWholeMessage, bool matchCase, string message)
    {
        bool result = false;
        if (!string.IsNullOrEmpty(message))
        {
            if (matchWholeMessage)
                result = matchFromMessages.Any(m => matchCase ? m == message : string.Equals(m, message, StringComparison.CurrentCultureIgnoreCase));
            var matchMessages = matchFromMessages?.Any(m => message.Contains(m, matchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase));
            result = matchMessages ?? false;
        }
        return result;
    }

    public bool MatchLinkMessage(Message message)
    {
        return message.Entities?.Any(e => e.Type == MessageEntityType.Url
                                       || e.Type == MessageEntityType.TextLink
                                       || e.Type == MessageEntityType.TextMention
                                       || e.Type == MessageEntityType.Mention) ?? false;
    }

    public bool MatchCardNumber(string message)
    {
        return Tools.CardNumberRegex.Match(message).Success;
    }
}