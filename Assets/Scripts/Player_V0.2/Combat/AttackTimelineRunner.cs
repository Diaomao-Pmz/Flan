using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    // 【正式版整改 批次A】AttackTimelineRunner 从 AttackTimeline.cs 拆成独立文件
    // 症状：玩家 prefab 的 Add Component 里搜不到 Attack Timeline Runner
    // 根因：Unity 要求 MonoBehaviour 的类名与文件名一致才能在编辑器里挂载；
    //       原先它和数据类一起写在 AttackTimeline.cs 里，只能靠运行时代码补挂

    /// <summary>【时间表执行器】持有所有在跑的攻击实例，每帧推进</summary>
    [DefaultExecutionOrder(50)]
    public class AttackTimelineRunner : MonoBehaviour
    {
        public bool verboseLog = false;

        [Tooltip("运行时在 Scene 视图里画出正在生效的拼刀框（青色）")]
        public bool showClashBox = true;

        private readonly List<AttackInstance> instances = new List<AttackInstance>();

        // 【正式版整改 D · 规范 6】动画事件不能是唯一生命线。
        // 症状：出刀 / 出弹的时间点完全来自动画事件，动画里漏打事件（或没配动画）时，
        //       这一招照常播动画、照常锁手，但静默地不出任何伤害，Console 里什么都没有。
        // 现在：出招时检查一次，每个招式只警告一次（不刷屏）。
        private readonly HashSet<ComboNode> warnedNodes = new HashSet<ComboNode>();

        private void WarnIfNoStrikeEvent(AttackInstance inst)
        {
            ComboNode node = inst.node;
            if (node == null || inst.data.firstStrikeTime >= 0f) return;
            if (!warnedNodes.Add(node)) return;

            if (node.attackClip == null)
            {
                PlayerLog.Warn(
                    $"[时间表] 招式「{node.nodeName}」没有指定 Attack Clip，读不到出刀 / 出弹的时间点，" +
                    "这一招不会产生任何判定或子弹。", node);
            }
            else
            {
                PlayerLog.Warn(
                    $"[时间表] 招式「{node.nodeName}」的动画「{node.attackClip.name}」里没有 " +
                    "TriggerAttackHitbox / SpawnProjectile 事件，这一招不会产生任何判定或子弹，" +
                    "并且整招按「攻击段」处理（期间不能移动）。\n" +
                    "如果这一招本来就不出伤害，可以忽略本警告。", node);
            }
        }

        private PlayerHitDetection hit;
        private PlayerWeaponEmitter emitter;
        private PlayerEffectSpawner fx;
        private PlayerStateMachine sm;
        private PlayerController controller;

        // 每个攻击实例的拼刀框（开过一次就记在这里，招式结束时关掉并移除）
        private readonly Dictionary<AttackInstance, ClashWindow> clashOf = new Dictionary<AttackInstance, ClashWindow>();

        private void Awake()
        {
            hit = GetComponent<PlayerHitDetection>();
            emitter = GetComponent<PlayerWeaponEmitter>();
            fx = GetComponent<PlayerEffectSpawner>();
            sm = GetComponent<PlayerStateMachine>();
            controller = GetComponent<PlayerController>();
        }

        private void OnDisable()
        {
            foreach (ClashWindow w in clashOf.Values) ClashRegistry.Close(w);
            clashOf.Clear();
        }

        /// <summary>一招的总时长 = 前摇 + 攻击 + 后摇</summary>
        public static float ExpectedDuration(ComboNode node)
        {
            AttackTimelineData.ResolvePhases(node, out float w, out float a, out float r, out _);
            return w + a + r;
        }

        // ==========================================================
        // 开始
        // ==========================================================

        public AttackInstance Begin(ComboNode node, WeaponMoveSet weapon, InputCmd cmd, bool isFly = false)
        {
            AttackInstance inst = new AttackInstance
            {
                node = node,
                weapon = weapon,
                cmd = cmd,
                isFly = isFly,
                data = AttackTimelineData.Get(node != null ? node.attackClip : null),
                startFrame = Time.frameCount,
                startTime = Time.time,
            };

            AttackTiming timing = AttackTimelineData.ResolveTiming(node);
            float windup = timing.windup, active = timing.active, recovery = timing.recovery;

            inst.timing = timing;
            inst.activeExplicit = timing.activeExplicit;
            inst.windupEnd = windup;
            inst.activeEnd = windup + active;
            inst.duration = timing.Duration;
            inst.eventShift = windup - timing.animWindup;

            WarnIfNoStrikeEvent(inst);

            instances.Add(inst);

            if (verboseLog)
            {
                PlayerLog.Info($"[时间表] {node.nodeName} 开始：前摇 {windup:F2}s / 攻击 {active:F2}s / 后摇 {recovery:F2}s（总长 {inst.duration:F2}s），" +
                          $"动画倍速 ×{timing.SpeedOf(0):F2} / ×{timing.SpeedOf(1):F2} / ×{timing.SpeedOf(2):F2}，" +
                          $"事件 {inst.data.events.Count} 个" +
                          (inst.data.attackEndTime < 0f ? "，动画里没有 MarkAttackEnd 事件（攻击段为推算值）" : ""));
            }

            return inst;
        }

        // ==========================================================
        // 每帧推进
        // ==========================================================

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < instances.Count; i++)
            {
                AttackInstance inst = instances[i];
                if (!inst.alive) continue;

                // 出招当帧不推进：动画要到本帧 LateUpdate 才从 0 开始播，下一帧两边一起走 dt
                if (inst.startFrame == Time.frameCount) continue;

                Tick(inst, dt);
            }

            for (int i = instances.Count - 1; i >= 0; i--)
            {
                if (!instances[i].alive)
                {
                    CloseClash(instances[i]);
                    instances.RemoveAt(i);
                }
            }
        }

        private void Tick(AttackInstance inst, float dt)
        {
            inst.elapsed += dt;

            // 先推进已经在跑的判定 / 连射，再触发新事件（新开的那份自己会做第 0 帧的检测）
            for (int i = inst.hitRuns.Count - 1; i >= 0; i--)
            {
                hit.TickRun(inst.hitRuns[i], dt);
                if (inst.hitRuns[i].done) inst.hitRuns.RemoveAt(i);
            }

            for (int i = inst.bursts.Count - 1; i >= 0; i--)
            {
                emitter.TickBurst(inst.bursts[i], dt, inst.ownsAnimation, inst.spawnedBullets);
                RecordBorrowIds(inst.spawnedBullets, inst.spawnedBulletIds);
                if (inst.bursts[i].done) inst.bursts.RemoveAt(i);
            }

            var evs = inst.data.events;
            while (inst.alive && inst.nextEvent < evs.Count
                   && inst.timing.ToReal(evs[inst.nextEvent].time) <= inst.elapsed)
            {
                TimelineEvent e = evs[inst.nextEvent];
                inst.nextEvent++;

                // 攻击段已经结束：晚到的出刀/出弹事件不再触发（特效照常）
                bool isStrike = e.kind == TimelineEventKind.Hitbox || e.kind == TimelineEventKind.Projectile;
                if (isStrike && inst.timing.ToReal(e.time) > inst.activeEnd + 0.0001f) continue;

                Fire(inst, e);
            }

            // 拼刀窗口：判定段开始时打开，到时间关掉
            TickClash(inst);

            // 攻击段结束。
            if (!inst.strikesClosed && inst.elapsed >= inst.activeEnd)
            {
                if (inst.activeExplicit)
                {
                    for (int i = 0; i < inst.hitRuns.Count; i++) hit?.EndRun(inst.hitRuns[i]);
                    inst.hitRuns.Clear();
                }
                inst.strikesClosed = true;
            }

            // 总时长走完：这一招彻底结束
            if (inst.elapsed >= inst.duration)
            {
                StopStrikes(inst);
                inst.alive = false;
            }
        }

        private void Fire(AttackInstance inst, TimelineEvent e)
        {
            switch (e.kind)
            {
                case TimelineEventKind.Hitbox:
                    if (hit != null)
                    {
                        PlayerHitDetection.HitboxRun run = hit.BeginRun(inst.node);
                        if (run != null && !run.done) inst.hitRuns.Add(run);
                    }
                    break;

                case TimelineEventKind.Projectile:
                    if (emitter != null)
                    {
                        PlayerWeaponEmitter.BurstRun b = emitter.BeginBurst(
                            inst.node, inst.weapon, inst.ownsAnimation, inst.spawnedBullets);
                        RecordBorrowIds(inst.spawnedBullets, inst.spawnedBulletIds);
                        if (b != null && !b.done) inst.bursts.Add(b);
                    }
                    break;

                case TimelineEventKind.Effect:
                    if (fx != null)
                        Track(inst.spawnedEffects, inst.spawnedEffectIds,
                              fx.SpawnAttackEffect(inst.node, inst.activeEnd - inst.elapsed, inst.duration - inst.elapsed,
                                                   MarkerLife(inst, e)));
                    break;

                case TimelineEventKind.EffectByKey:
                    if (fx != null)
                        Track(inst.spawnedEffects, inst.spawnedEffectIds,
                              fx.SpawnEffectByKey(e.str, inst.node, inst.activeEnd - inst.elapsed, inst.duration - inst.elapsed,
                                                  MarkerLife(inst, e)));
                    break;
            }
        }

        /// <summary>
        /// 特效从出生到配对的 EffectEnd 有多久；没配对则 -1。
        /// 出生时就算好交给特效自己计时 —— 所以招式提前结束、实例被删掉，特效照样演到标记处。
        /// 两个事件都按三段倍速换算成实际时间再相减 —— 动画被加速 / 放慢时，特效寿命跟着画面走。
        /// </summary>
        private static float MarkerLife(AttackInstance inst, TimelineEvent e)
            => e.endTime >= 0f ? inst.timing.ToReal(e.endTime) - inst.timing.ToReal(e.time) : -1f;

        private static void Track(List<GameObject> list, List<int> ids, GameObject go)
        {
            if (go == null) return;
            list.Add(go);
            RecordBorrowIds(list, ids);
        }

        // ==========================================================
        // 【正式版整改 批次E】回收自己生成的特效 / 子弹之前，先核对它还是不是"当初那一次借出"
        // 症状（隐患）：KillAll 直接回收记下来的对象。如果那发子弹早已命中回池、
        // ==========================================================

        /// <summary>给表里新加进来的对象补记借出号（生成的当帧调用）</summary>
        private static void RecordBorrowIds(List<GameObject> list, List<int> ids)
        {
            for (int i = ids.Count; i < list.Count; i++)
            {
                GameObject go = list[i];
                if (go == null) ids.Add(-1);
                else if (go.TryGetComponent(out PooledObject po)) ids.Add(po.BorrowId);
                else if (go.TryGetComponent(out ManagedEffect me)) ids.Add(me.PlayId);   // EffectManager 播的特效
                else ids.Add(-1);
            }
        }

        /// <summary>只回收"还在外面、且仍是当初那一次借出"的对象</summary>
        /// <param name="interrupted">
        /// 是招式被打断引起的回收：不受打断影响的特效（Fixed / Until Marker，看 EffectManager 表里的设置）留着继续演。
        /// </param>
        private static void RecycleIfStillOurs(List<GameObject> list, List<int> ids, bool interrupted = false)
        {
            RecordBorrowIds(list, ids);

            ObjectPoolManager pool = ObjectPoolManager.Instance;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = list[i];
                if (go == null) continue;

                // EffectManager 播的特效：交还给它（它自己会核对是不是同一次播放）
                if (go.TryGetComponent(out ManagedEffect me))
                {
                    if (!(interrupted && me.SurvivesInterrupt)) EffectManager.Instance?.Stop(me, ids[i]);
                    continue;
                }

                if (pool == null) continue;
                if (!go.TryGetComponent(out PooledObject po)) continue;
                if (po.IsInPool || po.BorrowId != ids[i]) continue;

                pool.Recycle(go);
            }

            list.Clear();
            ids.Clear();
        }

        // ==========================================================
        // 结束的几种方式
        // ==========================================================

        // ==========================================================
        // 拼刀（2026-11-20）
        // ==========================================================

        /// <summary>
        /// 招式勾了 Can Clash：判定段开始那一刻打开拼刀框（登记到 ClashRegistry），
        /// 过了节拍总表里的 Clash Window 就关掉。招式结束 / 被掐断时也会关掉（StopStrikes）。
        /// 拼刀成功与否由 Boss 那边在结算伤害前判断（见 ClashRegistry）。
        /// </summary>
        private void TickClash(AttackInstance inst)
        {
            ComboNode node = inst.node;
            if (node == null || !node.canClash) return;

            if (clashOf.TryGetValue(inst, out ClashWindow w))
            {
                if (w.open && inst.elapsed >= w.closeAt) ClashRegistry.Close(w);
                return;
            }

            if (inst.elapsed < inst.windupEnd) return;

            float length = node.clashWindow;
            if (length <= 0f) return;

            w = new ClashWindow
            {
                node = node,
                origin = transform,
                controller = controller,
                offset = node.clashBoxOffset,
                size = node.clashBoxSize,
                effect = node.clashEffect,
                closeAt = inst.windupEnd + length,
            };
            w.onClash = (cw, attackerPos) => OnClashed(inst, cw, attackerPos);
            clashOf[inst] = w;   // 开过一次就记下，这一招不会再开第二次

            if (inst.elapsed < w.closeAt)
            {
                ClashRegistry.Open(w);
                if (verboseLog) PlayerLog.Info($"[拼刀] {node.nodeName} 拼刀框打开（{length:F2}s）");
            }
        }

        private void CloseClash(AttackInstance inst)
        {
            if (clashOf.TryGetValue(inst, out ClashWindow w))
            {
                ClashRegistry.Close(w);
                clashOf.Remove(inst);
            }
        }

        /// <summary>Boss 那边判定拼刀成功后回调（Boss 这一下已经确定不造成伤害）</summary>
        private void OnClashed(AttackInstance inst, ClashWindow w, Vector2 attackerPos)
        {
            ComboNode node = inst.node;
            PlayerLog.Info($"[拼刀] {(node != null ? node.nodeName : "?")} 拼刀成功 → " +
                           (w.effect == ClashEffect.AbortRoutine ? "中断 Boss 套路" : "只结束 Boss 当前招 + 急退"));

            if (w.effect != ClashEffect.EndCurrentAction || node == null) return;

            // 急退：往远离 Boss 的方向（Boss 正好重叠时按面朝的反方向）
            float distance = node.clashRecoilDistance;
            if (distance <= 0f) return;

            float dx = transform.position.x - attackerPos.x;
            float dir = Mathf.Abs(dx) > 0.01f
                ? Mathf.Sign(dx)
                : (controller != null && controller.facingDirection < 0 ? 1f : -1f);

            bool started = sm != null && sm.currentState == sm.comboState
                && sm.comboState.StartClashRecoil(inst, new Vector2(dir * distance, 0f), node.clashRecoilDuration);

            if (!started && verboseLog)
                PlayerLog.Info($"[拼刀] {node.nodeName} 已经不在出招状态（动画被顶掉 / 飞行中），这次不急退");
        }

        private void OnDrawGizmos()
        {
            if (!showClashBox || !Application.isPlaying) return;

            IReadOnlyList<ClashWindow> list = ClashRegistry.Active;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].origin != transform) continue;
                Gizmos.DrawWireCube(list[i].Center, list[i].size);
            }
        }

        private void StopStrikes(AttackInstance inst)
        {
            CloseClash(inst);

            for (int i = 0; i < inst.hitRuns.Count; i++) hit?.EndRun(inst.hitRuns[i]);
            inst.hitRuns.Clear();

            for (int i = 0; i < inst.bursts.Count; i++) emitter?.EndBurst(inst.bursts[i]);
            inst.bursts.Clear();
        }

        /// <summary>
        /// 掐断：之后不再产生任何判定/子弹/特效；已经生成的子弹不动，特效按特效表里的存活模式决定收不收。
        /// 前摇作废、冲刺/滑铲打断、受击都走这个。
        /// </summary>
        public void Void(AttackInstance inst)
        {
            if (inst == null || !inst.alive) return;
            StopStrikes(inst);
            inst.alive = false;

            // 特效跟着招式走的（Follow Active / Follow Attack）：招式被掐断，特效一起收掉，不留残影。
            // Fixed / Until Marker 的留着演完（逐个特效看它在特效表里的设置）。子弹不动。
            RecycleIfStillOurs(inst.spawnedEffects, inst.spawnedEffectIds, interrupted: true);
        }

        /// <summary>
        /// 整发撤掉（R5 判定方式 B：第一发单蓄力被强化招替换）：
        /// 判定、连射停掉，已经生成的特效和子弹也回收。
        /// </summary>
        public void KillAll(AttackInstance inst)
        {
            if (inst == null) return;

            StopStrikes(inst);
            inst.alive = false;

            RecycleIfStillOurs(inst.spawnedEffects, inst.spawnedEffectIds);
            RecycleIfStillOurs(inst.spawnedBullets, inst.spawnedBulletIds);
        }

        /// <summary>掐断所有地面攻击实例（冲刺/滑铲打断、受击）</summary>
        public void VoidAll(bool includeFly = true)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (!includeFly && instances[i].isFly) continue;
                Void(instances[i]);
            }
        }

        /// <summary>有没有地面攻击实例正处于判定阶段（含动画已被顶掉、逻辑还在跑的）</summary>
        public bool AnyGroundActive()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                AttackInstance inst = instances[i];
                if (inst.alive && !inst.isFly && inst.Phase == AttackPhase.Active) return true;
            }
            return false;
        }
    }
}
