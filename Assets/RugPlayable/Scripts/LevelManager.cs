using UnityEngine;
namespace RugMaster
{
    public sealed class LevelManager : Singleton<LevelManager>
    {
        public RugSimulation Simulation { get; private set; }
        public int CurrentLevel { get; private set; }
        public void LoadLevel(int level)
        {
            CurrentLevel = Mathf.Max(1, level);
            var data = RugPlayableAssets.Current.level;
            Simulation = new RugSimulation(data);
            Simulation.OnTrim += cell => EventManager.Trigger(new OnTrimEvent { cell = cell });
            Simulation.OnFiberLanded += fiber => EventManager.Trigger(new OnFiberLandedEvent { fiber = fiber });
            Simulation.OnBasketCompleted += basket => EventManager.Trigger(new OnBasketCompletedEvent { basket = basket });
            Simulation.OnWon += () => EventManager.Trigger(new OnLevelWonEvent { level = CurrentLevel });
            Simulation.OnJammed += () => EventManager.Trigger(new OnLevelJammedEvent { level = CurrentLevel });
            GameManager.Ins.ClearPause();
            ControllerManager.Ins.ResetForLevel();
            BoosterManager.Ins.ResetForLevel();
            EventManager.Trigger(new OnLevelLoadedEvent { level = CurrentLevel, simulation = Simulation });
        }
        public void RestartLevel() { LoadLevel(CurrentLevel); }
        public void NextLevel() { LoadLevel(CurrentLevel + 1); }
        private void Update()
        {
            if (Simulation == null) return;
            var input = ControllerManager.Ins;
            input.UpdateLayout();
            if (GameManager.Ins.IsModalPaused || (GameManager.Ins.View != null && GameManager.Ins.View.PresentationBlocksInput))
            { input.CancelPointer(); return; }
            input.HandlePointer();
            Simulation.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
            input.AfterSimulationTick();
        }
    }
}
