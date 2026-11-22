using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【装备中枢】管理 3 个动作槽（跳/冲/铲）+ 1 个主动技能槽。
    ///
    /// ==========================================================
    /// 【批次B 改动】
    ///
    /// 1. GemActionProcessor 已整个删除。
    ///    它是「只装不卸」这个 bug 的根源 —— 直接往状态机字段写死值，
    ///    而 ExecuteModifierEnd 是空的，卸下宝石永远还原不了。
    ///
    /// 2. 原本并存的两套宝石系统（IKeymodule 一轨 + GemType 一轨）合并为一套。
    ///    同一颗宝石资产既能插动作槽当强化，也能插主动槽当大招。
    ///
    /// 3. 装备配置只在【装备的那一刻】生效一次，
    ///    不再是原来的「每次跳跃/冲刺进状态时重刷一遍」。
    ///
    /// 4. 校验同一颗宝石不能同时装两处（因为设计上是「四选三 + 剩余一颗当主动」）。
    ///
    /// 【设计约定】按你的决定，宝石只在安全区/菜单里切换，
    /// 所以 OnUnequip 不需要处理「动作进行到一半被换掉」的极端中间态。
    /// SetGem() 里仍然做了基础防护，但没有为战斗中热切做复杂设计。
    /// ==========================================================
    /// </summary>
    public class LoadoutManager : MonoBehaviour
    {
        [Header("动作强化插槽 (拖入宝石资产)")]
        public GemSO jumpGem;
        public GemSO dashGem;
        public GemSO slideGem;

        [Header("主动技能插槽")]
        public GemSO activeGem;

        [Header("设置")]
        [Tooltip("是否禁止同一颗宝石同时装在多个槽位")]
        public bool forbidDuplicateGems = true;

        // 槽位 → 运行时实例
        private readonly Dictionary<GemSlot, GemRuntime> runtimes = new Dictionary<GemSlot, GemRuntime>();

        private PlayerController controller;
        private PlayerStateMachine stateMachine;
        private PlayerState state;
        private bool isReady = false;

        private static readonly GemSlot[] AllSlots =
        {
            GemSlot.Jump, GemSlot.Dash, GemSlot.Slide, GemSlot.Active
        };

        // ==========================================
        // 生命周期
        // ==========================================

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            stateMachine = GetComponent<PlayerStateMachine>();
            state = GetComponent<PlayerState>();

            if (state != null) state.EnsureInitialized();
        }

        private void Start()
        {
            // 放在 Start 而不是 Awake：
            // 确保 PlayerState / PlayerStateMachine 都已完成初始化，
            // 宝石 OnEquip 里贴便利贴时属性面板已经就绪。
            EquipAllFromInspector();
            isReady = true;
        }

        private void Update()
        {
            if (!isReady) return;

            float dt = Time.deltaTime;
            foreach (var kv in runtimes)
            {
                kv.Value?.UpdateRuntime(dt);
            }
        }

        private void OnDestroy()
        {
            UnequipAll();
        }

        // ==========================================
        // 装备管理
        // ==========================================

        private void EquipAllFromInspector()
        {
            if (forbidDuplicateGems) ValidateNoDuplicates();

            SetGemInternal(GemSlot.Jump, jumpGem);
            SetGemInternal(GemSlot.Dash, dashGem);
            SetGemInternal(GemSlot.Slide, slideGem);
            SetGemInternal(GemSlot.Active, activeGem);
        }

        /// <summary>
        /// 装备/更换某个槽位的宝石。菜单界面调用这个。
        /// 传 null 表示卸空该槽。
        /// </summary>
        public void SetGem(GemSlot slot, GemSO gem)
        {
            if (gem != null && forbidDuplicateGems)
            {
                foreach (var s in AllSlots)
                {
                    if (s == slot) continue;
                    if (GetGemSO(s) == gem)
                    {
                        Debug.LogWarning(
                            $"[装备中枢] 宝石「{gem.displayName}」已装在 {s} 槽，" +
                            $"不能同时装到 {slot} 槽。", this);
                        return;
                    }
                }
            }

            if (gem != null && !gem.CanEquipTo(slot))
            {
                Debug.LogWarning($"[装备中枢] 宝石「{gem.displayName}」不允许装在 {slot} 槽。", this);
                return;
            }

            // 同步回 Inspector 字段，避免「字典与序列化字段双份存储不同步」的老问题
            switch (slot)
            {
                case GemSlot.Jump: jumpGem = gem; break;
                case GemSlot.Dash: dashGem = gem; break;
                case GemSlot.Slide: slideGem = gem; break;
                case GemSlot.Active: activeGem = gem; break;
            }

            SetGemInternal(slot, gem);
        }

        private void SetGemInternal(GemSlot slot, GemSO gem)
        {
            // 1. 先把旧的卸干净
            if (runtimes.TryGetValue(slot, out var old) && old != null)
            {
                old.OnUnequip();
            }
            runtimes.Remove(slot);

            if (gem == null) return;

            // 2. 生成本次装备专属的草稿本
            GemRuntime runtime = gem.CreateRuntime();
            if (runtime == null)
            {
                Debug.LogError($"[装备中枢] 宝石「{gem.displayName}」的 CreateRuntime() 返回了 null。", this);
                return;
            }

            var context = new GemContext
            {
                controller = controller,
                stateMachine = stateMachine,
                state = state,
                loadout = this,
                slot = slot
            };

            runtime.Bind(gem, context);
            runtimes[slot] = runtime;

            // 3. 配置在【装备的这一刻】生效一次，而不是每次进状态重刷
            runtime.OnEquip();
        }

        public void UnequipAll()
        {
            foreach (var kv in runtimes)
            {
                kv.Value?.OnUnequip();
            }
            runtimes.Clear();
        }

        private void ValidateNoDuplicates()
        {
            var seen = new HashSet<GemSO>();
            foreach (var slot in AllSlots)
            {
                var g = GetGemSO(slot);
                if (g == null) continue;

                if (!seen.Add(g))
                {
                    Debug.LogWarning(
                        $"[装备中枢] 宝石「{g.displayName}」被装在了多个槽位。" +
                        "按设计应为「四选三装动作 + 剩余一颗当主动」，请检查配置。", this);
                }
            }
        }

        // ==========================================
        // 查询
        // ==========================================

        public GemSO GetGemSO(GemSlot slot)
        {
            switch (slot)
            {
                case GemSlot.Jump: return jumpGem;
                case GemSlot.Dash: return dashGem;
                case GemSlot.Slide: return slideGem;
                case GemSlot.Active: return activeGem;
                default: return null;
            }
        }

        public GemRuntime GetRuntime(GemSlot slot)
            => runtimes.TryGetValue(slot, out var r) ? r : null;

        public static GemSlot ActionToSlot(ActionType action)
        {
            switch (action)
            {
                case ActionType.Jump: return GemSlot.Jump;
                case ActionType.Dash: return GemSlot.Dash;
                case ActionType.Slide: return GemSlot.Slide;
                default: return GemSlot.Jump;
            }
        }

        // ==========================================
        // 状态卡带调用的三个钩子
        // ==========================================

        /// <summary>
        /// 进入动作状态时问一句：宝石要不要接管？
        /// 没装宝石就返回 Normal，卡带跑默认逻辑。
        /// </summary>
        public GemActionResult NotifyActionEnter(ActionType action, bool isFirstUse)
        {
            var rt = GetRuntime(ActionToSlot(action));

            GemActionResult result = rt != null
                ? rt.OnActionEnter(action, isFirstUse)
                : GemActionResult.Normal;

            // 受身标记是一次性的：问完宝石立刻清零，
            // 免得下一次常规冲刺被误判成受身、白嫖一次无敌。
            if (stateMachine != null) stateMachine.isBreakingHitStun = false;

            return result;
        }

        public void NotifyActionUpdate(ActionType action, float deltaTime)
        {
            GetRuntime(ActionToSlot(action))?.OnActionUpdate(action, deltaTime);
        }

        public void NotifyActionExit(ActionType action, bool wasOverridden)
        {
            GetRuntime(ActionToSlot(action))?.OnActionExit(action, wasOverridden);
        }

        // ==========================================
        // 受身查询
        // ==========================================

        /// <summary>
        /// 受击硬直中按下 cmd，有没有宝石允许受身打断？
        ///
        /// 原写法是 HasShieldGem() —— 写死查「这个槽是不是 Shield 类型」。
        /// 现在是问所有装备的宝石「你能不能受身」，
        /// 想让 Pulse 也能受身时不需要改这里。
        /// </summary>
        public bool CanBreakHitStun(InputCmd cmd)
        {
            foreach (var kv in runtimes)
            {
                if (kv.Value != null && kv.Value.CanBreakHitStun(cmd)) return true;
            }
            return false;
        }

        /// <summary>【兼容层】旧接口，内部转调 CanBreakHitStun</summary>
        [System.Obsolete("请改用 CanBreakHitStun(cmd)")]
        public bool HasShieldGem(InputCmd cmd) => CanBreakHitStun(cmd);

        /// <summary>
        /// 【批次C 新增 · 扩展点】空中按下指令时，有没有宝石要接管？
        ///
        /// 由 PlayerCommandRouter 在「空中按 C」时调用。
        /// 优先问该指令对应槽位的宝石，没接就问其余宝石。
        ///
        /// 目前四颗宝石都还没有覆写 TryHandleAirCommand，所以永远返回 false
        /// （= 空中按 C 暂时无效果）。想好动作之后覆写对应宝石即可，
        /// 本文件与路由器都不用改。
        /// </summary>
        public bool TryHandleAirCommand(InputCmd cmd)
        {
            // 1. 先问该指令「本命槽位」的宝石
            GemSlot primary = CommandToSlot(cmd);
            var primaryRuntime = GetRuntime(primary);
            if (primaryRuntime != null && primaryRuntime.TryHandleAirCommand(cmd)) return true;

            // 2. 再问其余宝石（主动槽宝石也有机会响应）
            foreach (var kv in runtimes)
            {
                if (kv.Key == primary) continue;
                if (kv.Value != null && kv.Value.TryHandleAirCommand(cmd)) return true;
            }

            return false;
        }

        /// <summary>指令 → 它的「本命槽位」</summary>
        public static GemSlot CommandToSlot(InputCmd cmd)
        {
            switch (cmd)
            {
                case InputCmd.Jump: return GemSlot.Jump;
                case InputCmd.Dash: return GemSlot.Dash;
                case InputCmd.Crouch:
                case InputCmd.Slide: return GemSlot.Slide;
                default: return GemSlot.Active;
            }
        }

        /// <summary>
        /// 【批次O 新增】有没有宝石让蓄力突刺无视沿途敌人？
        /// 任何一颗说 true 就算 true。
        /// </summary>
        public bool IgnoresThrustInterruption()
        {
            foreach (var kv in runtimes)
            {
                if (kv.Value != null && kv.Value.IgnoresThrustInterruption) return true;
            }
            return false;
        }

        // ==========================================
        // 主动技能
        // ==========================================

        /// <summary>触发主动技能槽的宝石效果</summary>
        public void TriggerActiveSkill()
        {
            var rt = GetRuntime(GemSlot.Active);
            if (rt == null)
            {
                Debug.Log("[装备中枢] 主动技能槽为空");
                return;
            }
            rt.ExecuteActive();
        }
    }
}
