using PinballScores.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PinballScores.Services
{
    public class NotificationService
    {
        private const string TestWebhook = "https://hooks.slack.com/services/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";
        private const string Webhook = "https://hooks.slack.com/services/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";
		/// <summary>
		/// 0: User name
		/// 1: Table ID
		/// 2: Table name
		/// 3: Table image
		/// 4: Score title
		/// 5: Score value
		/// </summary>
        private const string SlackTemplate = @"
        {{
			""text"": ""{0} now holds {2} {4} with {5} points!"",
			""blocks"": [
				{{
					""type"": ""header"",
					""text"": {{
						""type"": ""plain_text"",
						""text"": ""New {2} High-Score!""
					}}
				}},
				{{
					""type"": ""section"",
					""text"": {{
						""type"": ""mrkdwn"",
						""text"": ""{0} now holds *{4}* with *{5}* points!\n\n<https://ombrello-pinball.web.app#{1}|Check Scores>""
					}},
					""accessory"": {{
						""type"": ""image"",
						""image_url"": ""{3}"",
						""alt_text"": ""{2}""
					}}
				}}
			]
		}}
        ";

        public async Task NotifySlack(ScoreModel newScore, TableModel table, PlayerModel? player)
        {
			string username = newScore?.Player ?? "";
			if (!string.IsNullOrEmpty(player?.Slack))
				username = $"<@{player.Slack}> ({username})";
			else if (!string.IsNullOrEmpty(player?.Name))
				username = $"{player.Name} ({username})";

			string title = newScore?.Title ?? "";
			if (table.Scores.Count(s => s.Title == title) > 1)
				title += " #" + (table.Scores.Count(s => s.Title == title && s.Score > newScore?.Score) + 1);

			string message = string.Format(SlackTemplate, username, table.Key, table.Name, table.Image, title, string.Format("{0:n0}", newScore?.Score ?? 0));

			using (HttpClient client = new HttpClient())
            {
				var request = new HttpRequestMessage(HttpMethod.Post, Webhook);
				request.Content = new StringContent(message, Encoding.UTF8, "application/json");
				var response = await client.SendAsync(request);
            }
        }
    }
}
