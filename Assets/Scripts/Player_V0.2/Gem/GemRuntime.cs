using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【宝石运行时实例】—— 每次装备生成一份，装什么状态都行。
    ///
    /// ==========================================================
    /// 生命周期钩子一览
    ///
    ///   OnEquip             装备的那一刻（不是每次跳跃！）→ 贴便利贴、注册被动
    ///   OnUnequip           卸下时 → 撕便利贴、清残留
    ///   UpdateRuntime       每帧 → 给有状态的宝石用（如 Relay 的锚点回收）
    ///
    ///   OnActionEnter       进入对应动作状态 → 【带返回值，可接管默认逻辑】
    ///   OnActionUpdate      动作状态每帧
    ///   OnActionExit        退出动作状态 → 关无敌、结算
    ///
    ///   CanBreakHitStun     受击硬直中能否受身（Shield 用）
    ///   TryHandleAirCommand 空中按下指令时能否接管【批次C 新增】
    ///   ExecuteActive       主动技能引爆（Active 槽用）
    /// ==========================================================
    /// </summary>
    public abstract class GemRuntime
    {
        public GemSO Definition { get; private set; }
        protected GemContext ctx;

        public GemSlot Slot => ctx != null ? ctx.slot : GemSlot.Active;
        public string DisplayName => Definition != null ? Definition.displayName : "???";

        public void Bind(GemSO definition, GemContext context)
        {
            Definition = definition;
            ctx = context;
        }

        // ==========================================
        // 装备生命周期
        // ==========================================

        public virtual void OnEquip() { }

        /// <summary>
        /// 卸下时调用一次。
        /// 基类已经帮你撕掉了属性面板上的便利贴，
        /// 子类只需要处理自己额外持有的东西（比如 Relay 的锚点物体）。
        /// </summary>
        public virtual void OnUnequip()
        {
            ctx?.stats?.RemoveAllModifiersFrom(this);
        }

        public virtual void UpdateRuntime(float deltaTime) { }

        // ==========================================
        // 动作生命周期
        // ==========================================

        /// <returns>
        /// Normal   → 状态卡带继续跑默认逻辑
        /// Override → 我接管了，卡带别跑默认逻辑，直接收尾
        /// Reject   → 这次动作不该发生，卡带回退
        /// </returns>
        public virtual GemActionResult OnActionEnter(ActionType action, bool isFirstUse)
            => GemActionResult.Normal;

        public virtual void OnActionUpdate(ActionType action, float deltaTime) { }

        public virtual void OnActionExit(ActionType action, bool wasOverridden) { }

        // ==========================================
        // 非动作类钩子
        // ==========================================

        /// <summary>
        /// 受击硬直中，玩家按下 cmd 时能否受身打断。
        ///
        /// 路由器问的是「你能不能受身」而不是「你是不是 Shield」，
        /// 所以以后想让 Pulse 也能受身，只改 Pulse 自己即可。
        /// </summary>
        public virtual bool CanBreakHitStun(InputCmd cmd) => false;

        /// <summary>
        /// 【批次C 新增 · 扩展点】空中按下指令时，本宝石要不要接管？
        ///
        /// 目前唯一的用例是「空中按 C」——
        /// 你说过想加个动作但还没定，所以线已经接好、留空实现。
        ///
        /// 覆写返回 true 表示「我处理了」，路由器就不会再往下走。
        /// 例如：Pulse 在空中触发震荡波、Relay 在空中放锚点、Shield 在空中张盾。
        ///
        /// 想好效果之后，只需要在对应宝石里覆写这个方法 ——
        /// PlayerCommandRouter 和所有状态卡带都不用改。
        /// </summary>
        public virtual bool TryHandleAirCommand(InputCmd cmd) => false;

        /// <summary>
        /// 【批次O 新增】蓄力突刺途中，本宝石是否让玩家无视沿途的敌人？
        ///
        /// 默认 false = 撞到第一个敌人就对它打出蓄力招。
        /// Relay 覆写为 true = 一定冲到最远端再打（配合锚点传送的玩法）。
        ///
        /// 之所以做成"问宝石"而不是在 ChargeState 里写死
        /// "检查装的是不是 Relay"，是因为以后想让别的宝石也有这个特性时，
        /// 只需要改那颗宝石自己 —— 蓄力状态一行都不用动。
        /// </summary>
        public virtual bool IgnoresThrustInterruption => false;

        /// <summary>主动技能引爆。只有插在 Active 槽时才会被调用</summary>
        public virtual void ExecuteActive() { }

        // ==========================================
        // 给子类用的小工具
        // ==========================================

        /// <summary>本宝石所在槽位对应的充能器（Jump/Active 槽返回 null）</summary>
        protected ComboSkill Skill => ctx?.GetSkillForSlot();

        protected void Log(string msg)
        {
            Debug.Log($"[宝石·{DisplayName}@{Slot}] {msg}");
        }
    }
}
