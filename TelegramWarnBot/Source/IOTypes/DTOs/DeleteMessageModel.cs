using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramWarnBot.Source.IOTypes.DTOs
{
	public class DeleteMessageModel
	{
		public int? MessageId { get; set; }
        public DateTime messageDate { get; set; }
		public long ChatId { get; set; }
	}
}