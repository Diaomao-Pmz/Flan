using UnityEngine;

/// <summary>
/// Boss 的基础移动：基础移速、撞墙检测、被击退。
/// 拖到 Boss 身上 BossController 的 Movement Config 栏位。运行时只读。
/// </summary>
[CreateAssetMenu(fileName = "BossMovementConfig", menuName = "Flandre/Boss/Movement Config")]
public class BossMovementConfig : ScriptableObject
{
    [Header("--- 移速 ---")]
    [Tooltip("Boss 的基础移动速度（格/秒）。\n" +
             "战斗节奏（Tempo Config）里每个姿态的 Move Speed Multiplier 是在这个值上乘的倍率")]
    [Min(0f)]
    public float baseMoveSpeed = 3f;

    [Header("--- 撞墙检测 ---")]
    [Tooltip("走位时往前打的撞墙射线长度（格）")]
    public float wallCheckDistance = 1.5f;

    [Tooltip("墙壁 / 死角的图层。冲刺、突进斩、被推动时也用它判断墙")]
    public LayerMask wallLayer;

    [Header("--- 被击退 ---")]
    [Tooltip("被玩家的招式击退时，滑多久（秒）。滑多远由招式决定（ComboNode 的 Boss Knockback Distance）。\n" +
             "速度由快到慢。招式还带硬直（Boss Hit Stun）时取两者里长的那个，结束后回到移动状态重新做决定。时间越长显得越「重」")]
    [Min(0.02f)]
    public float knockbackDuration = 0.25f;
}
