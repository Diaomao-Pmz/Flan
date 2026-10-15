using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【穿身开关】—— 冲刺 / 滑铲期间，让玩家的身体可以穿过敌人。
    ///
    /// ==========================================================
    /// 比喻：做"闪身动作"的这一小段时间里，敌人的身体对玩家来说是空气；
    ///       动作做完，身体又变回实的。
    ///
    /// 【怎么做到的】
    /// Rigidbody2D 自带 Exclude Layers（排除图层）：列在里面的图层不会和这个刚体产生碰撞。
    /// 需要穿身时把 PlayerMovementConfig.passThroughLayers 加进去，不需要时拿掉。
    /// 只增减自己加进去的那几位，不会覆盖你在 Inspector 里原本配的 Exclude Layers。
    ///
    /// 【为什么每个物理帧问一次，而不是在状态 Enter/Exit 里开关】
    /// 地面冲刺、滑铲是状态，但飞行冲刺写在 FlightMotor 里，不是状态。
    /// 统一由状态机每帧回答"现在是不是在冲刺/滑铲"，就不会出现某条退出路径漏了关闭、
    /// 导致玩家永久穿身的问题。
    ///
    /// 【防卡身体】动作结束时如果玩家还和敌人重叠，先别恢复碰撞，
    /// 等分开了再恢复（最多等 MaxOverlapGrace 秒）。
    /// 否则物理引擎会把玩家硬弹出去，看起来像被撞飞。
    /// ==========================================================
    /// </summary>
    public class PassThroughController
    {
        /// <summary>动作结束后仍与敌人重叠时，最多再穿身这么久（秒），超时就强制恢复碰撞</summary>
        private const float MaxOverlapGrace = 0.5f;

        private readonly Rigidbody2D rb;
        private readonly BoxCollider2D body;

        private bool active;
        private int addedBits;     // 本开关额外加进 excludeLayers 的那几位（原本就有的不算）
        private float graceTimer;

        /// <summary>当前是否处于穿身状态</summary>
        public bool IsActive => active;

        public PassThroughController(Rigidbody2D rigidbody, BoxCollider2D bodyCollider)
        {
            rb = rigidbody;
            body = bodyCollider;
        }

        /// <summary>
        /// 每个物理帧调用一次（由 PlayerStateMachine.FixedUpdate 调用）。
        /// </summary>
        /// <param name="wanted">此刻是否在冲刺 / 滑铲</param>
        /// <param name="layers">可以穿过的图层（PlayerMovementConfig.passThroughLayers）</param>
        public void FixedTick(bool wanted, LayerMask layers, float deltaTime)
        {
            if (rb == null) return;

            // 没配图层 = 功能关闭
            if (layers.value == 0)
            {
                Deactivate();
                return;
            }

            if (wanted)
            {
                graceTimer = 0f;
                if (!active) Activate(layers);
                return;
            }

            if (!active) return;

            // 动作已结束：还卡在敌人身体里就再等等，分开了（或等太久）就恢复碰撞
            if (graceTimer < MaxOverlapGrace && IsOverlapping(layers))
            {
                graceTimer += deltaTime;
                return;
            }

            Deactivate();
        }

        /// <summary>强制恢复碰撞（复活、重开局等场合）</summary>
        public void ForceRestore() => Deactivate();

        private void Activate(LayerMask layers)
        {
            int current = rb.excludeLayers.value;
            addedBits = layers.value & ~current;   // 只记自己新加的位
            rb.excludeLayers = current | layers.value;
            active = true;
        }

        private void Deactivate()
        {
            if (!active) return;

            rb.excludeLayers = rb.excludeLayers.value & ~addedBits;
            addedBits = 0;
            graceTimer = 0f;
            active = false;
        }

        private bool IsOverlapping(LayerMask layers)
        {
            if (body == null) return false;

            Bounds b = body.bounds;
            return Physics2D.OverlapBox(b.center, b.size, 0f, layers) != null;
        }
    }
}
