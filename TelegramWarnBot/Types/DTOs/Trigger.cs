﻿namespace TelegramWarnBot;

public class Trigger
{
    public string[] Messages { get; set; }
    public string Response { get; set; }
    public bool MatchCase { get; set; }
    public bool MatchWholeMessage { get; set; }
}
