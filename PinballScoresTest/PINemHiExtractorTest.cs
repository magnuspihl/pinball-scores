using Microsoft.VisualStudio.TestTools.UnitTesting;
using PinballScores.ScoreExtraction;
using PinballScores.Services;
using System.Linq;

namespace PinballScoresTest
{
    [TestClass]
    public class PINemHiExtractorTest
    {
        [TestMethod]
        public void TestScoreParsing()
        {
            string input = @"GRAND CHAMPION
MHP       128,591,800

HIGH SCORES
#1 MHP       100,609,520
#2 KHP        81,263,930
#3 D N        75,000,000
#4 K O        55,000,000

GAUNTLET CHAMP 1
J B   15 POINTS

GAUNTLET CHAMP 2
G S   10 POINTS

GAUNTLET CHAMP 3
M S   5 POINTS

PIRATE KING
KEF   25 POINTS

DAVY JONES CHAMPION
XAQ";
            var pinemhi = new PINemHiExtractor();
            var scores = pinemhi.ParseScores(input).ToList();
            Assert.IsNotNull(scores);
            Assert.IsTrue(scores.Any());
            Assert.IsNotNull(scores?.FirstOrDefault(s => s.Title == "GRAND CHAMPION" && s.Player == "MHP" && s.Score == 128591800));
            Assert.IsNotNull(scores?.FirstOrDefault(s => s.Title == "DAVY JONES CHAMPION" && s.Player == "XAQ"));
            Assert.IsNotNull(scores?.FirstOrDefault(s => s.Title == "GAUNTLET CHAMP 3" && s.Player == "M S" && s.Score == 5));
        }

        [TestMethod]
        public void TestCleanScore()
        {
            Assert.AreEqual(PINemHiExtractor.CleanScore("10.000.000"), 10000000f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10,000,000"), 10000000f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10,000.000"), 10000000f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10.000,50"), 10000.5f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10.000.50"), 10000.5f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10.000,5"), 10000.5f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10.000.5"), 10000.5f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("123"), 123f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("123,5"), 123.5f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("123.5"), 123.5f);
            Assert.AreEqual(PINemHiExtractor.CleanScore("10000000"), 10000000f);
        }
    }
}