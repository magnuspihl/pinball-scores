using Google.Cloud.Firestore;

namespace PinballScores.Models
{
    [FirestoreData]
    public class TableModel
    {
        public string? Key { get; set; }

        [FirestoreProperty(Name="image")]
        public string? Image { get; set; }

        [FirestoreProperty(Name="name")]
        public string? Name { get; set; }

        [FirestoreProperty(Name="scores")]
        public IEnumerable<ScoreModel> Scores { get; set; }

        public TableModel()
        {
            Scores = new List<ScoreModel>();
        }

        public override string ToString()
        {
            return Key + Environment.NewLine + string.Join(Environment.NewLine, Scores);
        }
    }
}
