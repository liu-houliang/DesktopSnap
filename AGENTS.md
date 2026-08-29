# DesktopSnap - 开发规范与发布 SOP

本文档为 AI Agent 提供核心开发规范与发布流程约束。

---

## 1. 国际化规范（I18n）
- **严禁硬编码**：所有面向用户的 UI 字符串必须注册在 `I18n.cs` 中。
- **双语同步**：新增词条必须同时提供中文 (`zh`) 与英文 (`en`)。

---

## 2. 坐标系规范
- **图标坐标**：Explorer `SysListView32` 读写的 `IconInfo.X / IconInfo.Y` 处于控件客户区空间（原点为虚拟桌面外接矩形左上角，始终 $\ge 0$）。
- **屏幕坐标**：`DisplayManager` 读取的 `DisplayInfo` 处于 Windows 虚拟屏幕坐标系（主屏左上角为 `0, 0`，左侧/上方副屏可能存在负坐标）。
- **UI 预览绘制**：必须以 `totalMinX = displays.Min(d => d.Left)` 与 `totalMinY = displays.Min(d => d.Top)` 为全局基准对齐。

---

## 3. 版本升级与发布流程 SOP

### 版本升级提交规范（Version Bump Commit）
版本升级提交（`chore: 更新版本号至X.Y.Z`）必须且仅包含以下 3 个核心文件的修改：
1. `DesktopSnap.csproj`：更新 `<Version>X.Y.Z</Version>`（3 位）
2. `Package.appxmanifest`：仅更新 `<Identity Version="X.Y.Z.0" />`（4 位），**严禁改动 `<TargetDeviceFamily MinVersion="..." />`**
3. `I18n.cs`：更新 `AboutChangelog` 词条的**中英文双语本版更新说明**

关联同步文件：
- `desktopsnap-web/config.js`：同步更新 `version: "vX.Y.Z"` 与 `file_size: "... MB"`

### 发布执行规范
- 日常功能与 Bug 修复代码单独提交，版本号升级与发布说明单独提交并打 Tag。
- 运行根目录下的自动化构建脚本：`./publish.ps1 -Version X.Y.Z`。
- 构建输出将统一放入 `dist/` 目录（已加入 `.gitignore`）。
- **Git 推送原则**：自动化脚本只执行本地 commit 与打 Tag，**严禁自动向远端执行 `git push`**，由用户确认后手动推送。

---

## 4. 版本说明生成规范（Release Notes）

每次发布新版本必须同时生成以下两套说明文本：

### 4.1 GitHub Release Notes 格式规范
遵循分类 Emoji + `• 标题 - 描述` 结构，中英文部分使用 `---` 分隔：

```markdown
✨ 新增功能
• 功能标题 - 功能简要描述与操作说明

🚀 优化改进
• 优化标题 - 性能或体验优化说明

🐛 修复
• 缺陷标题 - 修复的具体问题说明

---

✨ New Features
• Feature Title - Description of the new feature

🚀 Improvements
• Improvement Title - Description of the improvement

🐛 Fixed
• Bug Title - Description of the fixed issue
```

### 4.2 微软商店发行说明规范（Microsoft Store Update Notes）
提供纯文本编号列表（中英文各一份），要求简洁明了，直接说明核心变化：

```
[中文 (zh-Hans)]
1. 新增...
2. 修复...
3. 优化...

[英文 (en-US)]
1. Added...
2. Fixed...
3. Improved...
```
