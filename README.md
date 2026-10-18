# QookiL · 局域网快传

[![CI](https://github.com/weimosheng/QookiL/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/weimosheng/QookiL/actions/workflows/ci.yml)

用 **C# + WinUI 3** 写的局域网文件传输小工具。在电脑上选好要共享的文件/文件夹、点「启动服务」，手机或其他电脑**用浏览器直接访问**即可浏览、下载文件，也能把文件上传回电脑——对方不需要安装任何软件。

## 功能一览

- **网页服务器**：内嵌 Kestrel（ASP.NET Core），局域网设备浏览器直接访问 `http://电脑IP:端口`
- **二维码 / 链接**：自动列出本机所有局域网 IPv4 地址，一键复制，扫码即达
- **共享列表**：添加文件或整个文件夹，支持拖拽进窗口；重启程序后自动恢复上次共享
- **实时同步**：电脑端增删共享，手机网页 2.5 秒内自动刷新（版本号轮询），无需手动刷新
- **网页端浏览**：文件夹逐级进入、面包屑导航；图片/视频/音频在线预览；共享的照片以真实尺寸平铺展示，点开即看
- **下载**：支持 Range 断点续传（大文件可暂停续传、视频可拖进度条），中文文件名无损
- **上传**：手机网页拖拽或选文件发送到电脑「接收文件夹」（默认系统「下载」文件夹，自动跟随重定向），同名文件自动改名不覆盖
- **传输记录**：谁（IP）在什么时间下载/上传了什么文件、多大，实时滚动显示
- **原生界面**：Windows 设置风格标题栏 + Mica 云母背景 + 明暗主题自适应，深色移动端页面
- **防火墙放行**：启动服务前自动检测 Windows 防火墙；未放行时引导一键授权（UAC 提权一次，自动添加仅作用于本程序的入站规则），从此手机不再连不上
- **安全兜底**：路径越界（`../`）拦截、非法文件名过滤

## 环境要求

- Windows 10 19041（2004）及以上 / Windows 11
- 目标机器**无需**安装 .NET 运行时或 Windows App SDK——已配置自包含发布
- 开发机需要 .NET 8 SDK（本仓库另带 `NuGet.config` 指向国内镜像）

## 快速开始

```powershell
# 编译运行（开发）
dotnet run --project QookiL -c Release -p:Platform=x64

# 发布单机绿色版（QookiL\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish）
dotnet publish QookiL/QookiL.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true
```

发布出来的文件夹整体拷走即可，双击 `QookiL.exe` 运行。

## 使用方法

1. 启动程序，把要共享的文件/文件夹拖进左侧列表（或点「添加文件 / 添加文件夹」）
2. 确认端口号（默认 `5188`），点 **启动服务**（可打开「启动时自动开启」，下次打开程序直接就绪）
3. 程序**启动时**就会检测防火墙，未放行会弹窗引导授权——点「允许」后在弹出的 UAC 框里选「是」即可，**只需设置一次**
4. 手机连**同一个 Wi‑Fi / 局域网**，扫码或浏览器输入右侧显示的地址，例如 `http://192.168.1.5:5188`
5. 手机上即可浏览下载；共享的照片会以大图展示；把手机里的文件发回电脑也只要一步
6. 电脑上随时增删共享，手机页面会自动跟着变；「停止服务」立即断开所有访问

### 接收文件夹说明

- 默认就是**系统「下载」文件夹**（通过 Windows 已知文件夹 API 解析），手机传来的文件直接出现在「下载」里，无需手动指定
- 自动跟随系统重定向：如果你把「下载」移动到了其他盘（或被 OneDrive 接管），程序会解析到真实位置
- 手动选过其他文件夹会记住并在下次沿用；选回「下载」则恢复默认跟随
- 启动服务时如果当前目录不可访问（被删、U 盘拔了等），自动回退到系统「下载」文件夹并在提示条说明，不会启动失败

### 防火墙放行说明

- 程序在**启动时**和**每次启动服务前**都会检测防火墙（读取规则不需要管理员权限）；已放行则完全无感
- 未放行**或读不到状态**时都会弹窗引导：说明弹窗 → UAC 管理员确认（程序以管理员身份重启自己，加完规则立即退出，不弹窗口）→ 自动复核
- 添加的规则只作用于 `QookiL.exe` 本身、仅入站 TCP、域/专用/公用网络都生效（很多家庭 Wi‑Fi 被 Windows 归类为“公用”，只放行专用网络是设备连不上的最常见原因）
- 若之前误点过系统的「取消」产生了阻止规则（阻止规则优先级高于放行），授权流程会先清掉它再放行
- 点了「暂不」服务仍会启动（本机访问不受影响），但其他设备可能无法连接；停止后重新点「启动服务」可再次设置
- **规则按 exe 完整路径生效**：`dotnet run`（Debug 目录）、`dotnet run -c Release`、发布版是不同路径，各自首次运行会各请求一次
- 手动放行（可选）：`netsh advfirewall firewall add rule name="QookiL" dir=in action=allow program="QookiL.exe完整路径" enable=yes profile=any`

### 其他设备连不上？排查清单

1. **点控制栏的「⚙ 连接诊断」**：直接显示防火墙状态、本程序规则、当前网络类别、所有网卡地址（含虚拟网卡标记）和排查建议；诊断记录同时写入 `%TEMP%\qookil-diag.log`
2. 确认程序里出现「允许局域网访问」弹窗时点了「允许」，且 UAC 里选了「是」（只在未放行时弹）
3. 设备和电脑必须**连同一个 Wi‑Fi / 路由器**：手机浏览器输入的地址前三段要与手机自己的 IP 一致（如都是 192.168.13.x）
4. 路由器开了 **AP 隔离**、或设备连的是**访客网络**：去路由器设置里关闭
5. 电脑装了 **360 / 火绒**等安全软件：它们自带防火墙，需要在它们里单独放行（Windows 防火墙规则管不到它们）
6. 电脑连了 **VPN**：流量被引走，局域网地址可能不通
7. **扫错地址**：装了 VMware/VirtualBox/WSL 的电脑会有多个虚拟网卡地址（如 169.254.x.x），手机连不上它们；二维码已优先选择物理网卡（Wi‑Fi > 以太网 > 虚拟），诊断面板里可逐个查看

## 项目结构

```
QookiL/
├── QookiL.csproj            # WinUI 3 + Windows App SDK 1.6 + ASP.NET Core
├── App.xaml(.cs)             # 应用入口（含 --firewall-setup 防火墙辅助模式）
├── MainWindow.xaml(.cs)      # 主界面：设置风格标题栏 / 共享列表 / 二维码 / 传输记录
├── Assets/                   # 应用图标（app.ico 多尺寸）与品牌 logo
├── Core/
│   ├── SharedItem.cs         # 共享项（文件/文件夹）
│   ├── SharedStore.cs        # 线程安全共享存储 + 版本号（网页实时刷新的依据）
│   ├── SettingsStore.cs      # 设置持久化（APPDATA → LOCALAPPDATA → 程序目录三级回退）
│   ├── KnownFolders.cs       # 系统已知文件夹解析（「下载」跟随重定向）
│   ├── FirewallHelper.cs     # 防火墙检测/提权放行（COM 读规则 + UAC 自重启加规则）
│   ├── NetworkHelper.cs      # 局域网 IPv4 枚举（Wi‑Fi 优先）
│   └── FileSizeFormat.cs     # 文件大小可读化
├── Web/
│   ├── ShareWebServer.cs     # Kestrel 服务器：路由 + 路径安全 + 传输日志 + rev 计数
│   └── index.html            # 手机端单页（内嵌程序集，零外部依赖、离线可用）
└── tools/
    └── ServerSmoke/          # 冒烟测试：14 项自动化校验（--hold 可挂起供设计检查）
```

## HTTP 接口一览

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/` | 手机端网页（内嵌 HTML） |
| GET | `/api/info` | 主机名 / 端口 / **rev（共享版本号）/ uploads（上传计数）**，网页轮询它实现实时刷新 |
| GET | `/api/roots` | 共享根列表 + 「接收的文件」虚拟根 |
| GET | `/api/items/{id}?path=` | 列出某共享根（或其子文件夹）内容 |
| GET | `/dl/{id}?path=&disposition=inline` | 下载文件；`inline` 用于预览/缩略图；支持 Range |
| POST | `/upload` | multipart 表单上传，字段名 `files`，可多文件 |

`{id}` 为共享项 GUID；`uploads` 固定指向接收文件夹。

## 网页端设计说明

手机端页面按 `design-taste-frontend` 设计规范构建（该技能库为本地开发工具，随工作区使用、不入库）：

- **方向**：`product-performs`（产品在你面前干活）——页面主体就是真实的共享文件列表和照片，而非产品截图
- **色板**：结构沿用发牌比例（近黑地面 / 单一步进表面 / 单一强调色），颜色取自 QookiL 品牌标志的赤陶色系（#cb7740 强调色即 logo 主色）；检查时按「既有品牌」特例 `--allow palette-terracotta` 放行
- **字体**：发牌字体 Bricolage Grotesque 因本机无法访问 Google Fonts（离线环境）回退为系统字体栈，已如实记录
- **门禁**：lint 0 FAIL / 0 WARN；inspect 三轮修复后 0 FAIL（图片底线 6 张真实照片、动效 32 个、对比度 7.3:1、无溢出）；剩余 1 项 no-footage WARN 为可辩护项（离线无法获取视频素材，且以用户真实照片的慢速缩放作为页面动态）

## 冒烟测试

```powershell
dotnet run --project tools/ServerSmoke -c Release          # 17 项接口校验
dotnet run --project tools/ServerSmoke -c Release -- 5199 hold   # 挂起服务器供手动/设计检查
```

覆盖：首页 / 共享列表 / 子目录 / 图片内联（缩略图）/ 下载字节一致性 / Range 206 / 越界拦截 400 / 未知共享 404 / 上传落盘 / rev 版本号变化（实时刷新机制）/ 防火墙三态检查 / 网卡排序（虚拟网卡在后）/ 系统下载文件夹解析。

## CI 自动构建与发布

- **推送 `main` / PR**：[GitHub Actions](.github/workflows/ci.yml) 自动执行 还原 → Release x64 编译 → 17 项冒烟测试 → 自包含发布 → 构建产物上传为 Artifact（保留 14 天，Actions 页面下载）；也支持手动触发
- **推送 `v*` 标签**（如 `git tag v1.0.0 && git push origin v1.0.0`）：[release.yml](.github/workflows/release.yml) 自动构建 → 冒烟测试 → 同时产出**安装器 exe（Inno Setup，中文向导、开始菜单/桌面快捷方式、可选开机自启、卸载自动清理防火墙规则）**和**绿色版 zip** → 创建 GitHub Release 并附上两个文件，在仓库 Releases 页面直接下载

注意：CI 会把 `NuGet.config` 临时替换为 nuget.org 官方源（本地保留国内镜像配置，互不影响）。

## 安全说明

本工具定位是**可信局域网内的临时快传**：没有内置鉴权，同一局域网内任何人都能访问共享内容。防火墙放行规则覆盖域/专用/公用三类网络（保证家庭 Wi‑Fi 被 Windows 归类为“公用”时也能用），因此在公共 Wi‑Fi 下运行时共享内容对同网络的所有人可见——用完随手「停止服务」或清空共享列表；不放行公用网络的话，把规则 profile 改为 `private,domain` 即可（代价是网络被归类为公用时设备连不上）。
