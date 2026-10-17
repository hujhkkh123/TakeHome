using System;
using System.Collections.Generic;

namespace TakeHome.Geometry
{
    // 二维坐标点。用来表示轮廓顶点、门的两个端点、物体的角点等。
    // 坐标单位统一为毫米(mm)。
    public struct Vec2
    {
        public double X;   // 横坐标（毫米）
        public double Y;   // 纵坐标（毫米）
        // 构造函数：用 x、y 创建一个点
        public Vec2(double x, double y) { X = x; Y = y; }
    }

    // 轴对齐矩形（四条边分别平行于 X 轴、Y 轴）。
    // 题目要求物体与轮廓边平行/垂直摆放，所以物体都是轴对齐的，
    // 因此一个矩形只需"左下角(MinX,MinY)"和"右上角(MaxX,MaxY)"两个点就能完整描述。
    public struct Rect
    {
        public double MinX, MinY, MaxX, MaxY; // 矩形的左下角与右上角坐标（不是"4个点"，是2个角点）
        public Rect(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public double Width => MaxX - MinX;          // 矩形宽度（沿 X 方向）
        public double Height => MaxY - MinY;         // 矩形高度（沿 Y 方向）
        public double Cx => (MinX + MaxX) / 2.0;     // 中心点 X 坐标
        public double Cy => (MinY + MaxY) / 2.0;     // 中心点 Y 坐标
    }

    /// <summary>几何工具类：提供点/线段/多边形/矩形之间的各种判定。坐标单位为毫米，采用 1e-3 容差。</summary>
    // 所有"物品是否放得下、是否重叠、是否贴墙、是否挡门"的判断，都集中在这个类里。
    public static class Geo
    {
        // 浮点比较容差：两个坐标之差小于 0.001 毫米就认为相等。
        // 输入坐标带 4 位小数，1e-3 既能消除舍入误差，又小到不会把"真正贴紧"误判成"重叠"。
        public const double Eps = 1e-3;

        // ============ 叉乘（向量外积）============

        // 二维叉乘：向量 a 与向量 b 的外积（都以原点为起点）。
        // 结果是一个标量 = |a| * |b| * sin(夹角)，其正负号表示两个向量的相对旋转方向：
        //   > 0 ：b 在 a 的逆时针方向（向左转）
        //   < 0 ：b 在 a 的顺时针方向（向右转）
        //   = 0 ：a、b 共线（平行或反向）
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        // 带基准点 o 的叉乘：等价于 Cross(a - o, b - o)。
        // 用来判断：站在点 o 看，从 a 转向 b 是顺时针还是逆时针。
        // 例如 Cross(o, a, b) 的符号可判断点 b 在直线 oa 的哪一侧。
        public static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        // ============ 点在线段上的判定 ============

        // 判断点 p 是否在线段 ab 上（含两个端点，带容差）。
        // 分两步：
        //   第1步：用叉乘判断 p、a、b 是否共线（叉乘接近 0 即共线，不共线则一定不在线段上）。
        //   第2步：判断 p 是否落在 ab 的包围盒内（即 p 的 x、y 都在 ab 的范围内）。
        public static bool PointOnSegment(Vec2 p, Vec2 a, Vec2 b, double eps)
        {
            if (Math.Abs(Cross(a, p, b)) > eps) return false;   // 不共线 → 直接返回 false
            return p.X >= Math.Min(a.X, b.X) - eps && p.X <= Math.Max(a.X, b.X) + eps
                && p.Y >= Math.Min(a.Y, b.Y) - eps && p.Y <= Math.Max(a.Y, b.Y) + eps; // 在包围盒内 → 在线段上
        }

        // ============ 点在多边形内（射线法）============

        /// <summary>射线法判断点 p 是否在多边形内（含边界）。</summary>
        public static bool PointInPolygon(Vec2 p, List<Vec2> poly)
        {
            int n = poly.Count;
            bool inside = false;   // 初始假设在外部
            for (int i = 0; i < n; i++)
            {
                Vec2 a = poly[i];               // 当前边的起点
                Vec2 b = poly[(i + 1) % n];     // 当前边的终点（% n 让最后一条边回到第一个点，闭合多边形）
                if (PointOnSegment(p, a, b, Eps)) return true;   // p 恰好在某条边上 → 算作内部

                // (a.Y > p.Y) != (b.Y > p.Y)：判断边 ab 是否"跨越"了过 p 点的水平射线。
                // 即：边的一个端点严格在射线之上，另一个严格在射线之下（一真一假，异或成立）。
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    // 计算水平射线（y = p.Y）与边 ab 的交点 x 坐标（线性插值）
                    double xint = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                    // 交点在 p 的右侧 → 翻转一次"内/外"状态
                    if (p.X < xint) inside = !inside;
                }
            }
            return inside; // 翻转奇数次 = 内部；偶数次 = 外部
        }

