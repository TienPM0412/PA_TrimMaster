using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RugMaster
{
    public enum PlayState { Playing, Jammed, Won }
    public enum FiberPhase { Flight, Channel, Capture }

    public sealed class Cell
    {
        public int Index, X, Y, Clip = -1, Ribbon = -1;
        public string Color;
        public bool Trimmed;
        public float TrimmedAt=-100,WobbleAt=-100,WobbleLockUntil=-100,WobbleRotation,WobbleStrength;
    }

    public sealed class Basket
    {
        public int Id, Column, Capacity, Collected, Reserved, Tied;
        public string Color;
        public bool Revealed, Completing;
        public float CompleteTimer;
        public float LandedAt=-100;
        public int Remaining => Capacity - Collected;
        public int Available => Capacity - Collected - Reserved;
    }

    public sealed class Fiber
    {
        public int Id, SourceCell;
        public string Color;
        public FiberPhase Phase;
        public float Age, Distance, Duration;
        public Basket Target;
        public Vector2 CaptureFrom;
        public Vector2 CaptureTo,Position,Start,PopTo,FlyFrom;
        public float FlyDuration,Dy,Rotation,VisualScale,WanderPhase,TwirlSpeed=1,Spin,ChannelDropY;
        // Hệ số tốc độ trong ống (RugSimulation.SpaceChannel) để các sợi tự giãn đều quanh vòng ống.
        public float SpeedScale=1;
        public int FlightStage,Variant;
        public bool Sweep;
    }

    public sealed class Ribbon
    {
        public int[] Cells;
        public bool Resolved;
        public float Timer = -1;
    }

    // Vị trí tấm thảm trên màn hình. Mọi đồ hoạ/toạ độ của thảm vẫn tính trong "toạ độ thảm" gốc
    // (khung thảm + viền 60,196 960x800; ô cắt 94,230 892x732), rồi thu nhỏ Scale và dời Origin
    // để nằm gọn trong ống bao quanh, dưới thanh Collection load: màn hình = Origin + Scale * toạ độ thảm.
    public static class RugFit
    {
        // Lòng ống: x 136.0.944, y 323.0.1090. Thanh load 500x72 ở y 333.0.405 (RugOriginalView.MeterAnchor),
        // khung thảm 960x800 * Scale = 797x664 căn giữa ngang (x 142.0.938), y Top..Top+664 = 415.0.1079.
        public const float Scale = 0.83f;
        public const float Top = 415f;
        public static readonly Vector2 Origin = new Vector2(540 - (60 + 480) * Scale, Top - 196 * Scale);
        public static Vector2 ToScreen(Vector2 rug) => Origin + rug * Scale;
        public static Vector2 ToRug(Vector2 screen) => (screen - Origin) / Scale;
    }

    /// <summary>
    /// Tốc độ chảy trong ống theo vị trí trên vòng: đoạn thẳng chạy nhanh (Cruise), sắp tới góc ngoặt thì phanh lại,
    /// trong góc chậm nhất (MinFlow, chậm thêm chút ở giữa góc như va vào thành ống), ra khỏi góc thì nhanh dần (ease-in).
    /// Mọi sợi cùng một hồ sơ tốc độ nên tự dồn lại ở góc rồi giãn ra ở đoạn thẳng.
    /// Tau = "thời gian chạy" quy ra quãng đường (∫ds/Flow): sợi cách đều theo Tau (SpaceChannel) thì dồn đúng chỗ chậm.
    /// </summary>
    public static class RugPipeFlow
    {
        public const float Cruise = 1.3f, MinFlow = 0.42f, Approach = 110f, Exit = 260f, MidCornerDip = 0.12f;
        private const int Samples = 2048;
        private static readonly float Arc = Mathf.PI / 2 * RugSimulation.Radius;
        private static readonly float[] CornerStart;
        private static readonly float[] table = new float[Samples + 1];
        public static readonly float TauLength;

        static RugPipeFlow()
        {
            float b = RugSimulation.BottomLength, side = RugSimulation.SideLength;
            CornerStart = new[] { b, b + Arc + side, 2 * b + 2 * Arc + side, 2 * b + 3 * Arc + 2 * side };
            float step = RugSimulation.LoopLength / Samples;
            for (int i = 0; i < Samples; i++) table[i + 1] = table[i] + step / Flow((i + 0.5f) * step);
            TauLength = table[Samples];
        }

        /// <summary>Hệ số tốc độ tại quãng đường distance (1 = RugSimulation.Speed).</summary>
        public static float Flow(float distance)
        {
            float s = Mathf.Repeat(distance, RugSimulation.LoopLength), v = Cruise;
            for (int k = 0; k < 4; k++) v = Mathf.Min(v, CornerFlow(s, CornerStart[k]));
            return v;
        }

        /// <summary>0 trên đoạn thẳng, 1 khi đang chậm nhất trong góc: dùng cho rung lắc/va thành ống.</summary>
        public static float CornerIntensity(float distance) => Mathf.Clamp01((Cruise - Flow(distance)) / (Cruise - MinFlow));

        private static float CornerFlow(float s, float start)
        {
            float loop = RugSimulation.LoopLength;
            float into = Mathf.Repeat(s - start, loop);
            if (into <= Arc) return MinFlow * (1 - MidCornerDip * Mathf.Sin(Mathf.PI * into / Arc));
            float after = into - Arc;
            if (after <= Exit) { float k = after / Exit; return Mathf.Lerp(MinFlow, Cruise, k * k); }
            float before = Mathf.Repeat(start - s, loop);
            if (before <= Approach) { float k = 1 - before / Approach; return Mathf.Lerp(Cruise, MinFlow, k * k * (3 - 2 * k)); }
            return Cruise;
        }

        /// <summary>Tau của quãng đường chưa quấn vòng (mỗi vòng cộng TauLength).</summary>
        public static float Tau(float distance)
        {
            float loop = RugSimulation.LoopLength;
            int lap = Mathf.FloorToInt(distance / loop);
            float x = (distance - lap * loop) / loop * Samples;
            int i = (int)x; if (i >= Samples) i = Samples - 1; if (i < 0) i = 0;
            return lap * TauLength + table[i] + (table[i + 1] - table[i]) * (x - i);
        }

        /// <summary>Ngược của Tau.</summary>
        public static float DistanceAt(float tau)
        {
            int lap = Mathf.FloorToInt(tau / TauLength);
            float r = tau - lap * TauLength;
            int lo = 0, hi = Samples;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (table[mid] <= r) lo = mid; else hi = mid; }
            float span = table[lo + 1] - table[lo];
            float t = span > 0 ? (r - table[lo]) / span : 0;
            return lap * RugSimulation.LoopLength + (lo + t) * RugSimulation.LoopLength / Samples;
        }
    }

    // Rules ported from payload-script-6.js. No WebView, SDK or network is involved.
    public sealed class RugSimulation
    {
        // Ống bao quanh tấm thảm: channel-back.png 1043x1071, góc trên-trái ở (PipeImageLeft, PipeImageTop) trong toạ độ
        // thiết kế 1080x1920 (đáy ống giữ ở y 1275 để miệng xả thẳng hàng giỏ). Số đo trong ảnh: đường tâm ống
        // trái x 59.5, phải x 984, trên y 61, đáy y 943.5; miệng hút tâm x 782.5, mép dưới y 186.
        // Đường tâm ống là hình chữ nhật bo góc; sợi chạy theo chiều kim đồng hồ:
        // đáy (phải -> trái, đi qua 3 miệng xả) -> cạnh trái (lên) -> cạnh trên (trái -> phải, qua miệng hút) -> cạnh phải (xuống).
        public const float PipeImageLeft = 18.5f, PipeImageTop = 204f;
        public const float PipeLeft = PipeImageLeft + 59.5f, PipeRight = PipeImageLeft + 984f, PipeTop = PipeImageTop + 61f,
            PipeBottom = PipeImageTop + 943.5f, Radius = 72f, Speed = 700f;
        public const float BottomLength = PipeRight - PipeLeft - 2 * Radius, SideLength = PipeBottom - PipeTop - 2 * Radius;
        public static readonly float LoopLength = 2 * BottomLength + 2 * SideLength + 2 * Mathf.PI * Radius;
        public static readonly float[] BasketX = { 226.44f, 540f, 853.56f };
        // Tâm 3 miệng xả ở đáy ống (đo từ channel-back.png).
        public static readonly float[] OutletX = { 235.5f, 541f, 846.5f };
        // Giãn đều: sợi nhanh lên khi khoảng trống phía trước lớn hơn LoopLength/số sợi, chậm lại khi nhỏ hơn.
        public const float SpacingGain = 1f, MaxSpacingChange = 0.5f;
        private readonly List<Fiber> lane = new List<Fiber>();
        public readonly LevelDefinition Definition;
        public readonly Cell[] Cells;
        public readonly List<Basket>[] Columns = { new List<Basket>(), new List<Basket>(), new List<Basket>() };
        public readonly List<Fiber> Fibers = new List<Fiber>();
        public readonly Ribbon[] Ribbons;
        public readonly int[] Clips;
        public readonly int[] ColumnBusy = new int[3];
        public PlayState State = PlayState.Playing;
        public bool PointerHeld;
        /// <summary>Tutorial: bông chỉ chạy vòng trong ống, chưa rơi xuống rổ ở các miệng ống.</summary>
        public bool HoldCapture;
        public int TrimmedCount, CompletedBaskets, PeakLoad;
        public float Elapsed, JamTimer;
        public float TrimVisualUntil=-100;
        public string Notice = "";
        public Action<Cell> OnTrim;
        public Action<Basket> OnBasketCompleted;
        public Action<Fiber> OnFiberLanded;
        public Action OnWon, OnJammed;
        private int nextFiber;
        private readonly HashSet<int> cutSeen = new HashSet<int>();
        private readonly System.Random motionRandom;
        private float Random01()=>(float)motionRandom.NextDouble();
        private readonly List<Release> releases = new List<Release>();
        private struct Release { public int Column; public float Remaining; }
        public int ContinueCount { get; private set; }
        private int extraCapacity;
        public int Capacity => Definition.channelCapacity + extraCapacity;

        public int ContinuePrice(int basePrice)
        {
            long price = Math.Max(1, basePrice);
            for (int i = 0; i < ContinueCount && price < int.MaxValue; i++)
                price = Math.Min(int.MaxValue, price * 2);
            return (int)price;
        }

        // Each continue adds 50% of the original capacity for this attempt only.
        public bool ExpandTube()
        {
            if (State != PlayState.Jammed) return false;
            extraCapacity += Math.Max(1, (int)Math.Ceiling(Definition.channelCapacity * 0.5));
            ContinueCount++;
            State = PlayState.Playing;
            JamTimer = 0;
            PointerHeld = false;
            Notice = "Tube expanded!";
            return true;
        }
        public int Upc => Math.Max(1, Definition.unitsPerCell);
        public bool Full => Fibers.Count + Upc > Capacity;
        public bool HasPending
        {
            get
            {
                if(releases.Count>0)return true;
                for(int i=0;i<ColumnBusy.Length;i++)if(ColumnBusy[i]>0)return true;
                for(int i=0;i<Fibers.Count;i++)if(Fibers[i].Phase!=FiberPhase.Channel)return true;
                for(int i=0;i<Ribbons.Length;i++)if(Ribbons[i].Timer>=0&&!Ribbons[i].Resolved)return true;
                return false;
            }
        }

        public RugSimulation(LevelDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            motionRandom=new System.Random(83175+definition.index*977);
            if (definition.cols < 1 || definition.rows < 1 || definition.grid.Length != definition.rows ||
                definition.channelCapacity < Upc) throw new ArgumentException("Invalid level dimensions/capacity");
            Cells = new Cell[definition.cols * definition.rows];
            for (int y = 0; y < definition.rows; y++)
            {
                if (definition.grid[y].Length != definition.cols) throw new ArgumentException("Grid row width mismatch");
                for (int x = 0; x < definition.cols; x++)
                {
                    int p = "0123456789abcdefghijklmnopqrstuvwxyz".IndexOf(definition.grid[y][x]);
                    if (p < 0 || p >= definition.palette.Length) throw new ArgumentException("Invalid palette index");
                    int i = y * definition.cols + x;
                    Cells[i] = new Cell { Index = i, X = x, Y = y, Color = definition.palette[p] };
                }
            }
            Clips = (definition.clips ?? new ClipDefinition[0]).Select(c => c.count).ToArray();
            for (int i = 0; i < Clips.Length; i++)
                foreach (var c in Cells.Where(c => InRect(c, definition.clips[i].rect))) c.Clip = i;
            Ribbons = (definition.ribbons ?? new RegionDefinition[0]).Select(r => new Ribbon {
                Cells = Cells.Where(c => InRect(c, r.rect)).Select(c => c.Index).ToArray()
            }).ToArray();
            for (int i = 0; i < Ribbons.Length; i++)
                foreach (int c in Ribbons[i].Cells) Cells[c].Ribbon = i;
            int id = 0;
            foreach (var b in definition.baskets)
            {
                if (b.column < 0 || b.column > 2 || b.capacity <= 0 || b.tied >= Ribbons.Length)
                    throw new ArgumentException("Invalid basket");
                Columns[b.column].Add(new Basket { Id = id++, Column = b.column, Color = b.color,
                    Capacity = b.capacity, Tied = b.tied, Revealed = !b.hidden });
            }
            for (int c = 0; c < 3; c++) if (Front(c) != null) Front(c).Revealed = true;
            ValidateEconomy();
        }

        private bool InRect(Cell c, float[] r)
        {
            float x = (c.X + 0.5f) / Definition.cols, y = (c.Y + 0.5f) / Definition.rows;
            return x >= r[0] && y >= r[1] && x <= r[2] && y <= r[3];
        }

        public Basket Front(int column) => Columns[column].Count == 0 ? null : Columns[column][0];
        public Basket EligibleFront(int column)
        {
            var b = Front(column);
            return ColumnBusy[column] == 0 && b != null && b.Tied < 0 && b.Revealed && !b.Completing ? b : null;
        }
        public bool Blocked(Cell c) => c.Clip >= 0 && Clips[c.Clip] > 0;

        public bool TryTrim(int index)
        {
            if (State != PlayState.Playing || index < 0 || index >= Cells.Length) return false;
            var c = Cells[index];
            if (c.Trimmed || Blocked(c) || Full) return false;
            c.Trimmed = true;
            c.TrimmedAt=Elapsed;
            TrimVisualUntil=Elapsed+0.44f;
            for(int dr=-2;dr<=2;dr++)for(int dc=-2;dc<=2;dc++)
            {
                int x=c.X+dc,y=c.Y+dr;
                if((dc==0&&dr==0)||x<0||y<0||x>=Definition.cols||y>=Definition.rows)continue;
                var neighbor=Cells[y*Definition.cols+x];
                if(neighbor.Trimmed||neighbor.WobbleLockUntil>Elapsed)continue;
                int ring=Math.Max(Math.Abs(dc),Math.Abs(dr));float strength=ring==1?1:0.4f;
                neighbor.WobbleLockUntil=Elapsed+0.22f;neighbor.WobbleAt=Elapsed+(ring==1?0:0.05f);
                neighbor.WobbleStrength=strength;
                neighbor.WobbleRotation=(dc==0?Random01()-0.5f:(dc > 0 ? 1 : dc < 0 ? -1 : 0))*0.16f*strength;
            }
            TrimmedCount++;
            for (int u = 0; u < Upc; u++)
            {
                var start=RugFit.ToScreen(new Vector2(94+(c.X+0.5f)*892/Definition.cols,230+(c.Y+0.5f)*732/Definition.rows));
                Fibers.Add(new Fiber { Id=nextFiber++,SourceCell=index,Color=c.Color,Phase=FiberPhase.Flight,
                    Position=start,Start=start,PopTo=start+new Vector2((Random01()-0.5f)*70,-40-Random01()*30),
                    FlyDuration=0.45f+Random01()*0.2f,Dy=(Random01()-0.5f)*22,Variant=Math.Min(5,(int)(Random01()*6)),
                    Rotation=Random01()*6,WanderPhase=Random01()*60,TwirlSpeed=0.8f+Random01()*0.5f,
                    Spin=(Random01()<0.5f?-1:1)*(3+Random01()*3) });
            }
            PeakLoad = Math.Max(PeakLoad, Fibers.Count);
            for (int i = 0; i < Ribbons.Length; i++)
            {
                var r = Ribbons[i];
                if (!r.Resolved && r.Timer < 0 && r.Cells.All(k => Cells[k].Trimmed)) r.Timer = 0.8f;
            }
            OnTrim?.Invoke(c);
            return true;
        }

        // Coordinates are local to the rug: x/y in [0,1]. Returns all cuts along the sampled blade path.
        public int CutSegment(Vector2 a, Vector2 b, int width, ref int horizontalDirection)
        {
            float mx = (b.x - a.x) * 892, my = (b.y - a.y) * 732;
            if (Mathf.Abs(mx) > 0.5f && Mathf.Abs(mx) >= Mathf.Abs(my) * 0.35f) horizontalDirection = mx > 0 ? 1 : -1;
            else if (Mathf.Abs(my) > 0.5f) horizontalDirection = 0;
            int samples = Math.Max(1, Mathf.CeilToInt(new Vector2(mx, my).magnitude /
                (Mathf.Min(892f / Definition.cols, 732f / Definition.rows) * 0.4f)));
            var seen = cutSeen;
            seen.Clear();
            int cuts = 0;
            for (int k = 0; k <= samples; k++)
            {
                var p = Vector2.Lerp(a, b, (float)k / samples);
                int row = Mathf.FloorToInt(p.y * Definition.rows);
                if (row < 0 || row >= Definition.rows) continue;
                float u = p.x * Definition.cols;
                int c0 = horizontalDirection > 0 ? Mathf.FloorToInt(u + width / 2f) - width + 1 :
                    horizontalDirection < 0 ? Mathf.FloorToInt(u - width / 2f) : Mathf.FloorToInt(u - width / 2f + 0.5f);
                for (int x = Math.Max(0, c0); x < Math.Min(Definition.cols, c0 + width); x++)
                {
                    int i = row * Definition.cols + x;
                    if (!seen.Add(i) || Cells[i].Trimmed) continue;
                    if (Blocked(Cells[i])) { Notice = "Fill baskets to release the clips"; continue; }
                    if (Full) { Notice = "Channel full - wait for matching baskets"; return cuts; }
                    if (TryTrim(i)) cuts++;
                }
            }
            return cuts;
        }

        // distance = 0 ở đầu phải của đoạn đáy; dy > 0 lệch ra phía ngoài vòng ống.
        public static Vector2 LoopPosition(float distance,float dy=0)
        {
            float s = Mathf.Repeat(distance, LoopLength), arc = Mathf.PI / 2 * Radius, r = Radius + dy;
            float left = PipeLeft + Radius, right = PipeRight - Radius, top = PipeTop + Radius, bottom = PipeBottom - Radius;
            if (s < BottomLength) return new Vector2(right - s, PipeBottom + dy);
            s -= BottomLength;
            if (s < arc) return Arc(left, bottom, Mathf.PI / 2 + s / Radius, r);
            s -= arc;
            if (s < SideLength) return new Vector2(PipeLeft - dy, bottom - s);
            s -= SideLength;
            if (s < arc) return Arc(left, top, Mathf.PI + s / Radius, r);
            s -= arc;
            if (s < BottomLength) return new Vector2(left + s, PipeTop - dy);
            s -= BottomLength;
            if (s < arc) return Arc(right, top, -Mathf.PI / 2 + s / Radius, r);
            s -= arc;
            if (s < SideLength) return new Vector2(PipeRight + dy, top + s);
            s -= SideLength;
            return Arc(right, bottom, s / Radius, r);
        }
        private static Vector2 Arc(float cx, float cy, float angle, float r) =>
            new Vector2(cx + Mathf.Cos(angle) * r, cy + Mathf.Sin(angle) * r);
        // Quãng đường trên vòng ống ứng với điểm x trên đoạn ống phía trên (nơi miệng hút đổ sợi vào).
        public static float TopDistance(float x) => BottomLength + SideLength + Mathf.PI * Radius + (x - PipeLeft - Radius);

        public void Tick(float delta)
        {
            // A bounded substep preserves crossing/capture when a frame stalls or simulation runs fast.
            int count = Math.Max(1, Mathf.CeilToInt(delta / (1f / 60)));
            for (int i = 0; i < count; i++) Step(delta / count);
        }

        private void Step(float dt)
        {
            if (State != PlayState.Playing) return;
            Elapsed += dt;
            for (int i = releases.Count - 1; i >= 0; i--)
            {
                var r = releases[i]; r.Remaining -= dt;
                if (r.Remaining <= 0) { ColumnBusy[r.Column]--; releases.RemoveAt(i); }
                else releases[i] = r;
            }
            foreach (var r in Ribbons)
            {
                if (r.Resolved || r.Timer < 0) continue;
                r.Timer -= dt;
                if (r.Timer > 0) continue;
                r.Resolved = true;
                int ri = Array.IndexOf(Ribbons, r);
                foreach (var b in Columns.SelectMany(c => c)) if (b.Tied == ri) b.Tied = -1;
                Notice = "Ribbon untied!";
            }
            for (int column = 0; column < 3; column++)
                for (int p = Columns[column].Count - 1; p >= 0; p--)
                {
                    var b = Columns[column][p];
                    if (!b.Completing) continue;
                    b.CompleteTimer -= dt;
                    if (b.CompleteTimer > 0) continue;
                    bool wasFront = p == 0;
                    Columns[column].RemoveAt(p);
                    var next = Front(column);
                    float revealDelay = wasFront && next != null && !next.Revealed ? 0.45f : 0;
                    if (wasFront && next != null) next.Revealed = true;
                    releases.Add(new Release { Column = column, Remaining = 0.42f + revealDelay });
                }
            SpaceChannel();
            for (int i = Fibers.Count - 1; i >= 0; i--)
            {
                var f = Fibers[i];var before=f.Phase;
                float previousX=before==FiberPhase.Channel?LoopPosition(f.Distance).x:0;
                RugFiberMotion.Advance(f,dt,Elapsed);
                if (before == FiberPhase.Capture)
                {
                    if (f.Age < f.Duration) continue;
                    var b = f.Target;
                    b.Reserved--; b.Collected++;
                    b.LandedAt=Elapsed;OnFiberLanded?.Invoke(f);
                    Fibers.RemoveAt(i);
                    if (b.Collected == b.Capacity && b.Reserved == 0) Complete(b);
                }
                else if(before==FiberPhase.Channel)
                {
                    var position=LoopPosition(f.Distance);
                    if(HoldCapture||Mathf.Repeat(f.Distance,LoopLength)>=BottomLength)continue;
                    for (int c = 0; c < 3; c++)
                    {
                        if (!(previousX>OutletX[c]&&position.x<=OutletX[c])) continue;
                        var b = EligibleFront(c);
                        if (b == null || b.Color != f.Color || b.Available < 1) continue;
                        Capture(f, b, 0.3f, new Vector2(OutletX[c],f.ChannelDropY));
                        break;
                    }
                }
            }
            Evaluate(dt);
        }

        // Giãn đều các sợi trong ống: sắp theo vị trí trên vòng (giữ nguyên thứ tự, không vượt nhau), tính bộ vị trí
        // cách đều LoopLength/n gần nhất với đoàn hiện tại, rồi mỗi sợi nhanh/chậm lại để tiến về vị trí của mình.
        // Đầy 100% thì đoàn sợi phủ kín cả vòng ống. Sợi không bao giờ dừng hẳn.
        private void SpaceChannel()
        {
            lane.Clear();
            for (int i = 0; i < Fibers.Count; i++) if (Fibers[i].Phase == FiberPhase.Channel) lane.Add(Fibers[i]);
            int n = lane.Count;
            if (n == 0) return;
            if (n == 1) { lane[0].SpeedScale = 1; return; }
            // Insertion sort: thứ tự gần như không đổi giữa các bước nên gần O(n), và không cấp phát.
            for (int i = 1; i < n; i++)
            {
                var f = lane[i]; int j = i - 1;
                while (j >= 0 && LaneAfter(lane[j], f)) { lane[j + 1] = lane[j]; j--; }
                lane[j + 1] = f;
            }
            // Cách đều theo Tau (thời gian chạy), không theo quãng đường: sợi tự dồn ở góc ngoặt (RugPipeFlow).
            float tauLength = RugPipeFlow.TauLength;
            float spacing = tauLength / n, origin = RugPipeFlow.Tau(Mathf.Repeat(lane[0].Distance, LoopLength)), offset = 0;
            for (int i = 0; i < n; i++) offset += LaneTau(lane[i], origin) - i * spacing;
            offset /= n;
            for (int i = 0; i < n; i++)
            {
                float error = offset + i * spacing - LaneTau(lane[i], origin);
                lane[i].SpeedScale = 1 + Mathf.Clamp(error / spacing * SpacingGain, -MaxSpacingChange, MaxSpacingChange);
            }
        }
        private static float LaneTau(Fiber f, float origin) =>
            Mathf.Repeat(RugPipeFlow.Tau(Mathf.Repeat(f.Distance, LoopLength)) - origin, RugPipeFlow.TauLength);
        // a nằm sau b trên vòng (tính từ điểm 0); cùng vị trí (vào ống cùng lúc) thì sợi vào trước (Id nhỏ) đứng phía trước.
        private static bool LaneAfter(Fiber a, Fiber b)
        {
            float x = Mathf.Repeat(a.Distance, LoopLength), y = Mathf.Repeat(b.Distance, LoopLength);
            return x != y ? x > y : a.Id < b.Id;
        }

        private void Capture(Fiber f, Basket b, float duration, Vector2 from)
        {
            b.Reserved++;
            f.Target = b; f.Phase = FiberPhase.Capture; f.Age = 0; f.Duration = duration; f.CaptureFrom = from;
            int p=Columns[b.Column].IndexOf(b);
            f.CaptureTo=new Vector2(BasketX[b.Column]+(p==0?(Random01()-0.5f)*130:0),p==0?1404:1696+(p-1)*132*0.92f-112);
        }

        private void Complete(Basket b)
        {
            if (b.Completing) return;
            b.Completing = true;
            b.CompleteTimer = Front(b.Column) == b ? 1.12f : 0.5f;
            ColumnBusy[b.Column]++;
            CompletedBaskets++;
            for (int i = 0; i < Clips.Length; i++) Clips[i] = Math.Max(0, Clips[i] - 1);
            Notice = "Packed!";
            OnBasketCompleted?.Invoke(b);
        }

        public bool AnyTrimmable(int released = 0, int clipReductions = 0)
        {
            if(Fibers.Count-released+Upc>Capacity)return false;
            for(int i=0;i<Cells.Length;i++)
            {
                var c=Cells[i];
                if(!c.Trimmed&&(c.Clip<0||Clips[c.Clip]-clipReductions<=0))return true;
            }
            return false;
        }

        private void Evaluate(float dt)
        {
            if (TrimmedCount == Cells.Length && Fibers.Count == 0 &&
                Columns[0].Count==0 && Columns[1].Count==0 && Columns[2].Count==0 && !HasPending)
            { State = PlayState.Won; OnWon?.Invoke(); return; }
            if (PointerHeld || HasPending || AnyTrimmable()) { JamTimer = 0; return; }
            // Only check matching baskets when the cheaper conditions cannot keep play alive.
            for(int c=0;c<3;c++)
            {
                var b=EligibleFront(c);
                if(b==null||b.Available<=0)continue;
                for(int i=0;i<Fibers.Count;i++)
                    if(Fibers[i].Color==b.Color){JamTimer=0;return;}
            }
            JamTimer += dt;
            if (JamTimer >= 1f) { State = PlayState.Jammed; OnJammed?.Invoke(); }
        }

        private List<KeyValuePair<Fiber, Basket>> PlanSweep(bool force)
        {
            var plan = new List<KeyValuePair<Fiber, Basket>>();
            var added = new Dictionary<Basket, int>();
            foreach (var f in Fibers.Where(f => f.Phase != FiberPhase.Capture))
            {
                Basket target = null;
                for (int pass = 0; pass < 2 && target == null; pass++)
                    foreach (var b in Columns.SelectMany(c => c))
                    {
                        int reserved = added.TryGetValue(b, out int value) ? value : 0;
                        if (b.Completing || (!force && b.Tied >= 0) || b.Color != f.Color ||
                            (Front(b.Column) == b && ColumnBusy[b.Column] > 0) ||
                            b.Available - reserved <= 0 || (pass == 0 && !b.Revealed)) continue;
                        target = b; break;
                    }
                if (target == null) continue;
                added[target] = (added.TryGetValue(target, out int n) ? n : 0) + 1;
                plan.Add(new KeyValuePair<Fiber, Basket>(f, target));
            }
            int completes = added.Count(p => p.Value >= p.Key.Available);
            int outstanding = Fibers.Count(f => f.Phase != FiberPhase.Capture);
            if (!force && !AnyTrimmable(plan.Count, completes) &&
                !(TrimmedCount == Cells.Length && plan.Count == outstanding)) plan.Clear();
            return plan;
        }

        public bool Sweep(bool force = false)
        {
            if (State == PlayState.Won) return false;
            var plan = PlanSweep(force);
            if (plan.Count == 0) { Notice = "No useful sweep yet"; return false; }
            foreach (var pair in plan)
            {
                var f = pair.Key; var b = pair.Value;
                b.Revealed = true;
                Vector2 from = f.Position;
                Capture(f, b, 0.55f + (f.Id % 10) * 0.06f, from);
                f.Sweep=true;
            }
            State = PlayState.Playing; JamTimer = 0;
            Notice = "Sweep assist";
            return true;
        }

        public bool Rearrange()
        {
            if (State == PlayState.Won) return false;
            var need = new Dictionary<string, int>();
            foreach (var f in Fibers.Where(f => f.Phase != FiberPhase.Capture))
                need[f.Color] = (need.TryGetValue(f.Color, out int n) ? n : 0) + 1;
            if (need.Count == 0)
                foreach (var c in Cells.Where(c => !c.Trimmed && !Blocked(c)))
                    need[c.Color] = (need.TryGetValue(c.Color, out int n) ? n : 0) + 1;
            var fronts = new HashSet<string>(Enumerable.Range(0, 3).Select(EligibleFront).Where(b => b != null).Select(b => b.Color));
            var used = new HashSet<int>();
            bool changed = false;
            for (int pass = 0; pass < 3; pass++)
                for (int k = 0; k < 3; k++)
                {
                    var f = Front(k);
                    if (f == null || used.Contains(f.Id) || ColumnBusy[k] > 0 || f.Collected != 0 || f.Reserved != 0 || f.Completing) continue;
                    if (pass == 0 && f.Tied < 0) continue;
                    if (pass == 1)
                    {
                        if (f.Tied >= 0 || f.Revealed || !need.ContainsKey(f.Color) || fronts.Contains(f.Color)) continue;
                        f.Revealed = true; used.Add(f.Id); fronts.Add(f.Color); changed = true; continue;
                    }
                    if (pass == 2 && (f.Tied >= 0 || (f.Revealed && need.ContainsKey(f.Color)))) continue;
                    Basket best = null; int bestColumn = -1, bestPosition = -1, weight = -1;
                    for (int c = 0; c < 3; c++)
                        for (int p = 1; p < Columns[c].Count; p++)
                        {
                            var b = Columns[c][p];
                            if (b.Tied >= 0 || b.Completing || b.Collected != 0 || b.Reserved != 0 || used.Contains(b.Id)) continue;
                            if (pass == 0 && b.Color != f.Color) continue;
                            if (pass == 2 && (!need.ContainsKey(b.Color) || fronts.Contains(b.Color))) continue;
                            int w = pass == 0 ? 10000 - c * 100 - p : need[b.Color] * 100 - p;
                            if (w <= weight) continue;
                            weight = w; best = b; bestColumn = c; bestPosition = p;
                        }
                    if (best == null) continue;
                    Columns[k][0] = best; Columns[bestColumn][bestPosition] = f;
                    f.Column = bestColumn; best.Column = k; best.Revealed = true;
                    used.Add(f.Id); used.Add(best.Id); fronts.Add(best.Color); changed = true;
                }
            if (!changed) { Notice = "No useful rearrangement"; return false; }
            State = PlayState.Playing; JamTimer = 0; Notice = "Baskets rearranged";
            return true;
        }

        public bool Revive()
        {
            if (State != PlayState.Jammed) return false;
            if (Sweep(true) || Rearrange()) return true;
            foreach (var b in Columns.SelectMany(c => c))
                if (Fibers.Any(f => f.Color == b.Color)) b.Revealed = true;
            if (Sweep(true)) return true;
            State = PlayState.Playing; JamTimer = 0;
            return true;
        }

        public void ValidateEconomy()
        {
            var colors = Cells.Select(c => c.Color).Concat(Columns.SelectMany(c => c).Select(b => b.Color)).Distinct();
            foreach (var color in colors)
            {
                int remaining = Cells.Count(c => !c.Trimmed && c.Color == color) * Upc + Fibers.Count(f => f.Color == color);
                int demand = Columns.SelectMany(c => c).Where(b => b.Color == color).Sum(b => b.Capacity - b.Collected);
                if (remaining != demand) throw new InvalidOperationException($"L{Definition.index} {color}: supply {remaining} != demand {demand}");
            }
            if (Fibers.Count > Capacity) throw new InvalidOperationException("Channel overflow");
            if (Columns.SelectMany(c => c).Any(b => b.Collected < 0 || b.Reserved < 0 || b.Available < 0))
                throw new InvalidOperationException("Basket overbooked");
        }
    }
}
