using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Reflection;

public class ConfigItemUI : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI labelText;
    public TMP_InputField inputField;
    public Toggle toggleField;

    private FieldInfo _targetField;
    private object _targetConfig;

    public void Setup(FieldInfo field, object configInstance)
    {
        _targetField = field;
        _targetConfig = configInstance;

        string displayName = field.Name;
        var tooltipAttr = field.GetCustomAttribute<TooltipAttribute>();
        if (tooltipAttr != null) displayName = tooltipAttr.tooltip;

        if (labelText != null) labelText.text = displayName;

        object value = field.GetValue(configInstance);

        if (field.FieldType == typeof(bool))
        {
            if (inputField != null) inputField.gameObject.SetActive(false);
            if (toggleField != null)
            {
                toggleField.gameObject.SetActive(true);
                toggleField.isOn = (bool)value;
            }
        }
        else
        {
            if (toggleField != null) toggleField.gameObject.SetActive(false);
            if (inputField != null)
            {
                inputField.gameObject.SetActive(true);
                inputField.text = value.ToString();

                if (field.FieldType == typeof(int))
                    inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
                else if (field.FieldType == typeof(float))
                    inputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            }
        }
    }

    public void ApplyValue()
    {
        if (_targetField == null || _targetConfig == null) return;

        if (_targetField.FieldType == typeof(bool))
        {
            if (toggleField != null) _targetField.SetValue(_targetConfig, toggleField.isOn);
        }
        else if (_targetField.FieldType == typeof(int))
        {
            if (inputField != null && int.TryParse(inputField.text, out int result))
                _targetField.SetValue(_targetConfig, result);
        }
        else if (_targetField.FieldType == typeof(float))
        {
            if (inputField != null && float.TryParse(inputField.text, out float result))
                _targetField.SetValue(_targetConfig, result);
        }
    }
}