using TMPro;
using UnityEngine;

public class UISetter : MonoBehaviour
{
    public TMP_Text t;
    public void Set(string s){ if (t) t.text = s; }
}
