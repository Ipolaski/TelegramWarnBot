﻿namespace TelegramWarnBot;

public class Configuration
{
    /// <summary>
    /// Количество дней для выхода из мута
    /// </summary>
    public int UnmuteUsersDelay { get; set; }
    public int TimeMinuteSendUnWarnMessages { get; set; }
    public int UpdateDelay { get; set; }
    public int StatsDelay { get; set; }
    public int MaxWarnings { get; set; }
    public bool DeleteWarnMessage { get; set; }
    public bool DeleteJoinedLeftMessage { get; set; }
    public bool DeleteLinksFromNewMembers { get; set; }
    public int NewMemberStatusFromHours { get; set; }
    public bool AllowAdminWarnings { get; set; }
    public string BOTUserName { get; set; }
    public int DeleteBotMessageTimeOutInSeconds { get; set; }
    public int TimeToAliveMessageInSeconds { get; set; }
    public string HiddenChatId { get; set; }
    public Captions Captions { get; set; }
}