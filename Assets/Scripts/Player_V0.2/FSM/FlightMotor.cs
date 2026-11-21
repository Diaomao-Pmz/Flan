using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【飞行引擎】—— FlyState（巡航）与 FlyCombatState（交战）共用的一台发动机。
    /// </summary>
    public class FlightMotor
    {
        private readonly PlayerStateMachine sm;
        private float manaAccumulator;

        // ---- 飞行冲刺（Shift）----
        private Vector2 dashDirection;
        private float dashTimer;            // 冲刺时间还剩多久
        private bool dashing;               // 冲刺中（时间到了但还在穿过 Boss 时也算）
        private readonly GuardedMove guard; // 终点裁判：不停在 Boss 身体里（见 DashEndpointGuard）

        /// <summary>正在飞行冲刺中</summary>
        public bool IsDashing => dashing;

        public FlightMotor(PlayerStateMachine stateMachine)
        {
            sm = stateMachine;
            guard = new GuardedMove(stateMachine);
        }

        /// <summary>一次新的飞行开始（起飞那一刻）。只在这里清零耗蓝零头</summary>
        public void BeginSession()
        {
            manaAccumulator = 0f;
            dashTimer = 0f;
            dashing = false;
            guard.End();
        }

        /// <summary>
        /// 【飞行冲刺】朝指定方向猛推一段，推完恢复正常八方向飞行 —— 人始终在飞。
        /// </summary>
        public void StartDash(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            dashDirection = direction.normalized;
            dashTimer = sm.dashDuration;   // 飞行参数分离时读的是飞行那一套
            dashing = true;
            SetTrail(true);

            var cfg = sm.playerState != null ? sm.playerState.movementConfig : null;
            guard.Begin(dashDirection, sm.dashSpeed * sm.dashDuration, sm.dashDuration,
                        cfg != null && cfg.dashPassThrough);

            // 冲刺动画（目前借用滑铲动画，见 PlayerAnimHash.Dash）。
            // 只在巡航时换：交战中正在演攻击动画，不去盖它
            if (sm.currentState == sm.flyState)
                sm.animDriver?.SetBase(PlayerAnimHash.Dash, restart: true);
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
            // 冲刺中：方向键不参与，按冲刺方向全速推进。
            // 精确距离：最后一个物理帧只走剩下的那一点；终点裁判可能让它停在 Boss 面前 / 穿到 Boss 身后
            if (dashing)
            {
                float dt = Time.fixedDeltaTime;
                float timeStep = Mathf.Max(0f, Mathf.Min(dt, dashTimer));
                dashTimer -= timeStep;

                float step = guard.Step(sm.dashSpeed * timeStep);
                sm.rb.linearVelocity = dashDirection * (step / dt);

                // 冲刺时长不变：被挡住就贴着 Boss 停到时间结束；要穿到身后就走完才结束
                if (dashTimer <= 0f && guard.Reached)
                {
                    dashing = false;
                    guard.End();
                    SetTrail(false);

                    // 冲刺结束：巡航中换回飞行动画（交战中由下一招的攻击动画接手）
                    if (sm.currentState == sm.flyState) sm.animDriver?.SetBase(PlayerAnimHash.Fly);
                }
                return;
            }

            Vector2 moveDir = sm.playerController.moveInput;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            sm.rb.linearVelocity = moveDir * sm.flySpeed;
        }
    }
}
