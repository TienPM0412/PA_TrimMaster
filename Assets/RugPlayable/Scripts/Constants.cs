using UnityEngine;
namespace RugMaster
{
    public static class Constants
    {
        // Lượt dùng miễn phí của mỗi booster trong một lượt chơi; hết thì bấm để Install (+1).
        public const int BoostersPerLevel = 1;
        // Playable chứa 5 level đầu của game gốc (chọn qua PAManager / Luna Playground).
        public const int LevelCount = 5;
        public static readonly Rect RugRect = new Rect(94, 230, 892, 732);
        public static int SourceIndex(int level) => Mathf.Clamp(level, 1, LevelCount);
        public static string LevelResource(int index) => $"RugMaster/Levels/level_{SourceIndex(index):000}";
    }
    public static class RugPrefs { public static int HeadWidth { get; set; } = 2; }
    public static class RugGameFlow
    {
        // Hook for an ad end card. Gameplay stops on the completed rug; no shop or scene transition.
        public static System.Action Completed;
        public static void OnWinPresentationFinished() { Completed?.Invoke(); }
    }
}
