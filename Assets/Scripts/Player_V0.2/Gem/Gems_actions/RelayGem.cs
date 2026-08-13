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
    /// 【这颗宝石是整个重构的主要动机】
    ///
    /// 改造前，Relay 的逻辑散落在四个地方：
    ///   1. GemActionProcessor      —— 改数值
    ///   2. PlayerStateMachine      —— 存 11 个 Relay 专属字段
    ///   3. Jump/Dash/SlideState    —— 各写一段 if (sm.isXxxRelay) {...}
    ///   4. PlayerStateMachine.Update —— 每帧轮询销毁锚点图片
    ///
    /// 现在全部收进本文件。三个状态卡带里的 Relay 分支已经删干净了。
    /// 新增 Pulse / Shield 不需要碰任何一个状态文件。
    /// ==========================================================
    /// </summary>
    [CreateAssetMenu(fileName = "Gem_Relay", menuName = "Flandre/Gems/Relay (中继)")]
    public class RelayGemSO : GemSO
    {
        [Header("锚点")]
        [Tooltip("锚点视觉预制体。留空则只有传送效果没有显示")]
        public GameObject anchorPrefab;

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
        private bool isHoldingBuffs = false;   // 当前是否持有无敌/穿透请求

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

            // 卸载时必须把中间态清干净：
            // 「锚点已放置但还没用」正是这类残留的典型
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

                // Override = 我接管了，卡带别再跑冲刺/跳跃的默认物理
                return GemActionResult.Override;
            }

            // ---- 第一段：放锚点，正常执行动作 ----
            if (isFirstUse)
            {
                anchorPos = ctx.transform.position;
                hasAnchor = true;

                if (cfg.anchorPrefab != null)
                {
                    // TODO: 阶段6 改走 ObjectPoolManager，消灭 Instantiate/Destroy
                    anchorVisual = Object.Instantiate(cfg.anchorPrefab, anchorPos, Quaternion.identity);
                }

                RequestBuffs();
                Log("锚点已放置");
            }

            return GemActionResult.Normal;
        }

        public override void OnActionExit(ActionType action, bool wasOverridden)
        {
            // 动作结束就归还增益。
            // 注意用的是引用计数，就算此刻受击无敌也在生效，也不会误伤对方。
            ReleaseBuffs();
        }

        public override void UpdateRuntime(float deltaTime)
        {
            // 锚点回收：本轮连段结束（充能器归零）就说明锚点过期了。
            //
            // 这段逻辑原先在 PlayerStateMachine.Update 里每帧轮询，
            // 现在搬进宝石自己肚子里 —— 状态机不需要知道锚点是什么。
            // （阶段6 会进一步改成事件驱动）
            if (!hasAnchor) return;

            if (Slot == GemSlot.Jump)
            {
                // 跳跃锚点在落地时失效
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
                if (cfg.anchorPrefab != null)
                    anchorVisual = Object.Instantiate(cfg.anchorPrefab, anchorPos, Quaternion.identity);
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

        private void ClearAnchor()
        {
            hasAnchor = false;

            if (anchorVisual != null)
            {
                Object.Destroy(anchorVisual);
                anchorVisual = null;
            }
        }
    }
}
