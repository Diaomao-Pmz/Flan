using UnityEngine;

/// <summary>冲刺卡往哪冲</summary>
public enum DashMoveTarget
{
    ToDistance,     // 冲到离玩家某个距离：太远就冲近，太近就往后冲
    AwayFromPlayer, // 后撤：远离玩家冲固定格数
    ThroughPlayer   // 穿过玩家：冲到玩家身后
}

/// <summary>
/// 冲刺卡：纯位移、不带攻击（「冲过去再砍」是突进斩 DashAttackNode）。
/// 用法和传送卡一样：放进 BossAIDecider 的位移卡池（太远 / 太近 / 撞墙 / 随机），或者放进套路的某一步。
/// 冷却用卡上的 Cooldown：位移卡池看冷却；套路里不看冷却，但照样记冷却。
/// 由 BAE_Dasher 执行。
/// </summary>
[CreateAssetMenu(fileName = "NewDashMoveNode", menuName = "ScriptableObjects/DashMoveNode")]
public class DashMoveNode : ActionNode
{
    [Header("--- 冲刺 ---")]
    [Tooltip("往哪冲：\n" +
             "To Distance      = 冲到离玩家某个距离（太远就冲近，太近就往后冲）\n" +
             "Away From Player = 后撤：远离玩家冲固定格数\n" +
             "Through Player   = 穿过玩家，冲到他身后")]
    public DashMoveTarget target = DashMoveTarget.ToDistance;

    [Tooltip("冲到离玩家多远（水平，格）。\n" +
             "-1 = 跟下一招的理想距离（套路里 = 下一步那招；位移卡池里 = 当前意图那招）。\n" +
             "套路最后一步填 -1 时后面没有招了，按 5 格算")]
    [Min(-1f)]
    public float landingDistance = -1f;

    [Tooltip("后撤多少格")]
    [Min(0f)]
    public float retreatDistance = 4f;

    [Tooltip("冲到玩家身后多少格（水平，从玩家身体中心算）")]
    [Min(0f)]
    public float behindDistance = 2.5f;

    [Tooltip("冲刺用时（秒）。每次冲刺节奏一样：速度 = 这次要冲的距离 ÷ 用时。\n" +
             "要穿到玩家身后时，用时到了也会按平均速度继续走，直到穿出来")]
    [Min(0.01f)]
    public float dashDuration = 0.25f;

    [Tooltip("一次最远冲多少格。要冲的距离超过它就只冲这么远")]
    [Min(0f)]
    public float maxDashDistance = 10f;

    [Tooltip("冲之前停多久（秒），给玩家读招")]
    [Min(0f)]
    public float windup = 0.2f;

    [Tooltip("冲完停多久（秒），再做下一件事")]
    [Min(0f)]
    public float pauseAfter = 0.1f;

    [Tooltip("要冲的距离小于这个（格）就不冲了，免得原地抖一下")]
    [Min(0f)]
    public float minDashDistance = 0.3f;

    [Header("--- 终点判定（不落进玩家身体，规则同玩家的 PlayerMovementConfig）---")]
    [Tooltip("勾上 = 冲刺期间每个物理帧检查：按剩下的路冲完，会不会停在玩家身体里。\n" +
             "会的话：能穿过玩家且已穿过一定比例 → 送到玩家身后；否则停在玩家面前（两个碰撞箱边贴边）。\n" +
             "起步时就和玩家贴在一起 → 不拦，让 Boss 冲出来")]
    public bool endpointGuard = true;

    [Tooltip("能不能穿过玩家：勾上 = 冲刺期间 Boss 和玩家互不碰撞（冲完还叠着就等分开再恢复，最多 0.5 秒）。\n" +
             "冲刺方式选 Through Player 时固定能穿")]
    public bool canPassThrough = false;

    [Tooltip("穿过比例：终点要穿进玩家身体多深才算「穿过去了」（0.5 = 过了玩家中线）。\n" +
             "调高 = 更难穿过去（更常停在面前）；调低 = 更容易穿过去")]
    [Range(0f, 1f)]
    public float endpointPassRatio = 0.5f;

    [Tooltip("重叠容差（格）：终点和玩家只重叠这么浅时当作没撞上，照常冲完")]
    [Min(0f)]
    public float endpointOverlapTolerance = 0.1f;

    /// <summary>这次冲刺实际能不能穿过玩家</summary>
    public bool CanPass => target == DashMoveTarget.ThroughPlayer || canPassThrough;

    [Header("--- 动画（可选）---")]
    [Tooltip("冲刺中的动画名。留空则不切换（前摇用 Charge Anim Name，冲完用 Recover Anim Name）")]
    public string dashAnimName;

    /// <summary>新建冲刺卡时射程默认给到 0~99：套路每一步出手前都会查射程，冲刺一般不该因为够不着被拦</summary>
    private void Reset()
    {
        minCastDistance = 0f;
        maxCastDistance = 99f;
    }
}
