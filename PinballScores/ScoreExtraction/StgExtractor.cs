using PinballScores.Models;
using PinballScores.MSStorage;
using PinballScores.Services;
using System.Configuration;
using System.Text.RegularExpressions;

namespace PinballScores.ScoreExtraction
{
    public class StgExtractor : IScoreExtractor
    {
        private Storage storage;

        private static readonly Regex HighScoreTitle = new Regex("(HighScore)([0-9]{1,2})");

        public StgExtractor()
        {
            if (ConfigService.stgPath == null)
                throw new ConfigurationErrorsException("stgPath is not configured in App.config");

            storage = new Storage(ConfigService.stgPath);
        }

        public IEnumerable<TableModel> GetAllScores(bool onlyNew = false)
        {
            List<TableModel> result = new List<TableModel>();
            if (onlyNew && !FileService.ShouldProcessFile(ConfigService.stgPath))
                return new TableModel[0];
            var tables = storage.GetTableNames().ToList();
            foreach (var table in tables)
                result.Add(new TableModel { Key = table, Scores = GetScores(table) });
            return result;
        }

        public IEnumerable<ScoreModel> GetScores(string table)
        {
            var variables = storage.GetTableVariables(table).ToList();
            return ParseScores(variables);
        }

        private IEnumerable<ScoreModel> ParseScores(IEnumerable<KeyValuePair<string,string>> variables)
        {
            List<ScoreModel> scores = new List<ScoreModel>();
            foreach (var variable in variables)
            {
                if (variable.Key.Contains("HighScore") && !variable.Key.EndsWith("Name"))
                {
                    if (!int.TryParse(variable.Value, out int score))
                        continue;
                    string player = variables.FirstOrDefault(v => v.Key == variable.Key + "Name").Value;
                    Match titleMatch = HighScoreTitle.Match(variable.Key);
                    string title = titleMatch.Success ? titleMatch.Groups[1].Value : variable.Key;

                    scores.Add(new ScoreModel { Title = title, Score = score, Player = player });
                }
            }
            return scores;
        }
    }
}
