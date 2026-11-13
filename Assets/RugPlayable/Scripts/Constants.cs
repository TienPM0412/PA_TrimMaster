using UnityEngine;
namespace RugMaster
{
    public static class Constants
    {
        public const int BoostersPerLevel = 2;
        public const int LevelCount = 1;
        public static readonly Rect RugRect = new Rect(94, 230, 892, 732);
        public static int SourceIndex(int level) => 1;
        public static string LevelResource(int index) => "RugMaster/Levels/level_001";
    }
    public static class RugPrefs { public static int HeadWidth { get; set; } = 2; }
    public static class RugGameFlow
    {
        // Hook for an ad end card. Gameplay stops on the completed rug; no shop or scene transition.
        public static System.Action Completed;
        public static void OnWinPresentationFinished() { Completed?.Invoke(); }
    }
}
