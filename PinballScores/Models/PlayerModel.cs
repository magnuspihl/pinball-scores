using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PinballScores.Models
{
    [FirestoreData]
    public class PlayerModel
    {
        public string? Key { get; set; }

        [FirestoreProperty(Name="name")]
        public string? Name { get; set; }

        [FirestoreProperty(Name="slack")]
        public string? Slack { get; set; }
    }
}
