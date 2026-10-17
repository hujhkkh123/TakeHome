using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using TakeHome.Models;
using TakeHome.Services;

//属于 TakeHome 项目下的 Controllers 区域
namespace TakeHome.Controllers
{
    [ApiController] //特性
    [Route("api")]
    //属于 TakeHome 项目下的 Controllers 区域
    public class PlacementController : ControllerBase
    {
        private readonly PlacementService _service;
        private readonly IWebHostEnvironment _env; // ASP.NET Core 框架内部的一个“环境上下文”对象
        //依赖注入
        public PlacementController(PlacementService service, IWebHostEnvironment env)
        {
            _service = service;
            _env = env;
        }

        [HttpPost("place")] //追加路径place
        public ActionResult<PlacementResult> Place([FromBody] PlacementInput input)
        {
            if (input == null) return BadRequest(new { feasible = false, reason = "请求体为空" });
            try
            {
                return Ok(_service.Place(input));
            }
            catch (Exception ex)
            {
                //服务器崩溃异常返回结果，只要不是服务器出现崩溃都会走Ok路径
                return StatusCode(500, new { feasible = false, reason = "服务端异常: " + ex.Message });
            }
        }
        //GET 接口 - 读取示例文件
        [HttpGet("examples")] //追加路径examples
        public IActionResult Examples()
        {
            var dir = Path.Combine(_env.WebRootPath, "examples");
            var result = new Dictionary<string, object>(); //泛型字典
            foreach (var name in new[] { "example1", "example2", "example3", "example4" })
            {
                var path = Path.Combine(dir, name + ".json");
                if (System.IO.File.Exists(path))
                {
                    result[name] = JsonSerializer.Deserialize<object>(System.IO.File.ReadAllText(path));
                    //反序列化后object 就变成了一个类似字典(Dictionary)
                }
            }
            return Ok(result);
        }
    }
}
