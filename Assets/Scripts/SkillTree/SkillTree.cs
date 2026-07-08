using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SkillTree : MonoBehaviour
{
    [SerializeField] int manaPoints = 4;
    public int ManaPoints => manaPoints;

    private List<SkillType> unlockedSkills = new List<SkillType>();

    public Action<SkillType> OnSkillUnlockedAction;

    [SerializeField] SkillDataBase skillDataBase;

    private Dictionary<Image, SkillData> UIDataDictionary = new Dictionary<Image, SkillData>();

    /// <summary>
    /// 按下按钮时触发此函数
    /// -添加枚举到技能列表
    /// -广播技能解锁事件
    /// </summary>
    /// <param name="skill">技能类型</param>
    private void UnlockSkill(SkillData skillData)
    {
        if (CanUnlockSkill(skillData) == false)
        {
            Debug.Log("You can not unlock this skill");
            return;
        }

        manaPoints -= skillData.ManaCost;
        unlockedSkills.Add(skillData.SkillType);
        OnSkillUnlockedAction?.Invoke(skillData.SkillType);
    }

    bool CanUnlockSkill(SkillData skillData)
    {
        return !(unlockedSkills.Contains(skillData.SkillType)
        || (skillData.RequiredSkillToUnlock != SkillType.None && unlockedSkills.Contains(skillData.RequiredSkillToUnlock) == false)
        || skillData.ManaCost > manaPoints);
    }

    void Start()
    {
        //遍历每个按钮
        foreach(SkillData skillData in skillDataBase.data)
        {
            Transform skillUI = transform.Find(skillData.SkillType.ToString());
            if (skillUI != null) {
                Button skillUIButton = skillUI.GetComponent<Button>();
                if (skillUIButton != null)
                {
                    //设置按钮的触发函数
                    skillUIButton.onClick.AddListener(() => UnlockSkill(skillData));
                }
                //找到图片组件，和skilldata一起放入字典
                Image skillImage = skillUI.GetComponent<Image>();
                if (skillImage != null)
                {
                    UIDataDictionary.Add(skillImage, skillData);
                }
            }
            else
            {
                Debug.LogError("Can't find skillUI object named "+skillData.SkillType.ToString());
            }
        }
    }

    void Update()
    {
        foreach(KeyValuePair<Image, SkillData> pair in UIDataDictionary)
        {
            Image img = pair.Key;
            SkillData sd = pair.Value;
            if (unlockedSkills.Contains(sd.SkillType))
            {
                img.color = Color.grey;
            }
            else if (CanUnlockSkill(sd) == false)
            {
                img.color = Color.red;
            }
            else
            {
                img.color = Color.white;
            }
        }
    }
}
