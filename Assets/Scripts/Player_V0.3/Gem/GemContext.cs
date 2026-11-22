using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【宝石的工作环境】
    ///
    /// 宝石需要摸到玩家的哪些部件，全部集中在这里一次性交付。
    ///
    /// 这么做的好处：以后玩家结构变了（比如阶段4 拆出 PlayerSensor），
    /// 只需要改这一个类，四颗宝石一行都不用动。
    ///
    /// 反面教材是原来的 GemActionProcessor —— 它直接吃一个 PlayerController，
    /// 然后自己一路 player.stateMachine.dashSkill.maxCombo 摸进去，
    /// 中间任何一层改名都会波及所有宝石逻辑。
    /// </summary>
    public class GemContext
    {
        public PlayerController controller;
        public PlayerStateMachine stateMachine;
        public PlayerState state;
        public LoadoutManager loadout;

        /// <summary>这颗宝石被插在哪个槽里。同一颗宝石在不同槽里效果不同</summary>
        public GemSlot slot;

        public Transform transform => controller != null ? controller.transform : null;
        public Rigidbody2D rb => controller != null ? controller.rb : null;
        public GameObject gameObject => controller != null ? controller.gameObject : null;

        /// <summary>属性面板的快捷方式（贴便利贴用）</summary>
        public PlayerStats stats => state != null ? state.stats : null;

        /// <summary>
        /// 取当前槽位对应的动作充能器。Active 槽返回 null。
        /// Jump 没有 ComboSkill（它用 jumpCount/maxJumps 计数），也返回 null。
        /// </summary>
        public ComboSkill GetSkillForSlot()
        {
            if (stateMachine == null) return null;

            switch (slot)
            {
                case GemSlot.Dash: return stateMachine.dashSkill;
                case GemSlot.Slide: return stateMachine.slideSkill;
                default: return null;
            }
        }

        /// <summary>槽位 → 动作类型。Active 槽返回 false</summary>
        public bool TryGetActionType(out ActionType action)
        {
            switch (slot)
            {
                case GemSlot.Jump: action = ActionType.Jump; return true;
                case GemSlot.Dash: action = ActionType.Dash; return true;
                case GemSlot.Slide: action = ActionType.Slide; return true;
                default: action = ActionType.Jump; return false;
            }
        }

        public bool IsActionSlot => slot != GemSlot.Active;
    }
}
