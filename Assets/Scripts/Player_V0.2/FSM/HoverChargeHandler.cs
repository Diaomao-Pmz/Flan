using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【悬停蓄力处理器】—— 长按飞行键在空中悬停，蓄满进入飞行。
    ///
    /// ==========================================================
    /// JumpState 和 FallState 里原本各有一份【几乎逐字相同】的实现，
    /// 加起来约 80 行重复代码。
    ///
    /// 这类重复最大的问题不是难看，而是：改一处忘改另一处是必然事件。
    /// 比如你调整了悬停时的重力处理，只改了 Jump 忘了 Fall，
    /// 就会出现"上升时能悬停、下落时不能"这种极难定位的手感 bug。
    ///
    /// 现在两张卡带共用同一份逻辑。
    /// ==========================================================
    ///
    /// 【释放锁机制】
    /// requireRelease 用来模拟旧输入系统的 GetKeyDown：
    /// 进入状态时如果飞行键已经按着（比如玩家一直按着 Q 起跳），
    /// 要求先松开一次才能开始蓄力 —— 否则起跳瞬间就会误触发悬停。
    /// </summary>
    public class HoverChargeHandler
    {
        public enum Result
        {
            /// <summary>没在蓄力，卡带该干嘛干嘛</summary>
            Idle,

            /// <summary>正在悬停蓄力中，卡带应当跳过自己的移动与重力逻辑</summary>
            Hovering,

            /// <summary>蓄满了，卡带应当立刻切到飞行状态</summary>
            EnterFly
        }

        private readonly PlayerStateMachine sm;

        private float holdTimer;
        private bool isHovering;
        private bool requireRelease;
        private float originalGravity;

        public bool IsHovering => isHovering;

        public HoverChargeHandler(PlayerStateMachine stateMachine)
        {
            sm = stateMachine;
        }

        /// <summary>卡带 Enter() 时调用</summary>
        public void OnEnter(float gravityToRestore)
        {
            holdTimer = 0f;
            isHovering = false;
            originalGravity = gravityToRestore;

            // 进入时就按着飞行键 → 上锁，要求先松开
            requireRelease = sm.playerController.isFlyHeld;
        }

        /// <summary>
        /// 卡带 Update() 里调用。
        /// 返回 Hovering 时，卡带应当 return，不要再跑自己的移动/重力逻辑。
        /// </summary>
        public Result Tick(float deltaTime)
        {
            bool flyHeld = sm.playerController.isFlyHeld;

            // 解锁：玩家松开了飞行键
            if (requireRelease)
            {
                if (!flyHeld) requireRelease = false;
            }

            if (!requireRelease && flyHeld)
            {
                if (!isHovering)
                {
                    isHovering = true;
                    sm.rb.gravityScale = 0f;
                    sm.rb.linearVelocity = Vector2.zero;
                }

                holdTimer += deltaTime;

                if (holdTimer >= sm.hoverChargeTime)
                {
                    return Result.EnterFly;
                }

                return Result.Hovering;
            }

            // 松开了 → 取消悬停，还原重力
            if (isHovering)
            {
                isHovering = false;
                sm.rb.gravityScale = originalGravity;
                holdTimer = 0f;
            }

            return Result.Idle;
        }

        /// <summary>卡带 Exit() 时调用，确保重力被还原</summary>
        public void OnExit()
        {
            if (isHovering)
            {
                sm.rb.gravityScale = originalGravity;
                isHovering = false;
            }
            holdTimer = 0f;
        }
    }
}
