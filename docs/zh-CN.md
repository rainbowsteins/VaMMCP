# VamMCP 中文说明

[English README](../README.md) | [Agent 操作规则](../AGENTS.md?plain=1)

VamMCP 是一个面向 **Virt-A-Mate + vamX** 的非官方 [Model Context Protocol](https://modelcontextprotocol.io/) 集成。它由 Python MCP 服务和 Virt-A-Mate **Session Plugin** 组成，让支持 MCP 的 Agent 根据自然语言对话调用工具，操作游戏内的部分功能，例如切换人物/外观、服装、场景、姿势和面部表情。

本项目与 Mesh VR、Virt-A-Mate 或 vamX 没有隶属、背书或官方支持关系。你需要合法拥有并使用相关软件和内容。详见 [NOTICE.md](../NOTICE.md?plain=1)。

**本项目不能生成素材。** 它搜索并加载硬盘上已有的文件，并且可以把已安装的 morph、发型和服装**组合**成一个原创角色，保存到本地角色库。它不会创造你没有的 morph、贴图或发型网格，也不会自动下载 Hub 内容。

当前版本：Session 插件 `VamMcpBridge` 0.8.0，Python 包 `vam-mcp` 0.6.0。

## 工作原理

```text
MCP 客户端 -> vam-mcp（Python）-> 本地 JSON 文件桥 -> VamMcpBridge（Session Plugin）-> Virt-A-Mate + vamX
```

文件桥完全在本机工作，不会在 Unity 内启动 HTTP 服务。

## 使用要求

- Windows
- Virt-A-Mate 1.20+ + vamX，安装目录中必须包含 `VaM.exe`
- Python 3.10+
- 支持 MCP 的客户端或编程 Agent，例如 Codex、Claude Code、Cursor、Grok、Copilot
- VAM 安装目录中已经存在需要加载的 Look、场景、服装和姿势

## 先告诉 Agent 你的 VAM 目录

Agent 可以把桥接插件安装到标准的本地脚本路径，但它**不能猜测** VAM 安装位置。

安装前，请提供 **包含 `VaM.exe` 的文件夹完整路径**。这个路径就是 `VAM_ROOT`：

```text
VAM_ROOT\VaM.exe
VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs
```

路径必须包含盘符和完整目录名。Agent 会先确认 `VaM.exe` 存在，然后才复制文件。

## 安装顺序

VAM 插件和 Python MCP 服务必须同时安装。插件必须添加到 Session Plugins，不能添加到 Scene Plugins，也不能挂在 Person 上。

### 1. 在 VAM 中允许插件

1. 启动 VAM / VaMX。
2. 按 `Esc` 打开主界面。
3. 打开 User Preferences -> Security。
4. 开启 Enable Plugins。
5. 建议同时开启 Plugins Always Enabled。

如果没有开启插件权限，后面的 Add Plugin 可能没有入口或插件不会运行。

### 2. 安装 VamMcpBridge 插件

把 `VAM_ROOT` 发给 Agent，让它在仓库根目录执行：

```powershell
.\scripts\install-dev.ps1 -VamRoot "VAM_ROOT"
```

脚本会优先创建目录联接，无法创建时则复制插件文件。最终必须存在以下文件：

```text
VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs
```

这是推荐且受支持的安装方式。部分 VaMX 版本虽然能够显示 `.var` 包，却会在加载时提示包内的 `VamMcpBridge.cs does not exist`。遇到这种情况不要选择包路径，直接使用上面的本地 `Custom/Scripts` 路径。修改 `.cs` 文件后，需要在 Session Plugins 中对 VamMcpBridge 执行 Reload。

### 3. 在 Virt-A-Mate + vamX 中启用插件

这部分必须由用户在 VAM 界面中完成，Agent 不会代替用户点击 VAM UI。

下面的截图来自 Virt-A-Mate 1.22 + vamX 1.52，不同版本的界面位置可能略有差异。

1. 启动 Virt-A-Mate + vamX，在界面底部切换到 **Edit Mode (E)**。

   <img src="../1.png" alt="步骤 1：将 Virt-A-Mate 和 vamX 切换到 Edit Mode" width="760">

2. 点击左下方附近带有三条横线的主菜单按钮。

   <img src="../2.png" alt="步骤 2：打开 Virt-A-Mate 主菜单" width="760">

3. 选择紫色的 **Session Plugins**，然后点击 **Add Plugin**。必须使用主界面的 Session Plugins，不要添加到 Scene Plugins，也不要添加到 Person 身上的插件面板。

   <img src="../3.png" alt="步骤 3：打开 Session Plugins 并点击 Add Plugin" width="760">

4. 从 VAM 本地目录选择 `Custom/Scripts/VamMcp/Bridge/VamMcpBridge.cs`。不要选择 `VamMcp.Bridge.N:/...` 包路径。
5. 如果 Virt-A-Mate 出现插件许可提示，选择 Allow。
6. 确认插件行存在、`enabled` 已开启，状态显示类似 `ready`。
7. 打开 Session Plugin Presets -> Change User Defaults -> Set Current As User Defaults。

如果不保存为用户默认，下一次启动 VAM 时桥接插件不会自动加载。使用 MCP 时请保持 VAM 运行。

### 4. 安装 Python MCP 服务

进入本仓库的 `mcp` 目录：

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
```

`VAM_ROOT` 必须设置为你提供给 Agent 的、包含 `VaM.exe` 的文件夹。

也可以使用 `uv --directory mcp run vam-mcp`，跳过手动创建虚拟环境。

### 5. 配置 MCP 客户端

`command` 指向仓库虚拟环境中的 `mcp\.venv\Scripts\python.exe`。保存配置后，重启 Agent 或重新加载 MCP 服务列表。

请把本仓库作为工作区打开，这样 Agent 才能自动读取根目录中的 `AGENTS.md`。

Codex 可在 `%USERPROFILE%\.codex\config.toml` 中添加：

```toml
[mcp_servers.vam]
command = 'REPO\mcp\.venv\Scripts\python.exe'
args = ["-m", "vam_mcp.server"]

[mcp_servers.vam.env]
VAM_ROOT = 'VAM_ROOT'
```

将 `REPO` 和 `VAM_ROOT` 替换为真实路径。其他客户端的配置示例位于仓库的 `examples` 目录。

### 6. 连通测试

确认 VAM 已启动、VamMcpBridge 已作为 Session Plugin 加载，然后运行：

```powershell
.\scripts\ping-bridge.ps1 -VamRoot "VAM_ROOT"
```

返回内容中包含 `"ok": "true"` 和 `"plugin": "VamMcpBridge"`，说明本地文件桥已经连通。接着让 MCP 客户端调用一次 `status`。

## 日常使用

1. 先启动 VAM，等待 Session Plugin 状态变为 `ready`。
2. 在本仓库中启动 Agent，确保它能读取 `AGENTS.md`。
3. 直接用自然语言提出操作要求。

典型流程是 `list_*` -> 选择准确的 `path` -> `load_*`。两个人加进当前场景并应用成对姿势时使用 `setup_couple`。

常见请求示例：

- “当前场景里有哪些人物？”
- “搜索这个名字的 Look，并应用到第一个人物。”
- “把这两个人加入当前房间，并应用坐姿。”
- “把她的表情改成微笑。”
- “她的头在跟着镜头转，把头锁住。”
- “把他往左移半米，转过来面向她。”

## 工具概览

| 工具 | 用途 |
| --- | --- |
| `status` | 检查 `VAM_ROOT` 和桥接插件是否正常 |
| `list_scenes` / `load_scene` | 搜索或加载场景 |
| `list_persons` | 查看当前场景中的 Person |
| `add_person` / `remove_person` / `set_person_on` | 添加、删除、显示或隐藏人物 |
| `capture_view` | 保存当前画面预览（整个 VAM 窗口，含 UI）|
| `list_looks` / `load_look` | 搜索并应用外观预设 |
| `list_clothing` / `load_clothing` | 搜索并应用服装预设 |
| `list_poses` / `load_pose` | 搜索并应用姿势 |
| `list_expressions` / `set_expression` | 查看并设置面部表情 |
| `lock_head` | 防止头部继续跟随监视器镜头 |
| `get_position` / `move_person` | 读取并修改人物的世界坐标与朝向 |
| `setup_couple` | 一次设置两个人物外观和成对姿势 |
| `list_morphs` / `set_morphs` | 搜索并设置任意 morph |
| `list_geometry_options` / `set_geometry_options` | 发型 / 服装开关 |
| `save_character` / `list_characters` / `load_character` | 本地角色库 |
| `debug_cameras` | 诊断用：相机、剔除掩码、人物渲染器与层、geometry 参数 |

## 常见问题

| 现象 | 检查内容 |
| --- | --- |
| Agent 询问 `VAM_ROOT` | 提供包含 `VaM.exe` 的文件夹完整路径 |
| `status` 提示插件不存在 | VAM 未运行，或 Session Plugin 未添加、未设为用户默认 |
| MCP 工具超时 | 插件 `enabled` 已关闭、VAM 正在加载，或插件误加到了 Scene Plugins |
| Add Plugin 没反应 | 先开启 User Preferences -> Security -> Enable Plugins |
| 重启后插件消失 | 执行 Set Current As User Defaults |
| `VaM.exe not found` | `VAM_ROOT` 不是包含 `VaM.exe` 的文件夹 |
| 包路径提示 `VamMcpBridge.cs does not exist` | 运行 `.\scripts\install-dev.ps1 -VamRoot "VAM_ROOT"`，然后选择 `Custom/Scripts/VamMcp/Bridge/VamMcpBridge.cs` |
| 新增 Look 或 `.var` 后搜索不到 | 重启 MCP 服务，让目录缓存重新扫描 |
| `setup_couple` 姿势失败 | 使用 `list_poses` / `load_pose`，或确认所需成对姿势包已安装 |
| 提示 `unknown op: move_person` | 插件版本过旧，更新本地 `.cs` 后在 Session Plugins 中 Reload（需要 0.6.0+）|

## 安全和限制

- 本项目只处理本机文件，不会生成或下载角色、衣服、Look、姿势或场景。
- MCP 服务可以控制当前运行的 VAM 会话，并加载 `VAM_ROOT` 下可见的场景和预设。
- 不要将该 MCP 服务交给不可信的 Agent，也不要将它暴露到网络。
- 新增 `.var` 或 Look 文件后，必须重启 MCP 进程，目录缓存才会更新。
- `capture_view` 抓的是帧末的真实后台缓冲，分辨率等于窗口大小，且包含 VAM 的 UI。需要插件 0.6.2+；更早的版本用离屏渲染，会静默地把人物漏掉（VAM 的身体是 GPU 蒙皮，不在那次渲染里）。
- `load_scene` 在资源加载完成前就返回，紧接着截的图可能是加载画面，需要稍后重截。
- `list_persons` 的 `gender` 是按角色名猜的。名字看不出性别的男性角色会被判成女性，可能导致 `setup_couple` 把两人的角色搞反。
- **挑发型/服装/鞋子先看素材包自带的预览图** —— `.vam` 旁边通常有同名 `.jpg`，是作者在标准机位下渲染的。把它们抽出来对比，不要在场景里逐个穿戴现渲：那样受镜头位置影响、容易切歪，而且发型的名字看不出形状。
- 关闭发型/服装要用 `list_geometry_options` 返回的带 `hair:` / `clothing:` 前缀的开关名。`geometry.hair` 里的 `id` 是裸路径，传它会落进返回值的 `failed` 数组。改完之后回读 `activeInPrefix` 验证，不要只信调用本身的返回。
- 做原创角色只能组合已装素材。**族裔不是滑块** —— VAM 没有通用的"亚洲"morph（全库只有一个 `Eyelids asian`），必须从一个已安装的对应族裔 Look 出发再调整。
- 如果人物身上加载了 `TittyMagic`，`Breasts Small` 会被它归零。可靠的旋钮是 `Flat Chested`。
- 发色在名为 `<发型internalId>Sim` 的 storable 上，参数是 `rootColor` / `tipColor`（HSV），不是 morph。
- 没有专门工具的参数（材质、颜色）可以手写一个 `setUnlistedParamsToDefault: false` 的 `.vap`，只列出目标 storable 和参数，再用 `load_look` 灌入 —— 插件会按 id 匹配任意 storable。参数名到 `AddonPackages` 里已有的 preset 里去查，不要猜。
- MCP 服务不会删除最后一条 `command.json`。插件 0.6.1+ 会在加载时认领该 id 而不执行；更早的版本每次加载都会把这条陈旧命令重放一遍。
- `move_person` 只移动人物的根控制器。姿势如果绑定了家具，大幅移动后可能出现悬空，需要重新应用姿势。

仓库代码采用 [MIT License](../LICENSE)。Virt-A-Mate 仍受其自身 EULA 约束。
