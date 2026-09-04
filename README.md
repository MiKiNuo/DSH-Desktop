# DSH-Desktop

DSH 的跨平台桌面宿主：Avalonia 12 + MiKiNuo.MVI + .NET 10。

- 架构基线：`docs/DSH-Desktop-MVI-Architecture.md`
- 领域词汇表：`CONTEXT.md`
- 架构决策记录：`docs/adr/`
- MVI 库（0.3.5）调研：`docs/MiKiNuo-Mvi-API-Research.md`、`docs/MiKiNuo-Mvi-Usage-Patterns.md`

## 解决方案结构

```
src/
├── DshDesktop.Domain/                  纯业务模型（RuntimeLifecycle 等）
├── DshDesktop.Application/             编排层接口（IRuntimeOrchestrator，Phase 1 最小边界）
├── DshDesktop.Infrastructure/          OS / 进程 / 文件 / HTTP 适配器（Phase 1b 填充）
├── DshDesktop.Presentation.Avalonia/   MVI Features（AppShell / Runtime / Workbench）
└── DshDesktop.App/                     引导：Program / App / MainWindow / 组合根
```

依赖方向：App → Presentation / Infrastructure → Application → Domain（单向）。

## 构建

```powershell
dotnet restore DshDesktop.slnx
dotnet build DshDesktop.slnx
dotnet run --project src/DshDesktop.App
```

说明：`Directory.Build.props` 中关闭了 `MSBuildEnableWorkloadResolver`——本机
.NET SDK 缺少 workload locator SDK 目录，开启时 restore 会以 MSB4276 失败；
本项目不使用 .NET Workload。

## Native AOT 冒烟

```powershell
dotnet publish src/DshDesktop.App -c Release -r win-x64 -p:PublishAot=true
```

## 运行配置

首次启动时在 exe 旁生成 `dsh-desktop.config.json`（自动探测结果，可手改）：

- `nodePath` / `dshEntryPath` / `harnessNodeEntryPath`：默认指向本机 Electron 版 DSH Desktop 的 vendored 运行时（扫描所有固定盘的 `Program Files\DSH Desktop\resources`）
- `dshHome`：`%LOCALAPPDATA%\DshDesktop\data\dsh-home`（独立数据目录，与 Electron 版隔离）
- `seedProfileFrom`：非空且目标缺失时，首启一次性从既有 harness 复制 `profiles\web` 作为种子（排除 DSH 自管的 `.dsh-module-fallback`）
- `port`：0 = 启动时探测空闲端口（ADR-0001）

行为：窗口秒开 → Runtime 后台自动启动 → Workbench 页加载 DSH Web UI（含 token 的 Session URL）→ 关闭窗口回收 DSH 进程树（ADR-0002）。

## Phase 状态

- [x] Phase 1a：解决方案骨架 + AppShell/Runtime/Workbench MVI 空壳跑通
- [x] Phase 1b：Runtime 启停（DshProcessHost + 配置化路径 + 动态端口）+ WebView 加载 DSH Web UI
- [x] Phase 2a：RuntimeSupervisor（5s HTTP 健康轮询、连续 3 次失败判 Unresponsive）、启动分阶段计时、§17 阶段清单 UI、崩溃检测（Running/Starting 中进程退出 → Failed）
- [x] Phase 2b：Diagnostics 事件流 Feature（DSH stdout/stderr + Supervisor + App 结构化事件 → 1000 条环形 UI + Serilog 滚动日志到 `data/logs/`，token 打码）
- [x] Phase 3a：插件清单与管控（列表/禁用/启用/卸载，纯文件级，DSH 不启动也可用）+ 安全模式（降级管理台，跨重启持久）+ 官方核心插件只读守卫
- [x] Phase 3b：插件安装事务（§19 全链路实测：快照→停→装→校验→启→健康→提交；坏插件触发回滚→恢复→重启）+ 一键全禁第三方并启动恢复动作 + 清单快照（`data/backups/`，留 5 份）
- [x] Phase 4：Update Center（DSH Runtime 自建 side-by-side 安装/激活/回退 + 插件更新检查与事务更新；npm 安装器；latest/alpha 通道）。已知限制：Runtime 升级可能破坏旧版第三方插件（实测 alpha.1→alpha.5 时 dsh-better-sidebar 不兼容），用安全模式/全禁恢复
- [ ] Phase 5：Desktop 更新（Velopack）+ Native AOT Release + CI/CD
- [ ] Phase 5：Native AOT Release + CI/CD + Velopack
