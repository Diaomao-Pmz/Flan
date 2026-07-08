using UnityEngine;
using TMPro;

public class ManaUI : MonoBehaviour
{
    private SkillTree skillTree;
    private TextMeshProUGUI manaText;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        skillTree = GetComponentInParent<SkillTree>();
        Debug.Log("SkillTree:"+ skillTree);
        manaText = GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
        manaText.text = "Mana: "+skillTree.ManaPoints.ToString();
    }
}
