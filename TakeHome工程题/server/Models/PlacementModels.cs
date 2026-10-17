using System.Collections.Generic;

//前后端数据契约(Data Contract)
namespace TakeHome.Models
{
    /// <summary>输入：轮廓 + 门 + 待摆放物体</summary>
    //输入模型
    public class PlacementInput
    {
        public List<List<double>> Boundary { get; set; } // 房屋轮廓
        public List<List<double>> Door { get; set; } //门的位置
        public bool IsOpenInward { get; set; } //是否内开门
        public Dictionary<string, List<double>> AlgoToPlace { get; set; } //待摆放的家具清单
    }

    /// <summary>单个已摆放物体的输出信息</summary>
    //当算法（大厨）计算完后，清单里的每一个物品都会变成这个类的一个实例。先根据json的数据吧每一个物体封装成一个类
    //内部确定最终摆放位置的模型
    //相当于根据json数据的转存数据
    public class PlacedItemDto
    {
        public string Id { get; set; } // 物品id
        public string Type { get; set; } // 物品类型
        public double CenterX { get; set; } //物品的中心坐标x
        public double CenterY { get; set; } //物品的中心坐标y
        public double Rotation { get; set; }   // 0 或 90 //是否需要旋转 90 度（Rotation 为 0 或 90）。
        public double Length { get; set; } //物品的长度
        public double Width { get; set; } //物品的宽度
        public double MinX { get; set; } //物品的最小x坐标
        public double MinY { get; set; } //物品的最小y坐标
        public double MaxX { get; set; } //物品的最大x坐标
        public double MaxY { get; set; } //物品的最大y坐标

        // 仅冰箱：开门边朝向 + 开门净空区
        public string FridgeDoorSide { get; set; } //冰箱的开门边朝向
        public double? ClearanceMinX { get; set; } //开门净空区的最小x坐标
        public double? ClearanceMinY { get; set; } //开门净空区的最小y坐标
        public double? ClearanceMaxX { get; set; } //开门净空区的最大x坐标
        public double? ClearanceMaxY { get; set; } //开门净空区的最大y坐标
    }

    /// <summary>求解结果</summary>
    public class PlacementResult
    {
        public bool Feasible { get; set; }
        public string Reason { get; set; }
        public List<PlacedItemDto> Placements { get; set; } = new();

        // 诊断信息（便于排查）
        public long ElapsedMs { get; set; }
        public long NodesExplored { get; set; }

        // 供前端绘制内开门摆动区
        public bool IsOpenInward { get; set; }
        public double? SwingMinX { get; set; }
        public double? SwingMinY { get; set; }
        public double? SwingMaxX { get; set; }
        public double? SwingMaxY { get; set; }
    }
}
