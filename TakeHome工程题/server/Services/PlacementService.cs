using System;
using System.Collections.Generic;
using System.Linq;
using TakeHome.Geometry;
using TakeHome.Models;

namespace TakeHome.Services
{
    /// <summary>
    /// 核心装箱算法：正交多边形（房间轮廓）+ 轴对齐矩形（家具）。
    /// 策略：候选位置枚举（边界顶点坐标 × 已放物体边坐标，覆盖四种贴墙姿态）+ 回溯搜索 + 打分（优先贴墙）。
    /// </summary>
    public class PlacementService
    {
        /// <summary>冰箱开门边前方需要保留的净空深度（毫米），可配置。</summary>
        public double FridgeDoorClearance { get; set; } = 800.0;

        /// <summary>货架（shelf）正面（长边）前方需要保留的通道深度（毫米），保证人能拿到货。</summary>
        public double ShelfAisleClearance { get; set; } = 400.0;

        /// <summary>制冰机（iceMaker）正面（长边）前方需要保留的通道深度（毫米）。</summary>
        public double IceMakerAisleClearance { get; set; } = 400.0;

        // ---------- 输入解析后的状态字段 ----------
        private List<Vec2> _boundary;      // 房间轮廓多边形（顶点列表，首尾闭合）
        private Vec2 _doorA, _doorB;       // 门的两个端点
        private bool _isOpenInward;        // 是否为内开门（内开会占 N×N 摆动区）
        private double _doorWidth;         // 门宽 N（门线段长度）
        private int _doorSide;             // 门内侧方向：-1 = 较小坐标侧，+1 = 较大坐标侧
        private Rect? _doorClear;           // 门预留区（N×N，门洞向内 N 深；内开门即摆动区，外开门即通行区）
        private double _minX, _minY, _maxX, _maxY; // 轮廓包围盒（用于候选快速剔除）

        // ---------- 搜索过程中的状态 ----------
        private readonly List<PlacedState> _placed = new(); // 当前已放好的物体（回溯栈）
        private List<PlacedState> _best;                    // 目前找到的最优完整解
        private double _bestScore = double.NegativeInfinity;// 最优解的得分（越大越好）
        private double _bestBBox = double.PositiveInfinity; // 最优解的整体包围盒面积（越小越紧凑）
        private long _nodes;                                // 已探索的搜索节点数
        private const long NodeBudget = 500_000;            // 节点数上限（防止无限搜索）
        private readonly System.Diagnostics.Stopwatch _timer = new(); // 计时器
        private const long TimeBudgetMs = 90000;            // 每个请求最多搜索 90 秒（保证瘦长房型 example3 也能稳定找到完整解）

