using UnityEngine;

namespace Flandre.CombatSystem.Gems
{
    /// <summary>
    /// 【护盾 Shield】
    ///
    /// 动作槽：受击硬直中，按下该动作对应的键可以「受身」打断硬直
    /// 主动槽：待定（先留接口）
    ///
    /// 注意 ComboInputBuffer 现在问的是「你能不能受身」，
    /// 而不是「你是不是 Shield 类型」。所以以后想让 Pulse 也能受身，
    /// 只需要改 Pulse 自己，不用碰输入层。
    /// </summary>
    [CreateAssetMenu(fileName = "Gem_Shield", menuName = "Flandre/Gems/Shield (护盾)")]
    public class ShieldGemSO : GemSO
    {
        [Header("受身")]
        [Tooltip("受身后是否给予一段短暂无敌")]
        public bool grantInvulnerabilityOnBreak = true;

        [Tooltip("受身无敌持续时间")]
        public float breakInvulnerableDuration = 0.3f;

        public override GemRuntime CreateRuntime() => new ShieldGemRuntime();
    }

    public class ShieldGemRuntime : GemRuntime
    {
        private ShieldGemSO Cfg => Definition as ShieldGemSO;

        private float invulnTimer = 0f;
        private bool isHoldingInvuln = false;

        /// <summary>
        /// 受击硬直中，这个键能不能受身？
        /// 只对本宝石所在槽位对应的那个键生效。
        /// </summary>
        public override bool CanBreakHitStun(InputCmd cmd)
        {
            if (!ctx.IsActionSlot) return false;

            switch (Slot)
            {
                case GemSlot.Jump: return cmd == InputCmd.Jump;
                case GemSlot.Dash: return cmd == InputCmd.Dash;
                case GemSlot.Slide: return cmd == InputCmd.Crouch || cmd == InputCmd.Slide;
                default: return false;
            }
        }

        public override GemActionResult OnActionEnter(ActionType action, bool isFirstUse)
        {
            var cfg = Cfg;
            if (cfg == null || !cfg.grantInvulnerabilityOnBreak) return GemActionResult.Normal;

            // 只有从受击硬直里挣脱出来的那一次才给无敌，
            // 常规使用该动作不应该白嫖无敌
            if (ctx.stateMachine != null && ctx.stateMachine.isBreakingHitStun)
            {
                ctx.state?.health.RequestUntargetable(this);
                isHoldingInvuln = true;
                invulnTimer = cfg.breakInvulnerableDuration;
                Log($"受身成功，获得 {cfg.breakInvulnerableDuration} 秒无敌");
            }

            return GemActionResult.Normal;
        }

        public override void UpdateRuntime(float deltaTime)
        {
            if (!isHoldingInvuln) return;

            invulnTimer -= deltaTime;
            if (invulnTimer <= 0f)
            {
                ctx.state?.health.ReleaseUntargetable(this);
                isHoldingInvuln = false;
            }
        }

        public override void OnUnequip()
        {
            base.OnUnequip();

            if (isHoldingInvuln)
            {
                ctx.state?.health.ReleaseUntargetable(this);
                isHoldingInvuln = false;
            }
        }

        public override void ExecuteActive()
        {
            // TODO: Shield 的主动技能效果待定
            Log("主动技能尚未实现");
        }
    }
}
