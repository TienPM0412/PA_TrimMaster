using UnityEngine;
namespace RugMaster
{
    [DefaultExecutionOrder(-100)]
    public sealed class GameManager : Singleton<GameManager>
    {
        public int startLevel = 1;
        public RugPlayableAssets assets;
        public RugOriginalView View { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsModalPaused => IsPaused;
        private void Awake() { if (assets == null) assets = GetComponent<RugPlayableAssets>(); assets.Bind(); }
        private void Start()
        {
            Application.targetFrameRate = 60;
            if (Camera.main == null)
            {
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.orthographic = true;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(238, 230, 212, 255);
            }
            Ensure<ControllerManager>();
            Ensure<BoosterManager>();
            Ensure<SoundManager>().PlayMusic(SoundFX.MusicInGame);
            // Level chọn trong PAManager (Luna Playground) thắng startLevel.
            if (PAManager.HasInstance) startLevel = PAManager.Ins.SelectedLevel;
            Ensure<LevelManager>().LoadLevel(startLevel);
            View = Ensure<RugOriginalView>();
            View.Initialize();
        }
        public void SetPaused(bool value) { IsPaused = value; ControllerManager.Ins.CancelPointer(); }
        public void ClearPause() { IsPaused = false; }
        private T Ensure<T>() where T : Component
        { var component = GetComponent<T>(); return component != null ? component : gameObject.AddComponent<T>(); }
    }
}
