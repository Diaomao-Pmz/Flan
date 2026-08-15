using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 固定方向激光。
/// 时序：预警（锁定角度、细线提示）→ 开火（粗光束、周期性伤害）→ 收招。
/// 角度在预警开始的瞬间锁定，之后不再跟踪 —— 玩家可以靠位移躲开。
///
/// 【外观归卡片管】beamSprite 与两个颜色放在这里而不是执行器上，
/// 这样红激光、蓝激光就是两张卡两个 Sprite，不需要改代码或加第二个执行器。
/// </summary>
[CreateAssetMenu(fileName = "NewLaserNode", menuName = "ScriptableObjects/LaserNode")]
public class LaserNode : ActionNode
{
    public override ActionCategory DefaultCategory => ActionCategory.SpecialBullet;

    [Header("--- 时序 ---")]
    [Tooltip("预警时长（秒）。玩家靠这段时间读招走位，是主要的难度旋钮。")]
    public float telegraphTime = 1.0f;

    [Tooltip("激光持续时长（秒）")]
    public float fireTime = 1.5f;

    [Tooltip("收招硬直（秒）。玩家的输出窗口。")]
    public float recoverTime = 0.6f;

    [Header("--- 形状 ---")]
    [Tooltip("激光最大长度。被墙挡住时会自动截断。")]
    public float maxLength = 25f;

    [Tooltip("激光判定宽度。同时也是开火阶段的视觉宽度。")]
    public float beamWidth = 1.0f;

    [Tooltip("预警线的视觉宽度（不参与判定）")]
    public float telegraphWidth = 0.12f;

    [Tooltip("锁定角度时的额外偏转（度）。0 表示正对玩家；配非 0 可以做故意打偏的假动作。")]
    public float aimAngleOffset = 0f;

    [Header("--- 外观 ---")]
    [Tooltip("光束贴图。留空则沿用 SpriteRenderer 上已有的 Sprite。\n" +
             "要求：Pivot = Left/Center、Mesh Type = Full Rect、Wrap Mode = Repeat。")]
    public Sprite beamSprite;

    [Tooltip("预警阶段的染色")]
    public Color telegraphColor = new Color(1f, 0.9f, 0.3f, 0.6f);

    [Tooltip("开火阶段的染色。用白色则完全显示贴图原色。")]
    public Color fireColor = Color.white;

    [Header("--- 伤害 ---")]
    [Tooltip("每一跳的伤害")]
    public int damagePerTick = 12;

    [Tooltip("伤害间隔（秒）。激光是持续伤害，不能每帧扣血。")]
    public float damageTickInterval = 0.25f;
}