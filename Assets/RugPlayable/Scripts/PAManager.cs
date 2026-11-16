using UnityEngine;

namespace RugMaster
{
    public enum PALevel { Level1 = 1, Level2 = 2, Level3 = 3, Level4 = 4, Level5 = 5 }

    /// <summary>
    /// Cấu hình playable: 5 level đầu (file JSON tham chiếu trong Inspector) và level được chạy.
    /// "level" là Luna Playground field: build lên Luna rồi chọn Level1..Level5 trong Playground, không cần build lại.
    /// Lưu ý: JSON chỉ được đọc lúc Bake (Rug Playable/Bake Assets Into Current Scene) -> RugPlayableAssets.levels,
    /// nên sửa/đổi file level trong levelFiles thì phải Bake lại.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PAManager : Singleton<PAManager>
    {
        [LunaPlaygroundField("Level", 0, "Gameplay", false, "Chọn level chạy trong playable (1-5)")]
        public PALevel level = PALevel.Level1;

        [Tooltip("File JSON của 5 level đầu, theo thứ tự Level1..Level5 (Resources/RugMaster/Levels).")]
        public TextAsset[] levelFiles = new TextAsset[Constants.LevelCount];

        public int SelectedLevel => Mathf.Clamp((int)level, 1, Constants.LevelCount);
    }
}
