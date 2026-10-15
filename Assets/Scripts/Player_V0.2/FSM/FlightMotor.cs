using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【飞行引擎】—— FlyState（巡航）与 FlyCombatState（交战）共用的一台发动机。
    ///
    /// ==========================================================
    /// 【为什么要单独抽出来】
    ///
    /// 飞行有两个状态：只飞（FlyState）、边飞边打（FlyCombatState）。
    /// 两者的"飞"必须一模一样：关重力、八方向移动、按秒扣蓝。
    /// 各写一份的话，以后调飞行手感就得改两处，漏一处就是"打的时候飞得不一样"。
    ///
    /// 比喻：飞机的巡航模式和交战模式是两个按钮，但发动机只有一台。
    ///       切换模式不会让发动机熄火，油表也不会归零。
    /// ==========================================================
    ///
    /// 【耗蓝零头为什么跨状态保留】
    /// MP 是整数，每秒 10 点 = 每 0.1 秒扣 1 点，中间的小数靠 manaAccumulator 攒着。
    /// 如果每次 Fly ↔ FlyCombat 切换都把零头清零，
    /// 频繁出招就能一直"抹掉"还没扣出去的那部分 —— 等于白飞。
    /// 所以零头只在【起飞那一刻】清零（BeginSession，由路由器调用），状态切换时不碰。
    /// </summary>
    public class FlightMotor
    {
        private readonly PlayerStateMachine sm;
        private float manaAccumulator;

        // ---- 飞行冲刺（Shift）----
        private Vector2 dashDirection;
        private float dashTimer;

        /// <summary>正在飞行冲刺中</summary>
        public bool IsDashing => dashTimer > 0f;

        public FlightMotor(PlayerStateMachine stateMachine)
        {
            sm = stateMachine;
        }

        /// <summary>一次新的飞行开始（起飞那一刻）。只在这里清零耗蓝零头</summary>
        public void BeginSession()
        {
            manaAccumulator = 0f;
            dashTimer = 0f;
        }

        /// <summary>
        /// 【飞行冲刺】朝指定方向猛推一段，推完恢复正常八方向飞行 —— 人始终在飞。
        ///
        /// 速度与时长和地面冲刺共用（PlayerMovementConfig 的 dashSpeed / dashDuration），
        /// 冷却由路由器在调用前用 dashSkill 校验。
        /// 冲刺写在引擎里而不是切去 DashState，是为了：冲完不会掉下来、冲的同时还能开火。
        /// </summary>
        public void StartDash(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            dashDirection = direction.normalized;
            dashTimer = sm.dashDuration;
            SetTrail(true);
        }

        private void SetTrail(bool on)
        {
            if (sm.dashTrail != null) sm.dashTrail.emitting = on;
        }

        /// <summary>飞行类状态 Enter 时调用：关掉重力</summary>
        public void Engage()
        {
            sm.rb.gravityScale = 0f;

            // Fly ⇄ FlyCombat 互切时冲刺还没推完 → 拖尾接着亮
            if (IsDashing) SetTrail(true);
        }

        /// <summary>
        /// 飞行类状态 Exit 时调用：照出厂值还原重力，并结算跳跃次数。
        ///
        /// 【跳跃次数】离开飞行时人还在空中 → 第一跳视为已用掉，二段跳保留：
        ///   地面直接起飞 → 落下后：无二段跳不能跳，有二段跳能跳 1 次
        ///   跳起后起飞   → 第一跳本来就用了，有二段跳能跳 1 次
        ///   二段跳后起飞 → 不能跳
        /// 否则"地面起飞再解除"会白送一次空中跳跃。
        ///
        /// Fly ⇄ FlyCombat 互切时也会走这里，但那时人还在飞、用不到跳跃；
        /// 贴地时状态机每帧会把 jumpCount 清零，所以都不会误伤。
        /// </summary>
        public void Disengage()
        {
            sm.rb.gravityScale = sm.defaultGravityScale;
            SetTrail(false);

            if (!sm.IsGrounded()) sm.jumpCount = Mathf.Max(sm.jumpCount, 1);
        }

        /// <summary>
        /// 渲染帧扣蓝。
        /// </summary>
        /// <returns>false = 蓝不够了，调用方应当立刻结束飞行</returns>
        public bool TickMana(float deltaTime)
        {
            var health = sm.playerState != null ? sm.playerState.health : null;
            if (health == null) return true;

            // 蓝已经见底就别等零头攒满，立刻掉下来
            if (health.currentMP <= 0) return false;

            manaAccumulator += sm.flyManaCostPerSecond * deltaTime;
            if (manaAccumulator < 1f) return true;

            int cost = Mathf.FloorToInt(manaAccumulator);
            manaAccumulator -= cost;

            // 剩的蓝不够扣这一口：扣光，然后结束飞行
            if (!health.ConsumeMP(cost))
            {
                health.SetMP(0);
                return false;
            }

            return true;
        }

        /// <summary>物理帧：八方向移动。X/Y 都由输入完全接管，不保留原速度</summary>
        public void FixedTick()
        {
            // 冲刺中：方向键不参与，按冲刺方向全速推进
            if (dashTimer > 0f)
            {
                sm.rb.linearVelocity = dashDirection * sm.dashSpeed;
                dashTimer -= Time.fixedDeltaTime;
                if (dashTimer <= 0f) SetTrail(false);
                return;
            }

            Vector2 moveDir = sm.playerController.moveInput;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            sm.rb.linearVelocity = moveDir * sm.flySpeed;
        }
    }
}
