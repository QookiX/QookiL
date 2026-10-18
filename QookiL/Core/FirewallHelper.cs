using System.Collections;
using System.Diagnostics;

namespace QookiL.Core;

/// <summary>防火墙检查结论：Allowed=确定已放行；Blocked=确定未放行；Unknown=读不出来。</summary>
public enum FirewallVerdict { Allowed, Blocked, Unknown }

public sealed record FirewallInfo(FirewallVerdict Verdict, string Details)
{
    /// <summary>是否需要（重新）设置放行规则。</summary>
    public bool NeedsSetup => Verdict != FirewallVerdict.Allowed;
}

/// <summary>
/// Windows 防火墙入站放行。
/// 读取规则不需要管理员；添加/删除规则需要管理员（UAC）。
/// 主实例检测到未放行时，以管理员身份重新启动自己（--firewall-setup）完成设置。
/// </summary>
public static class FirewallHelper
{
    /// <summary>提权辅助模式的命令行参数。</summary>
    public const string SetupArg = "--firewall-setup";

    // NET_FW_PROFILE_TYPE2 位掩码
    private const int ProfileDomain = 0x1;
    private const int ProfilePrivate = 0x2;
    private const int ProfilePublic = 0x4;
    private const int ProfileAll = ProfileDomain | ProfilePrivate | ProfilePublic;

    private const int DirectionIn = 1;          // NET_FW_RULE_DIRECTION_IN
    private const int ActionBlock = 0;          // NET_FW_ACTION_BLOCK（阻止规则优先于放行）
    private const int ActionAllow = 1;          // NET_FW_ACTION_ALLOW
    private const int ProfilesAny = 0x7FFFFFFF; // 域/专用/公用全部
    private const int ProtocolTcp = 6;

    private static dynamic? CreatePolicy()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
        return type is null ? null : Activator.CreateInstance(type);
    }

    private static string ProfileName(int mask) => mask switch
    {
        ProfileDomain => "域",
        ProfilePrivate => "专用",
        ProfilePublic => "公用",
        _ => "未知(" + mask + ")",
    };

    /// <summary>
    /// 读取防火墙与本程序规则的当前状态。结论是三态的：
    /// 读不到就是 Unknown，绝不猜成「已放行」——否则授权弹窗会被静默跳过，
    /// 用户只会看到「其他设备连不上」却不知道原因。
    /// </summary>
    public static FirewallInfo Check(string exePath)
    {
        try
        {
            var policy = CreatePolicy();
            if (policy is null)
                return new FirewallInfo(FirewallVerdict.Unknown, "无法访问 Windows 防火墙组件");

            int current = Convert.ToInt32(policy.CurrentProfileTypes) & ProfileAll;
            if (current == 0) current = ProfilePrivate;

            // 各网络类型的防火墙开关：只用于说明文字，读失败不影响结论
            string switchText;
            try
            {
                var parts = new List<string>();
                foreach (var bit in new[] { ProfileDomain, ProfilePrivate, ProfilePublic })
                {
                    if ((current & bit) == 0) continue;
                    parts.Add(ProfileName(bit) + (Convert.ToInt32(policy.FirewallEnabled[bit]) != 0 ? "已启用" : "已关闭"));
                }
                switchText = string.Join("，", parts);
            }
            catch
            {
                switchText = "开关状态读取失败";
            }

            var head = $"当前网络类别「{ProfileName(current)}」，防火墙{switchText}";

            // 遍历指向本程序的入站规则（全部是普通属性，比带参数的属性更稳）
            bool allowed = false, blocked = false;
            int matched = 0;
            var ruleNames = new List<string>();
            foreach (var raw in (IEnumerable)policy.Rules)
            {
                dynamic rule = raw;
                try
                {
                    var app = (string?)rule.ApplicationName;
                    if (string.IsNullOrEmpty(app)) continue;
                    if (!string.Equals(app, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Convert.ToInt32(rule.Enabled) == 0) continue;
                    if (Convert.ToInt32(rule.Direction) != DirectionIn) continue;
                    if ((Convert.ToInt32(rule.Profiles) & current) != current) continue;
                    matched++;
                    ruleNames.Add((string?)rule.Name ?? "(未命名)");
                    if (Convert.ToInt32(rule.Action) == ActionBlock) blocked = true;
                    else allowed = true;
                }
                catch { /* 个别系统规则属性不可读，跳过 */ }
            }

            var ruleText = matched == 0
                ? "没有本程序的放行规则"
                : $"规则 {matched} 条（{string.Join("、", ruleNames.Take(3))}）";

            if (blocked)
                return new FirewallInfo(FirewallVerdict.Blocked, $"{head}；{ruleText}，其中含阻止规则");
            if (allowed)
                return new FirewallInfo(FirewallVerdict.Allowed, $"{head}；{ruleText}");
            return new FirewallInfo(FirewallVerdict.Blocked, $"{head}；{ruleText}");
        }
        catch (Exception ex)
        {
            return new FirewallInfo(FirewallVerdict.Unknown, $"读取防火墙状态失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 以管理员身份重新启动本程序完成防火墙设置（会弹一次 UAC 确认框）。
    /// </summary>
    public static async Task<bool> RunElevatedSetupAsync(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = SetupArg,
                Verb = "runas",
                UseShellExecute = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0;
        }
        catch (OperationCanceledException) { return false; }               // 超时
        catch (System.ComponentModel.Win32Exception) { return false; }     // UAC 被用户取消
        catch { return false; }
    }

    /// <summary>
    /// 提权辅助模式里执行：删除本程序的全部旧规则（可能包含此前误点「取消」产生的
    /// 阻止规则——它的优先级高于放行规则，不清掉的话加了放行也没用），再添加一条
    /// 仅作用于本程序的入站放行规则。返回 0 表示规则写入成功；是否真的放行由主实例复核。
    /// </summary>
    public static int ApplyRules(string exePath)
    {
        try
        {
            var policy = CreatePolicy()
                ?? throw new InvalidOperationException("无法访问 Windows 防火墙（HNetCfg.FwPolicy2）");

            // 1. 收集并删除指向本程序的所有旧规则
            var staleNames = new List<string>();
            foreach (var raw in (IEnumerable)policy.Rules)
            {
                dynamic rule = raw;
                try
                {
                    var app = (string?)rule.ApplicationName;
                    if (string.IsNullOrEmpty(app)) continue;
                    if (!string.Equals(app, exePath, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = (string?)rule.Name;
                    if (!string.IsNullOrEmpty(name)) staleNames.Add(name);
                }
                catch { /* 跳过不可读的规则 */ }
            }
            foreach (var name in staleNames.Distinct(StringComparer.OrdinalIgnoreCase))
                policy.Rules.Remove(name);

            // 2. 添加放行规则：仅本程序、仅入站、所有网络类型、TCP
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FwRule")
                ?? throw new InvalidOperationException("无法创建防火墙规则对象（HNetCfg.FwRule）");
            dynamic newRule = Activator.CreateInstance(ruleType)!;
            newRule.Name = "QookiL";
            newRule.Description = "QookiL 局域网快传：允许局域网设备访问";
            newRule.ApplicationName = exePath;
            newRule.Direction = DirectionIn;
            newRule.Action = ActionAllow;
            newRule.Enabled = true;
            newRule.Profiles = ProfilesAny;
            newRule.Protocol = ProtocolTcp;
            policy.Rules.Add(newRule);

            DiagLog.Write("[fw] 辅助进程：放行规则已写入");
            return 0;
        }
        catch (Exception ex)
        {
            DiagLog.Write("[fw] 辅助进程失败：" + ex.Message);
            return 1;
        }
    }
}
