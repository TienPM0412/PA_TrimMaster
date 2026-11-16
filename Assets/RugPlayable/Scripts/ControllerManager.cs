using UnityEngine;

namespace RugMaster
{
    /// <summary>
    /// Input của người chơi: chạm/chuột kéo dao cắt thảm, chọn đầu dao, phím tắt desktop.
    /// Tọa độ nội bộ là toạ độ thảm: khung thiết kế 1080x1920 (gốc trên-trái) trước khi RugFit thu nhỏ/dời thảm vào trong ống.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerManager : Singleton<ControllerManager>
    {
        private int head = 2, direction;
        private bool dragging, heldWasFull, toolPlaced;
        private float scale = 1, offsetX, offsetY, placing, toolAngle, lastCutAt = -100;
        private Vector2 blade, previous, visualBlade, placeFrom;

        public int HeadWidth => head;
        public bool IsDragging => dragging;
        public Vector2 BladePosition => blade;
        public Vector2 ToolPosition => visualBlade;
        public bool IsToolRaised => !toolPlaced || !dragging || placing > 0;
        public bool IsCutting => dragging && Simulation != null && Simulation.Elapsed - lastCutAt < 0.12f;
        public bool HasPlacedTool => toolPlaced;
        public float ToolAngle => toolAngle;
        private static RugSimulation Simulation => LevelManager.Ins.Simulation;

        private void Awake() { head = RugPrefs.HeadWidth; }
        private void OnEnable() { EventManager.AddListener<OnTrimEvent>(OnTrim); }
        private void OnDisable() { EventManager.RemoveListener<OnTrimEvent>(OnTrim); }
        private void OnTrim(OnTrimEvent e) { if (Simulation != null) lastCutAt = Simulation.Elapsed; }
        private void OnApplicationFocus(bool focus) { if (!focus) CancelPointer(); }

        public void ResetForLevel()
        {
            dragging = false;
            blade = new Vector2(540, 730);
            toolPlaced = false; toolAngle = -0.14f; visualBlade = new Vector2(880, 560); lastCutAt = -100;
        }

        public void SetHead(int width, bool persist = true)
        {
            head = width;
            if (persist) RugPrefs.HeadWidth = head;
        }

        public void UpdateLayout()
        {
            // Released tool stays where it was, gently tilts and stops its motor.
            if (!dragging) toolAngle = Mathf.Lerp(toolAngle, -0.14f, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 10));
            scale = Mathf.Min(Screen.width / 1080f, Screen.height / 1920f);
            offsetX = (Screen.width - 1080 * scale) * 0.5f;
            offsetY = (Screen.height - 1920 * scale) * 0.5f;
        }

        public void HandleShortcuts() {}

        public void HandlePointer()
        {
            bool touch = Input.touchCount > 0;
            var screen = touch ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
            // Đổi sang toạ độ thảm (RugFit): dao, vùng chạm và nét cắt đều tính trong toạ độ thảm gốc.
            Vector2 raw = RugFit.ToRug(new Vector2((screen.x - offsetX) / scale, (Screen.height - screen.y - offsetY) / scale));
            bool down = touch ? Input.GetTouch(0).phase == TouchPhase.Began : Input.GetMouseButtonDown(0);
            bool held = touch ? Input.GetTouch(0).phase != TouchPhase.Ended && Input.GetTouch(0).phase != TouchPhase.Canceled : Input.GetMouseButton(0);
            if (down) BeginTrimAt(raw);
            if (!dragging) return;
            if (!held || Simulation.State != PlayState.Playing) { CancelPointer(); return; }
            MoveTrimTo(raw, Time.unscaledDeltaTime);
        }

        /// <summary>Giữ dao khi kênh đầy: kênh vừa trống chỗ thì cắt tiếp ngay tại chỗ.</summary>
        public void AfterSimulationTick()
        {
            if (dragging && placing <= 0 && heldWasFull && !Simulation.Full) Cut(blade, blade);
            heldWasFull = Simulation.Full;
        }

        // Dùng chung cho input thật và replay nét cắt ghi sẵn. Tọa độ thảm (khung thiết kế trước khi RugFit thu nhỏ).
        public bool BeginTrimAt(Vector2 raw)
        {
            if (!new Rect(60, 196, 960, 987).Contains(raw) || Simulation.State != PlayState.Playing) return false;
            dragging = true; placing = 0.12f; direction = 0; placeFrom = visualBlade;
            blade = previous = ClampBlade(raw - new Vector2(0, 187)); Simulation.PointerHeld = true; toolPlaced = true;
            return true;
        }

        public void MoveTrimTo(Vector2 raw, float deltaTime)
        {
            if (!dragging) return;
            // 187 rug units places the head about 1.5 finger widths above the touch.
            Vector2 targetBlade = ClampBlade(raw - new Vector2(0, 187));
            Vector2 travel = targetBlade - blade;
            float distance = travel.magnitude;
            float maxStep = 1800f * Mathf.Clamp(deltaTime, 0, 0.05f);
            if (distance > 0) blade += travel * Mathf.Min(1, maxStep / distance);
            if (placing > 0)
            {
                placing = Mathf.Max(0, placing - deltaTime);
                visualBlade = Vector2.Lerp(placeFrom, blade, 1 - Mathf.Pow(placing / 0.12f, 3));
                if (placing == 0 && SoundManager.HasInstance) SoundManager.Ins.PlayPlace();
                previous = blade; return;
            }
            if ((blade - previous).sqrMagnitude > 0.01f)
            {
                Vector2 delta = blade - previous;
                float target = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 0.5f ? Mathf.Clamp(delta.x * 0.6f, -22, 22) * Mathf.Deg2Rad : 0;
                toolAngle += (target - toolAngle) * 0.3f;
                Cut(previous, blade);
            }
            previous = blade;
            visualBlade = blade;
        }

        public void EndTrim() => CancelPointer();

        public void CancelPointer()
        {
            dragging = false;
            if (LevelManager.HasInstance && Simulation != null) Simulation.PointerHeld = false;
        }

        private Vector2 BladeCutOffset()
        {
            string h = head == 1 ? "narrow" : head == 4 ? "wide" : "normal";
            var cut = RugOriginalAssets.Record("tool-" + h + "-held")?.cut;
            return cut != null && cut.Length == 2 ? new Vector2(cut[0], cut[1]) : Vector2.zero;
        }

        private Vector2 ClampBlade(Vector2 p)
        {
            var cut = BladeCutOffset();
            return new Vector2(Mathf.Clamp(p.x, 82, 998), Mathf.Clamp(p.y, 222 - cut.y, 970 - cut.y));
        }

        private void Cut(Vector2 a, Vector2 b)
        {
            var cut = BladeCutOffset(); a += cut; b += cut;
            var r = Constants.RugRect;
            a = new Vector2((a.x - r.x) / r.width, (a.y - r.y) / r.height);
            b = new Vector2((b.x - r.x) / r.width, (b.y - r.y) / r.height);
            Simulation.CutSegment(a, b, head, ref direction);
        }
    }
}