        // ============ 线段相交判定 ============

        // 判断线段 ab 与线段 cd 是否"真相交"（严格地互相穿越，仅端点/边接触不算）。
        // 原理：用四次叉乘分别判断 c、d 是否在 ab 的两侧，以及 a、b 是否在 cd 的两侧。
        // 两个方向都"跨越"才算真相交，这样可以避免把"共线重叠、端点刚好碰到"误判成相交。
        public static bool SegmentsProperlyIntersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d, double eps)
        {
            double o1 = Cross(a, b, c);   // 点 c 相对直线 ab 的方位
            double o2 = Cross(a, b, d);   // 点 d 相对直线 ab 的方位
            double o3 = Cross(c, d, a);   // 点 a 相对直线 cd 的方位
            double o4 = Cross(c, d, b);   // 点 b 相对直线 cd 的方位
            // cross1：c、d 分居 ab 两侧（一正一负）
            bool cross1 = (o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps);
            // cross2：a、b 分居 cd 两侧（一正一负）
            bool cross2 = (o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps);
            return cross1 && cross2;   // 两个方向都跨越，才是真相交
        }

        // 判断点 p 是否"严格"位于矩形 r 的内部（不含边界）。
        // 用于检查：多边形的某个顶点是否刺进了矩形内部（若刺入，说明矩形有一部分伸出了房间）。
        static bool PointStrictlyInsideRect(Vec2 p, Rect r, double eps)
            => p.X > r.MinX + eps && p.X < r.MaxX - eps && p.Y > r.MinY + eps && p.Y < r.MaxY - eps;

        // ============ 矩形的四条边 ============

        /// <summary>返回矩形的四条边，顺序为：下、右、上、左，每条边用 (起点, 终点) 表示。</summary>
        //括号 () 在这里代表的是 C# 中的 元组（Tuple）
        public static List<(Vec2, Vec2)> RectEdges(Rect r)
        {
            return new List<(Vec2, Vec2)>
            {
                (new Vec2(r.MinX, r.MinY), new Vec2(r.MaxX, r.MinY)), // 下边（从左下角到右下角）
                (new Vec2(r.MaxX, r.MinY), new Vec2(r.MaxX, r.MaxY)), // 右边（从右下角到右上角）
                (new Vec2(r.MaxX, r.MaxY), new Vec2(r.MinX, r.MaxY)), // 上边（从右上角到左上角）
                (new Vec2(r.MinX, r.MaxY), new Vec2(r.MinX, r.MinY)), // 左边（从左上角到左下角）
            };
        }

        // ============ 矩形是否在多边形内 ============

        /// <summary>判断矩形 r 是否完全位于多边形内部（允许贴边）。</summary>
        public static bool RectInsidePolygon(Rect r, List<Vec2> poly)
        {
            // 条件1：矩形四个角都必须落在多边形内（含边界）
            if (!PointInPolygon(new Vec2(r.MinX, r.MinY), poly)) return false; // 左下角
            if (!PointInPolygon(new Vec2(r.MaxX, r.MinY), poly)) return false; // 右下角
            if (!PointInPolygon(new Vec2(r.MaxX, r.MaxY), poly)) return false; // 右上角
            if (!PointInPolygon(new Vec2(r.MinX, r.MaxY), poly)) return false; // 左上角

            // 条件2：矩形四条边不能与多边形任何一条边"真相交"（即不能穿墙）
            var redges = RectEdges(r);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vec2 a = poly[i], b = poly[(i + 1) % n];
                foreach (var (e1, e2) in redges)
                {
                    if (SegmentsProperlyIntersect(e1, e2, a, b, Eps)) return false;
                }
            }

