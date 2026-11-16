using UnityEngine;
namespace RugMaster
{
    /// <summary>
    /// Mỗi lượt chơi mỗi booster được dùng miễn phí Constants.BoostersPerLevel lần (1).
    /// Hết lượt thì bấm booster -> GetMoreViaInstall: mở store (PlayableAdHelper.InstallFullGame) và +1 lượt cho booster đó.
    /// </summary>
    public sealed class BoosterManager : Singleton<BoosterManager>
    {
        public int Sweeps { get; private set; } = Constants.BoostersPerLevel;
        public int Rearranges { get; private set; } = Constants.BoostersPerLevel;
        public int Count(bool sweep) => sweep ? Sweeps : Rearranges;
        public void ResetForLevel() { Sweeps = Rearranges = Constants.BoostersPerLevel; }
        public void AddBooster(bool sweep, int amount = 1) { if (sweep) Sweeps += amount; else Rearranges += amount; }

        /// <summary>Hết lượt miễn phí: mở store rồi cộng thêm 1 lượt cho booster đó.</summary>
        public void GetMoreViaInstall(bool sweep)
        {
            PlayableAdHelper.InstallFullGame();
            AddBooster(sweep);
        }

        public bool UseBooster(bool sweep)
        {
            var sim = LevelManager.Ins.Simulation;
            if (sim == null || sim.State == PlayState.Won || GameManager.Ins.IsModalPaused) return false;
            if (GameManager.Ins.View != null && GameManager.Ins.View.PresentationBlocksInput) return false;
            ControllerManager.Ins.CancelPointer();
            if (Count(sweep) <= 0) return false;
            bool used = sweep ? sim.Sweep() : sim.Rearrange();
            if (used) { if (sweep) Sweeps--; else Rearranges--; }
            else EventManager.Trigger(new OnBoosterUnavailableEvent { sweep = sweep, message = sweep ? "Trim some fibers first" : "Baskets are already in order" });
            return used;
        }
    }
}
