using UnityEngine;

public class PlayerGMTarget : MonoBehaviour
{
    [Header("Runtime Stats")]
    public int hp = 100;
    public int maxHp = 100;
    //这些到时候要从我们现有的playerstate之类里面拿，然后底下是去改这些playerstate里的值
    //可以加一些比如invincible之类的字段来实现无敌之类的效果

    [GMCommand("player_set_hp")]
    private void SetHp(int value)
    {
        hp = Mathf.Clamp(value, 0, maxHp);
    }

    [GMCommand("player_add_hp")]
    private void AddHp(int delta)
    {
        hp = Mathf.Clamp(hp + delta, 0, maxHp);
    }

    [GMCommand("player_fullheal")]
    private void FullHeal()
    {
        hp = maxHp;
    }
}