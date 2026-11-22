using System;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>招式类型。决定 ComboNode 面板显示哪些参数、以及出招时走哪条执行路线</summary>
    public enum ComboMoveType
    {
        Melee,
        Ranged,
        Mix,
        SpecialMelee,
        SpecialRanged,
    }

    /// <summary>特殊招式属于近战还是远程（下拉框按招式类型筛选用）</summary>
    public enum SpecialMoveKind
    {
        Melee,
        Ranged,
    }

    /// <summary>特殊招式在下拉框里显示的名字和所属类别</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SpecialMoveInfoAttribute : Attribute
    {
        public readonly string displayName;
        public readonly SpecialMoveKind kind;

        public SpecialMoveInfoAttribute(string displayName, SpecialMoveKind kind)
        {
            this.displayName = displayName;
            this.kind = kind;
        }
    }

    /// <summary>
    /// 特殊招式的参数（存在 ComboNode 资产里，运行时只读）。
    /// 每次出招由 Begin 生成一份 SpecialMoveRun，运行时状态都放在那里。
    /// </summary>
    [Serializable]
    public abstract class SpecialMove
    {
        /// <summary>前摇和判定段里角色不能移动（方向键也不能取消前摇），直到后摇</summary>
        public virtual bool HoldsStillUntilRecovery => true;

        /// <summary>
        /// 站住不动的这段时间里，这一刻按着方向键能以几倍移速走（不会转身）。0 = 不能走。
        /// </summary>
        /// <param name="inputX">方向键左右（-1~1）</param>
        /// <param name="facing">角色朝向（+1 右 / -1 左）</param>
        public virtual float MoveSpeedMultiplier(AttackInstance inst, float inputX, int facing) => 0f;

        /// <summary>出招时调用（攻击实例刚创建，elapsed = 0）</summary>
        public abstract SpecialMoveRun Begin(SpecialMoveContext ctx, AttackInstance inst);
    }

    /// <summary>一次特殊招式的运行时状态。由 AttackTimelineRunner 每帧推进，招式结束 / 被掐断时 End</summary>
    public abstract class SpecialMoveRun
    {
        public bool done;

        /// <summary>每帧调用（inst.elapsed 已经推进过）</summary>
        public abstract void Tick(float dt);

        /// <summary>立刻收掉（可重复调用）</summary>
        public abstract void End();

        public virtual void DrawGizmos() { }
    }

    /// <summary>特殊招式运行时能用到的角色身上的东西（AttackTimelineRunner 在 Awake 里组装一次）</summary>
    public class SpecialMoveContext
    {
        public Transform root;
        public Transform firePoint;
        public PlayerController controller;
        public PlayerAimProvider aim;
        public GameObject instigator;
        public string enemyTag = "Enemy";

        /// <summary>发射点：有枪口用枪口，没有用角色位置</summary>
        public Vector2 Origin => firePoint != null ? (Vector2)firePoint.position : (Vector2)root.position;

        public int Facing => controller != null && controller.facingDirection < 0 ? -1 : 1;
    }
}
