using PinballScores.Models;
using PinballScores.ScoreExtraction;

namespace PinballScores.Services
{
    public class ScoreService
    {
        public IEnumerable<TableModel> GetAllScores(bool onlyNew = false)
        {
            var extractors = new IScoreExtractor[]
            {
                new PINemHiExtractor(),
                new StgExtractor()
            };
            return extractors.SelectMany(extractor =>
            {
                try
                {
                    return extractor.GetAllScores(onlyNew);
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Failed to get scores from " + extractor.GetType().Name);
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);
                    return new TableModel[0];
                }
            });
        }
    }
}
