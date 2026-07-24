using UnityEngine;
using UnityEngine.UIElements;

public static class ConsoleUiNavigation
{
    public static bool IsEnabled => IsConsoleDevice(SystemInfo.deviceType);

    public static bool IsConsoleDevice(DeviceType deviceType)
    {
        return deviceType == DeviceType.Console;
    }

    public static void ConfigureButtons(VisualElement root)
    {
        ConfigureButtons(root, SystemInfo.deviceType);
    }

    public static void ConfigureButtons(VisualElement root, DeviceType deviceType)
    {
        if (root == null || IsConsoleDevice(deviceType))
            return;

        foreach (Button button in root.Query<Button>().ToList())
            button.focusable = false;
    }
}