            // 条件3：多边形的顶点不能严格落在矩形内部（防止房间的凹口或者内部多余构造"刺入"矩形）
            foreach (var v in poly)
            {
                if (PointStrictlyInsideRect(v, r, Eps)) return false;
            }
            return true;   // 三个条件都满足，才认为矩形完全在轮廓内
        }

        // ============ 两个矩形是否重叠 ============
        //通过坐标判断

        /// <summary>判断两个轴对齐矩形内部是否重叠（贴边不算重叠）。</summary>
        public static bool RectsOverlap(Rect a, Rect b, double eps)
            => a.MinX + eps < b.MaxX && b.MinX + eps < a.MaxX      // X 方向的投影区间有重叠
            && a.MinY + eps < b.MaxY && b.MinY + eps < a.MaxY;     // Y 方向的投影区间也有重叠
        // 两个轴对齐矩形，只有当 X、Y 两个方向都重叠时内部才会重叠；贴边（差<=eps）不算重叠。

        // ============ 共线重叠判定（用于贴墙/贴合）============

        /// <summary>判断两线段是否共线且重叠（用于判断物体边是否贴墙或贴到另一物体）。</summary>
        //下端更低的减去上端更高的
        public static bool CollinearOverlap(Vec2 a, Vec2 b, Vec2 c, Vec2 d, double eps)
        {
            bool abVert = Math.Abs(a.X - b.X) < eps;   // 线段 ab 是否竖直（x 几乎不变）
            bool cdVert = Math.Abs(c.X - d.X) < eps;   // 线段 cd 是否竖直
            if (abVert && cdVert)                       // 两条都竖直
            {
                if (Math.Abs(a.X - c.X) > eps) return false;       // 不在同一条竖线上 → 不可能重叠
                double lo = Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)); // 两条线段 y 区间的重叠下界
                double hi = Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)); // 两条线段 y 区间的重叠上界
                return hi - lo > eps;                   // 有正长度的重叠才算
            }
            bool abHor = Math.Abs(a.Y - b.Y) < eps;    // 线段 ab 是否水平（y 几乎不变）
            bool cdHor = Math.Abs(c.Y - d.Y) < eps;    // 线段 cd 是否水平
            if (abHor && cdHor)                         // 两条都水平
            {
                if (Math.Abs(a.Y - c.Y) > eps) return false;       // 不在同一条水平线上 → 不可能重叠
                double lo = Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)); // 两条线段 x 区间的重叠左界
                double hi = Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)); // 两条线段 x 区间的重叠右界
                return hi - lo > eps;                   // 有正长度的重叠才算
            }
            return false;                               // 一条竖直、一条水平 → 不可能共线
        }

        // ============ 统计贴墙边数 ============
        //直接遍历矩形的四条边，与房间的每条边比较
        /// <summary>统计矩形有多少条边贴到房间墙壁（返回 0~4）。</summary>
        public static int CountWallTouches(Rect r, List<Vec2> poly)
        {
            int count = 0;
            var redges = RectEdges(r);                   // 矩形的四条边
            int n = poly.Count;
            foreach (var (e1, e2) in redges)             // 遍历矩形的每条边
            {
                for (int i = 0; i < n; i++)              // 与房间的每条边比较
                {
                    if (CollinearOverlap(e1, e2, poly[i], poly[(i + 1) % n], Eps)) { count++; break; } // 该边与某墙共线重叠 → 贴墙，计数+1
                }
            }
            return count;
        }

        // ============ 门遮挡判定 ============
        //分为竖着的和水平的门
        /// <summary>判断矩形 r 是否遮挡门（门为轴对齐线段 d1-d2）。</summary>
        public static bool RectBlocksDoor(Rect r, Vec2 d1, Vec2 d2, double eps = Eps)
        {
            if (Math.Abs(d1.X - d2.X) < eps)   //一条横线          // 门是竖直的（两端点 x 相同）
            {
                double x = d1.X;                          // 门所在墙面的 x 坐标
                double ylo = Math.Min(d1.Y, d2.Y), yhi = Math.Max(d1.Y, d2.Y); // 门洞的 y 范围

                // 情况1：门所在墙面线从矩形内部穿过（物体横跨了这堵墙，必然挡门）
                // 就好像墙是一根竖着的棍子，家具是一根横着的棍子，家具把墙给“穿透”了
                if (r.MinX + eps < x && x < r.MaxX - eps)
                {
                    if (r.MinY + eps < yhi && ylo < r.MaxY - eps) return true;
                }
                // 情况2：物体的某条边正好贴在门所在墙面上（x 对齐），且覆盖了门洞
                //家具的右边缘（MaxX）或者左边缘（MinX）刚好和墙的坐标（x）重合。
                if (Math.Abs(r.MaxX - x) < eps || Math.Abs(r.MinX - x) < eps)
                {
                    if (r.MinY + eps < yhi && ylo < r.MaxY - eps) return true;
                }
            }
            else                                          // 门是水平的（两端点 y 相同）
            {
                double y = d1.Y;                          // 门所在墙面的 y 坐标
                double xlo = Math.Min(d1.X, d2.X), xhi = Math.Max(d1.X, d2.X); // 门洞的 x 范围

                if (r.MinY + eps < y && y < r.MaxY - eps) // 门所在墙面线从矩形内部穿过
                {
                    if (r.MinX + eps < xhi && xlo < r.MaxX - eps) return true;
                }
                if (Math.Abs(r.MaxY - y) < eps || Math.Abs(r.MinY - y) < eps) // 物体边贴在门墙面上
                {
                    if (r.MinX + eps < xhi && xlo < r.MaxX - eps) return true;
                }
            }
            return false;                                 // 不遮挡门
        }
    }
}
