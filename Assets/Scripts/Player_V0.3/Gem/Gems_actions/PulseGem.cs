using UnityEngine;

namespace Flandre.CombatSystem.Gems
{
    /// <summary>
    /// 【脉冲 Pulse】—— 效果待定，本文件是完整的空骨架。
    ///
    /// ==========================================================
    /// 【这个文件本身就是重构成果的证明】
    ///
    /// 在旧结构下，加一颗新宝石要动：
    ///   GemActionProcessor（加 3 个动作 × 1 个分支）
    ///   PlayerStateMachine（加若干专属字段）
    ///   JumpState / DashState / SlideState（各加 if 分支）
    ///   ComboInputBuffer（如果涉及受身）
    ///
    /// 现在只要：新建这一个文件 + 右键 Create 一个资产。
    /// 上面那些文件一行都不用改。
    ///
    /// 想好效果之后，把下面对应的钩子填上就行。
    /// ==========================================================
    /// </summary>
    [CreateAssetMenu(fileName = "Gem_Pulse", menuName = "Flandre/Gems/Pulse (脉冲)")]
    public class PulseGemSO : GemSO
    {
        [Header("参数占位")]
        [Tooltip("效果强度。具体含义待设计确定")]
        public float power = 1f;

        [Tooltip("效果半径。具体含义待设计确定")]
        public float radius = 3f;

        public override GemRuntime CreateRuntime() => new PulseGemRuntime();
    }

    public class PulseGemRuntime : GemRuntime
    {
        private PulseGemSO Cfg => Definition as PulseGemSO;

        public override void OnEquip()
        {
            // 【可填】装备时的一次性效果。
            //
            // 想加被动属性 → 贴便利贴，例如：
            //   ctx.stats.moveSpeed.AddModifier(StatModifier.Percent(0.1f, this));
            //
            // 想给动作加段数 → 例如：
            //   Skill?.ApplyGemModifier(this, comboDelta: 1);
            //
            // 不需要写任何「卸载时减回去」的代码，基类会按签名撕干净。

            Log("已装备（效果待实现）");
        }

        public override void OnUnequip()
        {
            base.OnUnequip();          // 自动撕掉属性面板上的便利贴
            Skill?.RemoveGemModifiers(this);   // 撕掉充能器上的便利贴

            // 【可填】如果持有了别的东西（生成物、无敌请求等），在这里清干净
        }

        public override GemActionResult OnActionEnter(ActionType action, bool isFirstUse)
        {
            // 【可填】进入动作时的效果。
            //
            // 返回值决定卡带怎么办：
            //   Normal   → 我只是加点料，你继续跑默认冲刺/跳跃
            //   Override → 我完全接管这次动作（像 Relay 的传送）
            //   Reject   → 这次动作不该发生，请回退

            return GemActionResult.Normal;
        }

        public override void OnActionUpdate(ActionType action, float deltaTime)
        {
            // 【可填】动作持续期间每帧的效果（如持续吸怪、留下轨迹）
        }

        public override void OnActionExit(ActionType action, bool wasOverridden)
        {
            // 【可填】动作结束时的收尾（释放无敌、结算伤害）
        }

        public override bool CanBreakHitStun(InputCmd cmd)
        {
            // 【可填】如果想让 Pulse 也能受身，这里返回 true 即可。
            // 输入层不需要任何改动 —— 它问的是「你能不能受身」，
            // 而不是「你是不是 Shield」。
            return false;
        }

        public override void ExecuteActive()
        {
            // 【可填】主动技能引爆
            Log("主动技能待实现");
        }
    }
}
