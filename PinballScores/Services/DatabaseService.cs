using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using PinballScores.Models;

namespace PinballScores.Services
{
    public class DatabaseService
    {
        private const string FirebaseCredentials = @"
        {
            ""type"": ""service_account"",
            ""project_id"": ""ombrello-pinball"",
            ""private_key_id"": ""56120ec4e35af0524c3e6b5d4c31dc21a18e2d4b"",
            ""private_key"": ""-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDJGRmwGZgY6gfO\nhB6qUyzC3rtaeiAiGbeyv8fsszF0sUQmcLT+W7pDYmnZspjMCVvzECW8iDk/Afju\nhx5UfMAEa7hJ1cYtOli/HZ3NwJCmYPfG3S9Au2X4S2nL1LzVQWdqSPqdZQU2qaHj\njTrWk0LcSEWbO3rdtz18e4bWMGsjG9Vm7h73dwhXpUbBt7zvUuoczVyc7ppzBXBc\nXQ6UE8BF6uHJBueBUXscVk5hWAZh/kKwwEKeH/pcLwbmHf7PlrsfT2HnpIf7fY0i\nARRCpBgBsM6jOINnpoJpMfvWP/llhi/l8Aq6ePuJFKHU6E88hwn1FRFt+916pLkB\nA50X3e9LAgMBAAECggEAE4XqzwRlFkIeOOftvDZv+Yk7IikKFpVtlp50z9+DtSMC\njULS54DLQAB9a0Oh/ukHsrzGFRwahqnv22RlxukHkUZN8WkeIMTx2bgL2m5P/L8c\nPwO5My0eVLEpl77TCFcdrZ1hK0ej7m8ghuFurWdFjfI8Q7pODtlb0EqDyCaSOCmu\nziEkBy063Qect66L2blXPdTz7I9GRCvoFlCn+j4UQFd4IUgssQ5eL55YnKqhVKOX\n7awu/48yRjtYOQcYn2KD5IPXI/mpdOr2TN1XEZcSehrxVQBkMBAwmqcijLerDhJo\nemQwJJYZWk5ScbMFtN+pgFs6Gx5oDKrv2C6YtK/iyQKBgQD7zQ3oPgwfZnCNmamJ\nMXVuE+2WiTHXzyNJkBgEG9QTaHUUSuVU5Hl7oI7C2rFZhVh9zyCT7ou0Cu37h5ck\n2CP4G1LrhYX79y2fmMOX5MLuoCNC6iXgSdL/2j3IHaldPoX+hqdu3uOLjBIvzBC+\nuiJ/lkVnDzYrWrj1pDQoo8EHaQKBgQDMc5f796G/6VIbBKMsd94MmEQ7bJcL2e7l\nWHYd4zaVDHd+ZjLgCX99XJNKJHIq7ghNwK+wd3FGuP9H6LlH6Vw39aci4aJm+gPf\nnxpIPn1vQ3CO9tNhAtAVTfarG0H7s94cdSpKMDCWx4d5k/A7mJFjZrp7n1miyrjn\nlw3TKDl+kwKBgQCxIhwRb2ypvi+ZfSqFh5R7Xmt5xYOZtG63djVe1pDHImmSX+ma\nYauQK2+iZaPzPNn2jvn09w+yFSo7ErRhx+btx5L6ehC6IrUmm5mkxdnXcjG8Beml\nMWnMcKObnjohjTVHy0QHu6ZI6b11gFGbSmewZ27lRA8wSn7o1p2WpTPKWQKBgCVd\nJrR0oAnbkRbA9TUmPt1cYnPdt4kB7gfZ79QsdwgsPBZMhRWuhq8ZYQ2WtYqk772N\n7k24tmnvWzAAFwROYH0VltLoG27oWBbhE8OKMBBKaeKmtyCui+qo0eCZXairSXc3\n7l+aiPa1RkvwvmGV01QwLPp0t8PpentQfsVNP5yjAoGAJHM/Vc8qdGgmbJilLHYq\nhyVRgzVMo11BveG12eMggMvBARsVy8imkCCJR1L6+JgqwOObKWnLHyekYS+ZaWr/\nGo4iasui43JwAK2MCP5rBlZwhWniyrMiX4x4lncca0R76FOV/95weNn9uZ+/OZVi\n7dh9OyRn0E0h+FeZiCpCrG4=\n-----END PRIVATE KEY-----\n"",
            ""client_email"": ""firebase-adminsdk-yyq59@ombrello-pinball.iam.gserviceaccount.com"",
            ""client_id"": ""105049259743902662210"",
            ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
            ""token_uri"": ""https://oauth2.googleapis.com/token"",
            ""auth_provider_x509_cert_url"": ""https://www.googleapis.com/oauth2/v1/certs"",
            ""client_x509_cert_url"": ""https://www.googleapis.com/robot/v1/metadata/x509/firebase-adminsdk-yyq59%40ombrello-pinball.iam.gserviceaccount.com""
        }
        ";

        private FirestoreDb? _firestoreDb = null;
        private FirestoreDb Firestore
        {
            get
            {
                if (_firestoreDb != null)
                    return _firestoreDb;
                var credential = GoogleCredential.FromJson(FirebaseCredentials);
                var builder = new FirestoreClientBuilder();
                builder.Credential = credential;

                return FirestoreDb.Create("ombrello-pinball", builder.Build());
            }
        }

        private NotificationService _notificationService = new NotificationService();

        public async Task UploadScores(IEnumerable<TableModel> tables)
        {
            foreach (var table in tables)
            {
                var snapshot = await Firestore.Collection("tables").Document($"{table.Key}").GetSnapshotAsync();
                var existing = snapshot.ConvertTo<TableModel>();
                if (existing != null)   //TODO: Should we notify on all non-existing? Not sure.
                {
                    existing.Key = table.Key;
                    var newScores = GetNewScores(existing.Scores, table.Scores);
                    foreach (var score in newScores)
                    {
                        await _notificationService.NotifySlack(score, existing, await GetPlayer(score.Player));
                    }
                }

                await Firestore.Collection("tables").Document($"{table.Key}").SetAsync(new Dictionary<string, object> { { "scores", table.Scores } }, SetOptions.MergeAll);
            }
        }

        public async Task<PlayerModel?> GetPlayer(string? initials)
        {
            if (string.IsNullOrEmpty(initials))
                return null;
            var snapshot = await Firestore.Collection("players").Document($"{initials}").GetSnapshotAsync();
            var player = snapshot.ConvertTo<PlayerModel>();
            return player;
        }

        private IEnumerable<ScoreModel> GetNewScores(IEnumerable<ScoreModel> oldScores, IEnumerable<ScoreModel> newScores)
        {
            return newScores.Where(n => !oldScores.Contains(n));
        }
    }
}
