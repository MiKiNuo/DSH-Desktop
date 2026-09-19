; DSH-Desktop Inno Setup 安装脚本
; 构建：ISCC.exe /DMyAppVersion=<x.y.z> installer/DshDesktop.iss（release.yml 以 git tag 传入）
; AppId 必须与 GitHubDesktopUpdater 中的卸载注册表探测键一致。

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName "DSH-Desktop"
#define MyAppExe "DshDesktop.App.exe"
#define PublishDir "..\src\DshDesktop.App\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{8F3A2C1E-7B4D-4E6F-9A1B-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=MiKiNuo
; 默认目录见 [Code] GetDefaultDir：优先 D:\Program Files（无 D 盘回退系统 Program Files）。
DefaultDirName={code:GetDefaultDir}
PrivilegesRequired=admin
DisableDirPage=no
OutputDir=..\releases
OutputBaseFilename=DSH-Desktop-Setup-{#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExe}
; 安装程序自身图标（鲸鱼，与应用 exe 内嵌图标同源；相对脚本目录解析）。
SetupIconFile=..\src\DshDesktop.App\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
; 语言文件随仓库自带（CI 的 choco Inno 不含 ChineseSimplified.isl，v0.1.3 发布失败根因）。
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; ADR-0009：数据根随安装根（<安装根>\data）。Program Files 下标准用户默认不可写，
; 预建并赋 users-modify ACL，运行期所有落盘（config / logs / runtime / dsh-home）才可写。
; 卸载时不删除（不在 [UninstallDelete]）：用户数据保留。
Name: "{app}\data"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Run]
; 交互安装：结束页"立即运行"勾选框（以原始用户身份，避免以管理员启动）。
Filename: "{app}\{#MyAppExe}"; Description: "立即运行 {#MyAppName}"; Flags: postinstall skipifsilent runasoriginaluser nowait unchecked
; 静默更新（应用内自更新 /VERYSILENT）：装完直接以原始用户身份拉起新版。
Filename: "{app}\{#MyAppExe}"; Flags: skipifnotsilent runasoriginaluser nowait

[Code]
function GetDefaultDir(Param: String): String;
begin
  if DirExists('D:\') then
    Result := 'D:\Program Files\DSH-Desktop'
  else
    Result := ExpandConstant('{autopf}\DSH-Desktop');
end;
