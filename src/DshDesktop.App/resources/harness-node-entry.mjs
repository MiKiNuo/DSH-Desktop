// DSH Desktop 自带的 Node harness 垫片（vendored 自 Electron 壳 resources\harness-node-entry.mjs，
// 并修复 dsh >=0.1.5-rc.1 兼容性：新版 bin.js 以 import.meta.main 守门，纯 import() 加载时
// 入口不会自执行，进程静默退出 0——这里在 import 后显式调用 runCli 导出）。
// 布局约定与 Electron 一致：本文件位于 <宿主根>\resources\，依赖的 koffi 等包经
// ./app/package.json 的 node_modules 向上查找解析（缺失时隐藏控制台能力静默降级，不阻塞启动）。
import childProcess from 'node:child_process'
import { syncBuiltinESMExports } from 'node:module'
import { pathToFileURL } from 'node:url'
import { enforceWindowsChildProcessHide } from './windows-child-process-hide.mjs'

// On macOS Harness runs inside an Electron utility process (TCC responsibility
// isolation), so `process.execPath` and `argv0` point at the Electron helper
// instead of a Node binary. Plugins re-invoke the dsh CLI through the
// executable running them — dsh-market forwards `process.execArgv` with it —
// and without Node mode that child boots as an Electron app, where the leading
// `--expose-internals` shifts argv and the CLI answers "--profile <name> is
// required" instead of installing. Declaring it here, after this process has
// already parsed the Chromium switches it was launched with, marks only the
// children as Node processes. Bundled-Node hosts (Windows, Linux) skip it.
if (process.versions.electron !== undefined) {
  process.env.ELECTRON_RUN_AS_NODE = '1'
}

const [dshEntryPath, ...dshArguments] = process.argv.slice(2)

function report(label, value) {
  process.stderr.write(`[harness-node] ${label}: ${value}\n`)
}

process.on('uncaughtException', (error) => report('uncaught exception', error?.stack ?? error))
process.on('unhandledRejection', (error) => report('unhandled rejection', error?.stack ?? error))

process.stdout.write(
  `[harness-node] runtime node=${process.version} platform=${process.platform} arch=${process.arch}\n`
)
process.stdout.write(`[harness-node] execPath=${process.execPath}\n`)
process.stdout.write(`[harness-node] cwd=${process.cwd()}\n`)
process.stdout.write(`[harness-node] DSH_HOME=${process.env.DSH_HOME ?? ''}\n`)

// Harness and the plugins running inside it spawn their own child processes
// (pwsh, git, ripgrep, …) without windowsHide — that flag on the Harness
// process itself only hides Harness's own console, not what it goes on to
// launch. Each of those visible console windows steals foreground focus on
// Windows. Patching child_process here, before dshEntryPath loads, catches
// every spawn made anywhere in this process tree — Harness internals and
// third-party plugins alike — without needing an upstream fix in each of
// them. A caller that explicitly sets windowsHide keeps its own choice.
if (process.platform === 'win32') {
  // The Harness is spawned console-less (detached + windowsHide), so child
  // console apps flash their own window unless the Harness owns a hidden
  // console for them to inherit (issue #233).
  const { createHiddenConsole } = await import('./windows-hidden-console.mjs')
  createHiddenConsole()

  enforceWindowsChildProcessHide(childProcess, syncBuiltinESMExports)

  process.stdout.write('[harness-node] windowsHide enforcement enabled for child processes\n')
}

if (!dshEntryPath) {
  report('startup error', 'missing DSH entry path')
  process.exitCode = 1
} else {
  process.stdout.write(`[harness-node] loading=${dshEntryPath}\n`)
  process.argv = [process.execPath, dshEntryPath, ...dshArguments]
  try {
    const entryModule = await import(pathToFileURL(dshEntryPath).href)
    process.stdout.write('[harness-node] DSH entry loaded\n')
    // dsh >=0.1.5-rc.1 的 bin.js 以 `if (import.meta.main)` 守卫自执行并导出 runCli；
    // 经 import() 加载时 import.meta.main 为 false，必须由宿主显式调用。
    // 旧版入口无 runCli 导出、import 即自执行，此分支跳过，行为与旧 harness 一致。
    if (typeof entryModule.runCli === 'function') {
      await entryModule.runCli()
    }
  } catch (error) {
    report('DSH entry failed', error?.stack ?? error)
    process.exitCode = 1
  }
}
