; QookiL 安装脚本（Inno Setup）
; CI 中由 ISCC.exe 命令行编译，产物 QookiL-Setup-x64.exe

#define MyAppName "QookiL"
#define MyAppNameZh "局域网快传"
#define MyAppVersion GetVersionNumbersString("publish\QookiL.exe")
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
UninstallDisplayName={#MyAppName}（{#MyAppNameZh}）
OutputDir=installer
OutputBaseFilename=QookiL-Setup-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
; 卸载时把防火墙规则一并清掉（安装的 exe 路径对应的那条）
UninstallFilesDir={app}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Languages\Default.isl"

[CustomMessages]
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
chinesesimplified.AutoStartProgram=开机自动启动(&S)
chinesesimplified.LaunchProgram=立即运行 {#MyAppName}
chinesesimplified.UninstallFirewall=清理防火墙放行规则
english.CreateDesktopIcon=Create a &desktop shortcut
english.AutoStartProgram=Start {#MyAppName} automatically at Windows startup
english.LaunchProgram=Launch {#MyAppName}
english.UninstallFirewall=Remove firewall allow rule

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutoStartProgram}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{MyAppExeName}"; Tasks: desktopicon

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
