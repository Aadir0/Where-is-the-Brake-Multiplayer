using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RandomTextOnEnable : MonoBehaviour
{
    [Header("Text List")]
    [Tooltip("List of texts to pick from randomly on enable.")]
    [TextArea(2, 5)]
    [SerializeField] private string[] randomTexts;

    [Header("UI Component References (Optional - Auto-detected if empty)")]
    [SerializeField] private TextMeshProUGUI tmpText;
    [SerializeField] private Text legacyText;

    [Header("Settings")]
    [Tooltip("If true, avoids repeating the same text immediately consecutive times.")]
    [SerializeField] private bool avoidImmediateRepeat = true;

    private int lastIndex = -1;

    private void Awake()
    {
        FindTextComponents();
    }

    private void OnEnable()
    {
        DisplayRandomText();
    }

    private void Reset()
    {
        FindTextComponents();
    }

    private void FindTextComponents()
    {
        if (tmpText == null)
        {
            tmpText = GetComponent<TextMeshProUGUI>();
            if (tmpText == null) tmpText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (legacyText == null)
        {
            legacyText = GetComponent<Text>();
            if (legacyText == null) legacyText = GetComponentInChildren<Text>(true);
        }
    }

    public void DisplayRandomText()
    {
        if (randomTexts == null || randomTexts.Length == 0) return;

        if (tmpText == null && legacyText == null)
        {
            FindTextComponents();
        }

        int selectedIndex = 0;
        if (randomTexts.Length > 1 && avoidImmediateRepeat)
        {
            do
            {
                selectedIndex = Random.Range(0, randomTexts.Length);
            } while (selectedIndex == lastIndex);
        }
        else
        {
            selectedIndex = Random.Range(0, randomTexts.Length);
        }

        lastIndex = selectedIndex;
        string chosenText = randomTexts[selectedIndex];

        if (tmpText != null)
        {
            tmpText.text = chosenText;
        }

        if (legacyText != null)
        {
            legacyText.text = chosenText;
        }
    }
    public void SetTexts(string[] newTexts)
    {
        randomTexts = newTexts;
        lastIndex = -1;
        DisplayRandomText();
    }
}
