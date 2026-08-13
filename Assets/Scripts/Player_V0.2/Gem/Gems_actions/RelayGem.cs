using UnityEngine;

namespace Flandre.CombatSystem.Gems
{
    /// <summary>
    /// 【中继 Relay】
    ///
    /// 动作槽：第一次使用在原地放锚点并短暂无敌 → 第二次使用传送回锚点
    /// 主动槽：手动放置 / 回收锚点
    ///
    /// ==========================================================
    /// 【接池改造】锚点从 Instantiate/Destroy 改为对象池借还
    ///
    /// 锚点看起来只有三个，似乎不值得接池 —— 但它是【每次冲刺/滑铲/跳跃都生成一个】。
    /// 在快节奏 ACT 里就是每秒好几次的 Instantiate + Destroy，
    /// 和子弹是同一量级的 GC 压力。
    ///
    /// 【池泄漏的三条路径都堵上了】
    ///   1. 正常传送用掉      → ClearAnchor
    ///   2. 连段超时自动过期  → UpdateRuntime 里回收
    ///   3. 宝石被卸下        → OnUnequip 里回收（「锚点已放置但还没用」的中间态）
    ///
    /// 第 3 条是最容易漏的 —— 正是 FormationCore 那个文件在防的同一类事故：
    /// 池化对象被当成普通物体处理，不回队列，池被永久抽干。
    /// ==========================================================
    /// </summary>
    [CreateAssetMenu(fileName = "Gem_Relay", menuName = "Flandre/Gems/Relay (中继)")]
    public class RelayGemSO : GemSO
    {
        [Header("锚点 (对象池)")]
        [Tooltip(
            "锚点视觉的【对象池 key】。必须与 ObjectPoolManager 上注册的 key 一致。\n" +
            "留空则不显示锚点，只有传送功能。")]
        public string anchorPoolKey = "";

        [Tooltip("锚点存在时间（秒）。会覆写该动作的派生窗口期")]
        public float anchorLifetime = 2.0f;

        [Header("第一段附加效果")]
        [Tooltip("放置锚点时是否短暂无敌")]
        public bool grantInvulnerability = true;

        [Tooltip("放置锚点时是否可穿过敌人")]
        public bool grantPhaseThrough = true;

        [Header("传送")]
        [Tooltip("传送后是否清空速度")]
        public bool zeroVelocityOnTeleport = true;

        public override GemRuntime CreateRuntime() => new RelayGemRuntime();
    }

    public class RelayGemRuntime : GemRuntime
    {
        private RelayGemSO Cfg => Definition as RelayGemSO;

        // ---- 全部运行时状态收在这本草稿本里 ----
        private Vector2 anchorPos;
        private bool hasAnchor = false;
        private GameObject anchorVisual;
        private bool isHoldingBuffs = false;

        public override void OnEquip()
        {
            var cfg = Cfg;
            if (cfg == null || !ctx.IsActionSlot) return;

            if (Slot == GemSlot.Jump)
            {
                // 跳跃：多一段，第二段用来传送回锚点
                ctx.stats.maxJumps.AddModifier(StatModifier.Flat(1, this));
            }
            else
            {
                // 冲刺/滑铲：多一段，且把派生窗口期撑到锚点存在时间
                Skill?.ApplyGemModifier(this, comboDelta: 1, windowOverride: cfg.anchorLifetime);
            }

            Log($"已装备，锚点存在时间 {cfg.anchorLifetime} 秒");
        }

        public override void OnUnequip()
        {
            base.OnUnequip();
            Skill?.RemoveGemModifiers(this);

            // 【池泄漏防线】卸载时必须把中间态清干净。
            // 「锚点已放置但还没用」正是这类残留的典型 ——
            // 不回收的话，这个池化对象会永远飘在场景里，池少一个。
            ClearAnchor();
            ReleaseBuffs();

            Log("已卸载，锚点与增益均已清理");
        }

        // ==========================================
        // 动作钩子
        // ==========================================

