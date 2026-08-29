using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【位移出厂说明书】
    ///
    /// 铁律：运行时只读！任何代码都不许往这上面写值。
    /// 想在运行时改数值 —— 去 PlayerStats 贴一张 StatModifier「便利贴」，
    /// 而不是涂改这本说明书。
    ///
    /// 为什么？因为 ScriptableObject 是磁盘上的资产。
    /// 运行时往它字段上写值，在编辑器里会污染资产文件（退出 Play Mode 后残留），
    /// 在打包后会被所有实例共享。这正是 readme 里「警惕易失性数据与状态残留」的另一种死法。
    ///
    /// 所有默认值 = 2026-08 时 PlayerStateMachine Inspector 上的实际值。
    /// 右键 Create 出来的资产开箱即用，不需要手抄。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Flandre/Player/Movement Config")]
    public class PlayerMovementConfig : ScriptableObject
    {
        [Header("Run Settings")]
        [Tooltip("基础跑动速度。注意：这只是「基础值」，最终生效值请读 PlayerStats.moveSpeed.Value")]
        public float moveSpeed = 6f;

        [Header("Jump Settings")]
        [Tooltip("基础最大跳跃次数。目前仍会被 GemActionProcessor 在运行时覆写，阶段2 修复")]
        public int maxJumps = 1;
        public float jumpForce = 13f;
        [Tooltip("松开跳跃键时，向上速度被削减到的值（实现「按多久跳多高」）")]
        public float minJumpVelocity = 2f;

        [Header("Dash Settings")]
        public float dashSpeed = 12f;
        public float dashDuration = 0.2f;

        [Header("Slide Settings")]
        [Tooltip("滑铲初速度 = moveSpeed * 本倍率")]
        public float slideStartSpeedMultiplier = 1.2f;
        [Tooltip("滑铲减速率，数值越大减速越快")]
        public float slideDeceleration = 8f;
        [Tooltip("滑铲速度衰减到 moveSpeed * 本倍率 时，自动转入蹲下")]
        public float slideToCrouchSpeedMultiplier = 0.3f;

        [Header("Crouch Settings")]
        [Tooltip("蹲行移动速度 = moveSpeed * 本倍率（原先在 CrouchState 里硬编码为 0.5）")]
        public float crouchMoveSpeedMultiplier = 0.5f;
        [Tooltip("蹲下时碰撞体高度压缩比例（原先在 SetColliderHeight 里硬编码为 0.6）")]
        public float crouchColliderHeightMultiplier = 0.6f;

        [Header("Fly Settings")]
        [Tooltip("长按飞行键多少秒后进入飞行状态")]
        public float hoverChargeTime = 0.7f;
        public float flyManaCostPerSecond = 5f;
        [Tooltip("主动取消飞行时给予的向上冲力")]
        public float flyCancelJumpForce = 8f;
        public float flySpeed = 5f;

        [Header("Charge Settings")]
        [Tooltip(
            "蓄力期间的移动速度倍率。\n" +
            "0.2 = 只有平时的两成，1 = 蓄力不影响移动速度。\n" +
            "原先硬编码在 ChargeState 里，现在挪出来方便调手感。")]
        public float chargeMoveSpeedMultiplier = 0.2f;

        [Header("Fly 衍生 (打出蓄力招后点按 Q 跃起进飞行)")]
        [Tooltip("跃起阶段的速度")]
        public float flyLeapSpeed = 14f;

        [Tooltip("跃起阶段持续多久，之后转为正常飞行操控")]
        public float flyLeapDuration = 0.25f;

        [Tooltip("没有按方向键时，跃起默认朝哪个方向。(0,1)=正上方")]
        public Vector2 flyLeapDefaultDirection = new Vector2(0f, 1f);

        [Header("Sensor Settings (原先是散落在代码里的魔法数字)")]
        [Tooltip("地面检测盒尺寸，原 IsGrounded() 中硬编码 (0.5, 0.2)")]
        public Vector2 groundCheckBoxSize = new Vector2(0.5f, 0.2f);
        [Tooltip("地面检测射线距离，原 IsGrounded() 中硬编码 0.1")]
        public float groundCheckDistance = 0.1f;
        [Tooltip("头顶检测盒尺寸，原 CanStand() 中硬编码 (0.4, 0.1)")]
        public Vector2 ceilingCheckBoxSize = new Vector2(0.4f, 0.1f);
        [Tooltip("头顶检测射线距离，原 CanStand() 中硬编码 0.1")]
        public float ceilingCheckDistance = 0.1f;
    }
}