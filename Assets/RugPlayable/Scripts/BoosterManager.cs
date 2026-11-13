using UnityEngine;
namespace RugMaster
{
    public sealed class BoosterManager : Singleton<BoosterManager>
    {
        public int Sweeps { get; private set; } = Constants.BoostersPerLevel;
        public int Rearranges { get; private set; } = Constants.BoostersPerLevel;
        public void ResetForLevel() { Sweeps = Rearranges = Constants.BoostersPerLevel; }
        public bool UseBooster(bool sweep)
        {
            var sim = LevelManager.Ins.Simulation;
            if (sim == null || sim.State == PlayState.Won || GameManager.Ins.IsModalPaused) return false;
            if (GameManager.Ins.View != null && GameManager.Ins.View.PresentationBlocksInput) return false;
            ControllerManager.Ins.CancelPointer();
            if ((sweep ? Sweeps : Rearranges) <= 0) return false;
            bool used = sweep ? sim.Sweep() : sim.Rearrange();
            if (used) { if (sweep) Sweeps--; else Rearranges--; }
            else EventManager.Trigger(new OnBoosterUnavailableEvent { sweep = sweep, message = sweep ? "Trim some fibers first" : "Baskets are already in order" });
            return used;
        }
    }
}
