# TrimMaster Playable gameplay

Open `Assets/RugPlayable/Scenes/GamePlay.unity` and press Play in Unity 2022.3.

Includes the original rug simulation, dragging the trimmer, fiber collection, baskets, intro/completion animation, three trimmer heads, and Rearrange/Sweep UI.

Removed Home/Loading, settings, level header/picker, currency, lives, shop/IAP, ads/analytics SDKs, remote config, tutorials, FPS/debug/capture tools, and audio dependencies. No extra packages required beyond Unity UI.

Ad behavior:
- One level (`Resources/RugMaster/Levels/level_001.json` and matching base/roll art).
- Both boosters unlocked immediately, two uses each. Empty boosters are disabled, without a purchase flow.
- A jam automatically revives the simulation without a popup.
- Winning keeps the finished rug on screen. Subscribe to `RugGameFlow.Completed` to show your end card or CTA.
- Trim selection resets to normal when the application starts. No PlayerPrefs or saved progression.

The scene hierarchy was rebuilt with the source gameplay view code in Unity 2022.3, avoiding a Unity 6 scene downgrade. Menu `Rug Playable/Rebuild Gameplay Scene` regenerates the scene and sets it as the build scene.

Verified in Unity 2022.3.62f3: compilation, simulation economy, Play Mode startup/intro, all three trim buttons, trimming fibers, Sweep button consumption, and no settings UI. Luna HTML export and final ad size/performance have not been verified. Existing Luna package/settings are preserved.
