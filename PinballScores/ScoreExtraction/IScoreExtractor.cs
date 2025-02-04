using PinballScores.Models;

namespace PinballScores.ScoreExtraction
{
    public interface IScoreExtractor
    {
        IEnumerable<TableModel> GetAllScores(bool onlyNew = false);
        IEnumerable<ScoreModel> GetScores(string game);
    }
}
