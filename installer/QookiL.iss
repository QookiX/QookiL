; QookiL 安装脚本（Inno Setup）
; CI 中由 ISCC.exe 命令行编译，产物 QookiL-Setup-x64.exe
; 中文语言文件由 CI 从官方 Translations 仓库下载到本目录（ChineseSimplified.isl），
; 本地没有该文件时自动回退英文（保证脚本在任何环境都能编译通过）。

#define MyAppName "QookiL"
#define MyAppVersion GetVersionNumbersString("..\\publish\\QookiL.exe")
#define MyAppPublisher "weimosheng"
#define MyAppURL "https://github.com/weimosheng/QookiL"
#define MyAppExeName "QookiL.exe"

[Setup]
AppId={{7C1B4E92-5A6D-4C0A-9E38-QOOKIL2026}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; 相对 .iss 所在目录；CI 里输出到仓库根的 installer\ 下
OutputDir=.
OutputBaseFilename=QookiL-Setup-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

[Languages]
; 简体中文（官方翻译，随仓库携带，语言名以 .isl 内部定义为准）
Name: "ChineseSimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
; 英文（默认语言文案，ChineseSimplified 若存在会覆盖同名键）
english.CreateDesktopIcon=Create a &desktop shortcut
english.AutoStartProgram=Start {#MyAppName} automatically at Windows startup
english.LaunchProgram=Launch {#MyAppName}
ChineseSimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
ChineseSimplified.AutoStartProgram=开机自动启动(&S)
ChineseSimplified.LaunchProgram=立即运行 {#MyAppName}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"
Name: "autostart"; Description: "{cm:AutoStartProgram}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; HKCU Run：不需要管理员权限（配合 PrivilegesRequired=lowest，普通用户即可安装）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载时清理本程序路径对应的防火墙规则（静默失败也没关系：最多留一条无害的孤立规则）
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""QookiL"" program=""{app}\{#MyAppExeName}"""; \
    Flags: runhidden; RunOnceId: "DelFwRule"