        // ============ 主入口 ============
        /// <summary>求解一次摆放。输入 JSON 解析后的对象，返回是否可行 + 每个物体的中心点/旋转角。</summary>
        public PlacementResult Place(PlacementInput input)
        {
            // 1. 解析输入：把 JSON 里的坐标数组转成 Vec2 点
            _boundary = input.Boundary.Select(p => new Vec2(p[0], p[1])).ToList();
            _doorA = new Vec2(input.Door[0][0], input.Door[0][1]);
            _doorB = new Vec2(input.Door[1][0], input.Door[1][1]);
            _isOpenInward = input.IsOpenInward;

            var result = new PlacementResult();

            // 2. 校验输入是否合法（非法直接返回原因）
            string err = Validate(input);
            if (err != null) { result.Feasible = false; result.Reason = err; return result; }

            // 3. 计算门相关几何信息
            _doorWidth = Dist(_doorA, _doorB);                                      // 门宽 N = 门线段长度
            _doorSide = DetermineInteriorSide(_doorA, _doorB, _boundary);           // 判断室内在门的哪一侧
            // 门预留区：内开门占 N×N 摆动区；外开门只留门洞向内 300mm 的通行区（避免堵门，但不需要 N 深）
            double doorClearDepth = _isOpenInward ? _doorWidth : Math.Min(_doorWidth, 300.0);
            _doorClear = BuildSwing(_doorA, _doorB, _doorSide, doorClearDepth);

            // 4. 计算轮廓包围盒，用于后续快速剔除明显在轮廓外的候选
            _minX = _boundary.Min(v => v.X); _minY = _boundary.Min(v => v.Y);
            _maxX = _boundary.Max(v => v.X); _maxY = _boundary.Max(v => v.Y);

            // 5. 构建并排序待摆放物体：冰箱最大且需开门净空，最先占位；离地架必须挂墙，其次；再按面积从大到小
            var items = BuildItems(input.AlgoToPlace);
            items.Sort((a, b) =>
            {
                int pa = TypePriority(a.Type), pb = TypePriority(b.Type);
                if (pa != pb) return pa.CompareTo(pb);
                int c = (b.Length * b.Width).CompareTo(a.Length * a.Width);
                return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            // 6. 重置搜索状态
            _placed.Clear(); //清空当前已经放置的家具列表。
            _best = null;    //清空最优解列表。
            _bestScore = double.NegativeInfinity; //重置最优解的得分（越大越好）
            _bestBBox = double.PositiveInfinity; //重置最优解的整体包围盒面积（越小越紧凑）
            _nodes = 0; //重置已探索的搜索节点数为 0
            _timer.Restart(); // 重新开始计时

            // 7. 开始回溯搜索
            Search(items, 0, 0.0);
            _timer.Stop();

            // 记录诊断信息（耗时、节点数）
            result.ElapsedMs = _timer.ElapsedMilliseconds;
            result.NodesExplored = _nodes;

            // 8. 根据搜索结果填充输出
            if (_best != null)
            {
                result.Feasible = true;                             // 找到了可行解
                result.Placements = _best.Select(ToDto).ToList();   // 转成 DTO 输出
            }
            else
            {
                result.Feasible = false;
                bool budgetHit = _nodes > NodeBudget || _timer.ElapsedMilliseconds > TimeBudgetMs;
                result.Reason = budgetHit
                    ? "在限定搜索预算内未找到可行方案（空间可能不足或约束过紧）"
                    : Diagnose(items);   // 未超预算则进一步诊断"为什么放不下"
            }

            // 9. 把门的预留区信息返回给前端（用于画图，所有门都有）
            result.IsOpenInward = _isOpenInward;
            if (_doorClear.HasValue)
            {
                result.SwingMinX = _doorClear.Value.MinX;
                result.SwingMinY = _doorClear.Value.MinY;
                result.SwingMaxX = _doorClear.Value.MaxX;
                result.SwingMaxY = _doorClear.Value.MaxY;
            }
            return result;
        }

        // ============ 回溯搜索（核心）============

        /// <summary>
        /// 深度优先回溯：把 items[index..] 依次放进去。
        /// index：当前要放的物体下标；score：已放物体累计得分。
        /// 返回是否找到了完整解（找到即提前返回 true，实现"可行性优先"）。
        /// </summary>
        //参数是要放入的下标跟得分
        private bool Search(List<ItemSpec> items, int index, double score)
        {
            // 搜索预算保护：节点数或时间超限就直接放弃本次搜索
            if (++_nodes > NodeBudget) return false;
            if (_timer.ElapsedMilliseconds > TimeBudgetMs) return false;

            if (index == items.Count)
            {
                // 所有物体都放完了 → 得到一个完整解
                double bbox = BBoxArea(_placed);   // 整体包围盒面积（衡量紧凑度）
                // 得分更高（贴墙更多），或得分相同但更紧凑时，更新最优解
                if (score > _bestScore || (Math.Abs(score - _bestScore) < 1e-9 && bbox < _bestBBox))
                {
                    _bestScore = score;
                    _bestBBox = bbox;
                    _best = _placed.Select(s => s.Clone()).ToList();
                }
                return true; // 找到完整解即返回（可行性优先，不做全局最优搜索）
            }

            // 枚举当前物体的所有候选位置（已按"贴墙得分"从高到低排序）
            foreach (var c in GenerateCandidates(items[index]))
            {
                _placed.Add(c.State);                                          // 放入该物体
                if (Search(items, index + 1, score + c.LocalScore)) return true; // 递归放下一件，成功则向上返回
                _placed.RemoveAt(_placed.Count - 1);                            // 回溯：撤销这次放置
            }
            return false; // 所有候选都试过仍放不下 → 本分支失败
        }

        // ============ 候选位置生成 ============

        /// <summary>为某个物体生成所有候选摆放位置（0° 和 90° 两个朝向）。</summary>
        private List<Candidate> GenerateCandidates(ItemSpec item)
        {
            var result = new List<Candidate>();
            var seen = new HashSet<(long, long)>();   // 用于去重：记录已经试过的"锚点"（左下角坐标）

            // 两个旋转角度：0°（长沿 X）和 90°（长沿 Y）
            foreach (var rot in new[] { 0.0, 90.0 })
            {
                double dx = rot == 0.0 ? item.Length : item.Width;  // 该朝向下的 x 方向尺寸
                double dy = rot == 0.0 ? item.Width : item.Length;  // 该朝向下的 y 方向尺寸

                // 候选坐标集合：
                //   1) 房间所有顶点的 x、y 坐标；
                //   2) 已放物体四条边的 x、y 坐标（便于靠紧已放物体摆放）。
                var xs = new HashSet<double>();
                var ys = new HashSet<double>();
                foreach (var v in _boundary) { xs.Add(v.X); ys.Add(v.Y); }
                foreach (var s in _placed)
                {
                    xs.Add(s.Rect.MinX); xs.Add(s.Rect.MaxX);
                    ys.Add(s.Rect.MinY); ys.Add(s.Rect.MaxY);
                }
                var xsList = xs.ToList(); xsList.Sort();   // 排序，保证候选遍历顺序确定（结果可复现）
                var ysList = ys.ToList(); ysList.Sort();

                // 枚举所有 (x, y) 组合，并尝试 4 种"锚点偏移"，覆盖四种贴边姿态：
                //   (x,y)            → 物体左下角对齐到 (x,y)
                //   (x-dx, y)        → 物体右边对齐到 x（右贴边）
                //   (x, y-dy)        → 物体上边对齐到 y（上贴边）
                //   (x-dx, y-dy)     → 物体右上角对齐到 (x,y)
                foreach (var x in xsList)
                {
                    foreach (var y in ysList)
                    {
                        AddCandidate(result, seen, item, rot, x, y, dx, dy);
                        AddCandidate(result, seen, item, rot, x - dx, y, dx, dy);
                        AddCandidate(result, seen, item, rot, x, y - dy, dx, dy);
                        AddCandidate(result, seen, item, rot, x - dx, y - dy, dx, dy);
                    }
                }
            }

            // 按"贴墙得分"从高到低排序；得分相同时按锚点坐标排序，保证顺序完全确定（可复现）
            result.Sort((a, b) =>
            {
                int c = b.LocalScore.CompareTo(a.LocalScore);
                if (c != 0) return c;
                c = a.State.Rect.MinX.CompareTo(b.State.Rect.MinX);
                if (c != 0) return c;
                return a.State.Rect.MinY.CompareTo(b.State.Rect.MinY);
            });
            return result;
        }

        /// <summary>尝试在锚点 (x,y) 放置尺寸为 (dx,dy) 的物体：去重 → 包围盒粗判 → 精确校验 → 打分。</summary>
        private void AddCandidate(List<Candidate> result, HashSet<(long, long)> seen,
            ItemSpec item, double rot, double x, double y, double dx, double dy)
        {
            // 锚点坐标量化到 0.001mm 作为去重键（避免同一位置重复生成）
            long kx = (long)Math.Round(x * 1000);
            long ky = (long)Math.Round(y * 1000);
            if (!seen.Add((kx, ky))) return;   // 已经试过这个锚点 → 跳过

            var r = new Rect(x, y, x + dx, y + dy);

            // 包围盒粗判：矩形完全在轮廓包围盒之外 → 直接剔除（省去精确计算）
            if (r.MaxX < _minX - Geo.Eps || r.MinX > _maxX + Geo.Eps || r.MaxY < _minY - Geo.Eps || r.MinY > _maxY + Geo.Eps)
                return;

            // 精确校验：是否放得下（不越界、不重叠、不挡门、满足挂墙/净空约束）
            if (!TryBuildState(item, rot, r, out var st)) return;

            // 计算该候选的局部得分：贴墙边数权重最大，其次贴合已放物体，贴两墙（靠角）再加分
            double local = st.WallTouch * 100.0 + st.Adjacency * 10.0 + (st.WallTouch >= 2 ? 20.0 : 0.0);
            result.Add(new Candidate { State = st, LocalScore = local });
        }

        // ============ 校验一个摆放是否合法 ============

        /// <summary>判断物体以 rot 角度、矩形 r 摆放是否合法；合法则输出构造好的 PlacedState。</summary>
        private bool TryBuildState(ItemSpec item, double rot, Rect r, out PlacedState st)
        {
            st = null;

            // 约束1：矩形必须完全在房间轮廓内（允许贴墙）
            if (!Geo.RectInsidePolygon(r, _boundary)) return false;

            // 约束2：不能遮挡门，也不能进入门洞向内预留的区域（内开门即摆动区，外开门即通行区）
            if (Geo.RectBlocksDoor(r, _doorA, _doorB)) return false;
            if (_doorClear.HasValue && Geo.RectsOverlap(r, _doorClear.Value, Geo.Eps)) return false;

            // 约束3：不能与已放物体重叠；也不能进入已放物体的"正面通道"（会挡住人取货/开门）
            foreach (var s in _placed)
            {
                if (Geo.RectsOverlap(r, s.Rect, Geo.Eps)) return false;
                if (s.FrontClearance.HasValue && Geo.RectsOverlap(r, s.FrontClearance.Value, Geo.Eps)) return false;
            }

            int wall = Geo.CountWallTouches(r, _boundary);   // 统计这个摆放贴了几条墙

            // 约束4：离地架（overShelf）必须挂墙（至少一条边贴墙）
            if (item.Type == "overShelf" && wall < 1) return false;

            // 约束5：需要"正面通道"的物体（冰箱/货架/制冰机）：
            //   确定正面（一条 length 边，必须朝向室内），并保证正面通道内没有别的物体（留出走道，人能接触）。
            Rect? clearance = null;
            string side = null;
            double clearanceDepth = GetAccessibleClearance(item.Type);
            if (clearanceDepth > 0)
            {
                side = ChooseFrontSide(r, rot);              // 确定正面朝向
                if (side == null) return false;              // 两条 length 边都朝墙 → 正面不可达，拒绝
                var (ea, eb, en) = GetFrontEdge(r, side);
                // 通道深度不超过正面到对面墙的实际距离，避免门/通道超出房间范围
                double depth = Math.Min(clearanceDepth, EdgeFreeDepth(ea, eb, en));
                var clr = BuildFrontClearance(r, side, depth);
                foreach (var s in _placed)
                {
                    if (Geo.RectsOverlap(clr, s.Rect, Geo.Eps)) return false; // 正面通道内不能有别的物体
                }
                clearance = clr;
            }

            // 构造该摆放的完整状态（供回溯栈记录）
            st = new PlacedState
            {
                Id = item.Id,
                Type = item.Type,
                Rect = r,
                Rotation = rot,
                Length = item.Length,
                Width = item.Width,
                FrontClearance = clearance,       // 正面通道（冰箱/货架/制冰机）
                FrontSide = side,                 // 正面朝向（top/bottom/left/right）
                WallTouch = wall,
                Adjacency = CountAdjacency(r),     // 与已放物体贴合的边数
            };
            return true;
        }

        // ============ 正面（可接触边）与通道 ============

        /// <summary>根据物体类型返回其"正面通道"深度（毫米）；返回 0 表示该类型无需正面通道。</summary>
        private double GetAccessibleClearance(string type) => type switch
        {
            "fridge" => FridgeDoorClearance,
            "shelf" => ShelfAisleClearance,
            "iceMaker" => IceMakerAisleClearance,
            _ => 0,   // 离地架（overShelf）长边挂墙后正面自然朝室内，无需额外通道
        };

        /// <summary>
        /// 确定物体的"正面"朝向（一条 length 边，长度为第一个维度 L）。
        /// 规则：正面必须朝向室内（空旷一侧）；若两条 length 边都朝室内，取更空旷的一侧；
        /// 若两条都朝墙（正面不可达），返回 null。
        /// </summary>
        private string ChooseFrontSide(Rect r, double rot)
        {
            // rot 0：length 沿 X，length 边是上下两条；rot 90：length 沿 Y，length 边是左右两条。
            (Vec2 a, Vec2 b, Vec2 n, string name) e1, e2;
            if (rot == 0.0)
            {
                e1 = (new Vec2(r.MinX, r.MaxY), new Vec2(r.MaxX, r.MaxY), new Vec2(0, 1), "top");
                e2 = (new Vec2(r.MinX, r.MinY), new Vec2(r.MaxX, r.MinY), new Vec2(0, -1), "bottom");
            }
            else
            {
                e1 = (new Vec2(r.MaxX, r.MinY), new Vec2(r.MaxX, r.MaxY), new Vec2(1, 0), "right");
                e2 = (new Vec2(r.MinX, r.MinY), new Vec2(r.MinX, r.MaxY), new Vec2(-1, 0), "left");
            }

            bool f1 = EdgeFacesRoom(e1.a, e1.b, e1.n);
            bool f2 = EdgeFacesRoom(e2.a, e2.b, e2.n);

            if (f1 && !f2) return e1.name;
            if (f2 && !f1) return e2.name;
            if (f1 && f2)
            {
                // 两个方向都朝向室内：以空闲深度为主，朝向门的一侧适当降权（软偏好，避免正面朝入口/门洞）
                double depth1 = EdgeFreeDepth(e1.a, e1.b, e1.n);
                double depth2 = EdgeFreeDepth(e2.a, e2.b, e2.n);
                if (FacesDoor(e1.a, e1.n)) depth1 -= 500.0;
                if (FacesDoor(e2.a, e2.n)) depth2 -= 500.0;
                return depth1 >= depth2 ? e1.name : e2.name;
            }
            return null; // 两条 length 边都朝墙 → 正面不可达
        }

        /// <summary>判断某条边朝外方向是否"朝向门"（即朝外会指向门所在墙的那一侧，用于让正面避开入口）。</summary>
        private bool FacesDoor(Vec2 p, Vec2 n)
        {
            if (Math.Abs(_doorA.X - _doorB.X) < Geo.Eps)   // 门竖直
            {
                double doorX = _doorA.X;
                if (n.X > 0 && doorX > p.X) return true;   // 朝右且门在右侧
                if (n.X < 0 && doorX < p.X) return true;   // 朝左且门在左侧
                return false;
            }
            else                                            // 门水平
            {
                double doorY = _doorA.Y;
                if (n.Y > 0 && doorY > p.Y) return true;   // 朝上且门在上侧
                if (n.Y < 0 && doorY < p.Y) return true;   // 朝下且门在下侧
                return false;
            }
        }

        /// <summary>判断某条边朝外是否指向室内：取边中点朝法线方向偏移 1mm 探测该点是否在房间内。</summary>
        private bool EdgeFacesRoom(Vec2 a, Vec2 b, Vec2 n)
        {
            Vec2 mid = new Vec2((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
            const double delta = 1.0;
            return Geo.PointInPolygon(new Vec2(mid.X + n.X * delta, mid.Y + n.Y * delta), _boundary);
        }

        /// <summary>根据正面朝向和通道深度，构造正面通道矩形（沿该边向外延伸 depth）。</summary>
        private static Rect BuildFrontClearance(Rect r, string side, double depth)
        {
            switch (side)
            {
                case "top": return new Rect(r.MinX, r.MaxY, r.MaxX, r.MaxY + depth);
                case "bottom": return new Rect(r.MinX, r.MinY - depth, r.MaxX, r.MinY);
                case "right": return new Rect(r.MaxX, r.MinY, r.MaxX + depth, r.MaxY);
                default: return new Rect(r.MinX - depth, r.MinY, r.MinX, r.MaxY); // left
            }
        }

        /// <summary>返回矩形某条边及其朝外法线（用于计算该方向的实际空闲深度）。</summary>
        private static (Vec2 a, Vec2 b, Vec2 n) GetFrontEdge(Rect r, string side)
        {
            switch (side)
            {
                case "top": return (new Vec2(r.MinX, r.MaxY), new Vec2(r.MaxX, r.MaxY), new Vec2(0, 1));
                case "bottom": return (new Vec2(r.MinX, r.MinY), new Vec2(r.MaxX, r.MinY), new Vec2(0, -1));
                case "right": return (new Vec2(r.MaxX, r.MinY), new Vec2(r.MaxX, r.MaxY), new Vec2(1, 0));
                default: return (new Vec2(r.MinX, r.MinY), new Vec2(r.MinX, r.MaxY), new Vec2(-1, 0)); // left
            }
        }

        /// <summary>
        /// 计算某条边沿法线方向 n 的"空闲深度"（毫米）。
        /// 沿边采样 5 个点，若最小值远小于中间值，说明只是局部小台阶/缺口（如 40mm 踢脚线），
        /// 用中间值更合理；否则（如斜墙导致的连续变化）用最小值以保证不超出房间。
        /// </summary>
        private double EdgeFreeDepth(Vec2 a, Vec2 b, Vec2 n)
        {
            var depths = new double[5];
            for (int i = 0; i < 5; i++)
            {
                double t = i / 4.0;
                var p = new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                depths[i] = DistanceToBoundary(p, n);
            }
            Array.Sort(depths);
            double min = depths[0], median = depths[2];
            return (min < median * 0.5) ? median : min;
        }

        /// <summary>从 origin 沿 dir 方向发射射线，返回与房间边界最近交点的距离（无交点返回正无穷）。</summary>
        private double DistanceToBoundary(Vec2 origin, Vec2 dir)
        {
            double best = double.PositiveInfinity;
            int n = _boundary.Count;
            for (int i = 0; i < n; i++)
            {
                double t = RaySegmentT(origin, dir, _boundary[i], _boundary[(i + 1) % n]); // 射线与每条边的交点参数 t
                if (t >= 0 && t < best) best = t;   // 取最近的正距离
            }
            return best;
        }

        /// <summary>计算射线 o + t*d（t>=0）与线段 ab 的交点参数 t；无交返回 -1。</summary>
        private static double RaySegmentT(Vec2 o, Vec2 d, Vec2 a, Vec2 b)
        {
            double ex = b.X - a.X, ey = b.Y - a.Y;       // 线段方向向量 e = b - a
            double denom = d.X * ey - d.Y * ex;          // d × e（叉乘）
            if (Math.Abs(denom) < Geo.Eps) return -1;    // 平行或共线 → 无交点
            double aox = a.X - o.X, aoy = a.Y - o.Y;     // 向量 a - o
            double t = (aox * ey - aoy * ex) / denom;    // 射线参数 t（沿 d 方向的距离倍数）
            double u = (aox * d.Y - aoy * d.X) / denom;  // 线段参数 u（0~1 表示交点在线段内）
            if (t >= -Geo.Eps && u >= -Geo.Eps && u <= 1 + Geo.Eps) return t; // t>=0 且 0<=u<=1 才是有效交点
            return -1;
        }

        // ============ 贴合已放物体统计 ============

        /// <summary>统计矩形 r 有多少条边与已放物体的边"共线贴合"（0~4）。</summary>
        private int CountAdjacency(Rect r)
        {
            int count = 0;
            var redges = Geo.RectEdges(r);               // 当前矩形的四条边
            foreach (var (e1, e2) in redges)
            {
                bool touch = false;
                foreach (var s in _placed)               // 与每个已放物体比较
                {
                    foreach (var (o1, o2) in Geo.RectEdges(s.Rect))
                    {
                        if (Geo.CollinearOverlap(e1, e2, o1, o2, Geo.Eps)) { touch = true; break; } // 共线重叠 → 贴合
                    }
                    if (touch) break;
                }
                if (touch) count++;                      // 该边贴合了某个已放物体 → 计数+1
            }
            return count;
        }

        // ============ 门相关 ============

        /// <summary>判断门所在墙的"室内侧"：-1 表示较小坐标侧，+1 表示较大坐标侧。</summary>
        private static int DetermineInteriorSide(Vec2 a, Vec2 b, List<Vec2> poly)
        {
            Vec2 mid = new Vec2((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0); // 门中点
            const double delta = 1.0;   // 采样偏移 1mm，用来探测门两侧哪边在室内
            if (Math.Abs(a.X - b.X) < Geo.Eps)   // 门是竖直的 → 探测左右两侧
            {
                bool left = Geo.PointInPolygon(new Vec2(mid.X - delta, mid.Y), poly);
                bool right = Geo.PointInPolygon(new Vec2(mid.X + delta, mid.Y), poly);
                if (left && !right) return -1;    // 左侧在室内
                if (right && !left) return +1;    // 右侧在室内
            }
            else                                  // 门是水平的 → 探测上下两侧
            {
                bool down = Geo.PointInPolygon(new Vec2(mid.X, mid.Y - delta), poly);
                bool up = Geo.PointInPolygon(new Vec2(mid.X, mid.Y + delta), poly);
                if (down && !up) return -1;       // 下侧在室内
                if (up && !down) return +1;       // 上侧在室内
            }
            return 0;   // 无法判断（理论上不会发生）
        }

        /// <summary>构造内开门的 N×N 摆动区矩形（门线段向内扩展 width 深度）。</summary>
        private static Rect BuildSwing(Vec2 a, Vec2 b, int side, double width)
        {
            if (Math.Abs(a.X - b.X) < Geo.Eps)   // 竖直门：沿 X 方向向内扩展
            {
                double x = a.X;
                double ylo = Math.Min(a.Y, b.Y), yhi = Math.Max(a.Y, b.Y);
                return side >= 0 ? new Rect(x, ylo, x + width, yhi) : new Rect(x - width, ylo, x, yhi);
            }
            else                                  // 水平门：沿 Y 方向向内扩展
            {
                double y = a.Y;
                double xlo = Math.Min(a.X, b.X), xhi = Math.Max(a.X, b.X);
                return side >= 0 ? new Rect(xlo, y, xhi, y + width) : new Rect(xlo, y - width, xhi, y);
            }
        }

        // ============ 不可行诊断 ============

        /// <summary>当搜索失败时，给出不可行的具体原因。</summary>
        private string Diagnose(List<ItemSpec> items)
        {
            // 临时清空已放物体，逐个检查"单独放一件"是否都放得下
            var saved = _placed.ToList();
            _placed.Clear();
            foreach (var it in items)
            {
                if (!CanFitAlone(it))
                {
                    _placed.Clear(); _placed.AddRange(saved);
                    return $"物品 {it.Id} 的尺寸({it.Length}x{it.Width})无法放入轮廓内";
                }
            }
            _placed.Clear();
            _placed.AddRange(saved);
            // 每件单独都能放下，只是无法同时放下 → 组合约束问题
            return "无法找到同时满足「不重叠、不遮挡门、贴墙」等约束的摆放方案";
        }

        /// <summary>判断单个物体 it 是否能在空房间里找到至少一个合法位置。</summary>
        private bool CanFitAlone(ItemSpec it)
        {
            foreach (var rot in new[] { 0.0, 90.0 })   // 两个朝向
            {
                double dx = rot == 0.0 ? it.Length : it.Width;
                double dy = rot == 0.0 ? it.Width : it.Length;
                var xs = _boundary.Select(v => v.X).ToHashSet();   // 房间顶点 x 坐标
                var ys = _boundary.Select(v => v.Y).ToHashSet();   // 房间顶点 y 坐标
                var xsList = xs.ToList(); xsList.Sort();           // 排序，保证遍历顺序确定
                var ysList = ys.ToList(); ysList.Sort();
                foreach (var x in xsList)
                {
                    foreach (var y in ysList)
                    {
                        // 4 种贴边锚点
                        var anchors = new[] { (x, y), (x - dx, y), (x, y - dy), (x - dx, y - dy) };
                        foreach (var (ax, ay) in anchors)
                        {
                            var r = new Rect(ax, ay, ax + dx, ay + dy);
                            if (!IsValidAnchor(it, rot, r)) continue;
                            return true;   // 找到合法位置
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>判断某个锚点摆放是否合法（供 CanFitAlone 复用）。</summary>
        private bool IsValidAnchor(ItemSpec it, double rot, Rect r)
        {
            if (!Geo.RectInsidePolygon(r, _boundary)) return false;            // 必须在轮廓内
            if (Geo.RectBlocksDoor(r, _doorA, _doorB)) return false;           // 不挡门
            if (_doorClear.HasValue && Geo.RectsOverlap(r, _doorClear.Value, Geo.Eps)) return false; // 不进入门预留区
            if (it.Type == "overShelf" && Geo.CountWallTouches(r, _boundary) < 1) return false; // 离地架须挂墙
            if (GetAccessibleClearance(it.Type) > 0 && ChooseFrontSide(r, rot) == null) return false; // 正面必须朝向室内
            return true;
        }

        // ============ 工具 ============

        /// <summary>把输入里的 algoToPlace 字典解析成 ItemSpec 列表（不排序，排序在 Place 里做）。</summary>
        private static List<ItemSpec> BuildItems(Dictionary<string, List<double>> map)
        {
            var list = new List<ItemSpec>();
            foreach (var kv in map)
            {
                list.Add(new ItemSpec
                {
                    Id = kv.Key,
                    Type = GetTypeFromName(kv.Key),   // 从名字推导类型（如 "shelf-1" → "shelf"）
                    Length = kv.Value[0],
                    Width = kv.Value[1],
                });
            }
            return list;
        }

        /// <summary>摆放优先级：冰箱 > 离地架 > 制冰机 > 货架。</summary>
        private static int TypePriority(string type) => type switch
        {
            "fridge" => 0,
            "overShelf" => 1,
            "iceMaker" => 2,
            _ => 3,
        };

        /// <summary>从物体名字提取类型：去掉末尾的 "-数字"。如 "shelf-1" → "shelf"，"fridge" → "fridge"。</summary>
        private static string GetTypeFromName(string name)
        {
            int idx = name.LastIndexOf('-');
            if (idx > 0 && int.TryParse(name.Substring(idx + 1), out _)) return name.Substring(0, idx);
            return name;
        }

        /// <summary>两点欧氏距离。</summary>
        private static double Dist(Vec2 a, Vec2 b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>计算一组已放物体的整体包围盒面积（用于衡量摆放紧凑度）。</summary>
        private static double BBoxArea(List<PlacedState> placed)
        {
            if (placed.Count == 0) return 0;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in placed)
            {
                minX = Math.Min(minX, s.Rect.MinX); minY = Math.Min(minY, s.Rect.MinY);
                maxX = Math.Max(maxX, s.Rect.MaxX); maxY = Math.Max(maxY, s.Rect.MaxY);
            }
            return (maxX - minX) * (maxY - minY);
        }

        /// <summary>把内部 PlacedState 转成输出 DTO（中心点、旋转角、包围盒、冰箱净空区等）。</summary>
        private static PlacedItemDto ToDto(PlacedState s)
        {
            var d = new PlacedItemDto
            {
                Id = s.Id,
                Type = s.Type,
                CenterX = s.Rect.Cx,
                CenterY = s.Rect.Cy,
                Rotation = s.Rotation,
                Length = s.Length,
                Width = s.Width,
                MinX = s.Rect.MinX,
                MinY = s.Rect.MinY,
                MaxX = s.Rect.MaxX,
                MaxY = s.Rect.MaxY,
                FrontSide = s.FrontSide,
            };
            if (s.FrontClearance.HasValue)   // 有正面通道（冰箱/货架/制冰机）
            {
                d.ClearanceMinX = s.FrontClearance.Value.MinX;
                d.ClearanceMinY = s.FrontClearance.Value.MinY;
                d.ClearanceMaxX = s.FrontClearance.Value.MaxX;
                d.ClearanceMaxY = s.FrontClearance.Value.MaxY;
            }
            return d;
        }

        /// <summary>校验输入合法性；返回 null 表示合法，否则返回错误原因。</summary>
        private string Validate(PlacementInput input)
        {
            if (input.Boundary == null || input.Boundary.Count < 3) return "轮廓点数量不足（至少 3 个点）";
            if (input.Door == null || input.Door.Count != 2) return "门的位置必须为两个端点";
            if (input.AlgoToPlace == null || input.AlgoToPlace.Count == 0) return "没有待摆放的物体";
            foreach (var kv in input.AlgoToPlace)
            {
                if (kv.Value == null || kv.Value.Count < 2) return $"物体 {kv.Key} 尺寸无效";
                if (kv.Value[0] <= 0 || kv.Value[1] <= 0) return $"物体 {kv.Key} 尺寸必须为正数";
            }
            // 门必须平行于坐标轴（竖直或水平），本算法只处理轴对齐门
            if (Math.Abs(_doorA.X - _doorB.X) > Geo.Eps && Math.Abs(_doorA.Y - _doorB.Y) > Geo.Eps)
                return "门必须平行于坐标轴";
            return null;
        }

        // ============ 内部数据类 ============

        /// <summary>一个待摆放物体的规格（id、类型、长、宽）。</summary>
        private class ItemSpec
        {
            public string Id;      // 物体名字（如 "fridge"、"shelf-1"）
            public string Type;    // 物体类型（如 "fridge"、"shelf"、"overShelf"）
            public double Length;  // 长（第一个尺寸）
            public double Width;   // 宽（第二个尺寸）
        }

        /// <summary>一个候选摆放：已构造好的状态 + 该摆放的局部得分。</summary>
        private class Candidate
        {
            public PlacedState State;    // 摆放状态
            public double LocalScore;    // 局部得分（贴墙越多越高）
        }

        /// <summary>一个已摆放物体的完整状态（用于回溯栈记录）。</summary>
        private class PlacedState
        {
            public string Id;
            public string Type;
            public Rect Rect;                  // 摆放的矩形（含位置）
            public double Rotation;            // 旋转角（0 或 90）
            public double Length;
            public double Width;
            public Rect? FrontClearance;       // 正面通道（冰箱/货架/制冰机）
            public string FrontSide;           // 正面朝向（top/bottom/left/right）
            public int WallTouch;              // 贴墙边数
            public int Adjacency;              // 与已放物体贴合的边数

            public PlacedState Clone() => (PlacedState)MemberwiseClone(); // 浅拷贝（用于保存最优解快照）
        }
    }
}
