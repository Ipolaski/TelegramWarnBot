namespace TelegramWarnBot;

public class WarnedUser
{
    public long Id { get; set; }
    public int Warnings { get; set; }
    public DateTime? Unmute { get; set; }
}