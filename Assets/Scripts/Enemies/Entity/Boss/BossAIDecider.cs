using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BossAIDecider : MonoBehaviour
{
    private BossController boss;
    //进入此距离可以攻击玩家
    [SerializeField] float AttackDistance = 10;
    //大于这个距离小于attack distance时可进行远程攻击
    [SerializeField] float LongRangeAttackDistance = 5;
    [SerializeField] bool finishedResting = true;
    [Header("Attack Settings")]
    [SerializeField] List<AttackConfig> attackConfigs;

    Coroutine RestCoroutine;

    //距离足够+已休息即可攻击玩家
    public bool canAttack => boss.DistanceToPlayer <= AttackDistance && finishedResting;

    void Start()
    {
        boss = GetComponent<BossController>();

        //初始化原始权重方便后续重置权重
        for(int i = 0; i < attackConfigs.Count; i++)
        {
            attackConfigs[i].SetToDefaultWeight();
        }
    }

    void Update()
    {
       
    }

    public void StartResting()
    {
        finishedResting = false;

        //目前先让休息时间为随机数
        float rt = UnityEngine.Random.Range(1, 3);

        RestCoroutine = StartCoroutine(DoResting(rt));
    }

    IEnumerator DoResting(float time)
    {
        boss.bossStatusText.SetText($"Resting for {time} seconds");
        yield return new WaitForSeconds(time);
        finishedResting = true;
        RestCoroutine = null;
    }

    public AttackNode SelectSkill()
    {
        if(attackConfigs.Count > 0)
        {
            //player若在远处则将短距离攻击的权重设为0
            Debug.Log($"Current distance to player is {boss.DistanceToPlayer}");
            if(boss.DistanceToPlayer > LongRangeAttackDistance)
            {
                List<AttackConfig> allShortRangeAttack = attackConfigs.Where(l => l.AttackNode.isFarAttack == false).ToList();
                foreach(AttackConfig attackConfig in allShortRangeAttack)
                {
                    attackConfig.SetCurrentWeight(0);
                }
            }

            #region 选取逻辑
            //计算权重总和
            int TotalSum = 0;
            foreach(AttackConfig attackConfig in attackConfigs)
            {
                TotalSum += attackConfig.CurrentWeight;
            }

            //以权重总和为范围取一个随机数，查看这个数落在哪个权重区间
            //比如attack[0]的权重为20，attack[1]权重为30，则若随机数在0-20时选择attack[0]，在21-50时选择attack[1]
            int rn = UnityEngine.Random.Range(1, TotalSum + 1);
            int compareNum = 0;
            AttackNode attackNode = null;
            foreach(AttackConfig attackConfig in attackConfigs)
            {
                compareNum += attackConfig.CurrentWeight;
                if(rn <= compareNum)
                {
                    Debug.Log($"Generated Random Number {rn}");
                    attackNode = attackConfig.AttackNode;
                    break;
                }
            }

            #endregion
            //重置权重
            for (int i = 0; i < attackConfigs.Count; i++)
            {
                attackConfigs[i].SetToDefaultWeight();
            }

            return attackNode;
        }
        return null;
    }
}

[Serializable]
public class AttackConfig
{
    public AttackNode AttackNode;
    public int DefaultWeight;
    public int CurrentWeight { get; private set; }

    public void SetToDefaultWeight()
    {
        CurrentWeight = DefaultWeight;
    }

    public void SetCurrentWeight(int weight)
    {
        CurrentWeight = weight;
    }
}