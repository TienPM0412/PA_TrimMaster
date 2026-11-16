using System;
using System.Linq;
using RugMaster;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public static class PlayableExportSetup
{
    [MenuItem("Rug Playable/Bake Assets Into Current Scene")]
    public static void BakeAssets()
    {
        var game = UnityEngine.Object.FindObjectOfType<GameManager>();
        if (game == null) throw new Exception("Open the gameplay scene first");
        var library = game.GetComponent<RugPlayableAssets>();
        if (library == null) library = game.gameObject.AddComponent<RugPlayableAssets>();
        // Lần 1: nạp asset để bake ảnh thảm nguyên lông (cần atlas/layout), lần 2: nạp lại kèm ảnh + material vừa tạo.
        var pa = EnsurePAManager();
        library.levels = pa.levelFiles.Select(f => JsonUtility.FromJson<LevelDefinition>(f.text)).ToArray();
        ScanEntries(library);
        library.originalManifest = JsonUtility.FromJson<OriginalManifest>(Resources.Load<TextAsset>("RugMaster/OriginalUI/manifest").text);
        library.Bind();
        for (int i = 1; i <= library.levels.Length; i++) BakePileImage(library.Level(i), i);
        int count = ScanEntries(library);
        library.originalManifest = JsonUtility.FromJson<OriginalManifest>(Resources.Load<TextAsset>("RugMaster/OriginalUI/manifest").text);
        library.originalDom = JsonUtility.FromJson<OriginalDom>(Resources.Load<TextAsset>("RugMaster/OriginalUI/dom-layout").text);
        library.presentationManifest = JsonUtility.FromJson<RugPresentationManifest>(Resources.Load<TextAsset>("RugMaster/Presentation/manifest").text);
        // JSON is parsed only by the editor. Playworks receives concrete serialized fields.
        foreach (var entry in library.entries) entry.json = null;
        library.Bind();
        game.assets = library;
        if (game.GetComponent<ControllerManager>() == null) game.gameObject.AddComponent<ControllerManager>();
        if (game.GetComponent<BoosterManager>() == null) game.gameObject.AddComponent<BoosterManager>();
        if (game.GetComponent<LevelManager>() == null) game.gameObject.AddComponent<LevelManager>();
        if (game.GetComponent<RugTrimEffects>() == null) game.gameObject.AddComponent<RugTrimEffects>();
        if (game.GetComponent<RugPresentation>() == null) game.gameObject.AddComponent<RugPresentation>();
        var view = game.GetComponent<RugOriginalView>();
        if (view != null && view.canvas != null)
        {
            view.EnsureRugFit();
            view.Bind(new RugSimulation(library.Level(pa.SelectedLevel)), pa.SelectedLevel);
            view.EnsureFiberBatches();
            view.EnsureWinPanel(false);
            EditorUtility.SetDirty(view);
        }
        EditorUtility.SetDirty(library);
        EditorUtility.SetDirty(game);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        EditorSceneManager.SaveScene(game.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("PLAYABLE_ASSETS_BAKED: " + count + " entries; " + library.levels.Length + " levels (selected " + pa.level + "); managers serialized.");
    }

    // Object "PAManager" trong scene: tham chiếu 5 file level (ô trống thì tự điền level_001..005 trong Resources).
    private static PAManager EnsurePAManager()
    {
        var pa = UnityEngine.Object.FindObjectOfType<PAManager>();
        if (pa == null) pa = new GameObject("PAManager").AddComponent<PAManager>();
        if (pa.levelFiles == null || pa.levelFiles.Length != Constants.LevelCount)
            Array.Resize(ref pa.levelFiles, Constants.LevelCount);
        for (int i = 0; i < pa.levelFiles.Length; i++)
        {
            if (pa.levelFiles[i] == null) pa.levelFiles[i] = Resources.Load<TextAsset>(Constants.LevelResource(i + 1));
            if (pa.levelFiles[i] == null) throw new Exception("PAManager: missing level file #" + (i + 1));
        }
        EditorUtility.SetDirty(pa);
        return pa;
    }
    // Dựng lại "Win panel" (tắt sẵn) trong overlay của scene đang mở. Lúc thắng RugOriginalView chỉ việc bật nó.
    [MenuItem("Rug Playable/Create Win Panel In Scene")]
    public static void CreateWinPanel()
    {
        var view = UnityEngine.Object.FindObjectOfType<RugOriginalView>();
        if (view == null || view.canvas == null) throw new Exception("Open the gameplay scene first");
        if (UnityEngine.Object.FindObjectOfType<RugPlayableAssets>() == null) throw new Exception("Bake assets first (Rug Playable/Bake Assets Into Current Scene)");
        view.GetComponent<RugPlayableAssets>()?.Bind();
        if (RugPlayableAssets.Load<Texture2D>(RugWinPanel.TitleKey) == null)
            throw new Exception("Title texture not baked: run Rug Playable/Bake Assets Into Current Scene first");
        view.EnsureWinPanel(true);
        EditorUtility.SetDirty(view);
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        Selection.activeGameObject = view.winPanel.gameObject;
        Debug.Log("PLAYABLE_WIN_PANEL_READY: " + view.winPanel.name + " (inactive) under " + view.winPanel.transform.parent.name);
    }
    // Chỉ đưa vào scene (=> vào build Luna) những asset code thật sự dùng. Asset không có trong danh sách
    // vẫn nằm trong Resources nhưng không được scene tham chiếu. Thêm asset mới mà code Load theo tên thì nhớ thêm vào đây.
    private static readonly System.Collections.Generic.HashSet<string> UsedOriginalUI = new System.Collections.Generic.HashSet<string>
    {
        "wood", "rugShadow", "binding", "soft", "channel-back", "channel-outlet-lips", "channel-intake-lip",
        "basket-queue-hidden", "qbShadow", "fiber-atlas", "flight-atlas", "meter-0", "rounded-mask",
        "kit-bow", "kit-fold", "kit-seal", "kit-tab", "kit-clip", "kit-counter",
        "kit-loadFill", "kit-loadFill-orange", "kit-loadFill-red", "kit-loadTrack",
        "tool-body-narrow", "tool-body-normal", "tool-body-wide", "blade-narrow", "blade-normal", "blade-wide",
        "PileMask",
    };
    private static readonly System.Collections.Generic.HashSet<string> UsedPresentation = new System.Collections.Generic.HashSet<string>
    { "Additive", "sweep", "dot", "star", "curl", "conf" };

    private static bool IsUsedAsset(string key, RugPlayableAssets library, System.Collections.Generic.HashSet<string> colors)
    {
        int slash = key.LastIndexOf('/');
        string folder = slash >= 0 ? key.Substring(0, slash) : "", name = key.Substring(slash + 1);
        int levels = library.levels.Length;
        bool LevelAsset(string prefix) =>
            name.StartsWith(prefix) && int.TryParse(name.Substring(prefix.Length), out int n) && n >= 1 && n <= levels;
        switch (folder)
        {
            case "RugMaster/Audio/Music":
            case "RugMaster/Audio/SFX":
            case "RugMaster/Art/Booster":
            case "RugMaster/Art/HeadSelector":
                return true;
            case "RugMaster/Fonts":
                return name == "Fredoka-600" || name == "Fredoka-700";
            case "RugMaster/PA":
                return key == RugWinPanel.TitleKey || key == RugWinPanel.ButtonFrameKey || key == RugWinPanel.ButtonFillKey;
            case "RugMaster/OriginalUI/Bases":
                return LevelAsset("base-");
            case "RugMaster/Presentation":
                return UsedPresentation.Contains(name) || LevelAsset("roll-");
            case "RugMaster/OriginalUI":
                if (UsedOriginalUI.Contains(name) || LevelAsset("pile-full-")) return true;
                // Rổ: chỉ màu có trong 5 level.
                foreach (var prefix in new[] { "basket-back-", "basket-front-" })
                    if (name.StartsWith(prefix)) return colors.Contains(name.Substring(prefix.Length));
                return false;
            default:
                // Levels/*.json, manifest, dom-layout: đã parse vào field của RugPlayableAssets / PAManager lúc Bake.
                return false;
        }
    }

    // Màu bông + màu rổ thực sự xuất hiện trong các level đã bake.
    private static System.Collections.Generic.HashSet<string> UsedColors(RugPlayableAssets library)
    {
        var colors = new System.Collections.Generic.HashSet<string>();
        foreach (var level in library.levels)
        {
            var sim = new RugSimulation(level);
            foreach (var c in sim.Cells) colors.Add(c.Color);
            foreach (var column in sim.Columns) foreach (var b in column) colors.Add(b.Color);
        }
        return colors;
    }

    private static int ScanEntries(RugPlayableAssets library)
    {
        var entries = new System.Collections.Generic.List<RugPlayableAssetEntry>();
        var skipped = new System.Collections.Generic.List<string>();
        var colors = UsedColors(library);
        const string resourceRoot = "Assets/RugPlayable/Resources/";
        foreach (var guid in AssetDatabase.FindAssets("", new[] { resourceRoot.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue;
            string key = path.Substring(resourceRoot.Length);
            key = key.Substring(0, key.LastIndexOf('.'));
            if (!IsUsedAsset(key, library, colors)) { skipped.Add(key); continue; }
            var entry = new RugPlayableAssetEntry { path = key };
            entry.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            entry.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            entry.font = AssetDatabase.LoadAssetAtPath<Font>(path);
            entry.material = AssetDatabase.LoadAssetAtPath<Material>(path);
            entry.audio = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (text != null) entry.json = text.text;
            if (entry.texture != null || entry.sprite != null || entry.font != null || entry.material != null || entry.audio != null || text != null)
                entries.Add(entry);
        }
        library.entries = entries.ToArray();
        skipped.Sort();
        Debug.Log("PLAYABLE_ASSETS_SKIPPED (" + skipped.Count + " không dùng, không tham chiếu trong scene):\n" + string.Join("\n", skipped));
        return entries.Count;
    }

    // Bake "thảm nguyên lông": đế đã cạo + AO lúc chưa cạo + bóng búi + mọi búi len, đúng khung "Rounded pile clipping"
    // (90,226,900,740 theo toạ độ thảm). Ảnh đục nên lưu JPG cho nhẹ. Lúc chạy ảnh này được khoét theo mask ô đã cạo.
    private const string PileShader = "RugMaster/UI Pile Mask";
    private const string PileMaterialPath = "Assets/RugPlayable/Resources/" + RugOriginalView.PileMaskMaterial + ".mat";
    private static void BakePileImage(LevelDefinition level, int sourceLevel)
    {
        const int width = 900, height = 740, uiLayer = 5;
        var shader = Shader.Find(PileShader);
        if (shader == null) throw new Exception("Missing shader " + PileShader);
        var material = AssetDatabase.LoadAssetAtPath<Material>(PileMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Uncut pile mask UI" };
            AssetDatabase.CreateAsset(material, PileMaterialPath);
        }
        else if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }

        var sim = new RugSimulation(level);
        var camera = new GameObject("Pile bake camera") { hideFlags = HideFlags.DontSave }.AddComponent<Camera>();
        camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.orthographic = true; camera.orthographicSize = height * 0.5f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 100;
        camera.cullingMask = 1 << uiLayer; camera.transform.position = new Vector3(0, 0, -10);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        camera.targetTexture = target; camera.aspect = (float)width / height;

        var root = new GameObject("Pile bake canvas", typeof(RectTransform)) { hideFlags = HideFlags.DontSave, layer = uiLayer };
        var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        var rootRect = (RectTransform)root.transform; rootRect.sizeDelta = new Vector2(width, height); rootRect.position = Vector3.zero;
        Texture2D shade = null, pixels = null;
        var previous = RenderTexture.active;
        try
        {
            // Khung 1080x1920 dời sao cho điểm (90,226) của thảm nằm ở góc trên-trái ảnh.
            var frame = RugOriginalView.Rect(root.transform, "Frame", -90, -226, 1080, 1920);
            RawImage Raw(string name, Rect r, Texture t)
            {
                var image = RugOriginalView.Rect(frame, name, r.x, r.y, r.width, r.height).gameObject.AddComponent<RawImage>();
                image.texture = t; image.raycastTarget = false; return image;
            }
            Raw("Base", new Rect(60, 196, 960, 800), RugOriginalAssets.Texture($"base-{sourceLevel:000}"));
            // AO lúc chưa cạo ô nào, giống RugOriginalView.Refresh.
            shade = new Texture2D(level.cols, level.rows, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var ao = new Color32[level.cols * level.rows];
            for (int i = 0; i < ao.Length; i++) ao[i] = new Color32(42, 22, 8, 255);
            shade.SetPixels32(ao); shade.Apply(false);
            Raw("Shade", Constants.RugRect, shade).color = new Color(1, 1, 1, 0.36f);
            var clipping = RugOriginalView.Rect(frame, "Rounded pile clipping", 90, 226, 900, 740);
            foreach (bool shadows in new[] { true, false })
            {
                var pile = RugOriginalView.Rect(clipping, shadows ? "Shadows" : "Tufts", -90, -226, 1080, 1920).gameObject.AddComponent<RugPileGraphic>();
                pile.shadows = shadows; pile.drawUncut = true; pile.raycastTarget = false; pile.Bind(sim);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = uiLayer;

            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(false);
            string key = RugOriginalView.PileImageKey(sourceLevel);
            string path = "Assets/RugPlayable/Resources/RugMaster/OriginalUI/" + key + ".jpg";
            System.IO.File.WriteAllBytes(path, pixels.EncodeToJPG(92));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.None; importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            Debug.Log("PLAYABLE_PILE_BAKED: " + path + " (" + new System.IO.FileInfo(path).Length / 1024 + " KB)");
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (shade != null) UnityEngine.Object.DestroyImmediate(shade);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    public static void BakeScene()
    {
        EditorSceneManager.OpenScene("Assets/RugPlayable/Scenes/GamePlay.unity");
        BakeAssets();
    }
    public static void VerifyRuntime()
    {
        EditorSceneManager.OpenScene("Assets/RugPlayable/Scenes/GamePlay.unity");
        foreach (var image in UnityEngine.Object.FindObjectsOfType<Image>(true))
        {
            if (image.sprite != null && !AssetDatabase.Contains(image.sprite))
                throw new Exception("Nonpersistent sprite on " + image.name);
        }
        Debug.Log("PLAYABLE_SPRITE_ASSETS_OK: scene UI uses persistent imported sprites.");
        SessionState.SetBool("RugPlayable.Verify", true);
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void InstallRuntimeCheck()
    {
        if (!SessionState.GetBool("RugPlayable.Verify", false)) return;
        double started = EditorApplication.timeSinceStartup;
        bool failed = false;
        Application.logMessageReceived += (message, stack, type) =>
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - started < 6) return;
            EditorApplication.update -= check;
            SessionState.SetBool("RugPlayable.Verify", false);
            try
            {
                if (failed) throw new Exception("Runtime logged an error");
                var game = GameManager.Ins;
                var view = game.View;
                var panel = view.selector.transform.Find("Pixeland selector/Panel").GetComponent<Image>();
                if (panel.sprite == null || panel.type != Image.Type.Sliced || panel.color != Color.white)
                    throw new Exception("Trim selector frame missing");
                if (view.PresentationBlocksInput) throw new Exception("Intro did not finish");
                ValidatePipeAndMeter(view);
                foreach (int width in new[] {1, 4, 2})
                {
                    view.FindUI("Head hit target " + width).GetComponent<Button>().onClick.Invoke();
                    if (ControllerManager.Ins.HeadWidth != width) throw new Exception("Head UI failed");
                }
                var sim = LevelManager.Ins.Simulation;
                var color = sim.Cells.GroupBy(c => c.Color).OrderByDescending(g => g.Count()).First().Key;
                foreach (var cell in sim.Cells.Where(c => c.Color == color).Take(5)) sim.TryTrim(cell.Index);
                if (sim.TrimmedCount != 5) throw new Exception("Runtime trim failed");
                view.boosters[1].GetComponent<Button>().onClick.Invoke();
                if (BoosterManager.Ins.Sweeps != 1) throw new Exception("Sweep UI failed");
                for (int i = 0; i < 100; i++) sim.Tick(.02f);
                sim.ValidateEconomy();
                if (view.FindUI("Settings button") != null) throw new Exception("Settings still present");
                ValidateFiberBatches(view);
                if (System.Environment.GetCommandLineArgs().Contains("--rug-visual-qa"))
                {
                    var setMeter = typeof(RugOriginalView).GetMethod("SetMeter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    setMeter.Invoke(view, new object[] { 25 });
                    view.CaptureReferenceFrame(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "meter-25-fibers.png"), 540, 960);
                    setMeter.Invoke(view, new object[] { 100 });
                    view.CaptureReferenceFrame(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "meter-100-fibers.png"), 540, 960);
                }
                if (failed) throw new Exception("Interaction logged an error");
                Debug.Log("PLAYABLE_RUNTIME_OK: trim selector frame, intro, three head buttons, trim, sweep button, economy, no settings.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        };
        EditorApplication.update += check;
    }
    private static void ValidatePipeAndMeter(RugOriginalView view)
    {
        var texture = view.channel.texture;
        var downsampled = new Texture2D(256, 256);
        view.channel.texture = downsampled;
        view.EnsureRugFit();
        if (view.channel.rectTransform.sizeDelta != new Vector2(1043, 1071))
            throw new Exception("Pipe layout depends on texture resolution");
        view.channel.texture = texture;
        UnityEngine.Object.Destroy(downsampled);
        if (view.meter.GetComponentsInChildren<RectMask2D>(true).Length != 0)
            throw new Exception("Collection load still depends on RectMask2D");
        var fill = view.meterFill;
        if (fill.GetComponent<CanvasRenderer>() == null) throw new Exception("Meter renderer missing");
        var populate = typeof(RugMeterFillGraphic).GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null);
        foreach (float amount in new[] { 0f, .01f, .25f, .5f, 1f })
        {
            fill.SetFill(amount, fill.fillTexture);
            using (var helper = new VertexHelper())
            {
                populate.Invoke(fill, new object[] { helper });
                var vertices = new System.Collections.Generic.List<UIVertex>();
                helper.GetUIVertexStream(vertices);
                if (amount == 0 && vertices.Count != 0) throw new Exception("Empty meter has visible geometry");
                if (amount > 0)
                {
                    float expected = fill.rectTransform.rect.xMin + fill.rectTransform.rect.width * amount;
                    if (vertices.Count == 0 || Mathf.Abs(vertices.Max(v => v.position.x) - expected) > .01f)
                        throw new Exception("Incorrect collection load fill width at " + amount);
                }
            }
        }
        fill.SetFill(0, fill.fillTexture);
        Debug.Log("PLAYABLE_LAYOUT_OK: pipe remains 1043x1071 at 256px texture; fill geometry verified at 0/1/25/50/100 percent.");
    }
    private static void ValidateFiberBatches(RugOriginalView view)
    {
        var sim = LevelManager.Ins.Simulation;
        sim.Fibers.Clear();
        int count = Mathf.Min(80, sim.Capacity);
        for (int i = 0; i < count; i++)
        {
            float distance = RugSimulation.LoopLength * (i + .5f) / count;
            sim.Fibers.Add(new Fiber { Id = i + 10000, Color = sim.Definition.palette[0], Variant = i % 6,
                Phase = FiberPhase.Channel, Distance = distance, Position = RugSimulation.LoopPosition(distance),
                VisualScale = .15f, TwirlSpeed = 1, WanderPhase = i });
        }
        var refresh = typeof(RugOriginalView).GetMethod("RefreshFibers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        refresh.Invoke(view, null);
        var roots = new[] { view.fibersRoot, view.dropsRoot, view.flyersRoot };
        int graphics = roots.Sum(r => r.GetComponentsInChildren<Graphic>(true).Length);
        int quads = view.pipeFiberBatch.QuadCount + view.dropFiberBatch.QuadCount + view.flyingFiberBatch.QuadCount;
        if (roots.Sum(r => r.GetComponentsInChildren<CanvasRenderer>(true).Length) != 3) throw new Exception("Fiber renderers missing");
        if (graphics != 3 || quads < count) throw new Exception("Fiber batching failed");
        var batchMesh = typeof(RugFiberBatchGraphic).GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null);
        using (var helper = new VertexHelper())
        {
            batchMesh.Invoke(view.pipeFiberBatch, new object[] { helper });
            if (helper.currentVertCount != view.pipeFiberBatch.QuadCount * 4) throw new Exception("Incomplete fiber batch mesh");
        }
        Debug.Log("PLAYABLE_FIBER_BATCH_OK: logicalFibers=" + count + " visiblePuffs=" + quads + " UI renderers=" + graphics);
    }
    [MenuItem("Rug Playable/Rebuild Gameplay Scene")]
    public static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Rug Playable");
        root.AddComponent<GameManager>();
        // Persist the empty scene before baking the asset library used by hierarchy generation.
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/RugPlayable/Scenes/GamePlay.unity");
        BakeAssets();
        var view = root.AddComponent<RugOriginalView>();
        view.BuildHierarchy();
        var text = Resources.Load<TextAsset>(Constants.LevelResource(1));
        view.Bind(new RugSimulation(JsonUtility.FromJson<LevelDefinition>(text.text)), 1);
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);
        camera.GetComponent<Camera>().orthographic = true;
        const string path = "Assets/RugPlayable/Scenes/GamePlay.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        AssetDatabase.SaveAssets();
        ValidateSimulation();
        Debug.Log("PLAYABLE_EXPORT_READY: scene saved; simulation, trim, boosters verified.");
    }
    private static void ValidateSimulation()
    {
        var text = Resources.Load<TextAsset>(Constants.LevelResource(1));
        var sim = new RugSimulation(JsonUtility.FromJson<LevelDefinition>(text.text));
        sim.ValidateEconomy();
        if (sim.Cells.Length == 0) throw new Exception("Empty rug");
        var color = sim.Cells.GroupBy(c => c.Color).OrderByDescending(g => g.Count()).First().Key;
        foreach (var cell in sim.Cells.Where(c => c.Color == color).Take(5))
            if (!sim.TryTrim(cell.Index)) throw new Exception("Trim failed");
        if (sim.TrimmedCount != 5) throw new Exception("Trim count mismatch");
        sim.Sweep(); sim.Rearrange();
        for (int i = 0; i < 100; i++) sim.Tick(.02f);
        sim.ValidateEconomy();
    }
}
