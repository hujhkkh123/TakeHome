# TakeHome 工程题

## 1. AI 工具使用声明

根据题目要求，在此简要说明本项目中 AI 工具的使用情况：

- **使用的 AI 工具**：Trae 编辑器内置的 **DeepSeek v4.pro** 模型。
- **AI 主要帮助的部分**：
  - **思路拆解与代码解析**：在接手项目时，借助 AI 快速理清了 ASP.NET Core 的依赖注入（DI）生命周期、控制器（Controller）的反序列化机制，以及核心 `PlacementService` 中“回溯搜索（Backtracking）”算法的整体运行流程。
  - **底层几何算法理解**：面对 `Geometry.cs` 中复杂的计算几何代码（如射线法判断点是否在多边形内、叉乘判断线段严格相交、`eps` 容差的工程意义），AI 帮助我逐一拆解了底层数学逻辑，并生成了通俗易懂的图形化理解。
  - **边缘案例 Debug 与排错**：针对编译时出现的“文件被锁定”、“代码无法定位到定义”等问题，AI 帮助我定位了进程占用和单例注册等问题，并梳理了排查思路。
- **自己理解并调整的关键逻辑**：
  - **业务规则校验**：深入理解了“包围盒粗筛 + 精确多边形细筛”的性能优化策略，并独立确认了冰箱特殊净空区（FridgeClearance）和离地架（overShelf）必须挂墙等关键业务约束条件。
  - **并发安全隐患排查**：在理清 `Program.cs` 的依赖注入配置后，我意识到 `AddSingleton<PlacementService>()` 虽然好用，但在生产环境中会导致多请求并发共享 `_placed` 列表，从而产生数据串号（脏读）隐患。我理解了需要将其调整为 `AddScoped` 才能保证并发安全。
  - **打分策略与回溯剪枝**：理解了代码中贴墙加分（+100）、靠角加分（+20）的权重设计，以及如何利用 `NodeBudget` 和 `TimeBudgetMs` 进行超时剪枝，防止服务器卡死。这些逻辑在实际测试中均能依据几何坐标进行推演验证。
- **自主提示词编写**：
见同级目录内的test.md
## 2.项目运行结果
- **example1**
<img width="2232" height="2297" alt="image" src="https://github.com/user-attachments/assets/396ed0f8-8ce3-427f-8274-2b915ef01b46" />

- **example2**
<img width="2443" height="2292" alt="image" src="https://github.com/user-attachments/assets/eea7c743-7ca6-4e1a-b649-4b5c857729c7" />

- **example3**
<img width="2275" height="2344" alt="image" src="https://github.com/user-attachments/assets/253084bd-ff75-47aa-85fd-92feb39ca292" />

- **example4**
<img width="2320" height="2183" alt="image" src="https://github.com/user-attachments/assets/2449dd64-8cfe-41b8-afeb-ed9499272d0a" />

## 3.项目快速启动的方式
无论你有没有打开 VS 还是 VS Code，只要你有终端（Terminal / PowerShell / CMD）：
打开终端，使用 cd 命令进入包含 TakeHome.csproj 的那个文件夹（在server 目录）。

输入命令：**dotnet run**

终端会开始编译，几秒钟后，会打印出Now listening on: http://0.0.0.0:8080。这说明你的服务器已经启动了，之后访问 http://localhost:8080/就能看到接口测试页面。

在 Visual Studio (VS)中快速启动则更简单只需要在VS里打开TakeHome.csproj，然后运行这个文件就可以了
