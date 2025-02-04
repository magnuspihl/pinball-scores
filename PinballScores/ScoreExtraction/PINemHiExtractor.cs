using PinballScores.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Configuration;
using PinballScores.Services;
using System.Globalization;

namespace PinballScores.ScoreExtraction
{
    public class PINemHiExtractor : IScoreExtractor
    {
        private static readonly Regex REGEX_POSITION = new Regex(@"\#?\d+\)?");
        private static readonly Regex REGEX_SCORE_LINE = new Regex(@"(.+?)( - ){0,1}([\d\,\.]*)([ -][A-Z]+){0,1}$");

        public IEnumerable<TableModel> GetAllScores(bool onlyNew = false)
        {
            if (ConfigService.nvramPath == null)
                throw new ConfigurationErrorsException("nvramPath is not configured in App.config");
            IEnumerable<string> files = Directory.GetFiles(ConfigService.nvramPath, "*.nv");
            if (onlyNew)
                files = files.Where(f => FileService.ShouldProcessFile(f));
            return files.Select(f => CleanName(f)).Select(f => new TableModel { Key = f, Scores = GetScores(f) });
        }

        public IEnumerable<ScoreModel> GetScores(string game)
        {
            string consoleOutput = RunPINemHi(game);
            return ParseScores(consoleOutput);
        }

        public string RunPINemHi(string game)
        {
            if (ConfigService.PINemHiPath == null)
                throw new ConfigurationErrorsException("PINemHiPath is not configured in App.config");

            Process pinemhi = new Process();
            pinemhi.StartInfo = new ProcessStartInfo(ConfigService.PINemHiPath);
            pinemhi.StartInfo.Arguments = game + ".nv";
            pinemhi.StartInfo.WorkingDirectory = Path.GetDirectoryName(ConfigService.PINemHiPath);
            pinemhi.StartInfo.UseShellExecute = false;
            pinemhi.StartInfo.CreateNoWindow = true;
            pinemhi.StartInfo.RedirectStandardOutput = true;
            pinemhi.StartInfo.RedirectStandardError = true;
            pinemhi.Start();

            string output = pinemhi.StandardOutput.ReadToEnd();
            string error = pinemhi.StandardError.ReadToEnd();

            pinemhi.WaitForExit();

            return output;
        }

        public IEnumerable<ScoreModel> ParseScores(string consoleOutput)
        {
            List<ScoreModel>? scores = new List<ScoreModel>();
            string[]? lines = consoleOutput.Split(Environment.NewLine);
            string? currentTitle = null;
            foreach (string? line in lines)
            {
                if (line.Trim() == "")
                {
                    currentTitle = null;
                }
                else if (currentTitle == null)
                {
                    currentTitle = line;
                }
                else
                {
                    var model = ParseScoreLine(line, currentTitle);
                    if (model != null)
                        scores.Add(model);
                }
            }
            return scores;
        }

        private ScoreModel? ParseScoreLine(string line, string? title)
        {
            //TODO: Move this to some sort of IoC handler registration
            if (title == "KING OF THE REALM")
            {
                if (line.StartsWith("CROWNED FOR THE"))
                    return null;
                if (DateTime.TryParse(line, out _))
                    return null;
                string p = REGEX_POSITION.Replace(line, "").Trim();
                return new ScoreModel { Title = title, Player = p, ExtractedLine = line };
            }
            if (line == "CASTLES DESTROYED")
                return null;
            if (line == "JOUST VICTORIES")
                return null;
            if (line == "CATAPULT SLAMS")
                return null;
            if (line == "PEASANT REVOLTS")
                return null;
            if (line == "DAMSELS SAVED")
                return null;
            if (line == "TROLLS DESTROYED")
                return null;
            if (title == "DESTROY RING CHAMPION")
                return new ScoreModel { Title = title, Player = line.Substring(0, line.IndexOf("-")).Trim(), ExtractedLine = line };

            Match? scoreLine = REGEX_SCORE_LINE.Match(line);
            string player = scoreLine.Groups[1].Value.Trim();
            player = REGEX_POSITION.Replace(player, "").Trim();

            var score = scoreLine.Groups[3].Value;
            float? scoreDecimal = CleanScore(score);

            return new ScoreModel { Title = title, Player = player, Score = scoreDecimal, ExtractedLine = line, ExtractedScore = score };
        }

        public static float? CleanScore(string score)
        {
            //Temp solution, ignore decimals
            score = score.Replace(".", "").Replace(",", "").Trim();

            //score = score.Replace(",", ".");
            //int lastSepIdx = score.LastIndexOf(".");
            //if (lastSepIdx >= 0 && lastSepIdx >= score.Length - 3)  //There's a decimal separator
            //{
            //    score = score.Substring(0, lastSepIdx).Replace(".", "") + score.Substring(lastSepIdx).Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            //}
            float? scoreDecimal = null;
            if (float.TryParse(score, NumberStyles.Number, CultureInfo.CurrentCulture, out float d))
                scoreDecimal = d;
            return scoreDecimal;
        }

        private string CleanName(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