        public override GemActionResult OnActionEnter(ActionType action, bool isFirstUse)
        {
            var cfg = Cfg;
            if (cfg == null) return GemActionResult.Normal;

            // ---- 第二段：传送回锚点，接管默认逻辑 ----
            if (!isFirstUse && hasAnchor)
            {
                ctx.transform.position = anchorPos;
                if (cfg.zeroVelocityOnTeleport) ctx.rb.linearVelocity = Vector2.zero;

                ClearAnchor();
                Log("传送触发！");

                return GemActionResult.Override;
            }

            // ---- 第一段：放锚点，正常执行动作 ----
            if (isFirstUse)
            {
                // 上一个锚点如果还在（理论上不该发生），先还回去再借新的
                ClearAnchor();

                anchorPos = ctx.transform.position;
                hasAnchor = true;

                SpawnAnchorVisual(cfg);
                RequestBuffs();

                Log("锚点已放置");
            }

            return GemActionResult.Normal;
        }

        public override void OnActionExit(ActionType action, bool wasOverridden)
        {
            // 动作结束就归还增益。
            // 用的是引用计数，就算此刻受击无敌也在生效，也不会误伤对方。
            ReleaseBuffs();
        }

        public override void UpdateRuntime(float deltaTime)
        {
            if (!hasAnchor) return;

            // 锚点过期回收。
            // 这段逻辑原先在 PlayerStateMachine.Update 里每帧轮询，
            // 现在收进宝石自己肚子里 —— 状态机不需要知道锚点是什么。
            if (Slot == GemSlot.Jump)
            {
                if (ctx.stateMachine != null && ctx.stateMachine.IsGrounded()
                    && ctx.stateMachine.jumpCount == 0)
                {
                    ClearAnchor();
                }
            }
            else
            {
                var skill = Skill;
                if (skill != null && skill.IsComboIdle) ClearAnchor();
            }
        }

        // ==========================================
        // 主动技能：手动放/收锚点
        // ==========================================
        public override void ExecuteActive()
        {
            var cfg = Cfg;
            if (cfg == null) return;

            if (!hasAnchor)
            {
                anchorPos = ctx.transform.position;
                hasAnchor = true;
                SpawnAnchorVisual(cfg);
                Log("主动放置锚点");
            }
            else
            {
                ctx.transform.position = anchorPos;
                if (cfg.zeroVelocityOnTeleport) ctx.rb.linearVelocity = Vector2.zero;
                ClearAnchor();
                Log("主动传送回锚点");
            }
        }

        // ==========================================
        // 内部
        // ==========================================

        private void SpawnAnchorVisual(RelayGemSO cfg)
        {
            if (string.IsNullOrEmpty(cfg.anchorPoolKey)) return;

            // key 未注册时池会自己报 LogError，这里拿到 null 就静默跳过
            anchorVisual = ObjectPoolManager.Instance?.Get(cfg.anchorPoolKey);
            if (anchorVisual != null)
            {
                anchorVisual.transform.position = anchorPos;
            }
        }

        /// <summary>
        /// 清掉锚点并把视觉对象还回池。
        /// 对同一对象重复调用是安全的 —— 池内部有 isInPool 幂等挡板。
        /// </summary>
        private void ClearAnchor()
        {
            hasAnchor = false;

            if (anchorVisual != null)
            {
                ObjectPoolManager.Instance?.Recycle(anchorVisual);
                anchorVisual = null;
            }
        }

        private void RequestBuffs()
        {
            var cfg = Cfg;
            if (cfg == null || isHoldingBuffs) return;

            if (cfg.grantInvulnerability) ctx.state?.health.RequestUntargetable(this);
            if (cfg.grantPhaseThrough) ctx.state?.RequestPhaseThrough(this);

            isHoldingBuffs = true;
        }

        private void ReleaseBuffs()
        {
            if (!isHoldingBuffs) return;

            ctx.state?.health.ReleaseUntargetable(this);
            ctx.state?.ReleasePhaseThrough(this);

            isHoldingBuffs = false;
        }
    }
}