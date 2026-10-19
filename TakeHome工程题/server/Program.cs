using Microsoft.Extensions.FileProviders;
using TakeHome.Services;

//此时程序只是建了一个“空壳子”，还没有“内容”
var builder = WebApplication.CreateBuilder(args);

//支持控制器模式，添加 JSON 选项，忽略属性名大小写差异
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
//使用单例模式
builder.Services.AddSingleton<PlacementService>();

// 从 Visual Studio 直接运行时，工作目录是 bin\Debug\net10.0，导致默认 WebRootPath（wwwroot）不存在。
// 这里做兜底：若默认 wwwroot 不存在，则回退到源码目录下的 wwwroot，并同步更新文件提供器。
if (!Directory.Exists(builder.Environment.WebRootPath))
{
    var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
    if (Directory.Exists(fallback))
    {
        builder.Environment.WebRootPath = fallback;
        builder.Environment.WebRootFileProvider = new PhysicalFileProvider(fallback);
    }
}

//app 就是这栋正在运行的 Web 大楼
//builder.Build() 方法会根据配置创建一个 WebApplication 实例，这个实例就是 app 变量的值
//app 是真正活着的实例。从这之后，你的配置全都是关于“怎么应对客人”的
var app = builder.Build();

//如果客人直接访问根目录（比如 http://localhost:8080/），自动帮他把请求指向 wwwroot 下的 index.html。
app.UseDefaultFiles();
app.UseStaticFiles();
//启用路由分发
app.MapControllers();

// 监听所有网卡，默认端口 8080（可用环境变量 PORT 覆盖），支持本机及局域网访问
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
