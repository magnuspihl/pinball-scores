using Google.Cloud.Firestore;

namespace PinballScores.Models
{
    [FirestoreData]
    public class ScoreModel
    {
        [FirestoreProperty("title")]
        public string? Title { get; set; }

        [FirestoreProperty("player")]
        public string? Player { get; set; }

        [FirestoreProperty("score")]
        public float? Score { get; set; }

        public string? ExtractedLine { get; set; }

        public string? ExtractedScore { get; set; }

        public override string ToString()
        {
            return $"Title: '{Title}' | Player: '{Player}' | Score: '{Score}' | ExtractedLine: '{ExtractedLine}' | ExtractedScore: '{ExtractedScore}'";
        }

        public override bool Equals(object? obj)
        {
            var other = obj as ScoreModel;
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Title == other.Title && Player == other.Player && Score == other.Score;
        }
    }
}