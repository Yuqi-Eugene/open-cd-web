# OpenCd.Web

基于本仓库的 `dotnet8` 前后端一体化多页网站，采用 iOS 毛玻璃风格。

## 功能页

- `/` 首页导航
- `/preprocess.html` 批量预处理（目录浏览器 + 批命名 / uint8+nodata / train-val-test）
- `/dataset.html` 左侧目录树 + 右侧可视窗口（自动匹配 A/B/label、label 矢量叠加）
- `/training.html` Open-CD 训练、验证、测试与指标曲线
- `/docs.html` 样本集规范说明

## 运行

```bash
dotnet run --project src/OpenCd.Web/OpenCd.Web.csproj --urls http://127.0.0.1:5099
```

访问：

- 页面：`http://127.0.0.1:5099/`
- 健康检查：`http://127.0.0.1:5099/api/health`

## 说明

1. 后端默认在仓库根目录执行 Python 脚本。
2. 需要确保当前环境可直接调用 `python`，并安装 Open-CD 相关依赖。
3. 为安全起见，接口仅允许访问仓库根目录内的文件路径。
