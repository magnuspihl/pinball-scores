using PinballScores.Services;

try
{
    ScoreService scoreService = new ScoreService();
    DatabaseService dbService = new DatabaseService();

    var scores = scoreService.GetAllScores().ToList();
    await dbService.UploadScores(scores);

    ConfigService.LastRun = DateTime.UtcNow;

    if (ConfigService.enableLogging)
        File.WriteAllText(DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")+".log", string.Join(Environment.NewLine + Environment.NewLine, scores));
}
catch (Exception ex)
{
    if (ConfigService.enableLogging)
        File.WriteAllText(DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + ".log", ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace);
}